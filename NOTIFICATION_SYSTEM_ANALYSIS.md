# Nudge Notification System - Comprehensive Analysis

## Summary

The Nudge system is a cross-platform productivity tracker that sends periodic notifications (snapshots) and collects user responses (YES/NO) to train ML models. The notification implementation is currently platform-specific with Windows and Linux implementations.

---

## 1. NOTIFICATION CREATION & DISPLAY

### Entry Point
**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge-tray.cs`
- **Function:** `ShowSnapshotNotification()` (Line 580-612)
- **Trigger:** When nudge.cs sends "SNAPSHOT" message on stdout

### Flow:
1. nudge.cs detects snapshot interval reached or ML trigger
2. nudge.cs prints "SNAPSHOT" to stdout (Line 1164)
3. nudge-tray.cs captures stdout and calls ShowSnapshotNotification()
4. ShowSnapshotNotification() dispatches to platform-specific implementation

**Trigger Detection Code:**
```csharp
// nudge-tray.cs, Lines 547-551
if (e.Data.Trim() == "SNAPSHOT")
{
    ShowSnapshotNotification();
}
```

---

## 2. PLATFORM-SPECIFIC NOTIFICATION UI CODE

### Windows Notifications (Native Toast)

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge-tray.cs`
**Lines:** 620-658

**Key Components:**
- **Initialization:** Lines 229-243 (`InitializeNotifications()`)
- **Display:** Lines 621-658 (`ShowWindowsNotification()`)
- **Handler:** Lines 245-295 (`OnNotificationActivated()`)

**Implementation Details:**
```csharp
// Register notification handler
ToastNotificationManagerCompat.OnActivated += OnNotificationActivated;

// Display notification with action buttons
new ToastContentBuilder()
    .AddText("Nudge - Productivity Check")
    .AddText("Were you productive during the last interval?")
    .AddButton(new ToastButton()
        .SetContent("Yes - Productive")
        .AddArgument("action", "yes")
        .SetBackgroundActivation())
    .AddButton(new ToastButton()
        .SetContent("No - Not Productive")
        .AddArgument("action", "no")
        .SetBackgroundActivation())
    .Show();
```

**Library Used:** `Microsoft.Toolkit.Uwp.Notifications`

### Linux Notifications (Native DBus)

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge-tray.cs`
**Lines:** 662-805

**Key Components:**
- **Primary Method:** Lines 665-781 (`ShowDbusNotification()`)
- **Fallback Method:** Lines 783-805 (`ShowFallbackNotification()`)

**Implementation Details:**
```csharp
// Native DBus notification with resident:true hint
// Lines 695-709: Sets up notification with hints dictionary
var arrayStart = writer.WriteDictionaryStart();
writer.WriteDictionaryEntryStart();
writer.WriteString("urgency");
writer.WriteVariant(VariantValue.Byte(2));
writer.WriteDictionaryEntryStart();
writer.WriteString("resident");
writer.WriteVariant(VariantValue.Bool(true)); // KEY: Makes notification persistent
writer.WriteDictionaryEntryStart();
writer.WriteString("x-kde-appname");
writer.WriteVariant(VariantValue.String("Nudge"));
writer.WriteDictionaryEntryStart();
writer.WriteString("x-kde-eventId");
writer.WriteVariant(VariantValue.String("productivity-check"));
writer.WriteDictionaryEnd(arrayStart);

// Action buttons (Lines 692-693)
writer.WriteArray(new string[] { "yes", "Yes - Productive", "no", "No - Not Productive" });
```

**Library Used:** `Tmds.DBus.Protocol`

**Fallback Method:**
```csharp
// Lines 787-799
notify-send -u critical -t 60000 "Nudge - Productivity Check" \
  "Were you productive? Use the tray menu to respond"
```

---

## 3. RESPONSE HANDLING (YES/NO)

### UDP Communication Architecture

**Response Port:** 45001 (constant defined in both files)

**Files Involved:**
1. **nudge-notify.cs** - Client that sends responses
2. **nudge.cs** - Server that listens for responses
3. **nudge-tray.cs** - Bridges notifications to response sending

### nudge-notify.cs - Response Sender

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge-notify.cs`
**Key Function:** `SendResponse()` (Lines 110-162)

```csharp
static int SendResponse(string response)
{
    try
    {
        Info($"Sending response: {FormatResponse(response)}");
        Info($"Target: {Color.DIM}{HOST}:{PORT}{Color.RESET}");

        using var client = new UdpClient();
        client.Client.SendTimeout = TIMEOUT_MS;
        client.Client.ReceiveTimeout = TIMEOUT_MS;

        byte[] data = Encoding.UTF8.GetBytes(response);  // "YES" or "NO"
        int sent = client.Send(data, data.Length, HOST, PORT);

        if (sent == data.Length)
        {
            Success("✓ Response sent successfully");
            return 0;
        }
    }
    catch (SocketException ex) { /* error handling */ }
}
```

**Response Validation:** Lines 90-100
- Accepts: "YES" or "NO" (case-insensitive)
- Rejects: Any other input
- Port: 45001
- Host: 127.0.0.1 (localhost only)

### nudge.cs - Response Receiver

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge.cs`
**Key Function:** `RunUDPListener()` (Lines 1221-1279)

```csharp
static void RunUDPListener()
{
    UdpClient listener = new UdpClient(UDP_PORT);  // Port 45001

    while (true)
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        byte[] data = listener.Receive(ref remote);
        string message = Encoding.UTF8.GetString(data).Trim().ToUpper();

        if (!_waitingForResponse)
        {
            Dim($"  Ignoring '{message}' (not waiting for response)");
            continue;
        }

        // Switch on response type
        switch (message)
        {
            case "YES":
                Info($"  Received: {Color.BGREEN}YES{Color.RESET} (productive)");
                SaveSnapshot(app, idle, attention, productive: true);
                break;

            case "NO":
                Info($"  Received: {Color.YELLOW}NO{Color.RESET} (not productive)");
                SaveSnapshot(app, idle, attention, productive: false);
                break;
        }
    }
}
```

**Listener Startup:** Lines 1212-1219
- Started on background thread during initialization
- Listens continuously on port 45001
- Port conflict detection with error handling (Lines 1264-1268)

### nudge-tray.cs - Notification Response Bridge

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge-tray.cs`

**Windows Bridge:** Lines 245-295 (`OnNotificationActivated()`)
```csharp
static void OnNotificationActivated(ToastNotificationActivatedEventArgsCompat e)
{
    var args = ToastArguments.Parse(e.Argument);

    if (args.Contains("action"))
    {
        var action = args["action"];

        if (action == "yes")
        {
            SendResponse(true);
        }
        else if (action == "no")
        {
            SendResponse(false);
        }
    }
}
```

**Linux Bridge:** Lines 740-765 (DBus listener)
```csharp
(Exception? ex, (uint id, string actionKey) signal, object? readerState, object? handlerState) =>
{
    if (signal.id == notificationId)
    {
        if (signal.actionKey == "yes")
        {
            Console.WriteLine("User responded: YES (productive)");
            SendResponse(true);
        }
        else if (signal.actionKey == "no")
        {
            Console.WriteLine("User responded: NO (not productive)");
            SendResponse(false);
        }

        cancellationSource.Cancel();
    }
}
```

**Send Response Function:** Lines 811-826
```csharp
public static void SendResponse(bool productive)
{
    try
    {
        using var udp = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Loopback, UDP_PORT);
        var message = productive ? "YES" : "NO";
        var bytes = Encoding.UTF8.GetBytes(message);
        udp.Send(bytes, bytes.Length, endpoint);
        Console.WriteLine($"✓ Sent response: {message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Failed to send response: {ex.Message}");
    }
}
```

### Response Timeout Mechanism

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge.cs`
**Lines:** 1166-1179

```csharp
_waitingForResponse = true;

// Cancel previous timeout timer if still running
_responseTimer?.Dispose();

// Create timeout timer (reusable, no thread leak)
_responseTimer = new System.Threading.Timer(_ =>
{
    if (_waitingForResponse)
    {
        Warning("⏱  Timeout - no response received");
        _waitingForResponse = false;
    }
}, null, RESPONSE_TIMEOUT_MS, Timeout.Infinite);  // 60 seconds (Line 118)
```

---

## 4. PLATFORM-SPECIFIC NOTIFICATION CODE

### Abstract Platform Service

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge.cs`
**Lines:** 87-92

```csharp
interface IPlatformService
{
    string GetForegroundApp();
    int GetIdleTime();
    string PlatformName { get; }
}
```

### Windows Platform Service

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge.cs`
**Lines:** 185-248

**Key Features:**
- Uses Windows API P/Invoke calls
- P/Invoke declarations: Lines 162-179
  - `GetForegroundWindow()` - Get active window handle
  - `GetWindowText()` - Get window title
  - `GetLastInputInfo()` - Get idle time
  - `GetTickCount()` - System tick count

**Methods:**
- `GetForegroundApp()` (Lines 194-221) - Returns window title
- `GetIdleTime()` (Lines 223-247) - Returns idle milliseconds
- Includes 500ms caching for app changes, 100ms for idle time

### Linux Platform Service

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge.cs`
**Lines:** 250-734

**Key Features:**
- Supports multiple compositors/desktop environments
- Uses SearchValues<T> optimization for process detection (.NET 9)
- Performance optimizations with Span<T> and CompositeFormat

**Supported Desktop Environments:**
```csharp
DetectCompositor() returns:
- "sway"      - via swaymsg command
- "gnome"     - via XDG_CURRENT_DESKTOP
- "kde"       - via XDG_CURRENT_DESKTOP
- "cinnamon"  - via XDG_CURRENT_DESKTOP or pgrep
```

**Methods by Desktop:**
- **Sway:** `GetSwayFocusedApp()` (Lines 374-385)
- **GNOME:** `GetGnomeFocusedApp()` (Lines 387-402)
- **KDE:** `GetKDEFocusedApp()` (Lines 404-450)
- **Cinnamon:** `GetX11FocusedApp()` (Lines 452-468)
- **Process-based (KDE Wayland):** `DetectActiveProcessKDE()` (Lines 556-622)

**Idle Time Methods:**
- `GetFreedesktopIdleTime()` (Lines 471-511) - qdbus/gdbus
- `GetGnomeIdleTime()` (Lines 513-536) - GNOME-specific
- `GetX11IdleTime()` (Lines 538-554) - xprintidle utility

### Tray Icon Display

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge-tray.cs`

**Windows Tray Icon:** Lines 216-227
```csharp
static void CreateTrayIcon()
{
    _trayIcon = new NotifyIcon
    {
        Icon = CreateSimpleIcon(),
        Visible = true,
        Text = "Nudge Productivity Tracker",
        ContextMenuStrip = CreateContextMenu()
    };
}
```

**Linux Tray Icon (Avalonia):** Lines 324-347
```csharp
static void CreateTrayIcon()
{
    _trayIcon = new TrayIcon
    {
        Icon = CreateAvaloniaIcon(),
        IsVisible = true,
        ToolTipText = "Nudge Productivity Tracker",
        Menu = CreateAvaloniaMenu()
    };
}
```

**Tray Icon Status Menu:** Lines 180-193
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

---

## 5. CONFIGURATION & SETTINGS SYSTEM

### Current Configuration (Minimal)

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge.cs`
**Lines:** 94-107

```csharp
static class PlatformConfig
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static string CsvPath => IsWindows
        ? Path.Combine(Path.GetTempPath(), "HARVEST.CSV")
        : "/tmp/HARVEST.CSV";

    public static string WhichCommand => IsWindows ? "where" : "which";

    public static string PythonCommand => IsWindows ? "python" : "python3";
}
```

### Command-Line Configuration

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge.cs`
**Lines:** 797-838

**Available Options:**
- `--help, -h` - Show help
- `--version, -v` - Show version
- `--interval N` - Snapshot interval in minutes (default: 5, random 5-10)
- `--ml` - Enable ML-powered adaptive notifications
- `--force-model` - Force trained model even if below 100 sample threshold
- `[csv-path]` - Custom CSV output path

**Example:**
```bash
nudge --interval 2 --ml /data/harvest.csv
```

### Configurable Parameters

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge.cs`

| Parameter | Lines | Type | Default | Purpose |
|-----------|-------|------|---------|---------|
| `SNAPSHOT_INTERVAL_MS` | 120 | int | 5-10 min (random) | Time between snapshots |
| `_customInterval` | 121 | bool | false | Track custom interval |
| `RESPONSE_TIMEOUT_MS` | 118 | const | 60,000ms | Response wait time |
| `UDP_PORT` | 117 | const | 45001 | Response listener port |
| `ML_CONFIDENCE_THRESHOLD` | 125 | const | 0.98 | ML decision threshold |
| `MIN_SAMPLES_THRESHOLD` | 126 | const | 100 | Min ML training samples |

### CSV Data Storage

**File:** `/home/user/Nudge/NudgeCrossPlatform/nudge.cs`
**Lines:** 1110-1140

**CSV Header:** Line 1127
```csv
foreground_app,idle_time,time_last_request,productive
```

**CSV Row Format:** Line 1189
```csv
<app_hash>,<idle_ms>,<attention_ms>,<productive_bool>
```

**Path Configuration:**
- **Windows:** `Path.Combine(Path.GetTempPath(), "HARVEST.CSV")`
- **Linux:** `/tmp/HARVEST.CSV`
- **Custom:** Via command-line argument

---

## 6. NOTIFICATION POSITION CONFIGURATION

### Current State

**IMPORTANT:** There is **NO explicit notification position configuration system** currently implemented in the codebase.

### Why Positions are Not Configurable Currently

1. **Windows:** Uses native Windows Toast notifications (ToastContentBuilder)
   - Position is controlled by Windows 10+ notification system
   - Not customizable from application code
   - Windows manages positioning automatically

2. **Linux:** Uses native DBus notifications
   - Position is controlled by the desktop environment's notification daemon
   - Not customizable from application code
   - Each DE (GNOME, KDE, Cinnamon) has its own position settings

3. **Tray Icon Menu:** Has no position configuration
   - Positioned relative to taskbar/system tray
   - Controlled by OS

### Opportunity for Custom Implementation

To add notification position configuration, you would need to:

1. **Replace native notifications with custom windows**
   - Windows: Create custom WinForms/WPF window instead of Toast
   - Linux: Create custom Avalonia window instead of DBus notification

2. **Store position settings in configuration file**
   - Suggested: JSON file in `%APPDATA%/Nudge/` (Windows) or `~/.config/nudge/` (Linux)
   - Format:
     ```json
     {
       "notification": {
         "position": "top-right",
         "x": 100,
         "y": 100,
         "width": 400,
         "height": 150
       }
     }
     ```

3. **Add configuration UI or CLI arguments**
   - Command-line: `--notification-position top-right`
   - Configuration file: Manual JSON editing
   - GUI settings panel in tray icon menu

---

## KEY FILES SUMMARY

| File | Purpose | Key Lines |
|------|---------|-----------|
| **nudge.cs** | Core tracker: snapshot timing, response listening, data collection | 797-884 (config), 1212-1279 (UDP listener), 1142-1180 (snapshot) |
| **nudge-notify.cs** | CLI tool to send YES/NO responses | 110-162 (send), 80-100 (validation) |
| **nudge-tray.cs** | System tray GUI and notifications | 580-612 (display), 621-658 (Windows), 665-781 (Linux) |
| **PlatformConfig** | Platform detection and paths | nudge.cs:94-107, nudge-tray.cs:41-54 |

---

## RESPONSE FLOW DIAGRAM

```
User Snapshot Request:
  nudge.cs: TakeSnapshot() [Line 1142]
    └─> Prints "SNAPSHOT" to stdout [Line 1164]
    └─> Starts response timeout [Line 1172-1179]
    └─> Sets _waitingForResponse = true

nudge-tray.cs: Detects SNAPSHOT output [Line 548]
    └─> Calls ShowSnapshotNotification() [Line 550]

ShowSnapshotNotification():
    ├─ Windows: ShowWindowsNotification() [Line 589]
    │    └─> ToastContentBuilder.Show() [Line 641]
    │    └─> Registers OnNotificationActivated() handler [Line 235]
    │
    └─ Linux: ShowDbusNotification() [Line 598]
         └─> Sends DBus Notify method call [Line 716-719]
         └─> Listens for ActionInvoked signal [Line 733-770]

User Clicks Response (YES or NO):
    ├─ Windows: OnNotificationActivated() [Line 245]
    │    └─> Parses action argument [Line 252]
    │    └─> Calls SendResponse(true/false) [Line 263/278]
    │
    └─ Linux: Signal handler [Line 740-765]
         └─> Checks actionKey = "yes" or "no"
         └─> Calls SendResponse(true/false) [Line 755/760]

nudge-tray.cs SendResponse() [Line 811]:
    └─> Creates UdpClient
    └─> Sends "YES" or "NO" to 127.0.0.1:45001 [Line 818]

nudge.cs RunUDPListener() [Line 1221]:
    └─> Receives "YES" or "NO" [Line 1231-1232]
    └─> Validates _waitingForResponse [Line 1234]
    └─> Calls SaveSnapshot(productive: true/false) [Line 1249/1254]
    └─> Sets _waitingForResponse = false [Line 1204]

SaveSnapshot() [Line 1182]:
    └─> Writes to CSV file [Line 1189]
    └─> Displays result with color coding [Line 1195]
    └─> Cancels response timeout [Line 1204]
```

---

## CONSTANTS & MAGIC NUMBERS

| Constant | File | Line | Value | Purpose |
|----------|------|------|-------|---------|
| `UDP_PORT` | nudge.cs | 117 | 45001 | Response listening port |
| `RESPONSE_TIMEOUT_MS` | nudge.cs | 118 | 60000 | Response wait before timeout |
| `SNAPSHOT_INTERVAL_MS` | nudge.cs | 120 | 5-10 min | Interval between snapshots |
| `CYCLE_MS` | nudge.cs | 116 | 1000 | Main loop cycle time |
| `ML_CONFIDENCE_THRESHOLD` | nudge.cs | 125 | 0.98 | 98% confidence required |
| `MIN_SAMPLES_THRESHOLD` | nudge.cs | 126 | 100 | Min samples for ML model |
| `ML_HOST` | nudge.cs | 127 | 127.0.0.1 | ML inference server |
| `ML_PORT` | nudge.cs | 128 | 45002 | ML inference port |
| `HOST` | nudge-notify.cs | 34 | 127.0.0.1 | UDP target host |
| `PORT` | nudge-notify.cs | 35 | 45001 | UDP target port |
| `TIMEOUT_MS` | nudge-notify.cs | 36 | 5000 | UDP timeout |

