# Nudge

AI-powered focus tracker. Nudge watches your foreground app and idle time, asks whether you were productive, trains a local ML model on your answers, and interrupts you only when it's confident you're drifting — not on a fixed schedule.

**Website:** https://sguergachi.github.io/Nudge/ | **Feedback:** [open an issue](https://github.com/sguergachi/Nudge/issues/new)

---

## Download (no build required)

Grab the latest release for your platform:

| Platform | Download |
|----------|----------|
| Windows 10/11 | [nudge-win-x64.zip](https://github.com/sguergachi/Nudge/releases/latest/download/nudge-win-x64.zip) |
| Linux (x64) | [nudge-linux-x64.tar.gz](https://github.com/sguergachi/Nudge/releases/latest/download/nudge-linux-x64.tar.gz) |

Binaries are self-contained — no .NET installation required.

**Linux quick install:**
```bash
tar -xzf nudge-linux-x64.tar.gz
cd linux
./install-linux-app.sh   # registers desktop entry + icon
./nudge-tray             # start tracking
```

**Windows quick install:**
```
Extract nudge-win-x64.zip → run nudge-tray.exe
```

**ML features** (optional): `pip install scikit-learn joblib pandas numpy`, then enable ML in Settings.

---

## Platform Support

| Platform | Status |
|----------|--------|
| Linux — Wayland (KDE, GNOME, Sway) | Full |
| Linux — X11 (Cinnamon, XFCE) | Full |
| Windows 10/11 | Full |
| macOS | Experimental |

---

## Build from Source

Requires **.NET 10 SDK** (pinned by `global.json`).

```bash
# Linux / macOS
cd NudgeCrossPlatform
./build.sh
./nudge-tray

# Windows
cd NudgeCrossPlatform
.\build.ps1
.\nudge-tray.exe
```

Both scripts build all three binaries, run tests, and copy outputs to the project root.

---

## How it works

```
1. nudge-tray starts and launches the harvest daemon (nudge)
2. nudge tracks foreground app + idle time every second
3. Every 60s the ML model checks: "Is the user drifting?"
   - High confidence unproductive → fires notification immediately
   - High confidence productive   → stays silent
   - Low confidence               → falls back to 5–10 min random interval
4. User answers Yes/No → data stored → model retrains automatically
```

---

## Features

- **Adaptive nudging** — interrupts at the moment of drift, not on a timer
- **Local ML** — scikit-learn classifier trained on your own responses, all data in `~/.nudge/`
- **AI Brain tab** — live sensor fusion, prediction history, next-check countdown
- **System tray** — status, Settings, Analytics, Send Feedback, Check for Updates
- **Privacy** — no cloud, no accounts, no telemetry

---

## Releasing a new version

1. Bump `const string VERSION = "x.y.z"` in `NudgeCrossPlatform/nudge-tray.cs`
2. Push to `master`

GitHub Actions detects the new version, builds self-contained binaries for Windows and Linux, and publishes a GitHub Release automatically. No manual tagging required.

---

## Documentation

| File | Contents |
|------|----------|
| [NudgeCrossPlatform/README.md](NudgeCrossPlatform/README.md) | Architecture, IPC protocol, data files |
| [NudgeCrossPlatform/QUICKSTART_ML.md](NudgeCrossPlatform/QUICKSTART_ML.md) | 3-step ML setup |
| [NudgeCrossPlatform/ML_README.md](NudgeCrossPlatform/ML_README.md) | Full ML system docs |
| [NudgeCrossPlatform/ALERT_LOGIC.md](NudgeCrossPlatform/ALERT_LOGIC.md) | Alert trigger logic |
| [WINDOWS_README.md](WINDOWS_README.md) | Windows-specific notes |
| [AGENTS.md](AGENTS.md) | Guide for AI agents and contributors |
