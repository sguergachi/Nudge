# Nudge - Windows Compatibility Guide

Nudge now supports Windows! This guide explains how to build and run Nudge on Windows 10 and Windows 11.

## Prerequisites

### Automatic Installation (Recommended)

The build script automatically installs dependencies using **winget** (Windows Package Manager):

1. **winget** - Pre-installed on Windows 10 (version 1809+) and Windows 11
   - If not available, install from: https://aka.ms/getwinget

2. **PowerShell** - Pre-installed on Windows
   - Used for running the build script

When you run `.\build.ps1`, it will automatically:
- Install .NET SDK 9 if not found
- Suggest Python installation command if needed for ML training

### Manual Installation (Alternative)

If you prefer manual installation or don't have winget:

1. **.NET SDK 8.0 or later**
   - Download from: https://dotnet.microsoft.com/download
   - Or install via winget: `winget install Microsoft.DotNet.SDK.9`

2. **Python 3.x** (optional)
   - Only needed for training custom ML models
   - Download from: https://www.python.org/downloads/
   - Or install via winget: `winget install Python.Python.3.12`

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

Nudge uses Windows-specific APIs to integrate with the operating system:

### Window Detection
Uses native Windows API via P/Invoke:
- `GetForegroundWindow()` - Gets the currently active window handle
- `GetWindowText()` - Retrieves the window title text
- Implemented in `nudge.cs` with conditional compilation (`#if WINDOWS`)

### Idle Time Detection
Uses native Windows API:
- `GetLastInputInfo()` - Returns time since last user input (keyboard/mouse)
- `GetTickCount()` - System uptime for calculating idle duration
- Returns idle time in milliseconds

### Notifications
**In Tray Mode (`nudge-tray.exe`):**
- Uses PowerShell scripts to display MessageBox dialogs with Yes/No buttons
- Runs PowerShell in background to avoid blocking the main UI
- Sends responses back to main process via UDP (port 45001)
- Fallback: System tray menu for manual responses

**Implementation Details:**
- Creates temporary PowerShell script with embedded MessageBox code
- Uses `System.Windows.Forms.MessageBox` for native Windows dialogs
- Automatically handles button clicks and sends YES/NO responses

### Data Storage
- Saves activity data to `%TEMP%\HARVEST.CSV`
- Uses `Path.GetTempPath()` for cross-platform compatibility
- Same CSV format as Linux version

### Build System
- `build.ps1` - Native PowerShell build script with automatic dependency installation
- Uses **winget** to install .NET SDK if not found
- Auto-detects installed .NET version and uses appropriate target framework
- Includes Avalonia UI framework and Tmds.DBus.Protocol (for Linux compatibility)
- Creates standalone executables with all dependencies
- Refreshes PATH after installations to immediately use new tools

## Differences from Linux Version

| Feature | Linux (Wayland) | Windows 10/11 |
|---------|----------------|---------------|
| **Window Detection** | `swaymsg`, `gdbus`, `qdbus` | `GetForegroundWindow()` API |
| **Idle Time** | D-Bus services | `GetLastInputInfo()` API |
| **Notifications** | Native DBus with action buttons | PowerShell MessageBox dialogs |
| **File Paths** | `/tmp/HARVEST.CSV` | `%TEMP%\HARVEST.CSV` |
| **Build Script** | `build.sh` (Bash) | `build.ps1` (PowerShell) |
| **Platform Detection** | XDG environment variables | `RuntimeInformation.IsOSPlatform()` |

### Implementation Approach
- Uses conditional compilation (`#if WINDOWS`) for platform-specific code
- No abstractions - direct API calls following Jon Blow's philosophy
- Platform detection at runtime using `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`

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

## Technical Implementation Details

### Platform Detection
```csharp
using System.Runtime.InteropServices;

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    // Windows-specific code
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    // Linux-specific code
}
```

### Windows API P/Invoke Declarations

**Window Detection (nudge.cs):**
```csharp
[DllImport("user32.dll", SetLastError = true)]
static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
```

**Idle Time Detection (nudge.cs):**
```csharp
[StructLayout(LayoutKind.Sequential)]
struct LASTINPUTINFO
{
    public uint cbSize;
    public uint dwTime;
}

[DllImport("user32.dll")]
static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

[DllImport("kernel32.dll")]
static extern uint GetTickCount();
```

### Conditional Compilation
Windows-specific code is wrapped with `#if WINDOWS` preprocessor directives:
```csharp
#if WINDOWS
    // Windows API calls
    static string GetWindowsFocusedApp() { ... }
    static int GetWindowsIdleTime() { ... }
#endif
```

The `WINDOWS` symbol is defined in build.ps1 when creating the project file:
```xml
<DefineConstants>WINDOWS</DefineConstants>
```

### Linux Notification Implementation

Native DBus notifications using Tmds.DBus.Protocol 0.21.0:

**Key Implementation Details (nudge-tray.cs):**
```csharp
private static async void ShowDbusNotification()
{
    using var connection = new Connection(Address.Session!);
    await connection.ConnectAsync();

    // Write notification with hints dictionary
    var arrayStart = writer.WriteDictionaryStart();
    writer.WriteDictionaryEntryStart();
    writer.WriteString("urgency");
    writer.WriteVariant(VariantValue.Byte(2));
    writer.WriteDictionaryEntryStart();
    writer.WriteString("resident");
    writer.WriteVariant(VariantValue.Bool(true));  // Keep notification visible
    writer.WriteDictionaryEnd(arrayStart);

    // Listen for ActionInvoked signals
    await connection.AddMatchAsync(
        actionMatchRule,
        (Message m, object? s) => { /* read action */ },
        (Exception? ex, (uint id, string actionKey) signal, ...) => {
            // Handle button clicks
        },
        ObserverFlags.None, null, null, true
    );

    // Keep connection alive (critical for resident:true to work!)
    await Task.Delay(-1, cancellationSource.Token);
}
```

**Why Connection Keep-Alive Matters:**
- Without keeping the connection alive, notifications expire after ~1 second
- `Task.Delay(-1, cancellationToken)` keeps the DBus connection open
- The `resident:true` hint only works while the connection is active
- Timeout set to 60 seconds to prevent resource leaks

## Future Improvements

Potential enhancements for Windows support:

- **Windows Toast Notifications**: Modern notification API with action buttons (WinRT APIs)
- **Process name detection**: Get executable name instead of just window title
- **Better tray integration**: Enhanced system tray experience with custom icons
- **Installation package**: MSI installer for easier setup
- **Auto-start**: Add to Windows startup folder automatically

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
