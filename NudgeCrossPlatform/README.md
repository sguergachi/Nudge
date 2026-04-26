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

- `nudge` / `nudge.exe` - main tracker
- `nudge-notify` / `nudge-notify.exe` - sends YES/NO responses
- `nudge-tray` / `nudge-tray.exe` - tray UI

## Run

### Linux / macOS

```bash
./nudge
./nudge-notify YES
./nudge-notify NO
./nudge-tray
```

### Windows

```powershell
.\nudge.exe
.\nudge-notify.exe YES
.\nudge-notify.exe NO
.\nudge-tray.exe
```

## Data Files

By default Nudge stores data in `~/.nudge/` on every platform:

- `HARVEST.CSV` - labeled productivity snapshots
- `ACTIVITY_LOG.CSV` - minute-by-minute foreground-app activity

## Architecture

Still intentionally direct:

- small shared helpers for pure logic and shared paths
- direct platform checks instead of large abstraction layers
- single-file-ish entry points for the main apps
- no trimming / AOT / publish tricks enabled by default
