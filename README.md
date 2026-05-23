# Nudge

Nudge is a local, cross-platform productivity tracker that watches your foreground app, asks whether you were productive, and trains a personalized ML model on your responses.

## Platform Support

- **Linux**: Wayland (Sway, GNOME, KDE Plasma) and X11 environments (Cinnamon, XFCE)
- **Windows 10/11**: native foreground-window and idle-time detection
- **macOS**: experimental

## Requirements

- **.NET 10 SDK**
- **Python 3** + `scikit-learn joblib pandas numpy` for ML features

A repo-level `global.json` pins the SDK to .NET 10 so builds, tests, and scripts stay aligned.

## Quick Start

### Linux / macOS

```bash
cd NudgeCrossPlatform
./build.sh
./nudge-tray          # data collection mode
./nudge-tray --ml     # ML-powered adaptive mode
```

### Windows

```powershell
cd NudgeCrossPlatform
.\build.ps1
.\nudge-tray.exe
.\nudge-tray.exe --ml
```

## Features

- **V2 Harvest Engine** — fuses focus source, browser domain, idle, and 300-second behavioral signals into 21 ML features
- **AI Brain tab** — live sensor fusion display, prediction history chart, next-check countdown, model training status
- **Adaptive nudging** — ML fires the nudge early when you're clearly unproductive; suppresses it when you're clearly productive; falls back to a 5–10 min random interval when uncertain
- **Continuous learning** — background trainer automatically retrains the model as you collect more data
- **Settings window** — interval, harvest engine mode, ML toggle
- **Pin button** — keep the Analytics window on top while you switch apps

## Documentation

- [NudgeCrossPlatform/README.md](NudgeCrossPlatform/README.md) — build, run, architecture, IPC protocol
- [NudgeCrossPlatform/ML_README.md](NudgeCrossPlatform/ML_README.md) — ML system, AI Brain tab, model training
- [NudgeCrossPlatform/QUICKSTART_ML.md](NudgeCrossPlatform/QUICKSTART_ML.md) — 3-step ML quick start
- [WINDOWS_README.md](WINDOWS_README.md) — Windows-specific setup
