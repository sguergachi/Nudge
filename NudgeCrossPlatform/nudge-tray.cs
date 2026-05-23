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
using System.Text.Json;
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
    sealed class Program
    {
        const int UDP_PORT = 45001;
        const string VERSION = "1.4.0";
        static Process? _nudgeProcess;
        static Process? _mlInferenceProcess;
        static Process? _mlTrainerProcess;
        internal static bool _mlEnabled;
        internal static HarvestEngineMode _harvestEngine = HarvestEngineMode.V2;
        static bool _forceTrainedModel;
        static DateTime? _nextSnapshotTime;
        static int _intervalMinutes;
        static Mutex? _singleInstanceMutex;
        internal const string SingleInstanceMutexName = NudgeCoreLogic.TraySingleInstanceMutexName;
        static readonly string _baseDir = AppContext.BaseDirectory;
        // Model lives in user data dir so it survives binary updates
        static readonly string _modelDirPath = Path.Combine(PlatformConfig.DataDirectory, "model");

        // Locate the venv Python: check next to binary first (release), then 3 levels up (dev).
        static string FindPython()
        {
            var local = Path.Combine(_baseDir, "venv", "bin", "python");
            if (File.Exists(local)) return local;
            var srcDir = Path.GetFullPath(Path.Combine(_baseDir, "..", "..", ".."));
            var dev    = Path.Combine(srcDir, "venv", "bin", "python");
            if (File.Exists(dev)) return dev;
            return PlatformConfig.PythonCommand;
        }

        // Locate a Python script: check next to binary first, then 3 levels up (dev).
        static string FindScript(string name)
        {
            var local = Path.Combine(_baseDir, name);
            if (File.Exists(local)) return local;
            var srcDir = Path.GetFullPath(Path.Combine(_baseDir, "..", "..", ".."));
            return Path.Combine(srcDir, name);
        }
        static readonly string[] _dbusNotificationActions = new[] { "yes", "Yes - Productive", "no", "No - Not Productive" };
        static bool _showAnalyticsOnStartup;
        static bool _verifyAnalyticsScrollOnStartup;
        static int _analyticsScrollVerificationAttempts;
        static bool _uiAuditMode;
        static string _uiAuditOutputDir = "";

        // Common tray icon for all platforms
        static TrayIcon? _trayIcon;
        static AnalyticsWindow? _analyticsWindow;
        static SettingsWindow? _settingsWindow;
        static NativeMenuItem? _statusItem;

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
            bool auditModeEarly = Array.IndexOf(args, "--ui-audit") >= 0;
            if (!auditModeEarly)
            {
                bool createdNew;
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
                if (NudgeCoreLogic.ShouldExitForExistingTrayInstance(createdNew))
                {
                    Console.WriteLine("[WARN] Another nudge-tray instance is already running. Exiting.");
                    return;
                }
            }

            // Add global exception handlers
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                // For DBus exceptions, log and continue - don't crash the app
                if (e.ExceptionObject is Exception ex &&
                    (ex.ToString().Contains("DBus") ||
                     ex.ToString().Contains("Tmds.DBus") ||
                     ex.ToString().Contains("TaskCanceledException")))
                {
                    Console.WriteLine($"[WARN] DBus exception caught (expected on Linux): {ex.Message}");
                    Console.WriteLine($"[INFO] Application continuing normally...");
                    return; // Don't crash - just log and continue
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
                // Handle DBus-related task exceptions gracefully
                // Note: e.Exception is AggregateException, so check string representation for inner types
                var exceptionStr = e.Exception.ToString();
                if (exceptionStr.Contains("DBus") ||
                    exceptionStr.Contains("Tmds.DBus") ||
                    exceptionStr.Contains("TaskCanceledException"))
                {
                    Console.WriteLine($"[WARN] DBus task exception (handled): {e.Exception.Message}");
                }
                else
                {
                    Console.WriteLine($"[ERROR] Unobserved task exception: {e.Exception.Message}");
                }
                e.SetObserved(); // Mark as observed to prevent crash
            };

            // Add First Chance exception handler to catch all exceptions (including DBus)
            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                // Silently suppress DBus-related exceptions (expected on Linux when DBus services unavailable)
                if (e.Exception != null &&
                    (e.Exception.ToString().Contains("DBus") ||
                     e.Exception.ToString().Contains("Tmds.DBus")))
                {
                    // Suppressed - these are expected and handled gracefully
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
                    _ = int.TryParse(args[i + 1], out interval);
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
                else if (args[i] == "--harvest-engine" && i + 1 < args.Length)
                {
                    if (NudgeCoreLogic.TryParseHarvestEngine(args[i + 1], out var parsedEngine))
                    {
                        _harvestEngine = parsedEngine;
                    }
                    i++;
                }
                else if (args[i] == "--show-analytics")
                {
                    _showAnalyticsOnStartup = true;
                }
                else if (args[i] == "--verify-analytics-scroll")
                {
                    _showAnalyticsOnStartup = true;
                    _verifyAnalyticsScrollOnStartup = true;
                }
                else if (args[i] == "--ui-audit")
                {
                    _uiAuditMode = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        _uiAuditOutputDir = args[i + 1];
                        i++;
                    }
                }
            }

            // ── Load persisted settings (CLI args take precedence) ────────────────
            var savedSettings = LoadSettings();
            if (savedSettings != null)
            {
                // Restore ML preference unless the user explicitly passed --ml already
                if (!_mlEnabled && savedSettings.MlEnabled)
                {
                    _mlEnabled = true;
                    Console.WriteLine("[INFO] ML re-enabled from saved settings");
                }

                if (NudgeCoreLogic.TryParseHarvestEngine(savedSettings.HarvestEngine, out var savedEngine) &&
                    !_HasExplicitHarvestEngineArg(args))
                {
                    _harvestEngine = savedEngine;
                }
            }

            // Persist whatever state we ended up with
            _intervalMinutes = interval; // ensure field is set before SaveSettings
            SaveSettings();

            // Print banner
            Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
            Console.WriteLine("║        Nudge Tray - Productivity Tracker          ║");
            Console.WriteLine($"║        Version {VERSION}                                   ║");
            Console.WriteLine($"║        Harvest Engine: {NudgeCoreLogic.GetHarvestEngineName(_harvestEngine).ToUpperInvariant(),-26}║");
            if (_mlEnabled)
            {
                Console.WriteLine("║        🧠 ML MODE ENABLED                         ║");
            }
            Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Start ML services if enabled
            if (_mlEnabled)
            {
                StartMLServices();
            }

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

                    if (!_uiAuditMode)
                    {
                        StartNudge(interval);
#if WINDOWS
                        InitializeNotifications();
#endif
                    }
                    CreateTrayIcon();

                    if (_uiAuditMode)
                    {
                        Dispatcher.UIThread.InvokeAsync(RunUiAuditAsync);
                        return;
                    }

                    if (_showAnalyticsOnStartup)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _analyticsWindow = _verifyAnalyticsScrollOnStartup
                                ? new AnalyticsWindow(CreateAnalyticsScrollVerificationData())
                                : new AnalyticsWindow();

                            if (_verifyAnalyticsScrollOnStartup)
                            {
                                _analyticsWindow.Opened += (_, __) =>
                                {
                                    DispatcherTimer.RunOnce(VerifyAnalyticsScroll, TimeSpan.FromMilliseconds(500));
                                };
                            }

                            _analyticsWindow.Show();
                            _analyticsWindow.Activate();
                        });
                    }
                });
        }

        static bool _HasExplicitHarvestEngineArg(string[] args)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--harvest-engine", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static void VerifyAnalyticsScroll()
        {
            if (_analyticsWindow == null)
            {
                Console.WriteLine("[AnalyticsScrollTest] FAIL window was not created");
                ShutdownApplication(1);
                return;
            }

            double extentHeight = _analyticsWindow.GetScrollExtentHeight();
            double viewportHeight = _analyticsWindow.GetScrollViewportHeight();
            if ((extentHeight <= 0 || viewportHeight <= 0) && _analyticsScrollVerificationAttempts < 10)
            {
                _analyticsScrollVerificationAttempts++;
                DispatcherTimer.RunOnce(VerifyAnalyticsScroll, TimeSpan.FromMilliseconds(250));
                return;
            }

            bool hasOverflow = _analyticsWindow.HasScrollableOverflow();
            double beforeOffset = _analyticsWindow.GetScrollOffsetY();
            bool scrolled = _analyticsWindow.ApplyWheelScrollDelta(-1);
            double afterOffset = _analyticsWindow.GetScrollOffsetY();
            bool passed = hasOverflow && scrolled && afterOffset > beforeOffset;

            Console.WriteLine(
                $"[AnalyticsScrollTest] {(passed ? "PASS" : "FAIL")} " +
                $"overflow={hasOverflow} extent={extentHeight:F1} viewport={viewportHeight:F1} " +
                $"before={beforeOffset:F1} after={afterOffset:F1}"
            );

            ShutdownApplication(passed ? 0 : 1);
        }

        // ━━ UI Audit ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        static async Task RunUiAuditAsync()
        {
            if (string.IsNullOrEmpty(_uiAuditOutputDir))
                _uiAuditOutputDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "nudge-ui-audit");

            Directory.CreateDirectory(_uiAuditOutputDir);
            Console.WriteLine($"[UI-AUDIT] Output → {_uiAuditOutputDir}");

            // ── Analytics window ─────────────────────────────────────────────────
            _analyticsWindow = new AnalyticsWindow();
            _analyticsWindow.Show();
            await Task.Delay(900);

            CaptureWindow(_analyticsWindow, "analytics_today.png");

            _analyticsWindow.AuditSelectTab(AnalyticsWindow.TimeFilter.ThisWeek);
            await Task.Delay(300);
            CaptureWindow(_analyticsWindow, "analytics_week.png");

            _analyticsWindow.AuditSelectTab(AnalyticsWindow.TimeFilter.ThisMonth);
            await Task.Delay(300);
            CaptureWindow(_analyticsWindow, "analytics_month.png");

            _analyticsWindow.AuditSelectTab(AnalyticsWindow.TimeFilter.AllTime);
            await Task.Delay(300);
            CaptureWindow(_analyticsWindow, "analytics_alltime.png");

            _analyticsWindow.AuditSelectAIBrainTab();
            await Task.Delay(400);
            CaptureWindow(_analyticsWindow, "analytics_ai_brain.png");

            _analyticsWindow.AuditSetSections(sensorSignalsOpen: true, trainingDetailsOpen: true);
            await Task.Delay(200);
            CaptureWindow(_analyticsWindow, "analytics_ai_brain_expanded.png");

            _analyticsWindow.Hide();

            // ── Settings window ──────────────────────────────────────────────────
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Show();
            await Task.Delay(600);
            CaptureWindow(_settingsWindow, "settings.png");
            _settingsWindow.Hide();

            // ── Notification window ──────────────────────────────────────────────
            var notification = new CustomNotificationWindow("Visual Studio Code");
            notification.Show();
            await Task.Delay(600);
            CaptureWindow(notification, "notification.png");

            notification.AuditSetActive(true);
            await Task.Delay(150);
            CaptureWindow(notification, "notification_active.png");

            notification.Hide();

            Console.WriteLine($"[UI-AUDIT] Done — {_uiAuditOutputDir}");
            ShutdownApplication(0);
        }

        static void CaptureWindow(Window window, string filename)
        {
            try
            {
                var path = Path.Combine(_uiAuditOutputDir, filename);
                int w = (int)Math.Max(window.Bounds.Width, window.Width);
                int h = (int)Math.Max(window.Bounds.Height, window.Height);
                if (w <= 0 || h <= 0)
                {
                    Console.WriteLine($"[UI-AUDIT] SKIP {filename} — zero bounds");
                    return;
                }
                using var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
                bmp.Render(window);
                bmp.Save(path);
                Console.WriteLine($"[UI-AUDIT] Captured {filename} ({w}×{h})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI-AUDIT] ERROR {filename}: {ex.Message}");
            }
        }


        static void ShutdownApplication(int exitCode)
        {
            Environment.ExitCode = exitCode;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(exitCode);
                return;
            }

            Environment.Exit(exitCode);
        }

        static AnalyticsData CreateAnalyticsScrollVerificationData()
        {
            var data = new AnalyticsData();

            for (int i = 1; i <= 12; i++)
            {
                data.AppUsage[$"app-{i:D2}"] = 15 + i;
                data.TotalActivityMinutes += 15 + i;
            }

            for (int hour = 8; hour <= 20; hour++)
            {
                var stats = new ProductivityStats
                {
                    ProductiveCount = (hour % 3) + 2,
                    UnproductiveCount = (hour % 2) + 1
                };

                data.HourlyProductivity[hour] = stats;
                data.ProductiveMinutes += stats.ProductiveCount;
                data.UnproductiveMinutes += stats.UnproductiveCount;
            }

            return data;
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
                if (!nextSnapshot.HasValue)
                    return "Status: Running...";

                var remaining = nextSnapshot.Value - DateTime.Now;
                return NudgeCoreLogic.FormatCountdown(remaining);
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
                Console.WriteLine($"[DEBUG] Creating tray icon for {(PlatformConfig.IsWindows ? "Windows" : "Linux")}...");

                _trayIcon = new TrayIcon
                {
                    Icon = CreateCommonIcon(),
                    IsVisible = true,
                    ToolTipText = "Nudge Productivity Tracker",
                    Menu = CreateAvaloniaMenu()
                };

                // Add left-click handler for analytics window
                _trayIcon.Clicked += OnTrayIconClicked;

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

                Console.WriteLine("[INFO] ✓ Tray icon created successfully");
                Console.WriteLine("[INFO]   • Left-click: Open Analytics Dashboard");
                Console.WriteLine("[INFO]   • Right-click: Show Menu");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to create tray icon: {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");

                // Don't throw - allow app to continue without tray icon
                Console.WriteLine("[WARN] Continuing without tray icon - notifications will still work");
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
                    }
                    else if (action == "no")
                    {
                        Console.WriteLine("[DEBUG] User clicked NO from notification");
                        _waitingForResponse = false;
                        SendResponse(false);
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

                // Status item - show next snapshot time
                string statusText = "Nudge Tracker";
                try
                {
                    statusText = GetMenuStatusText();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to get status text: {ex.Message}");
                }

                _statusItem = new NativeMenuItem
                {
                    Header = statusText,
                    IsEnabled = false
                };
                menu.Add(_statusItem);
                Console.WriteLine("[DEBUG] Added status item");

                // Tick every second to keep the countdown live
                var countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                countdownTimer.Tick += (_, _) =>
                {
                    if (_statusItem != null)
                        _statusItem.Header = GetMenuStatusText();
                };
                countdownTimer.Start();

                // Separator before quit option
                menu.Add(new NativeMenuItemSeparator());
                Console.WriteLine("[DEBUG] Added separator");

                // Analytics option
                var analyticsItem = new NativeMenuItem { Header = "📊 Analytics" };
                analyticsItem.Click += (s, e) =>
                {
                    try
                    {
                        Console.WriteLine("[DEBUG] Analytics menu item clicked");
                        OnTrayIconClicked(s, e);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Analytics handler failed: {ex.Message}");
                    }
                };
                menu.Add(analyticsItem);
                Console.WriteLine("[DEBUG] Added analytics item");

                // Separator
                menu.Add(new NativeMenuItemSeparator());

                var settingsItem = new NativeMenuItem { Header = "Settings" };
                settingsItem.Click += (s, e) =>
                {
                    try
                    {
                        ShowSettingsWindow();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Settings handler failed: {ex.Message}");
                    }
                };
                menu.Add(settingsItem);

                menu.Add(new NativeMenuItemSeparator());

                // Quit option
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

        static void OnTrayIconClicked(object? sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("[DEBUG] Tray icon clicked - showing analytics window");

                // Show analytics window on UI thread
                Dispatcher.UIThread.Post(() =>
                {
                    // If window already exists and is visible, bring it to front
                    if (_analyticsWindow != null && _analyticsWindow.IsVisible)
                    {
                        _analyticsWindow.Activate();
                        _analyticsWindow.Focus();
                    }
                    else
                    {
                        // Create and show new window
                        _analyticsWindow = new AnalyticsWindow();
                        _analyticsWindow.Show();
                        _analyticsWindow.Activate();
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to show analytics window: {ex.Message}");
            }
        }

        static void ShowSettingsWindow()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_settingsWindow != null && _settingsWindow.IsVisible)
                {
                    _settingsWindow.Activate();
                    _settingsWindow.Focus();
                    return;
                }

                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Show();
                _settingsWindow.Activate();
            });
        }

        static WindowIcon CreateCommonIcon()
        {
            // Create a more descriptive "N" icon for Nudge
            var renderBitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));

            using (var ctx = renderBitmap.CreateDrawingContext())
            {
                ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, 32, 32));

                // Draw a stylized N
                var pen = new Pen(Brushes.White, 3);
                var brush = new SolidColorBrush(Color.FromRgb(85, 136, 255));

                // Rounded background
                ctx.DrawRectangle(brush, null, new RoundedRect(new Rect(2, 2, 28, 28), 8));

                // Draw 'N'
                ctx.DrawLine(pen, new Point(10, 22), new Point(10, 10));
                ctx.DrawLine(pen, new Point(10, 10), new Point(22, 22));
                ctx.DrawLine(pen, new Point(22, 22), new Point(22, 10));
            }

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

        static bool CheckPythonVersion()
        {
            return true;
        }

        static bool EnsurePythonDependencies()
        {
            try
            {
                // First check Python version compatibility
                if (!CheckPythonVersion())
                {
                    return false;
                }

                Console.WriteLine("  Checking Python dependencies...");

                // Check if required packages are installed
                var requiredPackages = new[] { "sklearn", "joblib", "pandas", "numpy" };
                bool allInstalled = true;

                foreach (var package in requiredPackages)
                {
                    var checkProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = FindPython(),
                            Arguments = $"-c \"import {package}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    checkProcess.Start();
                    checkProcess.WaitForExit(5000);

                    if (checkProcess.ExitCode != 0)
                    {
                        allInstalled = false;
                        break;
                    }
                }

                if (allInstalled)
                {
                    Console.WriteLine("  ✓ All Python dependencies are installed");
                    return true;
                }

                Console.WriteLine("  Installing Python dependencies (this may take several minutes)...");

                // Try different requirements files in order
                var requirementsFiles = new[]
                {
                    Path.Combine(_baseDir, "requirements-cpu.txt"),
                    Path.Combine(_baseDir, "requirements.txt"),
                    Path.Combine(_baseDir, "requirements-minimal.txt")
                };

                string? selectedRequirementsPath = null;
                foreach (var reqPath in requirementsFiles)
                {
                    if (File.Exists(reqPath))
                    {
                        selectedRequirementsPath = reqPath;
                        break;
                    }
                }

                if (selectedRequirementsPath == null)
                {
                    Console.WriteLine("  ✗ No requirements files found");
                    return false;
                }

                // Try to install with --break-system-packages for system Python
                var installProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/home/sammy/Dev/Nudge/NudgeCrossPlatform/venv/bin/python",
                        Arguments = $"-m pip install --break-system-packages -r \"{selectedRequirementsPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                installProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"    {e.Data}");
                    }
                };

                installProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"    {e.Data}");
                    }
                };

                installProcess.Start();
                installProcess.BeginOutputReadLine();
                installProcess.BeginErrorReadLine();
                installProcess.WaitForExit(180000); // 3 minutes for installation

                if (installProcess.ExitCode == 0)
                {
                    Console.WriteLine("  ✓ Python dependencies installed successfully");
                    return true;
                }
                else
                {
                    // Try fallback to minimal requirements if full install failed
                    if (selectedRequirementsPath != requirementsFiles[2])
                    {
                        Console.WriteLine("  ⚠ Full dependencies failed, trying minimal requirements...");
                        string minimalPath = requirementsFiles[2];

                        if (File.Exists(minimalPath))
                        {
                            var fallbackProcess = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = FindPython(),
                                    Arguments = $"-m pip install --break-system-packages -r \"{minimalPath}\"",
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                }
                            };

                            fallbackProcess.OutputDataReceived += (s, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data))
                                {
                                    Console.WriteLine($"    {e.Data}");
                                }
                            };

                            fallbackProcess.ErrorDataReceived += (s, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data))
                                {
                                    Console.WriteLine($"    {e.Data}");
                                }
                            };

                            fallbackProcess.Start();
                            fallbackProcess.BeginOutputReadLine();
                            fallbackProcess.BeginErrorReadLine();
                            fallbackProcess.WaitForExit(120000);

                            if (fallbackProcess.ExitCode == 0)
                            {
                                Console.WriteLine("  ✓ Minimal dependencies installed (ML features limited)");
                                return true;
                            }
                        }
                    }

                    Console.WriteLine("  ✗ Failed to install Python dependencies");
                    Console.WriteLine("  Please try installing manually:");
                    Console.WriteLine($"    python -m pip install --break-system-packages -r \"{selectedRequirementsPath}\"");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Error checking dependencies: {ex.Message}");
                return false;
            }
        }

        static void StartMLServices()
        {
            try
            {
                Console.WriteLine("🧠 Starting ML services...");

                // Ensure Python dependencies are installed
                if (!EnsurePythonDependencies())
                {
                    Console.WriteLine("⚠ Python dependencies not available. ML services will be disabled.");
                    Console.WriteLine("  You can still use Nudge without ML by running without the --ml flag.");
                    _mlEnabled = false;
                    return;
                }

                string csvPath = PlatformConfig.CsvPath;

                // Start ML inference service (TCP on port 45002)
                Console.WriteLine("  Starting ML inference service...");
                _mlInferenceProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FindPython(),
                        Arguments = $"\"{FindScript("model_inference.py")}\" --host 127.0.0.1 --port 45002 --model-dir \"{_modelDirPath}\"",
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
                string trainerArgs = $"\"{FindScript("background_trainer.py")}\" --csv \"{csvPath}\" --model-dir \"{_modelDirPath}\" --check-interval 300";
                if (_forceTrainedModel)
                {
                    trainerArgs += " --min-total-samples 1";
                    Console.WriteLine("  Force model enabled: using min-total-samples=1");
                }
                _mlTrainerProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/home/sammy/Dev/Nudge/NudgeCrossPlatform/venv/bin/python",
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
                        TrainerState.ParseLine(e.Data);
                        Console.WriteLine($"[ML Trainer] {e.Data}");
                    }
                };

                _mlTrainerProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        TrainerState.ParseLine(e.Data);
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
                string nudgeDllPath = Path.Combine(_baseDir, "nudge.dll");
                if (!File.Exists(nudgeDllPath))
                {
                    Console.WriteLine($"✗ nudge assembly not found: {nudgeDllPath}");
                    Environment.Exit(1);
                }

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
                args += $" --harvest-engine {NudgeCoreLogic.GetHarvestEngineName(_harvestEngine)}";

                // Start the Nudge Harvest process
                _nudgeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = PlatformConfig.DotnetCommand,
                        Arguments = $"\"{nudgeDllPath}\" {args}",
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
                        Console.WriteLine($"[Nudge Harvest] {e.Data}");

                        // Detect snapshot requests (exact match only)
                        if (e.Data.Trim() == "SNAPSHOT")
                        {
                            ShowSnapshotNotification();
                        }
                        // Live ML prediction data → feed AI Brain tab
                        else if (e.Data.StartsWith("MLDATA:", StringComparison.Ordinal))
                        {
                            try
                            {
                                var evt = JsonSerializer.Deserialize(
                                    e.Data.AsSpan(7),
                                    NudgeJsonContext.Default.MLLiveEvent);
                                if (evt != null)
                                    LiveAIState.Add(evt);
                            }
                            catch { /* non-critical, silently ignore parse errors */ }
                        }
                        // Next scheduled ML check update
                        else if (e.Data.StartsWith("MLNEXT:", StringComparison.Ordinal))
                        {
                            if (long.TryParse(e.Data.AsSpan(7), out long ts))
                                LiveAIState.NextCheckAt = ts;
                        }
                        // Real-time foreground app tracking (format: APPFOCUS:app\ttitle)
                        else if (e.Data.StartsWith("APPFOCUS:", StringComparison.Ordinal))
                        {
                            var payload = e.Data.AsSpan(9);
                            int tab = payload.IndexOf('\t');
                            if (tab >= 0)
                            {
                                LiveAIState.CurrentApp    = payload.Slice(0, tab).ToString();
                                LiveAIState.CurrentDetail = payload.Slice(tab + 1).ToString();
                            }
                            else
                            {
                                LiveAIState.CurrentApp    = payload.ToString();
                                LiveAIState.CurrentDetail = "";
                            }
                        }
                        else if (e.Data.StartsWith("HARVEST:", StringComparison.Ordinal))
                        {
                            try
                            {
                                var sig = JsonSerializer.Deserialize(
                                    e.Data.AsSpan(8),
                                    NudgeJsonContext.Default.HarvestSignal);
                                if (sig != null)
                                    LiveAIState.LastHarvest = sig;
                            }
                            catch { /* non-critical */ }
                        }
                    }
                };

                _nudgeProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[Nudge Harvest] {e.Data}");
                    }
                };

                _nudgeProcess.Start();
                _nudgeProcess.BeginOutputReadLine();
                _nudgeProcess.BeginErrorReadLine();

                Console.WriteLine($"✓ Nudge Harvest started ({NudgeCoreLogic.GetHarvestEngineName(_harvestEngine).ToUpperInvariant()})");
                if (_mlEnabled)
                {
                    Console.WriteLine("  ML mode enabled - waiting for inference server connection...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to start Nudge Harvest: {ex.Message}");
                Environment.Exit(1);
            }
        }

        public static void ShowSnapshotNotification()
        {
            // Don't show notification if already waiting for a response
            if (_waitingForResponse)
            {
                Console.WriteLine("[DEBUG] Skipping notification - already waiting for response");
                return;
            }

            _nextSnapshotTime = DateTime.Now.AddMinutes(_intervalMinutes);
            Console.WriteLine("📸 Snapshot taken! Respond using the notification buttons.");

            // Use custom cross-platform notification
            ShowCustomNotification();
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // CUSTOM CROSS-PLATFORM NOTIFICATION
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private static bool _waitingForResponse;

        private static void ShowCustomNotification()
        {
            try
            {
                Console.WriteLine("[DEBUG] ShowCustomNotification called");

                _waitingForResponse = true;

                // Create and show custom notification window on Avalonia UI thread (works on all platforms)
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var appName = LiveAIState.CurrentApp ?? "";
                        var notificationWindow = new CustomNotificationWindow(appName);
                        notificationWindow.ShowWithAnimation((productive) =>
                        {
                            _waitingForResponse = false;

                            // If productive is null, notification was auto-dismissed (no snapshot taken)
                            if (productive.HasValue)
                            {
                                SendResponse(productive.Value);
                            }
                            else
                            {
                                Console.WriteLine("[DEBUG] Notification auto-dismissed - no response sent");
                            }

                            // Don't refresh menu after response - not necessary and can cause crashes on Linux
                        });
                    }
                    catch (Exception ex)
                    {
                        // Prevent stale waiting state if UI creation fails.
                        _waitingForResponse = false;
                        Console.WriteLine($"[ERROR] Failed to create custom notification window: {ex.Message}");
                    }
                });

                Console.WriteLine("✓ Custom notification shown with animation");

                // Don't refresh menu to show waiting state - can cause DBus crashes on Linux
                // The menu will update naturally on next user interaction
            }
            catch (Exception ex)
            {
                _waitingForResponse = false;
                Console.WriteLine($"[ERROR] Failed to show custom notification: {ex.Message}");
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

            // Run DBus operations without capturing Avalonia synchronization context
            // This prevents DBus from trying to use Avalonia dispatcher during cleanup
            await Task.Run(async () =>
            {
                // Clear synchronization context to prevent DBus from capturing it
                var oldContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(null);

                try
                {
                    using var connection = new DBusConnection(DBusAddress.Session!);
                    await connection.ConnectAsync().ConfigureAwait(false);

                    // Create and send Notify method call
                    MessageBuffer message;
                    {
                        static void WriteNotify(DBusConnection conn, out MessageBuffer msg)
                        {
                            using var writer = conn.GetMessageWriter();
                            writer.WriteMethodCallHeader(
                                destination: "org.freedesktop.Notifications",
                                path: "/org/freedesktop/Notifications",
                                @interface: "org.freedesktop.Notifications",
                                signature: "susssasa{sv}i",
                                member: "Notify");

                            writer.WriteString("Nudge");
                            writer.WriteUInt32(0);
                            writer.WriteString("");
                            writer.WriteString("Nudge - Productivity Check");
                            writer.WriteString("Were you productive during the last interval?");
                            writer.WriteArray(_dbusNotificationActions);
                            var arrayStart = writer.WriteDictionaryStart();
                            writer.WriteDictionaryEntryStart(); writer.WriteString("urgency"); writer.WriteVariant(VariantValue.Byte(2));
                            writer.WriteDictionaryEntryStart(); writer.WriteString("resident"); writer.WriteVariant(VariantValue.Bool(true));
                            writer.WriteDictionaryEntryStart(); writer.WriteString("x-kde-appname"); writer.WriteVariant(VariantValue.String("Nudge"));
                            writer.WriteDictionaryEntryStart(); writer.WriteString("x-kde-eventId"); writer.WriteVariant(VariantValue.String("productivity-check"));
                            writer.WriteDictionaryEnd(arrayStart);
                            writer.WriteInt32(0);
                            msg = writer.CreateMessage();
                        }
                        WriteNotify(connection, out message);
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
                catch (TaskCanceledException)
                {
                    Console.WriteLine("[DEBUG] DBus notification cancelled (expected during cleanup)");
                    // Swallow cancellation exceptions - these are expected
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG] Native DBus notification failed: {ex.Message}");
                    // Don't rethrow - just log and continue
                }
                finally
                {
                    // Restore original synchronization context
                    SynchronizationContext.SetSynchronizationContext(oldContext);
                }
            }).ConfigureAwait(false);
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

            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
            }

            Environment.Exit(0);
        }

        public static DateTime? GetNextSnapshotTime()
        {
            return _nextSnapshotTime;
        }

        // ─── Settings persistence ──────────────────────────────────────────────────

        private static string SettingsPath =>
            Path.Combine(PlatformConfig.DataDirectory, "tray-settings.json");

        /// <summary>
        /// Write current preferences to ~/.nudge/tray-settings.json so they
        /// survive process restarts.
        /// </summary>
        private static void SaveSettings()
        {
            try
            {
                var settings = new TraySettings
                {
                    MlEnabled       = _mlEnabled,
                    IntervalMinutes = _intervalMinutes > 0 ? _intervalMinutes : 5,
                    HarvestEngine   = NudgeCoreLogic.GetHarvestEngineName(_harvestEngine)
                };
                File.WriteAllText(
                    SettingsPath,
                    JsonSerializer.Serialize(settings, NudgeJsonContext.Default.TraySettings));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not save settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Read persisted preferences.  Returns null if the file doesn't exist yet.
        /// </summary>
        private static TraySettings? LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize(json, NudgeJsonContext.Default.TraySettings);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not load settings: {ex.Message}");
            }
            return null;
        }

        public static void RestartWithML()
        {
            Console.WriteLine("[INFO] Restarting with ML enabled...");

            // Enable ML and persist the choice immediately
            _mlEnabled = true;
            SaveSettings();

            // Clean up existing processes
            CleanupOldProcesses();

            // Start ML services
            StartMLServices();

            // Restart nudge process with ML flag
            if (_nudgeProcess != null && !_nudgeProcess.HasExited)
            {
                try
                {
                    _nudgeProcess.Kill(entireProcessTree: true);
                    _nudgeProcess.WaitForExit(2000);
                }
                catch { }
            }

            StartNudge(_intervalMinutes);
            Console.WriteLine("[INFO] ML mode enabled and nudge restarted");
        }

        internal static string CurrentVersion => VERSION;

        internal static HarvestEngineMode CurrentHarvestEngine => _harvestEngine;

        internal static void SetHarvestEngine(HarvestEngineMode engine)
        {
            if (_harvestEngine == engine)
                return;

            Console.WriteLine($"[INFO] Switching Nudge Harvest engine to {NudgeCoreLogic.GetHarvestEngineName(engine).ToUpperInvariant()}...");
            _harvestEngine = engine;
            SaveSettings();
            RestartHarvestProcess();
        }

        internal static void RestartHarvestProcess()
        {
            if (_nudgeProcess != null && !_nudgeProcess.HasExited)
            {
                try
                {
                    _nudgeProcess.Kill(entireProcessTree: true);
                    _nudgeProcess.WaitForExit(2000);
                }
                catch { }
            }

            StartNudge(_intervalMinutes > 0 ? _intervalMinutes : 5);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Trainer state — fed by background_trainer.py stdout, read by AI tab.
    // ─────────────────────────────────────────────────────────────────────────────
    internal static class TrainerState
    {
        private static readonly object _lock = new();
        private static readonly List<string> _log = new(capacity: 10);

        public static int  SampleCount;
        public static int  MinSamples   = 100;
        public static int  LastTrainedCount;
        public static bool IsTraining;
        public static float LastAccuracy = -1f;
        public static string Architecture = "";
        public static string LastError    = "";
        public static DateTime LastChecked = DateTime.MinValue;
        public static DateTime LastTrained = DateTime.MinValue;

        public static void ParseLine(string raw)
        {
            // raw is the line emitted by background_trainer.py (no prefix added yet)
            lock (_lock)
            {
                if (_log.Count >= 8) _log.RemoveAt(0);
                _log.Add(raw);
            }

            // [trainer] Labeled samples: 119  last-trained-at: 0  min: 100
            var m = System.Text.RegularExpressions.Regex.Match(raw,
                @"\[trainer\] Labeled samples:\s*(\d+)\s+last-trained-at:\s*(\d+)\s+min:\s*(\d+)");
            if (m.Success)
            {
                lock (_lock)
                {
                    SampleCount      = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    LastTrainedCount = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    MinSamples       = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                    LastChecked      = DateTime.Now;
                }
                return;
            }

            // [trainer] Training lightweight model on 119 samples...
            m = System.Text.RegularExpressions.Regex.Match(raw,
                @"\[trainer\] Training (\w+) model on (\d+) samples");
            if (m.Success)
            {
                lock (_lock)
                {
                    Architecture = m.Groups[1].Value;
                    SampleCount  = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    IsTraining   = true;
                    LastError    = "";
                }
                return;
            }

            // [trainer] Done. accuracy=0.872
            m = System.Text.RegularExpressions.Regex.Match(raw,
                @"\[trainer\] Done\. accuracy=([0-9.]+)");
            if (m.Success)
            {
                lock (_lock)
                {
                    IsTraining       = false;
                    LastTrained      = DateTime.Now;
                    LastTrainedCount = SampleCount;
                    if (float.TryParse(m.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float acc))
                        LastAccuracy = acc;
                }
                return;
            }

            // [trainer] Training failed: ...
            m = System.Text.RegularExpressions.Regex.Match(raw,
                @"\[trainer\] Training failed: (.+)");
            if (m.Success)
            {
                lock (_lock)
                {
                    IsTraining = false;
                    LastError  = m.Groups[1].Value;
                }
                return;
            }

            // [trainer] Nothing to do.
            if (raw.Contains("[trainer] Nothing to do"))
            {
                lock (_lock) { IsTraining = false; }
            }
        }

        public static IReadOnlyList<string> GetLog()
        {
            lock (_lock) { return _log.ToArray(); }
        }

        public static (int sample, int min, int lastTrained, bool training,
                        float acc, string arch, string err,
                        DateTime lastChecked, DateTime lastTrained2, IReadOnlyList<string> log) Snapshot()
        {
            lock (_lock)
            {
                return (SampleCount, MinSamples, LastTrainedCount, IsTraining,
                        LastAccuracy, Architecture, LastError,
                        LastChecked, LastTrained, _log.ToArray());
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Live AI state store — fed by MLDATA stdout lines from nudge.cs,
    // read by AnalyticsWindow AI Brain tab.
    // ─────────────────────────────────────────────────────────────────────────────
    internal static class LiveAIState
    {
        private static readonly object _lock = new();
        private static readonly List<MLLiveEvent> _events = new(capacity: 210);
        private static readonly string _historyFile;

        static LiveAIState()
        {
            string nudgeDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nudge");
            System.IO.Directory.CreateDirectory(nudgeDir);
            _historyFile = System.IO.Path.Combine(nudgeDir, "prediction_history.json");
            LoadFromDisk();
        }

        /// <summary>
        /// Unix timestamp (seconds UTC) of the next scheduled ML check.
        /// Emitted by nudge.cs as "MLNEXT:{ts}" at startup and after each cycle.
        /// 0 = not yet received.
        /// </summary>
        public static long NextCheckAt;
        /// <summary>Most-recent foreground app name; updated in real time via APPFOCUS: lines.</summary>
        public static volatile string CurrentApp = "";
        /// <summary>Window title / domain detail for the current app (tab-separated second field of APPFOCUS).</summary>
        public static volatile string CurrentDetail = "";
        /// <summary>Latest sensor fusion snapshot from the V2 Harvest Engine (HARVEST: lines), updated every 2s.</summary>
        public static volatile HarvestSignal? LastHarvest;

        public static void Add(MLLiveEvent evt)
        {
            lock (_lock)
            {
                _events.Add(evt);
                if (_events.Count > 200)
                    _events.RemoveAt(0);
            }
            SaveToDisk();
        }

        /// <summary>Returns a snapshot of recent events, oldest first.</summary>
        public static IReadOnlyList<MLLiveEvent> GetRecent()
        {
            lock (_lock)
            {
                return _events.ToArray();
            }
        }

        public static MLLiveEvent? Latest
        {
            get
            {
                lock (_lock)
                {
                    return _events.Count > 0 ? _events[_events.Count - 1] : null;
                }
            }
        }

        private static void LoadFromDisk()
        {
            try
            {
                if (!System.IO.File.Exists(_historyFile)) return;
                var json = System.IO.File.ReadAllText(_historyFile);
                var loaded = System.Text.Json.JsonSerializer.Deserialize(json, NudgeJsonContext.Default.ListMLLiveEvent);
                if (loaded == null) return;
                lock (_lock)
                {
                    _events.Clear();
                    int start = Math.Max(0, loaded.Count - 200);
                    for (int i = start; i < loaded.Count; i++)
                        _events.Add(loaded[i]);
                }
            }
            catch { }
        }

        private static void SaveToDisk()
        {
            try
            {
                List<MLLiveEvent> snapshot;
                lock (_lock) { snapshot = new List<MLLiveEvent>(_events); }
                var json = System.Text.Json.JsonSerializer.Serialize(snapshot, NudgeJsonContext.Default.ListMLLiveEvent);
                System.IO.File.WriteAllText(_historyFile, json);
            }
            catch { }
        }
    }

    // Avalonia application class - used on all platforms for custom notifications
    public class App : Avalonia.Application
    {
        public override void Initialize()
        {
            // No XAML needed for headless tray app
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Create an invisible main window to keep the app alive on Linux
                // and ensure proper tray icon display
                var mainWindow = new Window
                {
                    IsVisible = false,
                    ShowInTaskbar = false,
                    CanResize = false,
                    Width = 1,
                    Height = 1,
                    Topmost = false
                };

                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
