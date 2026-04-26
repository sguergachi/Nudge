# Nudge

Nudge is a local, cross-platform productivity tracker that watches your foreground app, asks whether you were productive, and can train a small ML model on your responses.

## Platform Support

- **Linux**: Wayland (Sway, GNOME, KDE Plasma) and X11 environments such as Cinnamon/XFCE
- **Windows 10/11**: native foreground-window and idle-time detection
- **macOS**: experimental

## Requirements

- **.NET 10 SDK**
- **Python 3.x** for ML features

A repo-level `global.json` now pins the SDK to .NET 10 so builds, tests, and scripts stay aligned.

## Quick Start

### Linux / macOS

```bash
cd NudgeCrossPlatform
./build.sh
./nudge
```

### Windows

```powershell
cd NudgeCrossPlatform
.\build.ps1
.\nudge.exe
```

Both build scripts use the checked-in project files, strip the shebang line from the script-style entry points, and run `dotnet test` by default.

## Documentation

- [NudgeCrossPlatform/README.md](NudgeCrossPlatform/README.md) - project-specific build and runtime details
- [WINDOWS_README.md](WINDOWS_README.md) - Windows-specific setup and usage notes
