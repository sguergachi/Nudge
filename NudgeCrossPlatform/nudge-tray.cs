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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    class Program
    {
        const int UDP_PORT = 45001;
        const string VERSION = "1.0.1";
        static Process? _nudgeProcess;

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
            Console.WriteLine("📸 Snapshot taken! Respond using the notification buttons.");

            // Try native DBus notifications first
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

                    // Write hints dictionary
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
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("x-kde-urls");
                    writer.WriteVariant(VariantValue.String(""));
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

                // Listen for ActionInvoked signal
                var matchRule = new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = "org.freedesktop.Notifications",
                    Member = "ActionInvoked"
                };

                var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                await connection.AddMatchAsync(
                    matchRule,
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
