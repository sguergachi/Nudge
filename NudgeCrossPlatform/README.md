# Nudge - Productivity Tracker

ML-powered productivity tracker that learns from your responses and keeps all data local.

## Requirements

### Linux

- Wayland compositor (Sway, GNOME, KDE) or X11 desktop (Cinnamon, XFCE, etc.)
- `xdotool` and optionally `xprintidle` for X11 environments
- **.NET 10 SDK**
- **Python 3** for ML features

### Windows

- **Windows 10 or later**
- **.NET 10 SDK**
- **Python 3** for ML features

## Build

### Linux / macOS

```bash
./build.sh
```

### Windows

```powershell
.\build.ps1
```

Build notes:

- the repo is pinned to **.NET 10** via `global.json`
- checked-in `.csproj` files are the source of truth
- the build scripts still strip the shebang line from `nudge.cs` and `nudge-notify.cs`
- tests run by default; use `--skip-tests` / `-SkipTests` to skip them

This produces three main executables:

- `nudge` / `nudge.exe` - main tracker (harvest subprocess)
- `nudge-notify` / `nudge-notify.exe` - sends YES/NO responses
- `nudge-tray` / `nudge-tray.exe` - tray UI with AI Brain tab

## Run

### Linux / macOS

```bash
./nudge-tray           # recommended — manages everything
./nudge-tray --ml      # enable ML-powered adaptive notifications
```

### Windows

```powershell
.\nudge-tray.exe
.\nudge-tray.exe --ml
```

`nudge-tray` automatically starts and manages:
- the harvest subprocess (`nudge.dll`)
- the ML inference server (when `--ml` is active)
- the background model trainer

## Data Files

By default Nudge stores data in `~/.nudge/` on every platform:

- `HARVEST.CSV` - labeled productivity snapshots (training data)
- `ACTIVITY_LOG.CSV` - minute-by-minute foreground-app activity
- `model/productivity_model.joblib` - trained scikit-learn model

## Architecture

```
nudge-tray (Avalonia GUI)
├── System tray icon + context menu
├── Settings window
├── Analytics window
│   ├── Today / This Week / This Month / All Time tabs
│   └── AI Brain tab
│       ├── In Focus Now  (live app + sensor fusion signals)
│       ├── Next AI Check (countdown to next ML inference)
│       ├── Prediction History (gradient chart of recent checks)
│       ├── Recent Checks (event log)
│       └── Model Training (accordion with training details)
│
└── nudge subprocess (nudge.dll) — IPC via stdout lines + UDP 45001
    ├── V2 Harvest Engine
    │   ├── ActivityContext (focus source, signal quality, idle, domain)
    │   └── FeatureVectorV2 (21 ML-ready features, 300s rolling windows)
    ├── ML check every 60s → TCP 127.0.0.1:45002
    └── Fallback: random 5–10 min snapshot interval
```

### IPC Protocol (nudge.dll → nudge-tray stdout)

| Prefix | Meaning |
|--------|---------|
| `SNAPSHOT` | Show nudge notification |
| `MLDATA:{json}` | ML prediction result (every 60s) |
| `MLNEXT:{unixts}` | Next scheduled check timestamp |
| `APPFOCUS:{app}\t{title}` | Foreground app changed |
| `HARVEST:{json}` | Sensor fusion signals (every 2s, V2 engine) |

## V2 Harvest Engine

The V2 engine builds a rich `ActivityContext` from:
- Focus source (KWin Script, X11 EWMH, Wayland Protocol, Sway IPC, …)
- Signal quality (Trusted / Usable / Poor)
- Browser domain extraction
- 300-second rolling window: switch counts, distinct apps, app share, anchor return

Signal quality colors in the AI Brain tab:
- **Green** — Trusted (compositor API, high confidence)
- **Amber** — Usable (browser without domain, or catalog disagrees)
- **Red** — Poor (heuristic scan, unknown source)

## ML System

Nudge uses a **scikit-learn** classifier served over TCP at `127.0.0.1:45002`.

Every 60 seconds:
1. V2 engine produces 21 features from current context
2. Features sent to inference server via TCP
3. Server returns `prediction` (0=not productive, 1=productive) + `confidence`
4. If not-productive + confidence ≥ threshold → nudge fires immediately
5. If productive + high confidence → check skipped
6. If low confidence → wait for 5–10 min fallback interval

See [ML_README.md](ML_README.md) for full ML documentation, and [ALERT_LOGIC.md](ALERT_LOGIC.md) for the complete alert trigger logic.
