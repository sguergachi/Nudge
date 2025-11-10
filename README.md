# Nudge

Using ML to nudge you back into productivity. For RU Hack 2017 hackathon.

> Sara Lazar, a neuroscientist at Harvard Medical School, says "Better control over th PCC can help you catch your mind in the act of wandering and nudge it gently back on task."

## Platform Support

Nudge now supports multiple platforms:

- **Linux** (Wayland: Sway, GNOME, KDE Plasma) - Full support
- **Windows 10/11** - Full support (NEW!)
- **macOS** - Experimental support

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

- .NET SDK 8.0 or later
- Platform-specific:
  - **Linux**: Wayland compositor (Sway, GNOME, or KDE Plasma)
  - **Windows**: Windows 10 or later
  - **macOS**: macOS 10.15 or later (experimental)
