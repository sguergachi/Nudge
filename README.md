# Nudge

Using ML to nudge you back into productivity. For RU Hack 2017 hackathon.

> Sara Lazar, a neuroscientist at Harvard Medical School, says "Better control over th PCC can help you catch your mind in the act of wandering and nudge it gently back on task."

## Platform Support

Nudge now supports multiple platforms with native system integration:

- **Linux** (Wayland: Sway, GNOME, KDE Plasma) - Full support
  - Native DBus notifications with action buttons
  - Direct Wayland compositor integration
  - Tmds.DBus.Protocol for notification handling

- **Windows 10/11** - Full support ✨ **NEW!** ✨
  - Native Windows API integration (P/Invoke)
  - Window detection via GetForegroundWindow
  - Idle tracking via GetLastInputInfo
  - PowerShell-based notification dialogs

- **macOS** - Experimental support
  - Basic functionality available
  - Notification support in development

## Quick Start

### Linux

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

See [WINDOWS_README.md](WINDOWS_README.md) for detailed Windows instructions.

## Requirements

- **.NET SDK 8.0 or later** - Required for building and running
- **Python 3.x** - Required for ML functionality

### Platform-Specific Requirements

- **Linux**: Wayland compositor (Sway, GNOME, or KDE Plasma)
- **Windows**: Windows 10 or later
- **macOS**: macOS 10.15 or later (experimental)

## Documentation

- [NudgeCrossPlatform/README.md](NudgeCrossPlatform/README.md) - Main project documentation
- [WINDOWS_README.md](WINDOWS_README.md) - Windows-specific setup and usage guide
- [WINDOWS_COMPATIBILITY_ANALYSIS.md](WINDOWS_COMPATIBILITY_ANALYSIS.md) - Implementation analysis (historical reference)
