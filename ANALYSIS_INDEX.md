# Windows Compatibility Analysis - Documentation Index

## Overview

This directory contains a comprehensive analysis of the Nudge codebase's platform-specific code and Windows compatibility requirements.

## Documents (in recommended reading order)

### 1. **PLATFORM_ANALYSIS_SUMMARY.txt** (START HERE)
- Quick overview of the entire project
- Current platform support status (Linux/macOS/Windows)
- List of critical, high, medium, and low priority issues
- Effort estimation for Windows support
- Files requiring changes
- Recommended approaches

**Read this first for a 5-minute overview.**

---

### 2. **PLATFORM_ISSUES_QUICK_REFERENCE.md** (USE FOR CODING)
- Exact line numbers of all platform-specific issues
- Code snippets showing the problem
- Specific fix recommendations for each issue
- Summary table of all issues with severity
- Organized by file (nudge.cs, nudge-tray.cs, build.sh, etc.)

**Reference this while implementing fixes - it has exact line numbers and code samples.**

---

### 3. **WINDOWS_COMPATIBILITY_ANALYSIS.md** (COMPREHENSIVE REFERENCE)
- Detailed project structure analysis
- 8 major platform-specific problem areas with examples
- Configuration file analysis (build script, project files, dependencies)
- 4 severity levels of issues with detailed explanations
- Dependency analysis (NuGet packages)
- Detailed solutions for each issue type
- Implementation order and phased approach
- Code structure recommendations

**Read this for comprehensive technical details and implementation strategies.**

---

## Key Findings Summary

### Project Type
- **Language**: C# (.NET 9.0 cross-platform runtime)
- **UI Framework**: Avalonia 11.2.2 (cross-platform, Windows-ready)
- **Purpose**: ML-powered productivity tracker

### Current Status
- **Linux**: Fully supported (primary platform)
- **macOS**: Partially supported (has OSX runtime binaries)
- **Windows**: NOT SUPPORTED

### Main Issues for Windows

| Category | Issue | Severity |
|----------|-------|----------|
| **Core Functionality** | Window detection (currently Sway/GNOME/KDE only) | CRITICAL |
| **Core Functionality** | Idle time detection (D-Bus only) | CRITICAL |
| **File System** | Hardcoded /tmp/HARVEST.CSV path | HIGH |
| **Commands** | 'which' command for finding executables | HIGH |
| **Environment** | XDG_SESSION_TYPE and XDG_CURRENT_DESKTOP checks | HIGH |
| **Notifications** | D-Bus, kdialog, notify-send (all Linux-specific) | HIGH |
| **Build System** | Bash script with Linux package managers | HIGH |
| **Dependencies** | Tmds.DBus.Protocol (Linux-only) | MEDIUM |

### Estimated Effort
- **Development**: 2-4 weeks
- **Testing**: 1 week
- **Difficulty**: Medium

---

## Critical Changes Required

1. **Window Detection** (nudge.cs)
   - Replace Sway/GNOME/KDE detection with Windows API
   - Add `GetForegroundWindow()` P/Invoke calls

2. **Idle Time Detection** (nudge.cs)
   - Replace D-Bus with `GetLastInputInfo()` Windows API
   - Add P/Invoke marshaling

3. **File Paths** (nudge.cs)
   - Replace `/tmp/HARVEST.CSV` with `Path.GetTempPath()` or `%APPDATA%`

4. **Build System** (build.sh)
   - Create PowerShell equivalent (build.ps1)
   - Add Windows dependency installation

5. **Notifications** (nudge-tray.cs)
   - Replace D-Bus/kdialog/notify-send with Windows Toast Notifications
   - Add MessageBox fallback

6. **Dependencies** (nudge-tray.csproj)
   - Make Tmds.DBus.Protocol conditional (Linux-only)

---

## Platform Detection Pattern

For all fixes, use this pattern:

```csharp
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
    // Windows-specific code
} else {
    // Linux-specific code (current)
}
```

Requires: `using System.Runtime.InteropServices;`

---

## Recommended Implementation Phases

### Phase 1: Core Functionality (1-2 weeks)
- [ ] Add Windows window detection (Windows API P/Invoke)
- [ ] Add Windows idle time detection (Windows API P/Invoke)
- [ ] Fix file paths to be cross-platform
- [ ] Add platform detection to environment validation
- [ ] Update GetIdleTime() to route to Windows/Linux versions
- [ ] Update GetForegroundApp() to route to Windows/Linux versions

### Phase 2: Build System (1 week)
- [ ] Create build.ps1 (PowerShell) for Windows builds
- [ ] Add Windows detection to OS detection logic
- [ ] Add Windows dependency installation (if needed)
- [ ] Make Tmds.DBus.Protocol conditional in .csproj

### Phase 3: User Interface (3-5 days)
- [ ] Implement Windows Toast Notifications (or MessageBox fallback)
- [ ] Add platform detection to notification system
- [ ] Test notification display on Windows 10+

### Phase 4: Testing & Refinement (1 week)
- [ ] Test on Windows 10/11
- [ ] Verify Python scripts work on Windows
- [ ] Cross-platform file path testing
- [ ] Build system testing on Windows

---

## File Change Summary

### Must Modify (Core Functionality)
- **nudge.cs** - Environment validation, window detection, idle time, file paths
- **nudge-tray.cs** - Notification system
- **build.sh** - Create PowerShell equivalent

### Conditional Changes
- **nudge-tray.csproj** - Make Tmds.DBus.Protocol conditional
- **train_model.py** - Test on Windows, fix paths if needed
- **validate_data.py** - Test on Windows, fix paths if needed

### No Changes Needed
- **nudge-notify.cs** - Pure UDP, cross-platform already
- **.json config files** - Platform-independent
- **nudge.csproj, nudge-notify.csproj** - Platform-independent

---

## Testing Checklist

- [ ] Build on Windows 10/11
- [ ] Window detection identifies active application
- [ ] Idle time detection works correctly
- [ ] CSV file creation and writing works
- [ ] Notifications display properly
- [ ] Command-line tools work (nudge, nudge-notify, nudge-tray)
- [ ] System tray icon appears
- [ ] UI renders correctly
- [ ] Python ML training works on Windows
- [ ] Cross-platform data file compatibility

---

## Additional Resources

### Windows API References
- **GetForegroundWindow**: https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow
- **GetWindowThreadProcessId**: https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid
- **GetLastInputInfo**: https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo
- **GetTickCount**: https://docs.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount

### .NET Documentation
- **RuntimeInformation**: https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.runtimeinformation
- **P/Invoke**: https://docs.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke
- **DllImport**: https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.dllimportattribute

### Avalonia Documentation
- **Avalonia for Windows**: https://docs.avaloniaui.net/docs/getting-started

---

## Questions?

Refer to the specific documentation:
- **"What's the overall strategy?"** → PLATFORM_ANALYSIS_SUMMARY.txt
- **"Where exactly is the code I need to fix?"** → PLATFORM_ISSUES_QUICK_REFERENCE.md
- **"How do I implement the fix?"** → WINDOWS_COMPATIBILITY_ANALYSIS.md (Section 4)
- **"What's the detailed technical background?"** → WINDOWS_COMPATIBILITY_ANALYSIS.md (Sections 1-3)

---

Generated: 2024-11-10
Codebase: /home/user/Nudge
Branch: claude/windows-compatibility-011CUzLNLHapmdgr8jVC3DE3
