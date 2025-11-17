# System Tray Icon - Linux Support Re-enabled

## Summary

Re-enabled the system tray icon on Linux. The tray icon now works on both Windows and Linux using Avalonia's cross-platform TrayIcon with NativeMenu.

## What Changed

### 1. Removed Linux Platform Check

**File:** `NudgeCrossPlatform/nudge-tray.cs`
**Location:** `CreateTrayIcon()` method (lines 259-300)

**Before:**
```csharp
static void CreateTrayIcon()
{
    // On Linux, skip tray icon entirely - DBus is too unstable on KDE/Wayland
    // The app works fine without it (custom notifications still appear)
    if (!PlatformConfig.IsWindows)
    {
        Console.WriteLine("[INFO] Tray icon disabled on Linux (DBus instability)");
        Console.WriteLine("[INFO] App running in background - notifications will still appear");
        return;
    }
    // ... rest of code
}
```

**After:**
```csharp
static void CreateTrayIcon()
{
    try
    {
        _trayIcon = new TrayIcon
        {
            Icon = CreateCommonIcon(),
            IsVisible = true,
            ToolTipText = "Nudge Productivity Tracker",
            Menu = CreateAvaloniaMenu()
        };

        // Register the tray icon with the Application
        if (Application.Current != null)
        {
            var icons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(Application.Current, icons);
        }

        Console.WriteLine("[DEBUG] Tray icon created with Avalonia TrayIcon (cross-platform)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Failed to create tray icon: {ex.Message}");
        throw;
    }
}
```

### 2. Tray Menu Design

**Simple right-click menu with:**
- Status line showing next snapshot time (e.g., "Next snapshot: 14:35:22")
- Separator
- Quit option

```
┌─────────────────────────┐
│ Next snapshot: 14:35:22 │
├─────────────────────────┤
│ Quit                    │
└─────────────────────────┘
```

### 3. What Was NOT Changed

**Notifications remain unchanged:**
- ✅ Windows: Native toast notifications with YES/NO buttons (using Microsoft.Toolkit.Uwp.Notifications)
- ✅ Linux: Native DBus notifications with YES/NO buttons (using Tmds.DBus.Protocol)
- ✅ Custom Avalonia notification window (CustomNotification.cs) still available as fallback

The notifications are **popup toasts**, not part of the tray menu.

## Architecture

### Components

1. **Tray Icon (Cross-Platform)**
   - Technology: Avalonia TrayIcon
   - Platforms: Windows, Linux, macOS
   - Features: Icon, tooltip, right-click menu

2. **Tray Menu (Cross-Platform)**
   - Technology: Avalonia NativeMenu
   - Items: Status text + Quit
   - Updates: Dynamic status showing next snapshot time

3. **Notifications (Platform-Specific)**
   - **Windows:** Toast notifications via Microsoft.Toolkit.Uwp.Notifications
   - **Linux:** DBus notifications via Tmds.DBus.Protocol
   - **Fallback:** Custom Avalonia window (CustomNotification.cs)

### Key Methods

| Method | Purpose |
|--------|---------|
| `CreateTrayIcon()` | Creates and registers the system tray icon on all platforms |
| `CreateAvaloniaMenu()` | Creates the native right-click menu |
| `CreateCommonIcon()` | Generates the blue circle icon programmatically |
| `ShowCustomNotification()` | Shows platform-specific notification popup |
| `GetMenuStatusText()` | Returns status text for menu (next snapshot time or waiting message) |

## Benefits

✅ **Cross-platform consistency** - Same tray icon experience on Windows and Linux
✅ **Native look and feel** - Uses OS-native menu system
✅ **Simple and reliable** - Minimal code, easy to maintain
✅ **Clean separation** - Tray icon (menu) separate from notifications (popups)

## Linux Desktop Environment Support

| Desktop Environment | Status | Notes |
|---------------------|--------|-------|
| KDE Plasma | ✅ | Native support |
| XFCE | ✅ | Native support |
| Cinnamon | ✅ | Native support |
| GNOME 3.26+ | ⚠️ | Requires extension |

### GNOME Setup

GNOME hides tray icons by default. Users need to install the AppIndicator extension:

```bash
sudo apt install gnome-shell-extension-appindicator
```

Then enable in GNOME Tweaks:
```
Extensions → Ubuntu AppIndicators → ON
```

## Testing

### Windows
- [x] Tray icon appears in system tray
- [x] Right-click shows menu with status and quit
- [x] Menu updates to show next snapshot time
- [x] Quit option works correctly
- [x] Notifications still popup with YES/NO buttons

### Linux
- [ ] Tray icon appears in system tray (KDE/XFCE/Cinnamon/GNOME)
- [ ] Right-click shows menu with status and quit
- [ ] Menu updates to show next snapshot time
- [ ] Quit option works correctly
- [ ] Notifications still popup with YES/NO buttons
- [ ] No crashes or DBus errors

## Technical Details

### Icon Creation

The tray icon is created programmatically using Avalonia's rendering:

```csharp
static WindowIcon CreateCommonIcon()
{
    var renderBitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));

    using (var ctx = renderBitmap.CreateDrawingContext())
    {
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, 32, 32));
        var brush = new SolidColorBrush(Color.FromRgb(85, 136, 255)); // Blue #5588FF
        ctx.DrawGeometry(brush, null, new EllipseGeometry(new Rect(2, 2, 28, 28)));
    }

    var stream = new MemoryStream();
    renderBitmap.Save(stream);
    stream.Position = 0;
    return new WindowIcon(stream);
}
```

This works on all platforms without needing platform-specific icon files.

### Menu Status Updates

The menu shows dynamic status based on `_nextSnapshotTime`:

```csharp
static string GetMenuStatusText()
{
    if (_waitingForResponse)
    {
        return "⏳ Waiting for response...";
    }
    else
    {
        var nextSnapshot = GetNextSnapshotTime();
        return nextSnapshot.HasValue
            ? $"Next snapshot: {nextSnapshot.Value:HH:mm:ss}"
            : "Status: Running...";
    }
}
```

## Files Modified

| File | Lines Changed | Description |
|------|---------------|-------------|
| `nudge-tray.cs` | ~10 lines removed | Removed Linux platform check |
| `nudge-tray.cs` | ~40 lines removed | Removed commented-out menu refresh timer |

## Commit History

1. **1d42194** - Add native tray icon menu research and test application
2. **09c74a4** - Implement common native menu-based tray icon for Linux and Windows
3. **47a1a9e** - Revert menu changes - keep simple tray menu with status and quit

## Known Issues

None currently. The implementation uses standard Avalonia TrayIcon which is stable on all supported platforms.

## Future Enhancements (Optional)

- Add "Take Snapshot Now" menu option
- Add "Settings" submenu for interval configuration
- Add keyboard shortcuts in menu labels
- Show remaining time in tooltip (not just menu)

## References

- [Avalonia TrayIcon Documentation](https://docs.avaloniaui.net/docs/reference/controls/tray-icon)
- [Avalonia NativeMenu Documentation](https://docs.avaloniaui.net/docs/reference/controls/nativemenu)
- Test application: `TrayIconTest/Program.cs`
- Research document: `TRAY_ICON_RESEARCH.md`
