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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

// Avalonia - used on all platforms for custom notifications
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif

namespace NudgeTray
{
    // Shared platform configuration (same as in nudge.cs)
    static class PlatformConfig
    {
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static string CsvPath
        {
            get
            {
                string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string nudgeDir = Path.Combine(homeDir, ".nudge");

                // Create directory if it doesn't exist
                if (!Directory.Exists(nudgeDir))
                {
                    Directory.CreateDirectory(nudgeDir);
                }

                return Path.Combine(nudgeDir, "HARVEST.CSV");
            }
        }

        public static string WhichCommand => IsWindows ? "where" : "which";

        public static string PythonCommand => IsWindows ? "python" : "python3";
    }

    class Program
    {
        const int UDP_PORT = 45001;
        const string VERSION = "1.2.0";
        static Process? _nudgeProcess;
        static Process? _mlInferenceProcess;
        static Process? _mlTrainerProcess;
        internal static bool _mlEnabled = false;
        static bool _forceTrainedModel = false;
        static DateTime? _nextSnapshotTime;
        static int _intervalMinutes;

        // Common tray icon for all platforms
        static TrayIcon? _trayIcon;

#if WINDOWS
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        const int ATTACH_PARENT_PROCESS = -1;
#endif

        [STAThread]
        static void Main(string[] args)
        {
            // Add global exception handlers
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                // For DBus exceptions during cleanup, exit gracefully instead of crashing
                if (e.ExceptionObject is Exception ex &&
                    (ex.ToString().Contains("DBus") ||
                     ex.ToString().Contains("Tmds.DBus") ||
                     ex.ToString().Contains("TaskCanceledException")))
                {
                    Console.WriteLine($"[INFO] DBus cleanup exception (expected on Linux) - exiting cleanly");
                    Environment.Exit(0); // Clean exit instead of crash
                }

                // For real errors, log and let the app crash
                Console.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");
                if (e.ExceptionObject is Exception exception)
                {
                    Console.WriteLine($"[FATAL] Message: {exception.Message}");
                    Console.WriteLine($"[FATAL] Stack: {exception.StackTrace}");
                }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Console.WriteLine($"[ERROR] Unobserved task exception (handled): {e.Exception.Message}");
                e.SetObserved(); // Mark as observed to prevent crash
            };

            // Add First Chance exception handler to catch all exceptions (including DBus)
            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                // Catch and suppress DBus-related exceptions
                if (e.Exception != null &&
                    (e.Exception.ToString().Contains("DBus") ||
                     e.Exception.ToString().Contains("Tmds.DBus")))
                {
                    Console.WriteLine($"[DEBUG] DBus exception caught and suppressed: {e.Exception.Message}");
                }
            };

#if WINDOWS
            // Attach to parent console for logging (when run from terminal)
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }
#endif

            int interval = 5; // default 5 minutes

            // Parse arguments
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--interval" && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out interval);
                    i++; // Skip the interval value
                }
                else if (args[i] == "--ml")
                {
                    _mlEnabled = true;
                }
                else if (args[i] == "--force-model")
                {
                    _forceTrainedModel = true;
                }
            }

            // Print banner
            Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
            Console.WriteLine("║        Nudge Tray - Productivity Tracker          ║");
            Console.WriteLine($"║        Version {VERSION}                                   ║");
            if (_mlEnabled)
            {
                Console.WriteLine("║        🧠 ML MODE ENABLED                         ║");
            }
            Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Clean up any old processes before starting new ones
            CleanupOldProcesses();

            // Start ML services if enabled
            if (_mlEnabled)
            {
                StartMLServices();
            }

            _intervalMinutes = interval;

            // Use Avalonia for cross-platform tray icon on all platforms
            BuildAvaloniaApp(interval).StartWithClassicDesktopLifetime(args);
        }

        static AppBuilder BuildAvaloniaApp(int interval)
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .AfterSetup(_ =>
                {
                    // Add Dispatcher exception handler to prevent DBus crashes on Linux
                    try
                    {
                        Dispatcher.UIThread.UnhandledException += (s, e) =>
                        {
                            Console.WriteLine($"[ERROR] Dispatcher exception (caught and handled): {e.Exception.Message}");
                            e.Handled = true; // Prevent crash
                        };
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Could not set up Dispatcher exception handler: {ex.Message}");
                    }

                    StartNudge(interval);
#if WINDOWS
                    InitializeNotifications();
#endif
                    CreateTrayIcon();
                });
        }


        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // SHARED MENU AND ICON HELPERS (Used by both Windows and Linux)
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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

        static void HandleQuitClicked()
        {
            Console.WriteLine("[DEBUG] Quit clicked from context menu");
            Quit();
        }

        static MemoryStream GetIconPngStream()
        {
            // Create PNG icon data: 32x32 blue circle on transparent background
            // This base64 string represents a PNG image with the same blue circle (#5588FF)
            string base64Icon = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAGJSURBVFhH7ZZBTsJAFIbfTGlLKVCgLDQxceHChStXbty5c+nWpUuvgBcQb+ANvIRewBsYE2NcYFy4cOFCEhMSFi5MiNLS+c0rDKV0OtPWhSb+yZd0+t/85r1pmzKGYRiGYRiGYRiGYRiG+Q8olk1N09RN07RNO9ixS9sx7Njlxy5by7btPduOcGz7FWPHLsb2/Yw8zyv+ooRdXV1d4/fv33e/vr7u8ft9nV9eXt7x+/f39yZ+//j4+MC/39/fb/j95eXlHb9fX1/f8Pv5+fkNv5+dnd3w++np6Q2/n5yc3PD78fHxDb8fHR3d8PvBwcENv+/v79/w+97e3g2/7+7u3vD7zs7ODb9vb2/f8PvW1tYNv29ubt7w+8bGxg2/r6+v3/D72traDb+vrq7e8PvKysoNvy8vL9/w+9LS0g2/Ly4u3vD7wsLCDb/Pz8/f8Pvc3NwNv8/Ozt7w+8zMzA2/T09P3/D71NTUDT9PTEzc8Pv4+PgNv4+Njd3w++jo6A0/j4yMDMMwDMMwDMMwjCrl8gebWMzCxQJ3TAAAAABJRU5ErkJggg==";

            byte[] iconBytes = Convert.FromBase64String(base64Icon);
            return new MemoryStream(iconBytes);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // COMMON TRAY ICON IMPLEMENTATION (Works on both Windows and Linux)
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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
                else
                {
                    Console.WriteLine("[ERROR] Application.Current is null - cannot register tray icon");
                }

                Console.WriteLine("[DEBUG] Tray icon created with Avalonia TrayIcon (cross-platform)");
                Console.WriteLine("[INFO] Right-click the tray icon to respond to snapshots");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to create tray icon: {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

#if WINDOWS
        static void InitializeNotifications()
        {
            try
            {
                // Register event handler for notification activation
                // ToastNotificationManagerCompat handles COM registration automatically
                ToastNotificationManagerCompat.OnActivated += OnNotificationActivated;

                Console.WriteLine("[DEBUG] Toast notification handler registered");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to initialize notifications: {ex.Message}");
            }
        }

        static void OnNotificationActivated(ToastNotificationActivatedEventArgsCompat e)
        {
            try
            {
                Console.WriteLine("[DEBUG] Notification button clicked");

                // Parse the action argument from toast arguments
                var args = ToastArguments.Parse(e.Argument);

                if (args.Contains("action"))
                {
                    var action = args["action"];
                    Console.WriteLine($"[DEBUG] Action: {action}");

                    if (action == "yes")
                    {
                        Console.WriteLine("[DEBUG] User clicked YES from notification");
                        _waitingForResponse = false;
                        SendResponse(true);

                        // Refresh tray menu to normal state
                        UpdateTrayMenu();
                    }
                    else if (action == "no")
                    {
                        Console.WriteLine("[DEBUG] User clicked NO from notification");
                        _waitingForResponse = false;
                        SendResponse(false);

                        // Refresh tray menu to normal state
                        UpdateTrayMenu();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to handle notification action: {ex.Message}");
            }
        }
#endif

        static NativeMenu CreateAvaloniaMenu()
        {
            try
            {
                Console.WriteLine("[DEBUG] Creating menu...");
                var menu = new NativeMenu();

                if (_waitingForResponse)
                {
                    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                    // WAITING FOR RESPONSE STATE - Show YES/NO options
                    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

                    var questionItem = new NativeMenuItem
                    {
                        Header = "⏳ Were you productive?",
                        IsEnabled = false
                    };
                    menu.Add(questionItem);
                    Console.WriteLine("[DEBUG] Added question item");

                    menu.Add(new NativeMenuItemSeparator());

                    // YES - Productive option
                    var yesItem = new NativeMenuItem { Header = "✓ Yes - Productive" };
                    yesItem.Click += (s, e) =>
                    {
                        try
                        {
                            Console.WriteLine("[USER] Clicked: YES - Productive");
                            HandleMenuResponse(true);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] YES handler failed: {ex.Message}");
                        }
                    };
                    menu.Add(yesItem);
                    Console.WriteLine("[DEBUG] Added YES item");

                    // NO - Not Productive option
                    var noItem = new NativeMenuItem { Header = "✗ No - Not Productive" };
                    noItem.Click += (s, e) =>
                    {
                        try
                        {
                            Console.WriteLine("[USER] Clicked: NO - Not Productive");
                            HandleMenuResponse(false);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] NO handler failed: {ex.Message}");
                        }
                    };
                    menu.Add(noItem);
                    Console.WriteLine("[DEBUG] Added NO item");
                }
                else
                {
                    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                    // NORMAL STATE - Show status and next snapshot time
                    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

                    string statusText = "Nudge Tracker";
                    try
                    {
                        statusText = GetMenuStatusText();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Failed to get status text: {ex.Message}");
                    }

                    var statusItem = new NativeMenuItem
                    {
                        Header = statusText,
                        IsEnabled = false
                    };
                    menu.Add(statusItem);
                    Console.WriteLine("[DEBUG] Added status item");
                }

                // Separator before quit option
                menu.Add(new NativeMenuItemSeparator());
                Console.WriteLine("[DEBUG] Added separator");

                // Quit option (always visible)
                var quitItem = new NativeMenuItem { Header = "Quit" };
                quitItem.Click += (s, e) =>
                {
                    try
                    {
                        Console.WriteLine("[DEBUG] Quit menu item clicked");
                        HandleQuitClicked();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Quit handler failed: {ex.Message}");
                    }
                };
                menu.Add(quitItem);
                Console.WriteLine("[DEBUG] Added quit item");

                Console.WriteLine("[DEBUG] Menu created successfully");
                return menu;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to create menu: {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");

                // Return minimal menu as fallback
                try
                {
                    var fallbackMenu = new NativeMenu();
                    fallbackMenu.Add(new NativeMenuItem { Header = "Nudge", IsEnabled = false });
                    return fallbackMenu;
                }
                catch
                {
                    return new NativeMenu();
                }
            }
        }

        static void HandleMenuResponse(bool productive)
        {
            Console.WriteLine($"✓ Menu response: {(productive ? "PRODUCTIVE" : "NOT PRODUCTIVE")}");

            // Clear waiting state
            _waitingForResponse = false;

            // Send response to nudge process
            SendResponse(productive);

            // Update menu back to normal state
            UpdateTrayMenu();
        }

        static WindowIcon CreateCommonIcon()
        {
            // Create icon programmatically using Avalonia rendering
            // This works on all platforms (Windows, Linux, macOS)
            var renderBitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));

            using (var ctx = renderBitmap.CreateDrawingContext())
            {
                // Clear with transparent background
                ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, 32, 32));

                // Draw blue circle (same color #5588FF)
                var brush = new SolidColorBrush(Color.FromRgb(85, 136, 255));
                ctx.DrawGeometry(brush, null, new EllipseGeometry(new Rect(2, 2, 28, 28)));
            }

            // Save to memory stream as PNG
            var stream = new MemoryStream();
            renderBitmap.Save(stream);
            stream.Position = 0;
            return new WindowIcon(stream);
        }

        static void CleanupOldProcesses()
        {
            try
            {
                Console.WriteLine("[DEBUG] Cleaning up old processes...");

                if (PlatformConfig.IsWindows)
                {
                    // Windows: use taskkill
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = "/F /IM nudge.exe /T",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Process.Start(psi)?.WaitForExit(2000);
                    }
                    catch { /* Ignore - processes might not exist */ }
                }
                else
                {
                    // Linux/macOS: kill each process type separately
                    string[] processPatterns = { "nudge$", "model_inference", "background_trainer" };

                    foreach (var pattern in processPatterns)
                    {
                        try
                        {
                            // Use pkill with pattern (no quotes needed when not using shell)
                            var psi = new ProcessStartInfo
                            {
                                FileName = "pkill",
                                Arguments = $"-9 -f {pattern}",  // -9 for SIGKILL, no quotes
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            var proc = Process.Start(psi);
                            proc?.WaitForExit(1000);
                        }
                        catch { /* Ignore - processes might not exist */ }
                    }
                }

                // Wait longer for processes to cleanup
                Console.WriteLine("[DEBUG] Waiting for processes to terminate...");
                Thread.Sleep(1000);
                Console.WriteLine("[DEBUG] Cleanup complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Cleanup warning: {ex.Message}");
            }
        }

        static void StartMLServices()
        {
            try
            {
                Console.WriteLine("🧠 Starting ML services...");

                // Use shared platform configuration
                string python = PlatformConfig.PythonCommand;
                string csvPath = PlatformConfig.CsvPath;

                // Start ML inference service (TCP on port 45002)
                Console.WriteLine("  Starting ML inference service...");
                _mlInferenceProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = python,
                        Arguments = "model_inference.py --host 127.0.0.1 --port 45002 --model-dir ./model",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _mlInferenceProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[ML Inference] {e.Data}");
                    }
                };

                _mlInferenceProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[ML Inference] {e.Data}");
                    }
                };

                _mlInferenceProcess.Start();
                _mlInferenceProcess.BeginOutputReadLine();
                _mlInferenceProcess.BeginErrorReadLine();

                // Wait for service to start
                Thread.Sleep(2000);

                // Try to verify TCP connection
                try
                {
                    using (var client = new System.Net.Sockets.TcpClient())
                    {
                        client.Connect("127.0.0.1", 45002);
                        Console.WriteLine("  ✓ ML inference service started (TCP port 45002)");
                    }
                }
                catch
                {
                    Console.WriteLine("  ⚠ ML inference service may not be ready yet");
                }

                // Start background trainer
                Console.WriteLine("  Starting background trainer...");
                string trainerArgs = $"background_trainer.py --csv \"{csvPath}\" --model-dir ./model --check-interval 300";
                if (_forceTrainedModel)
                {
                    trainerArgs += " --min-total-samples 1";
                    Console.WriteLine("  Force model enabled: using min-total-samples=1");
                }
                _mlTrainerProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = python,
                        Arguments = trainerArgs,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _mlTrainerProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[ML Trainer] {e.Data}");
                    }
                };

                _mlTrainerProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[ML Trainer] {e.Data}");
                    }
                };

                _mlTrainerProcess.Start();
                _mlTrainerProcess.BeginOutputReadLine();
                _mlTrainerProcess.BeginErrorReadLine();

                Console.WriteLine("  ✓ Background trainer started");
                Console.WriteLine($"✓ ML services ready (CSV: {csvPath})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Failed to start ML services: {ex.Message}");
                Console.WriteLine("  Continuing without ML...");
                _mlEnabled = false;
            }
        }

        static void StartNudge(int interval)
        {
            _intervalMinutes = interval;
            _nextSnapshotTime = DateTime.Now.AddMinutes(interval);

            try
            {
                // Determine nudge executable name based on platform
                string nudgeExe = PlatformConfig.IsWindows ? "nudge.exe" : "./nudge";

                // Build arguments
                string args = $"--interval {interval}";
                if (_mlEnabled)
                {
                    args += " --ml";
                }
                if (_forceTrainedModel)
                {
                    args += " --force-model";
                }

                // Start the main nudge process
                _nudgeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = nudgeExe,
                        Arguments = args,
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
                            ShowSnapshotNotification();
                        }
                    }
                };

                _nudgeProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[Nudge] {e.Data}");
                    }
                };

                _nudgeProcess.Start();
                _nudgeProcess.BeginOutputReadLine();
                _nudgeProcess.BeginErrorReadLine();

                Console.WriteLine("✓ Nudge process started");
                if (_mlEnabled)
                {
                    Console.WriteLine("  ML mode enabled - waiting for inference server connection...");
                }
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

            // Use custom cross-platform notification
            ShowCustomNotification();
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // CUSTOM CROSS-PLATFORM NOTIFICATION
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private static bool _waitingForResponse = false;

        static void UpdateTrayMenu()
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (_trayIcon != null)
                        {
                            _trayIcon.Menu = CreateAvaloniaMenu();
                            Console.WriteLine("[DEBUG] Tray menu updated");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Menu update failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to post menu update: {ex.Message}");
            }
        }

        private static void ShowCustomNotification()
        {
            try
            {
                Console.WriteLine("[DEBUG] Snapshot notification triggered");

                // Set waiting state
                _waitingForResponse = true;

                // Update tray menu to show YES/NO options
                UpdateTrayMenu();

                Console.WriteLine("✓ Tray menu updated with response options");
                Console.WriteLine("  Right-click the tray icon to respond (YES/NO)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to update notification menu: {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // LEGACY NOTIFICATIONS (Kept for reference, not used)
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

#if WINDOWS
        private static void ShowWindowsNotification()
        {
            try
            {
                Console.WriteLine("[DEBUG] ShowWindowsNotification called (native toast with Toolkit)");

                _waitingForResponse = true;

                // Build native Windows toast notification with action buttons
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

                Console.WriteLine("✓ Native Windows toast notification shown with Yes/No buttons");

                // Refresh tray menu to show Yes/No options
                UpdateTrayMenu();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to show Windows notification: {ex.Message}");
            }
        }
#endif

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // LINUX NOTIFICATIONS (Native Tmds.DBus.Protocol with resident:true hint)
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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

                var notificationId = await connection.CallMethodAsync(
                    message,
                    (Tmds.DBus.Protocol.Message m, object? s) => m.GetBodyReader().ReadUInt32(),
                    null);

                Console.WriteLine($"[DEBUG] Notification ID: {notificationId}");

                // Listen for ActionInvoked signal
                var actionMatchRule = new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = "org.freedesktop.Notifications",
                    Member = "ActionInvoked"
                };

                var cancellationSource = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));

                await connection.AddMatchAsync(
                    actionMatchRule,
                    (Tmds.DBus.Protocol.Message m, object? s) =>
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

            // Stop ML services
            if (_mlInferenceProcess != null && !_mlInferenceProcess.HasExited)
            {
                try
                {
                    Console.WriteLine("  Stopping ML inference service...");
                    _mlInferenceProcess.Kill();
                    _mlInferenceProcess.Dispose();
                }
                catch { }
            }

            if (_mlTrainerProcess != null && !_mlTrainerProcess.HasExited)
            {
                try
                {
                    Console.WriteLine("  Stopping background trainer...");
                    _mlTrainerProcess.Kill();
                    _mlTrainerProcess.Dispose();
                }
                catch { }
            }

            // Stop main nudge process
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

            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
            }

            Console.WriteLine("✓ Shutdown complete");
            Console.WriteLine("[DEBUG] Exiting nudge-tray...");

            Environment.Exit(0);
        }

        public static DateTime? GetNextSnapshotTime()
        {
            return _nextSnapshotTime;
        }
    }

    // Avalonia application class - used on all platforms for custom notifications
    public class App : Avalonia.Application
    {
        public override void Initialize()
        {
            // No XAML needed for headless tray app
        }
    }
}
