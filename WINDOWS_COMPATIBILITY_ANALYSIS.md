# Nudge Codebase - Windows Compatibility Analysis

## 1. PROJECT OVERVIEW

### Project Type
- **Language**: C# (.NET 9.0)
- **Framework**: 
  - .NET 9.0 cross-platform runtime
  - Avalonia 11.2.2 (cross-platform UI framework)
  - Tmds.DBus.Protocol 0.21.0 (D-Bus communication)
- **Purpose**: ML-powered productivity tracker that monitors foreground applications and user activity, collects training data, and aims to nudge users back to productive tasks
- **Architecture**: Monolithic single-file designs (Jon Blow style - no over-engineering)
- **Components**:
  - **nudge.cs**: Main tracker process (~857 lines)
  - **nudge-notify.cs**: CLI response tool (~212 lines)
  - **nudge-tray.cs**: System tray GUI (~482 lines)
  - **train_model.py**: ML model training script
  - **validate_data.py**: Data validation script

---

## 2. PLATFORM-SPECIFIC CODE ANALYSIS

### Critical Linux/Wayland Dependencies

#### 2.1 Environment Variables (Linux/X11 Specific)
**File**: nudge.cs (lines 170, 305)

```csharp
// Line 170: Check for Wayland session
var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
if (sessionType != "wayland") { ... }

// Line 305: Detect desktop environment
var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
if (desktop?.Contains("GNOME") == true) { ... }
if (desktop?.Contains("KDE") == true) { ... }
```

**Windows Impact**: 
- XDG_SESSION_TYPE doesn't exist on Windows
- XDG_CURRENT_DESKTOP is Linux/Unix specific
- Will fail environment validation on Windows

---

#### 2.2 Foreground Window Detection (Compositor-Specific)

**File**: nudge.cs (lines 314-444)

Three separate implementations for different Linux desktop environments:

1. **Sway (Wayland compositor)**
   ```csharp
   static string GetSwayFocusedApp() {
       var json = RunCommand("swaymsg", "-t get_tree");
       return ExtractFocusedAppFromSwayTree(json);
   }
   ```
   - Calls external `swaymsg` command
   - Parses JSON output from Sway IPC
   - **Windows Equivalent Needed**: Windows API (GetForegroundWindow, GetWindowTextA)

2. **GNOME (GTK on Wayland/X11)**
   ```csharp
   static string GetGnomeFocusedApp() {
       var output = RunCommand("gdbus", "call --session --dest org.gnome.Shell " +
           "--object-path /org/gnome/Shell " +
           "--method org.gnome.Shell.Eval " +
           "\"global.display.focus_window.get_wm_class()\"");
       return ExtractQuotedString(output);
   }
   ```
   - Uses D-Bus to communicate with GNOME Shell
   - **Windows Equivalent Needed**: Windows API (EnumWindows, GetForegroundWindow)

3. **KDE/Plasma (Qt on Wayland/X11)**
   ```csharp
   static string GetKDEFocusedApp() {
       // Tries xdotool first
       var windowName = RunCommand("xdotool", 
           "getactivewindow getwindowname");
       // Falls back to generic "kde-wayland-window"
       return "kde-wayland-window";
   }
   ```
   - Uses xdotool (X11 specific)
   - **Windows Equivalent Needed**: Windows API

---

#### 2.3 Idle Time Detection (D-Bus Specific)

**File**: nudge.cs (lines 446-541)

```csharp
static int GetFreedesktopIdleTime() {
    // Method 1: org.freedesktop.ScreenSaver via qdbus
    var output = RunCommand("qdbus",
        "org.freedesktop.ScreenSaver " +
        "/org/freedesktop/ScreenSaver " +
        "org.freedesktop.ScreenSaver.GetSessionIdleTime");
    
    // Method 2: Fallback to gdbus for GNOME
    output = RunCommand("gdbus",
        "call --session " +
        "--dest org.freedesktop.ScreenSaver " +
        "--object-path /org/freedesktop/ScreenSaver " +
        "--method org.freedesktop.ScreenSaver.GetSessionIdleTime");
    
    // Method 3: GNOME-specific
    output = RunCommand("gdbus",
        "call --session " +
        "--dest org.gnome.Mutter.IdleMonitor " +
        "--object-path /org/gnome/Mutter/IdleMonitor/Core " +
        "--method org.gnome.Mutter.IdleMonitor.GetIdletime");
}
```

**Windows Impact**:
- `qdbus`, `gdbus` don't exist on Windows
- D-Bus is Linux/Unix specific
- **Windows Equivalent Needed**: Windows API (GetLastInputInfo)

---

#### 2.4 External Command Execution (Unix-Specific Tools)

**File**: nudge.cs (lines 722-754)

**Commands that will fail on Windows**:
- `which` (line 747) - Unix command to find executables
  - Windows Equivalent: `where` command or PATH searching
- `swaymsg` - Sway-specific
- `gdbus` - D-Bus command
- `qdbus` - Qt D-Bus command
- `xdotool` - X11 specific
- `notify-send` - Linux notification daemon

```csharp
static bool CommandExists(string cmd) {
    try {
        var output = RunCommand("which", cmd);
        return !string.IsNullOrWhiteSpace(output);
    }
    catch { return false; }
}
```

---

#### 2.5 File Path Hardcoding (Unix Specific)

**File**: nudge.cs (line 79)

```csharp
static string _csvPath = "/tmp/HARVEST.CSV";
```

**Windows Impact**:
- `/tmp` doesn't exist on Windows
- Should use `Path.GetTempPath()` or environment variables like `%TEMP%` or `%APPDATA%`

---

#### 2.6 Shell Script Usage (Unix-Specific Shebang)

**Files**: 
- nudge.cs (line 1): `#!/usr/bin/env dotnet run`
- nudge-notify.cs (line 1): `#!/usr/bin/env dotnet run`
- build.sh: Complex bash script with Linux-specific commands

**Windows Impact**:
- Shebang (#!) ignored on Windows
- Build script uses bash syntax (not available on Windows without WSL/Git Bash)
- Must be converted to PowerShell or batch files

---

#### 2.7 Notification System (Linux-Specific)

**File**: nudge-tray.cs (lines 114-308)

Multiple fallback notification methods, all Linux-specific:

```csharp
// Method 1: D-Bus Notifications (lines 186-283)
private static async void ShowDbusNotification() {
    using var connection = new Connection(Address.Session!);
    await connection.ConnectAsync();
    // Calls org.freedesktop.Notifications D-Bus service
}

// Method 2: kdialog (KDE) - (lines 145-184)
FileName = "kdialog",
Arguments = "--title \"Nudge - Productivity Check\" " +
           "--yesno \"Were you productive...\" " +

// Method 3: notify-send (last resort) - (lines 286-308)
FileName = "notify-send",
Arguments = "-u critical -t 60000 \"Nudge - Productivity Check\" ..."
```

**Windows Equivalent Needed**:
- Windows 10+ has Toast Notifications API
- Can fall back to message boxes (System.Windows.Forms.MessageBox)
- Or use Windows Notifications library

---

## 3. CONFIGURATION FILES WITH PLATFORM-SPECIFIC SETTINGS

### 3.1 Build Script - build.sh (Major Issue)

**Path**: /home/user/Nudge/NudgeCrossPlatform/build.sh

**Platform-Specific Elements**:
- Bash syntax (not portable to Windows)
- OS detection logic (lines 68-79):
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
- Package manager calls (pacman, apt, dnf, brew)
- File operations with Unix paths

**What's Missing**:
- Windows detection
- Windows package management (if applicable)
- Windows-specific dependency installation
- No PowerShell equivalent

### 3.2 Runtime Configuration Files

**Files**:
- nudge-tray.runtimeconfig.json
- nudge.runtimeconfig.json
- nudge-notify.runtimeconfig.json

These are standard .NET configuration files and ARE platform-independent.

### 3.3 Project Files (.csproj)

**Files**:
- nudge.csproj
- nudge-notify.csproj
- nudge-tray.csproj

**Analysis**:
- Target framework: `net9.0` (cross-platform)
- NuGet packages ARE cross-platform:
  - Avalonia 11.2.2 (supports Windows via Avalonia.Win32)
  - Tmds.DBus.Protocol 0.21.0 (primarily Linux, but doesn't block Windows build)
- Dependency file shows Windows support exists in Avalonia:
  ```json
  "Avalonia.Win32": "11.2.2",
  "SkiaSharp.NativeAssets.Win32": "2.88.9",
  "HarfBuzzSharp.NativeAssets.Win32": "7.3.0.3",
  "Avalonia.Angle.Windows.Natives": "2.1.22045.20230930"
  ```

### 3.4 Missing Windows Runtime Packages

**File**: runtimes/ directory

**Current Structure**:
```
runtimes/
├── linux-arm/
├── linux-arm64/
├── linux-musl-x64/
├── linux-x64/
└── osx/
```

**Missing**:
- `win-x64/` (64-bit Windows)
- `win-x86/` (32-bit Windows)
- `win-arm64/` (ARM Windows)

---

## 4. AREAS REQUIRING CHANGES FOR WINDOWS COMPATIBILITY

### 4.1 CRITICAL ISSUES (Must Fix)

#### A. Window Detection System
**Severity**: CRITICAL  
**Files**: nudge.cs (lines 300-444)

**Current Implementation**: 
- Hard-coded for 3 specific Linux desktop environments
- Uses external commands (swaymsg, gdbus, qdbus, xdotool)

**Windows Solution**:
```csharp
static string GetWindowsFocusedApp() {
    try {
        // Use Windows API to get foreground window
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "unknown";
        
        // Get process name from window handle
        GetWindowThreadProcessId(hwnd, out uint pid);
        Process p = Process.GetProcessById((int)pid);
        return p.ProcessName;
    }
    catch { return "unknown"; }
}

[DllImport("user32.dll")]
static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll")]
static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);
```

#### B. Idle Time Detection
**Severity**: CRITICAL  
**Files**: nudge.cs (lines 446-541)

**Current Implementation**:
- Uses D-Bus services (org.freedesktop.ScreenSaver, org.gnome.Mutter.IdleMonitor)
- Requires external commands

**Windows Solution**:
```csharp
static int GetWindowsIdleTime() {
    try {
        LASTINPUTINFO lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(lii);
        if (GetLastInputInfo(ref lii)) {
            uint idleTime = GetTickCount() - lii.dwTime;
            return (int)idleTime;
        }
        return 0;
    }
    catch { return 0; }
}

[StructLayout(LayoutKind.Sequential)]
struct LASTINPUTINFO {
    public uint cbSize;
    public uint dwTime;
}

[DllImport("user32.dll")]
static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

[DllImport("kernel32.dll")]
static extern uint GetTickCount();
```

#### C. File Path Handling
**Severity**: HIGH  
**Files**: nudge.cs (line 79)

**Current**: 
```csharp
static string _csvPath = "/tmp/HARVEST.CSV";
```

**Windows Solution**:
```csharp
static string _csvPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Nudge",
    "HARVEST.CSV");
```

Or for temporary files:
```csharp
static string _csvPath = Path.Combine(
    Path.GetTempPath(),
    "HARVEST.CSV");
```

---

### 4.2 HIGH PRIORITY ISSUES

#### A. Command Detection
**Severity**: HIGH  
**Files**: nudge.cs (lines 743-754)

**Current**:
```csharp
static bool CommandExists(string cmd) {
    var output = RunCommand("which", cmd);
    return !string.IsNullOrWhiteSpace(output);
}
```

**Windows Solution**:
```csharp
static bool CommandExists(string cmd) {
    try {
        // Try Windows 'where' command
        var output = RunCommand("where", cmd);
        return !string.IsNullOrWhiteSpace(output);
    }
    catch {
        // Fallback: Check if file exists in PATH
        string[] pathDirs = Environment.GetEnvironmentVariable("PATH")
            ?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        
        foreach (var dir in pathDirs) {
            string path = Path.Combine(dir, cmd + ".exe");
            if (File.Exists(path)) return true;
        }
        return false;
    }
}
```

#### B. Environment Variable Validation
**Severity**: HIGH  
**Files**: nudge.cs (lines 163-234)

**Current**:
```csharp
var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
if (sessionType != "wayland") {
    Warning($"Not running on Wayland (detected: {sessionType ?? "none"})");
}
```

**Windows Solution**:
```csharp
// Skip Wayland checks on Windows
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
    return true; // Skip Wayland validation
}

var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
if (sessionType != "wayland") {
    Warning($"Not running on Wayland (detected: {sessionType ?? "none"})");
}
```

#### C. Notification System
**Severity**: HIGH  
**Files**: nudge-tray.cs (lines 114-308)

**Current**: Only D-Bus, kdialog, and notify-send

**Windows Solution**:
```csharp
// Use Windows Toast Notifications
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
    ShowWindowsToastNotification();
} else {
    ShowDbusNotification();  // Keep Linux path
}
```

---

### 4.3 MEDIUM PRIORITY ISSUES

#### A. Build System
**Severity**: MEDIUM  
**Files**: build.sh (entire file)

**Current**: Bash script with Linux-specific commands

**Windows Solution Options**:
1. Create build.ps1 (PowerShell script for Windows)
2. Create build.bat (Batch file for Windows)
3. Use .NET-only build approach (no shell dependencies)
4. Use Python (cross-platform) as build system

**Key Missing**:
- Windows detection in OS detection function
- Windows dependency installation (if needed)
- PowerShell equivalents for all bash operations

#### B. Shebang Handling
**Severity**: MEDIUM  
**Files**: nudge.cs (line 1), nudge-notify.cs (line 1)

**Current**:
```csharp
#!/usr/bin/env dotnet run
```

**Windows Solution**:
- Keep shebang (ignored on Windows)
- Or remove and use separate wrapper scripts
- Build system should already handle this

#### C. Python Scripts (train_model.py, validate_data.py)
**Severity**: MEDIUM

**Current**: Pure Python, should work on Windows

**Potential Issues**:
- May depend on Linux command-line tools
- File paths might be hardcoded

**Solution**: Test on Windows, verify file path handling

---

### 4.4 LOW PRIORITY ISSUES

#### A. Runtime Packages
**Severity**: LOW

**Status**: Can be generated by dotnet publish for Windows

The `.csproj` files will automatically pull Windows-specific native libraries from NuGet (Avalonia.Win32, etc.) during build.

#### B. ANSI Color Codes
**Severity**: LOW  
**Files**: Multiple console output sections

**Current**:
```csharp
static class Color {
    public const string RESET = "\u001b[0m";
    public const string BOLD = "\u001b[1m";
    // ... ANSI escape codes
}
```

**Status**: Windows 10+ supports ANSI codes in console by default.  
**Fallback**: Can be wrapped in Windows-specific handling if needed.

---

## 5. DEPENDENCY ANALYSIS

### NuGet Packages Status

| Package | Version | Windows Support | Status |
|---------|---------|-----------------|--------|
| Avalonia | 11.2.2 | YES (Avalonia.Win32) | Ready |
| Avalonia.Desktop | 11.2.2 | YES | Ready |
| Avalonia.Themes.Fluent | 11.2.2 | YES | Ready |
| Tmds.DBus.Protocol | 0.21.0 | NO (Linux-only) | ISSUE |

**Issue**: Tmds.DBus.Protocol is imported in nudge-tray.csproj but only used for D-Bus notifications. Windows build would fail if this is required.

**Solution**: Make DBus dependency conditional or remove from Windows builds.

---

## 6. SUMMARY TABLE OF CHANGES NEEDED

| Area | Component | Change | Difficulty | Priority |
|------|-----------|--------|------------|----------|
| Window Detection | nudge.cs | Replace Sway/GNOME/KDE detection with Windows API | HIGH | CRITICAL |
| Idle Time | nudge.cs | Replace D-Bus with GetLastInputInfo | HIGH | CRITICAL |
| File Paths | nudge.cs | Replace /tmp with Path.GetTempPath() | LOW | HIGH |
| Command Detection | nudge.cs | Replace 'which' with 'where' | LOW | HIGH |
| Environment Vars | nudge.cs | Add Windows detection logic | LOW | HIGH |
| Notifications | nudge-tray.cs | Add Windows Toast Notifications | MEDIUM | HIGH |
| Build System | build.sh | Create Windows build script | HIGH | MEDIUM |
| D-Bus Dependency | nudge-tray.csproj | Make conditional or extract | LOW | MEDIUM |
| Python Scripts | train_model.py | Test/verify on Windows | MEDIUM | MEDIUM |
| | validate_data.py | Test/verify on Windows | MEDIUM | MEDIUM |

---

## 7. RECOMMENDED IMPLEMENTATION ORDER

1. **Phase 1 (Core Functionality)**
   - Add Windows window detection (Windows API)
   - Add Windows idle time detection (Windows API)
   - Fix file paths (use Path APIs)
   - Add platform detection (RuntimeInformation)

2. **Phase 2 (Build System)**
   - Create build.ps1 for Windows
   - Or refactor to .NET-only build (no shell dependency)
   - Update OS detection in build script

3. **Phase 3 (User Interface)**
   - Add Windows notification support (Toast or MessageBox)
   - Update build.sh to handle Windows notifications properly

4. **Phase 4 (Testing & Refinement)**
   - Test Python scripts on Windows
   - Verify all file paths work on Windows
   - Test all notification methods

---

## 8. CODE STRUCTURE FOR PLATFORM ABSTRACTION

Recommended refactoring approach:

```csharp
// New file: PlatformAbstraction.cs
public interface IPlatformService {
    string GetForegroundApp();
    int GetIdleTime();
    void ShowNotification(string title, string message);
    string GetDefaultCsvPath();
}

// Windows implementation
public class WindowsPlatformService : IPlatformService { }

// Linux implementation  
public class LinuxPlatformService : IPlatformService { }

// In Main:
IPlatformService platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    ? new WindowsPlatformService()
    : new LinuxPlatformService();
```

This keeps current "no abstraction" philosophy while enabling platform-specific code.

---

## CONCLUSION

The Nudge project is **fundamentally a Linux/Wayland-specific application**. Achieving Windows compatibility requires:

1. **Replacing all Linux/Wayland-specific system calls** with Windows API equivalents
2. **Creating Windows-compatible build system** (PowerShell or .NET-only)
3. **Adding platform detection and conditional execution** throughout the codebase
4. **Updating notification system** for Windows compatibility

**Estimated Effort**: 2-4 weeks of development + 1 week testing

**Difficulty Level**: Medium (Windows API requires P/Invoke knowledge, but Avalonia framework handles much of the heavy lifting)

