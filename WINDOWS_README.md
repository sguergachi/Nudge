# Nudge - Windows Compatibility Guide

Nudge now supports Windows! This guide explains how to build and run Nudge on Windows 10 and Windows 11.

## Prerequisites

1. **.NET SDK 8.0 or later**
   - Download from: https://dotnet.microsoft.com/download
   - Required for building and running Nudge

2. **PowerShell** (pre-installed on Windows)
   - Used for running the build script

3. **Python 3.x** (optional)
   - Only needed if you want to train custom ML models
   - Download from: https://www.python.org/downloads/

## Building on Windows

1. Open PowerShell in the `NudgeCrossPlatform` directory
2. Run the build script:

```powershell
cd NudgeCrossPlatform
.\build.ps1
```

To clean before building:

```powershell
.\build.ps1 -Clean
```

## Running Nudge

### CLI Mode

Start the tracker:
```powershell
.\nudge.exe
```

View help:
```powershell
.\nudge.exe --help
```

Custom snapshot interval (every 2 minutes):
```powershell
.\nudge.exe --interval 2
```

### System Tray Mode

Start with system tray icon:
```powershell
.\nudge-tray.exe
```

With custom interval:
```powershell
.\nudge-tray.exe --interval 2
```

### Responding to Snapshots

When Nudge takes a snapshot, respond using:

```powershell
.\nudge-notify.exe YES    # I was productive
.\nudge-notify.exe NO     # I was not productive
```

Alternatively, in tray mode, a dialog will pop up asking for your response.

## How It Works on Windows

Nudge uses Windows-specific APIs to:

1. **Window Detection**: Uses `GetForegroundWindow()` and `GetWindowText()` to track the active window
2. **Idle Time Detection**: Uses `GetLastInputInfo()` to measure user inactivity
3. **Notifications**: Uses Windows MessageBox dialogs (in tray mode)
4. **Data Storage**: Saves activity data to `%TEMP%\HARVEST.CSV`

## Differences from Linux Version

- **No Wayland/X11 requirement**: Uses native Windows APIs instead
- **No DBus dependency**: Windows notifications use native dialogs
- **File paths**: Uses Windows temp directory instead of `/tmp/`
- **MessageBox dialogs**: Instead of desktop notifications (simpler but functional)

## Troubleshooting

### Build Errors

If you encounter build errors:

1. Ensure .NET SDK is installed: `dotnet --version`
2. Check you're in the correct directory: `NudgeCrossPlatform`
3. Try cleaning first: `.\build.ps1 -Clean`

### Runtime Errors

If the application crashes:

1. Check the CSV file location: `%TEMP%\HARVEST.CSV`
2. Ensure you have write permissions to the temp directory
3. Check if another instance is already running (UDP port 45001 conflict)

### Notifications Not Showing

If notifications don't appear:
- Tray mode uses MessageBox dialogs which should always work
- Make sure Windows focus assist isn't blocking dialogs
- Use the tray menu to manually respond

## Training ML Models

If you want to train custom productivity models:

1. Install Python and pip
2. Install dependencies:
```powershell
python -m pip install -r requirements-cpu.txt
```

3. Collect data by running Nudge for a few days
4. Train the model:
```powershell
python train_model.py
```

## Future Improvements

Potential enhancements for Windows support:

- **Windows Toast Notifications**: Modern notification API with action buttons
- **Process name detection**: Get executable name instead of window title
- **Better tray integration**: Enhanced system tray experience
- **Installation package**: MSI installer for easier setup

## Contributing

If you encounter issues or have suggestions for improving Windows support, please open an issue on GitHub.

## Platform Comparison

| Feature | Linux (Wayland) | Windows 10/11 | macOS |
|---------|----------------|---------------|-------|
| Window Detection | ✓ Full support | ✓ Full support | ⚠ Experimental |
| Idle Time Detection | ✓ Full support | ✓ Full support | ⚠ Experimental |
| Notifications | ✓ Desktop notifications | ✓ MessageBox dialogs | ⚠ Experimental |
| Tray Icon | ✓ Supported | ✓ Supported | ✓ Supported |
| Build System | build.sh | build.ps1 | build.sh |

Legend: ✓ = Fully supported, ⚠ = Experimental/Limited support
