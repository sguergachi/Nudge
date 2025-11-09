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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DesktopNotifications;
using DesktopNotifications.Avalonia;

namespace NudgeTray
{
    class Program
    {
        const int UDP_PORT = 45001;
        const string VERSION = "1.0.0";
        static Process? _nudgeProcess;
        static INotificationManager? _notificationManager;

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
            try
            {
                // Start the main nudge process
                _nudgeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "./nudge",
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
                            Dispatcher.UIThread.Post(() => _ = ShowSnapshotNotificationAsync());
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

        public static async Task ShowSnapshotNotificationAsync()
        {
            Console.WriteLine("📸 Snapshot taken! Respond using the notification buttons.");

            // Try multiple notification methods in order of preference
            bool success = false;

            // Method 1: Try DesktopNotifications library
            if (_notificationManager != null)
            {
                try
                {
                    var notification = new Notification
                    {
                        Title = "Nudge - Productivity Check",
                        Body = "Were you productive during the last interval?",
                        Expiration = TimeSpan.FromSeconds(60),
                        Buttons =
                        {
                            ("Yes - Productive", "yes"),
                            ("No - Not Productive", "no")
                        }
                    };

                    notification.OnClicked += OnNotificationClicked;

                    await _notificationManager.ShowNotification(notification);
                    Console.WriteLine("✓ Desktop notification sent via DesktopNotifications library");
                    success = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ DesktopNotifications failed: {ex.Message}");
                }
            }

            // Method 2: Try native DBus notifications (Linux)
            if (!success)
            {
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
            }

            // Method 3: Fallback to notify-send (no buttons)
            if (!success)
            {
                ShowFallbackNotification();
            }
        }

        private static void ShowDbusNotification()
        {
            Console.WriteLine("[DEBUG] ShowDbusNotification called");

            // Send native Linux notification via gdbus with action buttons
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gdbus",
                    Arguments = @"call --session --dest org.freedesktop.Notifications " +
                               @"--object-path /org/freedesktop/Notifications " +
                               @"--method org.freedesktop.Notifications.Notify " +
                               @"""Nudge"" 0 ""dialog-question"" " +
                               @"""Nudge - Productivity Check"" " +
                               @"""Were you productive during the last interval?"" " +
                               @"[""yes"",""Yes - Productive"",""no"",""No - Not Productive""] " +
                               @"{} 60000",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            Console.WriteLine($"[DEBUG] Running: gdbus call...");
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Console.WriteLine($"[DEBUG] gdbus exit code: {process.ExitCode}");
            Console.WriteLine($"[DEBUG] gdbus stdout: {output}");
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"[DEBUG] gdbus stderr: {error}");
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"gdbus failed with exit code {process.ExitCode}: {error}");
            }

            // Parse notification ID from output like "(uint32 123,)"
            var notificationId = ParseNotificationId(output);
            Console.WriteLine($"[DEBUG] Parsed notification ID: {notificationId}");

            if (notificationId > 0)
            {
                // Start listening for action responses in background
                Console.WriteLine($"[DEBUG] Starting action listener for notification {notificationId}");
                StartActionListener(notificationId);
            }
            else
            {
                Console.WriteLine("[DEBUG] WARNING: Failed to parse notification ID, no action listener started");
            }
        }

        private static int ParseNotificationId(string output)
        {
            try
            {
                var cleaned = output.Trim()
                    .Replace("(", "")
                    .Replace(")", "")
                    .Replace("uint32", "")
                    .Replace(",", "")
                    .Trim();
                return int.TryParse(cleaned, out int id) ? id : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static void StartActionListener(int notificationId)
        {
            // Listen for notification action clicks via DBus in background thread
            var listenerThread = new System.Threading.Thread(() =>
            {
                try
                {
                    Console.WriteLine($"[DEBUG] Action listener thread started for notification {notificationId}");

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "gdbus",
                            Arguments = @"monitor --session --dest org.freedesktop.Notifications",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Console.WriteLine($"[DEBUG] DBus monitor: {e.Data}");

                            // Look for ActionInvoked signal
                            if (e.Data.Contains("ActionInvoked") && e.Data.Contains(notificationId.ToString()))
                            {
                                Console.WriteLine($"[DEBUG] ActionInvoked detected for notification {notificationId}!");

                                if (e.Data.Contains("\"yes\""))
                                {
                                    Console.WriteLine("✓ User responded: YES (productive) via notification");
                                    SendResponse(true);
                                    process.Kill();
                                }
                                else if (e.Data.Contains("\"no\""))
                                {
                                    Console.WriteLine("✓ User responded: NO (not productive) via notification");
                                    SendResponse(false);
                                    process.Kill();
                                }
                            }
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();

                    Console.WriteLine("[DEBUG] Waiting for action invocations (60s timeout)...");

                    // Timeout after 60 seconds
                    if (!process.WaitForExit(60000))
                    {
                        Console.WriteLine("[DEBUG] Action listener timeout reached, killing monitor");
                        process.Kill();
                    }
                    else
                    {
                        Console.WriteLine("[DEBUG] Action listener exited normally");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Action listener failed: {ex.Message}");
                    Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                }
            });

            listenerThread.IsBackground = true;
            listenerThread.Start();
            Console.WriteLine("[DEBUG] Action listener thread spawned");
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

        private static void OnNotificationClicked(object? sender, NotificationClickedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.ActionId))
            {
                Console.WriteLine("Notification clicked without action ID");
                return;
            }

            Console.WriteLine($"Notification clicked: {e.ActionId}");

            if (e.ActionId == "yes")
            {
                Console.WriteLine("User responded: YES (productive)");
                SendResponse(true);
            }
            else if (e.ActionId == "no")
            {
                Console.WriteLine("User responded: NO (not productive)");
                SendResponse(false);
            }
        }

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
            if (_nudgeProcess != null && !_nudgeProcess.HasExited)
            {
                try
                {
                    // Try graceful shutdown first
                    _nudgeProcess.CloseMainWindow();
                    if (!_nudgeProcess.WaitForExit(2000))
                    {
                        // Force kill if graceful shutdown fails
                        _nudgeProcess.Kill();
                    }
                }
                catch
                {
                    // Fallback to kill if graceful shutdown fails
                    try { _nudgeProcess.Kill(); } catch { }
                }
                finally
                {
                    _nudgeProcess.Dispose();
                }
            }
            Environment.Exit(0);
        }
    }

    public class App : Application
    {
        private TrayIcon? _trayIcon;

        public override void Initialize()
        {
            // Must call base first for Avalonia
            base.Initialize();

            // Initialize notification manager
            InitializeNotifications();
        }

        private async void InitializeNotifications()
        {
            try
            {
                Program._notificationManager = DesktopNotificationManagerBuilder.CreateDefault().Build();
                await Program._notificationManager.Initialize();
                Console.WriteLine("✓ Notification system initialized");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Failed to initialize notifications: {ex.Message}");
                Console.WriteLine("  Will fall back to system commands");
            }
        }

        private NativeMenu CreateMenu()
        {
            var menu = new NativeMenu();

            // Status item
            var statusItem = new NativeMenuItem("Status: Running...");
            statusItem.IsEnabled = false;
            menu.Add(statusItem);

            menu.Add(new NativeMenuItemSeparator());

            // Response buttons
            var yesItem = new NativeMenuItem("✓ YES (Productive)");
            yesItem.Click += (s, e) => Program.SendResponse(true);
            menu.Add(yesItem);

            var noItem = new NativeMenuItem("✗ NO (Not Productive)");
            noItem.Click += (s, e) => Program.SendResponse(false);
            menu.Add(noItem);

            menu.Add(new NativeMenuItemSeparator());

            // Quit
            var quitItem = new NativeMenuItem("Quit");
            quitItem.Click += (s, e) => Program.Quit();
            menu.Add(quitItem);

            return menu;
        }

        private void ShowStatus()
        {
            Console.WriteLine("Status window not yet implemented");
        }

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
                Menu = CreateMenu(),
                Icon = CreateSimpleIcon(),
                IsVisible = true
            };

            _trayIcon.Clicked += (s, e) => ShowStatus();

            // Add to TrayIcons collection
            if (TrayIcon.GetIcons(this) == null)
            {
                TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
