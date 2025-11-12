# Custom Notification System

## Overview

The Nudge productivity tracker now features a beautiful, modern, cross-platform custom notification system that replaces the native platform notifications.

## Features

### 🎨 Beautiful Design
- Modern, dark-themed UI with rounded corners and subtle shadows
- Gradient-colored buttons for YES (green) and NO (red)
- Pulsing icon animation for attention
- Smooth fade-in slide animation when appearing
- Professional layout with clear visual hierarchy

### ⌨️ Keyboard Shortcuts
The notification responds to global keyboard shortcuts:
- **Ctrl + Shift + Y**: Quick YES (productive) response
- **Ctrl + Shift + N**: Quick NO (not productive) response

The shortcuts are displayed directly under each button for easy reference.

### 🖱️ Drag & Drop
- Click and drag the notification to reposition it anywhere on your screen(s)
- The notification remembers your preferred position across sessions
- Cursor changes to indicate draggability

### 💾 Position Persistence
- Your notification position is automatically saved to a config file
- Survives application restarts
- Config stored in: `~/.config/nudge-notification-config.json` (Linux) or `%APPDATA%/nudge-notification-config.json` (Windows)

### 🖥️ Cross-Platform
- Works on Windows, Linux, and macOS
- Uses Avalonia UI framework for consistent look and feel
- Always stays on top of other windows

## Implementation Details

### Files Added
1. **CustomNotification.cs** - The custom notification window implementation
   - Location: `/home/user/Nudge/NudgeCrossPlatform/CustomNotification.cs`
   - Features: Animation system, drag handling, keyboard shortcuts, position persistence

### Files Modified
1. **nudge-tray.cs** - Integration with main tray application
   - Added `ShowCustomNotification()` method
   - Added Avalonia initialization for Windows
   - Replaced native notifications with custom notification

2. **nudge-tray.csproj** - Project file
   - Added CustomNotification.cs to compilation

3. **build.sh** - Build script
   - Updated to include CustomNotification.cs in generated project file

## Technical Architecture

### Animation System
- Smooth slide-in from right with cubic easing
- Fade-in effect combined with position animation
- Fade-out when dismissed
- Icon pulsing using timer-based opacity animation

### Keyboard Handling
- Implements KeyDown event handler
- Checks for Ctrl+Shift modifier combination
- Responds to Y and N keys
- Prevents event bubbling with `e.Handled = true`

### Drag Functionality
- Uses Avalonia's pointer events (PointerPressed, PointerMoved, PointerReleased)
- Tracks drag state with boolean flag
- Captures pointer during drag operation
- Updates cursor to provide visual feedback

### Position Persistence
- Uses System.Text.Json for serialization
- Stores X, Y coordinates and HasSavedPosition flag
- Loads position on startup
- Falls back to default position (bottom-right with margin) if no saved position exists
- Saves position automatically when drag is released

## Building

The custom notification system is automatically included when you build with:

```bash
./build.sh
```

Or on Windows:
```powershell
.\build.ps1
```

## Usage

Simply run the tray application as usual:
```bash
./nudge-tray
```

Or with a custom interval:
```bash
./nudge-tray --interval 2
```

When a snapshot is triggered, you'll see the beautiful custom notification instead of the native system notification.

## Customization

### Changing Colors
Edit `CustomNotification.cs` and modify:
- Button colors: Line 171 (YES green) and Line 177 (NO red)
- Background color: Line 93 (dark theme)
- Border color: Line 107
- Icon gradient: Lines 133-141

### Changing Animation Speed
Edit the animation parameters:
- Slide-in steps: Line 385 (`int steps = 30`)
- Slide-in delay: Line 386 (`int delayMs = 10`)
- Fade-out steps: Line 419

### Changing Keyboard Shortcuts
Edit the keyboard handling code at lines 258-273 in `CustomNotification.cs`.

## Configuration File Format

```json
{
  "X": 1500,
  "Y": 800,
  "HasSavedPosition": true
}
```

## Troubleshooting

### Notification doesn't appear
- Check console output for error messages
- Ensure Avalonia dependencies are installed
- Try running `./build.sh` again

### Keyboard shortcuts not working
- Ensure the notification window has focus
- Check if another application is capturing the same shortcuts
- Click on the notification to give it focus

### Position not saving
- Check file permissions for the config directory
- Look for error messages in console output
- Manually create the config file with proper permissions

## Future Enhancements

Potential improvements for future versions:
- Sound effects when notification appears
- Customizable themes (light/dark mode)
- Configurable keyboard shortcuts
- Multiple monitor awareness improvements
- Notification history/log
- Animation style preferences
