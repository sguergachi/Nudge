## Install

### Windows — Setup (recommended)
Download **Nudge-win-Setup.exe** and run it. Installs to your user folder — no admin or UAC prompt required. Adds a Start Menu shortcut.

### Windows — Portable
Download **nudge-win-x64.zip**, extract, and run **nudge-tray.exe**.

### Linux
1. Download **nudge-linux-installer.run** and run: `chmod +x nudge-linux-installer.run && ./nudge-linux-installer.run`
2. Or use **nudge-linux-x64.tar.gz**: extract and run `./install-linux-app.sh`

### ML features
When you enable ML mode (in Settings or via `--ml`), Nudge automatically:
- Creates a Python virtual environment in `~/.nudge/venv/`
- Installs scikit-learn, joblib, pandas, and numpy
- No manual pip install needed

---
**Feedback?** [Open an issue](https://github.com/sguergachi/Nudge/issues/new) — every report shapes what gets built next.
