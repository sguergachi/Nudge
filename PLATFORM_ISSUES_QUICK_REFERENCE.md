# Quick Reference: Platform-Specific Code Locations

## File: nudge.cs

### Line 79: Hardcoded /tmp path
```csharp
static string _csvPath = "/tmp/HARVEST.CSV";
```
**Issue**: /tmp doesn't exist on Windows
**Fix**: Use `Path.GetTempPath()` or `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nudge")`

---

### Lines 1: Shell shebang
```csharp
#!/usr/bin/env dotnet run
```
**Issue**: Unix-specific, though ignored on Windows
**Fix**: Can remove or keep (harmless on Windows)

---

### Lines 170-176: XDG_SESSION_TYPE check
```csharp
var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
if (sessionType != "wayland") {
    Warning($"Not running on Wayland (detected: {sessionType ?? "none"})");
    valid = false;
}
```
**Issue**: XDG_SESSION_TYPE doesn't exist on Windows, will show warning
**Fix**: Add check: `if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { ... }`

---

### Lines 179-205: Compositor detection
```csharp
_compositor = DetectCompositor();  // Lines 179
// ... later validation checks for swaymsg, gdbus, qdbus
```
**Issue**: Will always return "unknown" on Windows (no Sway, GNOME, KDE)
**Fix**: Implement `GetWindowsCompositor()` that returns "windows"

---

### Lines 300-312: DetectCompositor() method
```csharp
static string DetectCompositor() {
    if (CommandExists("swaymsg"))
        return "sway";
    var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
    // ... etc
}
```
**Issue**: XDG_CURRENT_DESKTOP is Linux-specific
**Fix**: Add Windows case at start

---

### Lines 305: XDG_CURRENT_DESKTOP check
```csharp
var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
if (desktop?.Contains("GNOME") == true)
    return "gnome";
if (desktop?.Contains("KDE") == true)
    return "kde";
```
**Issue**: Environment variable doesn't exist on Windows
**Fix**: Skip this section on Windows

---

### Lines 314-444: GetForegroundApp() and related methods
**Problem**: All three implementations (Sway, GNOME, KDE) are Linux-specific
- `GetSwayFocusedApp()` - Uses swaymsg command
- `GetGnomeFocusedApp()` - Uses gdbus D-Bus command
- `GetKDEFocusedApp()` - Uses xdotool X11 command

**Fix**: Add `GetWindowsFocusedApp()` using Windows API:
```csharp
[DllImport("user32.dll")]
static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll")]
static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

static string GetWindowsFocusedApp() {
    try {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "unknown";
        GetWindowThreadProcessId(hwnd, out uint pid);
        Process p = Process.GetProcessById((int)pid);
        return p.ProcessName;
    }
    catch { return "unknown"; }
}
```

---

### Lines 336: swaymsg command
```csharp
var json = RunCommand("swaymsg", "-t get_tree");
```
**Issue**: swaymsg doesn't exist on Windows
**Fix**: Will be skipped via compositor detection

---

### Lines 411-414: gdbus command (GNOME)
```csharp
var output = RunCommand("gdbus", "call --session --dest org.gnome.Shell " +
    "--object-path /org/gnome/Shell " +
    "--method org.gnome.Shell.Eval " +
    "\"global.display.focus_window.get_wm_class()\"");
```
**Issue**: gdbus is Linux/D-Bus specific
**Fix**: Will be skipped via compositor detection

---

### Lines 493-593: GetKDEFocusedApp() (KDE)
```csharp
// Uses KWin D-Bus scripting API for Wayland + X11
// Falls back to xdotool for X11 compatibility
var windowName = RunCommand("xdotool", "getactivewindow getwindowname");
```
**Status**: Now supports KDE Wayland via KWin D-Bus scripting API
**Fallback**: xdotool for X11, generic identifier if both fail
**Fix**: Cross-platform via compositor detection

---

### Lines 446-541: GetIdleTime() and related methods
**Problem**: All use D-Bus services (Linux-specific)
- `GetFreedesktopIdleTime()` - Uses qdbus/gdbus
- `GetGnomeIdleTime()` - Uses gdbus for Mutter

**Issue**: Commands don't exist on Windows
**Fix**: Add `GetWindowsIdleTime()` using Windows API:
```csharp
[DllImport("user32.dll")]
static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

[DllImport("kernel32.dll")]
static extern uint GetTickCount();

[StructLayout(LayoutKind.Sequential)]
struct LASTINPUTINFO {
    public uint cbSize;
    public uint dwTime;
}

static int GetWindowsIdleTime() {
    try {
        LASTINPUTINFO lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(lii);
        if (GetLastInputInfo(ref lii)) {
            return (int)(GetTickCount() - lii.dwTime);
        }
        return 0;
    }
    catch { return 0; }
}
```

---

### Lines 477-515: GetFreedesktopIdleTime()
```csharp
var output = RunCommand("qdbus", "org.freedesktop.ScreenSaver ...");
// fallback:
output = RunCommand("gdbus", "call --session --dest org.freedesktop.ScreenSaver ...");
```
**Issue**: qdbus and gdbus don't exist on Windows
**Fix**: Will be handled by GetIdleTime() routing to Windows version

---

### Lines 521-540: GetGnomeIdleTime()
```csharp
var output = RunCommand("gdbus", "call --session " +
    "--dest org.gnome.Mutter.IdleMonitor ...");
```
**Issue**: gdbus is Linux-specific
**Fix**: Will be handled by GetIdleTime() routing

---

### Lines 722-741: RunCommand() method
```csharp
static string RunCommand(string cmd, string args) {
    var psi = new ProcessStartInfo {
        FileName = cmd,
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    // ...
}
```
**Status**: Works on Windows, but will fail if command doesn't exist
**Fix**: Ensure all called commands exist or handle gracefully

---

### Lines 743-754: CommandExists() method
```csharp
static bool CommandExists(string cmd) {
    try {
        var output = RunCommand("which", cmd);
        return !string.IsNullOrWhiteSpace(output);
    }
    catch { return false; }
}
```
**Issue**: "which" doesn't exist on Windows
**Fix**: Replace with Windows equivalent:
```csharp
static bool CommandExists(string cmd) {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
        try {
            var output = RunCommand("where", cmd);
            return !string.IsNullOrWhiteSpace(output);
        }
        catch { return false; }
    }
    // ... Linux path
}
```

---

## File: nudge-tray.cs

### Lines 1: Shell shebang
```csharp
#!/usr/bin/env dotnet run
```
**Issue**: Unix-specific, though ignored on Windows
**Fix**: Can remove or keep (harmless)

---

### Lines 114-308: ShowSnapshotNotification() and related methods
**Problem**: All notification methods are Linux-specific
1. Lines 186-283: `ShowDbusNotification()` - Uses D-Bus
2. Lines 145-184: `ShowKDialogNotification()` - Uses kdialog (KDE only)
3. Lines 286-308: `ShowFallbackNotification()` - Uses notify-send

**Fix**: Add Windows notification support:
```csharp
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
    ShowWindowsNotification();
} else {
    ShowDbusNotification();
}

private static void ShowWindowsNotification() {
    // Option 1: Toast Notifications (Windows 10+)
    // Option 2: MessageBox fallback
    // Option 3: Use Windows Notifications library
}
```

---

### Lines 153: kdialog command
```csharp
FileName = "kdialog",
Arguments = "--title \"Nudge - Productivity Check\" " +
           "--yesno \"Were you productive...\"",
```
**Issue**: kdialog is KDE-specific, Windows has no equivalent
**Fix**: Will be replaced by Windows notification method

---

### Lines 295: notify-send command
```csharp
FileName = "notify-send",
Arguments = "-u critical -t 60000 \"Nudge - Productivity Check\" ...",
```
**Issue**: notify-send is Linux daemon, doesn't exist on Windows
**Fix**: Will be replaced by Windows notification method

---

## File: nudge-notify.cs

**Status**: No platform-specific code identified
- Uses UDP sockets (cross-platform)
- Uses standard .NET I/O
- Should work on Windows without changes

---

## File: build.sh

**Issue**: Entire file is Bash script (Unix-only)
- OS detection via `/etc/` files (Lines 68-79)
- Linux package managers (Lines 88-204)
- No Windows support

**Fix**: Create `build.ps1` (PowerShell) or `build.cmd` (Batch) equivalent

### Specific problematic sections:

Lines 68-79: OS Detection
```bash
detect_os() {
    if [ -f /etc/arch-release ]; then echo "arch"
    elif [ -f /etc/debian_version ]; then echo "debian"
    elif [ -f /etc/fedora-release ]; then echo "fedora"
    elif [[ "$OSTYPE" == "darwin"* ]]; then echo "macos"
    else echo "unknown"
    fi
}
```
**Issue**: No Windows detection
**Fix**: Add Windows detection using PowerShell

Lines 88-204: install_dotnet(), install_python_deps(), install_runtime_deps()
**Issue**: Linux package manager commands only
**Fix**: Add Windows equivalents (choco, winget, or manual download)

---

## File: nudge-tray.csproj

### Line 397 (in generated file): Tmds.DBus.Protocol dependency
```xml
<PackageReference Include="Tmds.DBus.Protocol" Version="0.21.0" />
```
**Issue**: Linux-only package, will prevent Windows build
**Fix**: Make conditional:
```xml
<PackageReference Include="Tmds.DBus.Protocol" Version="0.21.0" Condition="$([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform($([System.Runtime.InteropServices.OSPlatform]::Linux)))" />
```

---

## Files: train_model.py, validate_data.py

**Status**: Need testing on Windows
- Likely issues: hardcoded paths, subprocess calls to Unix commands
- Action: Test and fix any hardcoded paths like `/tmp/HARVEST.CSV`

---

## Summary Table

| Issue | File | Lines | Severity | Type | Fix Type |
|-------|------|-------|----------|------|----------|
| Hardcoded /tmp path | nudge.cs | 79 | HIGH | Path | Use Path.GetTempPath() |
| XDG_SESSION_TYPE check | nudge.cs | 170-176 | HIGH | Env Var | Skip on Windows |
| XDG_CURRENT_DESKTOP check | nudge.cs | 305-309 | HIGH | Env Var | Skip on Windows |
| Compositor detection | nudge.cs | 300-312 | CRITICAL | System | Add Windows case |
| Window detection (Sway) | nudge.cs | 314-344 | CRITICAL | API | Replace with Windows API |
| Window detection (GNOME) | nudge.cs | 407-423 | CRITICAL | API | Replace with Windows API |
| Window detection (KDE) | nudge.cs | 425-444 | CRITICAL | API | Replace with Windows API |
| Idle time (D-Bus) | nudge.cs | 446-541 | CRITICAL | API | Replace with Windows API |
| CommandExists (which) | nudge.cs | 743-754 | HIGH | Command | Replace with Windows equivalent |
| D-Bus Notifications | nudge-tray.cs | 186-283 | HIGH | Notification | Add Windows Toast/MessageBox |
| kdialog Notification | nudge-tray.cs | 145-184 | HIGH | Notification | Add Windows Toast/MessageBox |
| notify-send Notification | nudge-tray.cs | 286-308 | HIGH | Notification | Add Windows Toast/MessageBox |
| Build system (Bash) | build.sh | entire | HIGH | Build | Create build.ps1 |
| Tmds.DBus dependency | nudge-tray.csproj | 397 | MEDIUM | Dependency | Make conditional |

