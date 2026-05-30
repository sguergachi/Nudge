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

`NudgeCore.TestableLogic.cs` builds an `ActivityContext` each tick from: focus source (KWin Script/X11 EWMH/Sway IPC/Wayland protocol), signal quality (`Trusted`/`Usable`/`Poor`), browser domain extraction, and 300s rolling windows (switch counts, distinct apps, app share, anchor return). Produces 26 ML features.

## Alert logic

- ML check runs every 60s (`ML_CHECK_INTERVAL_MS`).
- **Not productive** + confidence ≥ **98%** (`ML_CONFIDENCE_THRESHOLD`) → ML-triggered snapshot.
- **Not productive** + confidence < 98% → defers to interval (`_mlLowConfidence = true`), interval timer NOT reset.
- **Productive** → skip snapshot, reset interval timer (`_productivityConfirmed = true`).
- Interval fallback: random 5–10 min (or `--interval N`).
- Log patterns: `ML TRIGGER`, `ML SKIP`, `ML DEFER`, `INTERVAL SNAPSHOT`.
- Requires 100 (`MIN_SAMPLES_THRESHOLD`) training samples before ML activates.

### SnapshotGate — suppression rules

Every snapshot (ML or interval) passes through `SnapshotGate.Evaluate()` before firing. A suppressed snapshot skips the notification, logs `SUPPRESS:{reason}`, and resets the interval timer without counting toward stats.

| Reason | Condition |
|--------|-----------|
| `Afk` | `AfkFlag == 1` (idle > 60s) |
| `PoorSignal` | `SignalQuality == Poor` (unknown app, process scan, etc.) |
| `InMeeting` | Mic/camera active (Core Audio API, PipeWire, PulseAudio, Windows Registry, or app scan) |
| `ScreenSharing` | PipeWire screen-cast stream active |

Presence detection (`GetPresenceState()`) uses graceful degradation across 3 layers on each platform. Linux: PipeWire (`pw-dump`) → PulseAudio (`pactl`). Windows uses a 4-layer approach:

0. **IAudioSessionManager2 Core Audio API** — directly queries the audio subsystem for active capture sessions. Catches ALL mic usage including WASAPI exclusive mode, virtual devices, and apps that bypass the consent store. Most reliable layer.
1. **CapabilityAccessManager registry** (`ConsentStore\microphone`, `ConsentStore\webcam`) — hardware-level mic/camera detection. Handles both `REG_QWORD` and `REG_DWORD` value types. Stale entries (process not running) are filtered out.
2. **Process scan** — checks if known meeting apps are running (Teams, Zoom, Skype, Webex, Slack, Discord, GoToMeeting, BlueJeans, RingCentral, Whereby, Lark, DingTalk, Tencent Meeting, Voov).
3. **Window title heuristic** — checks the foreground window title for meeting keywords (e.g. "Zoom Meeting", "Google Meet", "Microsoft Teams").

Gated by `--no-meeting-suppression` flag — passes `PresenceState.Unavailable` so gate fails open (never suppresses).

## ML system

### V1 Seed Model (bundled)

A pretrained V1 model is bundled in `NudgeCrossPlatform/model/` and shipped in releases:
- `productivity_model.joblib` — GradientBoosting classifier, 26 V3 features, trained on synthetic seed data
- `scaler.json` — feature scaler parameters
- `trainer_meta.json` — training metadata (model_version, accuracy, sample count)

On first ML startup, `DeployBundledModel()` in `nudge-tray.cs` copies these files from the app directory to `~/.nudge/model/` so the AI works immediately without the user needing 100 labeled snapshots first. The background trainer continues refining/replacing the seed model once real labels accumulate.

### Training

```bash
# Train model
python3 train_model.py ~/.nudge/HARVEST.CSV --model-dir ~/.nudge/model

# Architectures: lightweight (<200 samples), standard (200+)
python3 train_model.py ~/.nudge/HARVEST.CSV --architecture standard
```

- Python scikit-learn served over TCP `127.0.0.1:45002`.
- Background trainer (`background_trainer.py`) retrains at 20+ new samples, 300s check interval.
- Seed data generation: `python3 generate_sample_data.py` creates synthetic labeled data from HARVEST.CSV schema.
- Train model supports V1→V2 schema migration and applies sample weighting (class balance + recency 60d halflife + V1 penalty 0.1x).
- Data all local in `~/.nudge/`.

## Release pipeline

Releases are automated via `.github/workflows/release.yml`.

**To ship a new version:**
1. Edit `const string VERSION = "x.y.z"` in `NudgeCrossPlatform/nudge-tray.cs`
2. Push to `master`

The workflow extracts the version string, checks whether a `v{version}` tag already exists (skips if it does), builds self-contained single-file binaries for `win-x64` and `linux-x64`, and creates a GitHub Release with both archives attached. No manual tagging.

The release binaries are self-contained (no .NET runtime needed by users). The harvest daemon (`nudge`/`nudge.exe`) is published as a self-contained binary too — `nudge-tray` detects and launches it directly. In dev builds (where only `nudge.dll` exists) it falls back to `dotnet nudge.dll`.

**Website:** `docs/index.html` + `docs/style.css` — pure static HTML, hosted via GitHub Pages (enable in repo Settings → Pages → `master`/`docs`). Download buttons point to `releases/latest/download/`.

## Tray menu items

The system tray context menu (built in `CreateAvaloniaMenu()` in `nudge-tray.cs`) contains:

| Item | Behaviour |
|------|-----------|
| Status (disabled) | Live countdown to next check |
| Analytics | Opens Analytics window |
| Settings | Opens Settings window |
| Send Feedback | Opens `github.com/sguergachi/Nudge/issues/new` in browser |
| Check for Updates | Opens releases page; text changes to "Update available: vX.Y.Z ↗" if a newer release is detected on startup |
| Quit | Stops all subprocesses and exits |

The update check fires once on startup (`Task.Run(CheckForUpdateAsync)` in `AfterSetup`). It calls the GitHub Releases API and compares `tag_name` against `VERSION`.

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
- If you see changes in the working tree that you did not make, leave them alone — they belong to another agent working in parallel.
