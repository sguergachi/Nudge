# Nudge — Agent Guide

## Build (must use build script)

```bash
cd NudgeCrossPlatform
./build.sh              # Linux/macOS
.\build.ps1             # Windows (param: -SkipTests, -Clean)
```

The `.csproj` files have `EnableDefaultCompileItems=false` — all sources are explicit. Build scripts strip the shebang line from `nudge.cs → nudge_build.cs` and `nudge-notify.cs → nudge-notify_build.cs` before compilation.

**When editing `nudge.cs` or `nudge-notify.cs`**, you must also edit the corresponding `*_build.cs` file with the same changes — the build script regenerates it but agents should keep both in sync. Edit both files.

## .NET 10

Pinned via `global.json` at repo root (`"version": "10.0.100", "rollForward": "latestFeature"`). Same SDK requirement on all platforms.

## Projects (all in `NudgeCrossPlatform/`)

| Binary | Role | Key sources |
|---|---|---|
| `nudge` | Harvest daemon | `nudge_build.cs`, `NudgeCore.TestableLogic.cs`, `NudgeJsonContext.cs` |
| `nudge-notify` | Send YES/NO responses | `nudge-notify_build.cs` |
| `nudge-tray` | GUI tray + Analytics | `nudge-tray.cs`, `AnalyticsWindow.cs`, `CustomNotification.cs`, `SettingsWindow.cs` |

Standalone `dotnet build` works per-project. Shared code lives in `NudgeCore.TestableLogic.cs` (namespace `NudgeCore`) — compiled into both `nudge` and `nudge-tray`.

## Tests

xunit only (no moq). Project in `NudgeCrossPlatform.Tests/`. Targets `net10.0`.

```bash
dotnet test NudgeCrossPlatform.Tests/NudgeCrossPlatform.Tests.csproj --no-restore -v quiet
```

Or let the build script run them: `build.sh` always runs tests; `build.ps1 -SkipTests` skips.

## Architecture

### Two separate UI surfaces (do not conflate)

1. **Tray icon + menu** — Avalonia `TrayIcon` with `NativeMenu`. Shows status + Quit. Does NOT show YES/NO.
2. **Notification window** (`CustomNotification.cs`) — custom Avalonia window with animations. Shows YES/NO buttons. Position persisted to `~/.config/nudge-notification-config.json`. **This is the only notification system** — do not replace with native notifications.

### IPC protocol (nudge daemon → tray via stdout)

| Prefix | Purpose |
|---|---|
| `SNAPSHOT` | Trigger notification |
| `MLDATA:{json}` | `MLLiveEvent` — every ML check |
| `MLNEXT:{unixts}` | Next ML check timestamp |
| `APPFOCUS:{app}\t{title}` | Foreground app changed |
| `HARVEST:{json}` | Sensor fusion signal (2s) |

YES/NO responses flow back via UDP `127.0.0.1:45001`.

### V2 Harvest Engine

`NudgeCore.TestableLogic.cs` builds an `ActivityContext` each tick from: focus source (KWin Script/X11 EWMH/Sway IPC/Wayland protocol), signal quality (`Trusted`/`Usable`/`Poor`), browser domain extraction, and 300s rolling windows (switch counts, distinct apps, app share, anchor return). Produces 21 ML features.

## Alert logic

- ML check runs every 60s (`ML_CHECK_INTERVAL_MS`).
- **Not productive** + confidence ≥ **98%** (`ML_CONFIDENCE_THRESHOLD`) → ML-triggered snapshot.
- **Not productive** + confidence < 98% → defers to interval (`_mlLowConfidence = true`), interval timer NOT reset.
- **Productive** → skip snapshot, reset interval timer (`_productivityConfirmed = true`).
- Interval fallback: random 5–10 min (or `--interval N`).
- Log patterns: `ML TRIGGER`, `ML SKIP`, `ML DEFER`, `INTERVAL SNAPSHOT`.
- Requires 100 (`MIN_SAMPLES_THRESHOLD`) training samples before ML activates.

## ML system

```bash
# Train model
python3 train_model.py ~/.nudge/HARVEST.CSV --model-dir ~/.nudge/model

# Architectures: lightweight (<200 samples), standard (200+)
python3 train_model.py ~/.nudge/HARVEST.CSV --architecture standard
```

- Python scikit-learn served over TCP `127.0.0.1:45002`.
- Background trainer (`background_trainer.py`) retrains at 50+ new samples, 300s check interval.
- Data all local in `~/.nudge/`.

## Code quality rules

Zero warning policy on builds. Key suppressible patterns:

- **CA1852**: `sealed` internal types not inherited from.
- **CA1805**: Remove explicit `= 0`/`= false`/`= null` default init.
- **CA1822**: Mark methods `static` if they don't access instance data.
- **CA1838**: Use `char[]` buffer, not `StringBuilder`, in P/Invokes.
- **CA1305/CA1310**: Always pass `CultureInfo.InvariantCulture` or `StringComparison.Ordinal` for machine strings.
- **CA1863**: Pre-parse format strings as `static readonly CompositeFormat`.

## Platform-specific gotchas

- `/tmp/HARVEST.CSV` is hardcoded (Linux-only). Windows uses `Path.GetTempPath()`.
- `XDG_SESSION_TYPE`, `XDG_CURRENT_DESKTOP` env vars are Linux-only — guard with `RuntimeInformation.IsOSPlatform()`.
- Compositor detection returns "unknown" on Windows — add `GetWindowsCompositor()`.
- Window detection on KDE/Wayland uses a KWin script auto-installed to `~/.local/share/kwin/scripts/nudge-window-tracker/` via `Tmds.DBus.Protocol`.
- **`org.kde.KWin.queryWindowInfo` is BANNED** — triggers interactive crosshair window-picker. Never use it.
- `ext-foreign-toplevel-list-v1` does NOT expose focus state — only app_id + title.

## Lessons from past mistakes

- **Read existing code thoroughly** before changing. Especially polished code with animations/styling — it represents significant user effort.
- **Make the MINIMAL change.** Don't "improve" code not mentioned in the request. If the request is about the tray icon, don't touch the notification system.
- **Don't replace working polished code** with native alternatives without asking. The custom notification window is intentionally cross-platform and preferred over native notifications.
- **Custom notification** uses Avalonia window with keyboard shortcuts (`Ctrl+Shift+Y` = YES, `Ctrl+Shift+N` = NO), drag-to-reposition, auto-dismiss 30s, position persistence.
- **Prefer simple over clever.** The repo has a history of over-engineering (3 training scripts, thread-safe CSV locking, hash collision detection). Delete before adding.

## What not to do

- Do not edit `bin/`, `obj/`, or `dist/`.
- Do not run `dotnet publish` manually — build script handles platform publishing.
- Do not touch `*.dll`, `*.exe`, `*.pdb`, or `*.runtimeconfig.json` in `NudgeCrossPlatform/` — they are build outputs and gitignored.
- Do not add moq or other mocking frameworks — tests use xunit only, testable code via `NudgeCoreLogic` static methods.
