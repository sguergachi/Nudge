// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Nudge Tray - System Tray GUI for Nudge Productivity Tracker
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// Cross-platform system tray application for Nudge.
// Works with the main `nudge` process via UDP communication.
//
// Usage:
//   nudge-tray [--interval N]
//
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Tmds.DBus.Protocol;

namespace NudgeTray
{
    // Windows API for native context menu
    static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        public static extern bool TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        public static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public const uint MF_STRING = 0x00000000;
        public const uint MF_SEPARATOR = 0x00000800;
        public const uint MF_GRAYED = 0x00000001;
        public const uint TPM_RETURNCMD = 0x0100;
        public const uint TPM_LEFTBUTTON = 0x0000;

        public const uint WM_NULL = 0x0000;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
    }

    class Program
    {
        const int UDP_PORT = 45001;
        const string VERSION = "1.0.1";
        static Process? _nudgeProcess;
        static DateTime? _nextSnapshotTime;
        static int _intervalMinutes;

        [STAThread]
        static void Main(string[] args)
        {
            int interval = 5; // default 5 minutes

            // Parse arguments
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--interval" && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out interval);
                    i++; // Skip the interval value
                }
            }

            // Build Avalonia app
            BuildAvaloniaApp(interval).StartWithClassicDesktopLifetime(args);
        }

        static AppBuilder BuildAvaloniaApp(int interval)
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .AfterSetup(_ =>
                {
                    StartNudge(interval);
                });
        }

        static void StartNudge(int interval)
        {
            _intervalMinutes = interval;
            _nextSnapshotTime = DateTime.Now.AddMinutes(interval);

            try
            {
                // Determine nudge executable name based on platform
                string nudgeExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "nudge.exe"
                    : "./nudge";

                // Start the main nudge process
                _nudgeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = nudgeExe,
                        Arguments = $"--interval {interval}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _nudgeProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[Nudge] {e.Data}");

                        // Detect snapshot requests (exact match only)
                        if (e.Data.Trim() == "SNAPSHOT")
                        {
                            Dispatcher.UIThread.Post(() => ShowSnapshotNotification());
                        }
                    }
                };

                _nudgeProcess.Start();
                _nudgeProcess.BeginOutputReadLine();
                _nudgeProcess.BeginErrorReadLine();

                Console.WriteLine("✓ Nudge process started");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to start nudge: {ex.Message}");
                Environment.Exit(1);
            }
        }

        public static void ShowSnapshotNotification()
        {
            _nextSnapshotTime = DateTime.Now.AddMinutes(_intervalMinutes);
            Console.WriteLine("📸 Snapshot taken! Respond using the notification buttons.");

            // Platform-specific notifications
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ShowWindowsNotification();
                return;
            }

            // Linux: Try native DBus notifications first
            bool success = false;
            try
            {
                ShowDbusNotification();
                Console.WriteLine("✓ Desktop notification sent via DBus");
                success = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ DBus notification failed: {ex.Message}");
            }

            // Fallback to kdialog if notifications don't work
            if (!success && ShowKDialogNotification())
            {
                Console.WriteLine("✓ Dialog shown via kdialog (fallback)");
                return;
            }

            // Last resort: notify-send (no buttons)
            if (!success)
            {
                ShowFallbackNotification();
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // WINDOWS NOTIFICATIONS
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private static void ShowWindowsNotification()
        {
            Console.WriteLine("[DEBUG] ShowWindowsNotification called (native Windows Toast Notification)");

            try
            {
                // Create PowerShell script for Windows Toast notification with action buttons
                var scriptPath = Path.Combine(Path.GetTempPath(), $"nudge-notification-{Guid.NewGuid()}.ps1");
                Console.WriteLine($"[DEBUG] Creating PowerShell script at: {scriptPath}");

                var scriptContent = @"
# Windows Toast Notification with action buttons
try {
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    [Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

    $APP_ID = 'Nudge.ProductivityTracker'

    # Toast template with persistent notification and action buttons
    $template = @""
<toast scenario='reminder' duration='long'>
    <visual>
        <binding template='ToastGeneric'>
            <text>Nudge - Productivity Check</text>
            <text>Were you productive during the last interval?</text>
        </binding>
    </visual>
    <actions>
        <action content='Yes - Productive' arguments='YES' activationType='background'/>
        <action content='No - Not Productive' arguments='NO' activationType='background'/>
    </actions>
    <audio src='ms-winsoundevent:Notification.Default'/>
</toast>
""@

    Write-Output 'Creating XML document...'
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml($template)

    Write-Output 'Creating toast notification...'
    $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($APP_ID)

    Write-Output 'Setting up event handlers...'
    # Listen for activation
    $toast.add_Activated({
        param($sender, $eventArgs)
        $args = $eventArgs.Arguments
        Write-Output ""Button clicked: $args""

        try {
            $udp = New-Object System.Net.Sockets.UdpClient
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($args)
            $sent = $udp.Send($bytes, $bytes.Length, 'localhost', " + UDP_PORT + @")
            $udp.Close()
            Write-Output ""Sent $sent bytes via UDP""
        } catch {
            Write-Error ""UDP send failed: $_""
        }
    })

    $toast.add_Dismissed({
        param($sender, $eventArgs)
        Write-Output ""DISMISSED: $($eventArgs.Reason)""
    })

    $toast.add_Failed({
        param($sender, $eventArgs)
        Write-Error ""TOAST FAILED: $($eventArgs.ErrorCode)""
    })

    Write-Output 'Showing toast notification...'
    $notifier.Show($toast)
    Write-Output 'TOAST_SHOWN'

    # Keep script running to receive events
    Write-Output 'Waiting for user interaction (60 seconds)...'
    Start-Sleep -Seconds 60
    Write-Output 'Timeout reached'
} catch {
    Write-Error ""Exception: $($_.Exception.Message)""
    Write-Error ""Stack: $($_.Exception.StackTrace)""
}
";

                File.WriteAllText(scriptPath, scriptContent);
                Console.WriteLine("[DEBUG] PowerShell toast notification script created successfully");

                // Run PowerShell script in background
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false,  // Show window for debugging
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[PS-OUT] {e.Data}");
                        if (e.Data == "YES" || e.Data == "NO")
                        {
                            Console.WriteLine($"✓ User responded: {e.Data} via Windows toast notification");
                            SendResponse(e.Data == "YES");
                        }
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[PS-ERR] {e.Data}");
                    }
                };

                process.Exited += (s, e) =>
                {
                    Console.WriteLine($"[DEBUG] PowerShell process exited with code: {process.ExitCode}");
                    try { File.Delete(scriptPath); } catch { }
                };

                process.EnableRaisingEvents = true;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Console.WriteLine("[DEBUG] PowerShell process started successfully");
                Console.WriteLine("✓ Native Windows Toast notification script running");
                Console.WriteLine("[DEBUG] Waiting for user interaction (60s timeout)...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Windows toast notification error details: {ex.GetType().Name}");
                Console.WriteLine($"✗ Windows toast notification failed: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[DEBUG] Inner exception: {ex.InnerException.Message}");
                }
                Console.WriteLine("Falling back to tray menu for response");
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // LINUX NOTIFICATIONS (Native Tmds.DBus.Protocol with resident:true hint)
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private static bool ShowKDialogNotification()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "kdialog",
                        Arguments = "--title \"Nudge - Productivity Check\" " +
                                   "--yesno \"Were you productive during the last interval?\" " +
                                   "--yes-label \"Yes - Productive\" " +
                                   "--no-label \"No - Not Productive\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();

                // kdialog exit codes: 0 = yes, 1 = no, 2 = cancel
                if (process.ExitCode == 0)
                {
                    Console.WriteLine("User responded: YES (productive)");
                    SendResponse(true);
                }
                else if (process.ExitCode == 1)
                {
                    Console.WriteLine("User responded: NO (not productive)");
                    SendResponse(false);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async void ShowDbusNotification()
        {
            Console.WriteLine("[DEBUG] ShowDbusNotification called (native DBus)");

            try
            {
                using var connection = new Connection(Address.Session!);
                await connection.ConnectAsync();

                // Create and send Notify method call
                MessageBuffer message;
                {
                    using var writer = connection.GetMessageWriter();

                    writer.WriteMethodCallHeader(
                        destination: "org.freedesktop.Notifications",
                        path: "/org/freedesktop/Notifications",
                        @interface: "org.freedesktop.Notifications",
                        signature: "susssasa{sv}i",
                        member: "Notify");

                    writer.WriteString("Nudge");  // app_name
                    writer.WriteUInt32(0);        // replaces_id
                    writer.WriteString("");       // app_icon
                    writer.WriteString("Nudge - Productivity Check"); // summary
                    writer.WriteString("Were you productive during the last interval?"); // body

                    // Write actions array
                    writer.WriteArray(new string[] { "yes", "Yes - Productive", "no", "No - Not Productive" });

                    // Write hints dictionary with RESIDENT:TRUE for persistent notifications
                    var arrayStart = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("urgency");
                    writer.WriteVariant(VariantValue.Byte(2));
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("resident");
                    writer.WriteVariant(VariantValue.Bool(true));
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("x-kde-appname");
                    writer.WriteVariant(VariantValue.String("Nudge"));
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("x-kde-eventId");
                    writer.WriteVariant(VariantValue.String("productivity-check"));
                    writer.WriteDictionaryEnd(arrayStart);

                    writer.WriteInt32(0);  // expire_timeout (0 = infinite)

                    message = writer.CreateMessage();
                }
                // MessageWriter is now disposed, safe to await

                var notificationId = await connection.CallMethodAsync(
                    message,
                    (Message m, object? s) => m.GetBodyReader().ReadUInt32(),
                    null);

                Console.WriteLine($"[DEBUG] Notification ID: {notificationId}");

                var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                // Listen for NotificationClosed to debug why it's closing
                var closedMatchRule = new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = "org.freedesktop.Notifications",
                    Member = "NotificationClosed"
                };

                await connection.AddMatchAsync(
                    closedMatchRule,
                    (Message m, object? s) =>
                    {
                        var reader = m.GetBodyReader();
                        return (reader.ReadUInt32(), reader.ReadUInt32());
                    },
                    (Exception? ex, (uint id, uint reason) signal, object? readerState, object? handlerState) =>
                    {
                        if (ex != null)
                        {
                            Console.WriteLine($"[DEBUG] Closed listener error: {ex.Message}");
                            return;
                        }

                        if (signal.id == notificationId)
                        {
                            string reasonText = signal.reason switch
                            {
                                1 => "expired",
                                2 => "dismissed by user",
                                3 => "closed by CloseNotification call",
                                4 => "undefined/reserved",
                                _ => $"unknown ({signal.reason})"
                            };
                            Console.WriteLine($"[DEBUG] Notification closed: reason={reasonText}");
                        }
                    },
                    ObserverFlags.None,
                    null,
                    null,
                    true
                );

                // Listen for ActionInvoked signal
                var actionMatchRule = new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = "org.freedesktop.Notifications",
                    Member = "ActionInvoked"
                };

                await connection.AddMatchAsync(
                    actionMatchRule,
                    (Message m, object? s) =>
                    {
                        var reader = m.GetBodyReader();
                        return (reader.ReadUInt32(), reader.ReadString());
                    },
                    (Exception? ex, (uint id, string actionKey) signal, object? readerState, object? handlerState) =>
                    {
                        if (ex != null)
                        {
                            Console.WriteLine($"[DEBUG] Action listener error: {ex.Message}");
                            return;
                        }

                        if (signal.id == notificationId)
                        {
                            Console.WriteLine($"[DEBUG] Action invoked: {signal.actionKey}");

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
                    },
                    ObserverFlags.None,
                    null,
                    null,
                    true
                );

                // Keep connection alive until cancelled
                Console.WriteLine("[DEBUG] Waiting for notification interaction (60s timeout)...");
                await Task.Delay(-1, cancellationSource.Token).ContinueWith(_ => { });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Native DBus notification failed: {ex.Message}");
                throw;
            }
        }

        private static void ShowFallbackNotification()
        {
            // Fallback to notify-send on Linux (without buttons)
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "notify-send",
                        Arguments = "-u critical -t 60000 \"Nudge - Productivity Check\" \"Were you productive? Use the tray menu to respond\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                Console.WriteLine("✓ Sent notification via fallback method (use tray menu to respond)");
            }
            catch
            {
                Console.WriteLine("✗ All notification methods failed - use tray menu");
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // COMMON FUNCTIONS
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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

        public static void Quit()
        {
            Console.WriteLine("[DEBUG] Quit() called - shutting down Nudge...");

            if (_nudgeProcess != null && !_nudgeProcess.HasExited)
            {
                Console.WriteLine($"[DEBUG] Nudge process PID: {_nudgeProcess.Id}");
                Console.WriteLine("[DEBUG] Attempting to kill nudge process...");

                try
                {
                    _nudgeProcess.Kill(entireProcessTree: true); // Kill process and all children
                    _nudgeProcess.WaitForExit(5000); // Wait up to 5 seconds

                    if (_nudgeProcess.HasExited)
                    {
                        Console.WriteLine($"[DEBUG] Nudge process terminated (exit code: {_nudgeProcess.ExitCode})");
                    }
                    else
                    {
                        Console.WriteLine("[WARN] Nudge process did not exit within timeout");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to kill nudge process: {ex.Message}");
                }
                finally
                {
                    _nudgeProcess.Dispose();
                }
            }
            else
            {
                Console.WriteLine("[DEBUG] Nudge process already exited or null");
            }

            Console.WriteLine("[DEBUG] Exiting nudge-tray...");
            Environment.Exit(0);
        }

        public static DateTime? GetNextSnapshotTime()
        {
            return _nextSnapshotTime;
        }
    }

    public class App : Application
    {
        private TrayIcon? _trayIcon;
        private System.Timers.Timer? _menuRefreshTimer;

        public override void Initialize()
        {
            // Must call base first for Avalonia
            base.Initialize();
        }

        private NativeMenu CreateMenu()
        {
            var menu = new NativeMenu();

            // Status item showing next snapshot time
            var nextSnapshot = Program.GetNextSnapshotTime();
            var statusText = nextSnapshot.HasValue
                ? $"Next snapshot: {nextSnapshot.Value:HH:mm:ss}"
                : "Status: Running...";
            var statusItem = new NativeMenuItem(statusText);
            statusItem.IsEnabled = false;
            menu.Add(statusItem);

            menu.Add(new NativeMenuItemSeparator());

            // Quit option
            var quitItem = new NativeMenuItem("Quit");
            quitItem.Click += (s, e) =>
            {
                Console.WriteLine("[DEBUG] Quit menu item clicked");
                Program.Quit();
            };
            menu.Add(quitItem);

            Console.WriteLine($"[DEBUG] Menu created with status: {statusText}");
            return menu;
        }

        private void RefreshMenu()
        {
            if (_trayIcon != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _trayIcon.Menu = CreateMenu();
                    Console.WriteLine("[DEBUG] Tray menu refreshed");
                });
            }
        }

        private void ShowStatus()
        {
            Console.WriteLine("Status window not yet implemented");
        }

        private void ShowWindowsContextMenu()
        {
            Console.WriteLine("[DEBUG] ShowWindowsContextMenu called");

            // Create native Windows popup menu
            IntPtr menu = NativeMethods.CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                Console.WriteLine("[ERROR] Failed to create popup menu");
                return;
            }

            try
            {
                // Menu IDs
                const uint ID_STATUS = 1;
                const uint ID_QUIT = 2;

                // Add status item (grayed out)
                var nextSnapshot = Program.GetNextSnapshotTime();
                var statusText = nextSnapshot.HasValue
                    ? $"Next snapshot: {nextSnapshot.Value:HH:mm:ss}"
                    : "Status: Running...";

                NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | NativeMethods.MF_GRAYED, ID_STATUS, statusText);
                Console.WriteLine($"[DEBUG] Added status item: {statusText}");

                // Add separator
                NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, string.Empty);

                // Add Quit option
                NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, ID_QUIT, "Quit");
                Console.WriteLine("[DEBUG] Added Quit item");

                // Get cursor position
                NativeMethods.GetCursorPos(out var cursorPos);
                Console.WriteLine($"[DEBUG] Cursor position: {cursorPos.X}, {cursorPos.Y}");

                // We need a window handle for TrackPopupMenuEx
                // For now, use IntPtr.Zero (desktop window)
                // Make this window the foreground window (required for menu to work properly)
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle == IntPtr.Zero)
                {
                    // If no main window, try to get console window
                    handle = GetConsoleWindow();
                }

                Console.WriteLine($"[DEBUG] Window handle: {handle}");

                if (handle != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(handle);
                }

                // Show menu and get selected item
                uint selectedId = NativeMethods.TrackPopupMenuEx(
                    menu,
                    NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_LEFTBUTTON,
                    cursorPos.X,
                    cursorPos.Y,
                    handle,
                    IntPtr.Zero);

                Console.WriteLine($"[DEBUG] Selected menu ID: {selectedId}");

                // Handle selection
                if (selectedId == ID_QUIT)
                {
                    Console.WriteLine("[DEBUG] Quit selected from context menu");
                    Program.Quit();
                }

                // Post a null message to make the menu disappear (Windows quirk)
                if (handle != IntPtr.Zero)
                {
                    NativeMethods.PostMessage(handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
                }
            }
            finally
            {
                NativeMethods.DestroyMenu(menu);
                Console.WriteLine("[DEBUG] Menu destroyed");
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private WindowIcon? CreateSimpleIcon()
        {
            try
            {
                // Create a simple 32x32 icon programmatically
                var width = 32;
                var height = 32;
                var bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Bgra8888,
                    AlphaFormat.Premul);

                using (var fb = bitmap.Lock())
                {
                    unsafe
                    {
                        var ptr = (uint*)fb.Address.ToPointer();

                        // Draw a simple blue circle on transparent background
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                // Calculate distance from center
                                float dx = x - width / 2.0f;
                                float dy = y - height / 2.0f;
                                float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                                // Create a filled circle
                                if (distance < width / 2.0f - 2)
                                {
                                    // Blue color (BGRA format)
                                    ptr[y * width + x] = 0xFF5588FF;
                                }
                                else
                                {
                                    // Transparent
                                    ptr[y * width + x] = 0x00000000;
                                }
                            }
                        }
                    }
                }

                return new WindowIcon(bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create icon: {ex.Message}");
                return null;
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Don't show any windows - we're tray-only
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            // Create and configure tray icon
            _trayIcon = new TrayIcon
            {
                ToolTipText = "Nudge Productivity Tracker",
                Icon = CreateSimpleIcon(),
                IsVisible = true
            };

            // Set initial menu
            var initialMenu = CreateMenu();
            _trayIcon.Menu = initialMenu;

            Console.WriteLine($"[DEBUG] TrayIcon created: {_trayIcon != null}");
            Console.WriteLine($"[DEBUG] Menu assigned: {_trayIcon.Menu != null}");
            Console.WriteLine($"[DEBUG] Menu items count: {initialMenu.Items.Count}");

            // Add click handlers - show native Windows menu on right-click
            _trayIcon.Clicked += (s, e) =>
            {
                Console.WriteLine("[DEBUG] Tray icon CLICKED event fired");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    ShowWindowsContextMenu();
                }
            };

            // Add to TrayIcons collection
            if (TrayIcon.GetIcons(this) == null)
            {
                TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
            }
            else
            {
                Console.WriteLine("[DEBUG] TrayIcons already exists, adding to collection");
                var icons = TrayIcon.GetIcons(this);
                if (icons != null && !icons.Contains(_trayIcon))
                {
                    icons.Add(_trayIcon);
                }
            }

            // Refresh menu every 10 seconds to update countdown timer
            _menuRefreshTimer = new System.Timers.Timer(10000); // 10 seconds
            _menuRefreshTimer.Elapsed += (s, e) => RefreshMenu();
            _menuRefreshTimer.AutoReset = true;
            _menuRefreshTimer.Start();

            Console.WriteLine("[DEBUG] Tray icon initialized with menu");
            Console.WriteLine("[DEBUG] TrayIcon.IsVisible: " + _trayIcon.IsVisible);

            base.OnFrameworkInitializationCompleted();
        }
    }
}
