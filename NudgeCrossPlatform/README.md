# Nudge - Productivity Tracker

ML-powered productivity tracker that learns from your behavior.

> "Better control over the PCC can help you catch your mind in the act of wandering and nudge it gently back on task." - Sara Lazar, neuroscientist at Harvard Medical School

## What It Does

1. Tracks what app you're using every 5 minutes
2. Asks "Were you productive?" (via notification)
3. Trains ML model on your responses
4. (Eventually) Nudges you when it detects unproductive patterns

## Requirements

### Linux
- **Wayland** compositor (Sway, GNOME, or KDE)
- **.NET SDK 8.0+** (dotnet)
- **Python 3** with TensorFlow (required for ML)

### Windows
- **.NET SDK 8.0+**
- **Windows 10 or later**
- **Python 3** (required for ML)

## Build

### Linux/macOS
```bash
./build.sh
```

### Windows
```powershell
.\build.ps1
```

This creates three executables:
- `nudge` / `nudge.exe` - Main tracker (runs continuously)
- `nudge-notify` / `nudge-notify.exe` - Send YES/NO responses
- `nudge-tray` / `nudge-tray.exe` - System tray GUI

## Run

### Linux/macOS

Terminal 1 (tracker):
```bash
./nudge
```

Terminal 2 (when prompted):
```bash
./nudge-notify YES    # I was productive
./nudge-notify NO     # I was not productive
```

Or use system tray mode:
```bash
./nudge-tray
```

### Windows

PowerShell 1 (tracker):
```powershell
.\nudge.exe
```

PowerShell 2 (when prompted):
```powershell
.\nudge-notify.exe YES    # I was productive
.\nudge-notify.exe NO     # I was not productive
```

Or use system tray mode:
```powershell
.\nudge-tray.exe
```

See [WINDOWS_README.md](../WINDOWS_README.md) for detailed Windows instructions.

## Train Model

After collecting data (20+ examples):

```bash
python3 -m pip install -r requirements.txt
python3 train_model.py
```

## Files

- `nudge.cs` (~900 lines) - Main tracker with Windows/Linux support
- `nudge-notify.cs` (~200 lines) - CLI notifier
- `nudge-tray.cs` (~650 lines) - System tray GUI with native notifications
- `train_model.py` (300 lines) - ML training
- `validate_data.py` (100 lines) - Data validation
- `build.sh` - Linux/macOS build script
- `build.ps1` - Windows PowerShell build script

Total: ~2,150 lines of actual code

## Data Format

### Linux
CSV at `/tmp/HARVEST.CSV`:
```
foreground_app,idle_time,time_last_request,productive
-123456789,1500,30000,1
987654321,500,120000,0
```

### Windows
CSV at `%TEMP%\HARVEST.CSV`:
```
foreground_app,idle_time,time_last_request,productive
-123456789,1500,30000,1
987654321,500,120000,0
```

Same format, different location using `Path.GetTempPath()`

## Architecture

**No architecture.** Just code that works.

- No interfaces (only 1 implementation)
- No factories (just call the function)
- No separate projects (everything's inline)
- No abstraction layers (direct system calls)

Read the code top-to-bottom. It does what it says.

## Philosophy

This is Jon Blow-style programming:
- **Specific over general** - Solves this problem, not hypothetical futures
- **Inline over abstract** - Read the actual code, not architectural diagrams
- **Working over perfect** - Does the job, doesn't pretend to be elegant

### Platform Support Approach

Windows and Linux support use **conditional compilation** (`#if WINDOWS`) rather than abstractions:
- No interfaces (just direct platform checks)
- No factory patterns (just `if (IsWindows) { ... } else { ... }`)
- No separate platform projects (everything's inline)
- Platform detection at runtime using `RuntimeInformation.IsOSPlatform()`

**Linux**: Direct D-Bus calls via Tmds.DBus.Protocol for notifications
**Windows**: Direct Windows API P/Invoke for window detection and idle time

If you need X11 support, add another conditional block.
If you need macOS support, add another conditional block.

Don't build abstractions for problems you don't have.

## Previous Versions

See `BRUTAL_TRUTH.md` - the analysis that led to this rewrite.

This version deleted:
- 53,838 lines of legacy code
- 3 separate C# projects
- Over-engineered abstractions
- Duplicate training scripts

What remains: Direct, readable code that solves the actual problem.

## License

MIT - Do whatever you want with it

## Credits

- Original hackathon project (RU Hack 2017): Sammy Guergachi
- Ruthless simplification: Jon Blow's philosophy applied
