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
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#if !WINDOWS
using Tmds.DBus.Protocol;
#endif

using NudgeCore;

// Avalonia - used on all platforms for custom notifications
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif

namespace NudgeTray
{
    sealed class Program
    {
        const int UDP_PORT = 45001;
        const string VERSION = "2.0.5";
        const string NudgeExeName = "nudge";
        const string NudgeDllName = "nudge.dll";
        const int TRAINER_CHECK_INTERVAL_SEC = 15;
        static Process? _nudgeProcess;
        static Process? _mlInferenceProcess;
        static Process? _mlTrainerProcess;
        static DispatcherTimer? _menuCountdownTimer;
        internal static bool _mlEnabled;
        internal static bool _experimentalMode;
        internal static bool _notificationsPaused;
        internal static volatile string MlLoadingStep = "";
        internal static volatile string MlSetupError = "";
        private static DateTime _lastHarvestRefresh = DateTime.MinValue;
        static bool _forceTrainedModel;
        static DateTime? _nextSnapshotTime;
        static int _intervalMinutes;
        public static int IntervalMinutes => _intervalMinutes;
        static int _mlCheckIntervalSeconds;
        public static int MlCheckIntervalSeconds => _mlCheckIntervalSeconds;
        public static bool ExperimentalMode => _experimentalMode;
        static Mutex? _singleInstanceMutex;
        internal const string SingleInstanceMutexName = NudgeCoreLogic.TraySingleInstanceMutexName;
        static readonly string _baseDir = AppContext.BaseDirectory;
        // Model lives in user data dir so it survives binary updates
        static readonly string _modelDirPath = Path.Combine(PlatformConfig.DataDirectory, "model");
        static readonly string _modelDirPathExp = Path.Combine(PlatformConfig.DataDirectory, "model_exp");

        static string FindPython() => PlatformConfig.FindPython(_baseDir);

        // Locate a Python script: check next to binary first, then 3 levels up (dev).
        static string FindScript(string name)
        {
            var local = Path.Combine(_baseDir, name);
            if (File.Exists(local)) return local;
            var srcDir = Path.GetFullPath(Path.Combine(_baseDir, "..", "..", ".."));
            return Path.Combine(srcDir, name);
        }
        static bool _showAnalyticsOnStartup;
        static bool _verifyAnalyticsScrollOnStartup;
        static int _analyticsScrollVerificationAttempts;
        static bool _uiAuditMode;
        static string _uiAuditOutputDir = "";
        static bool _uiAuditStaging;

        // Common tray icon for all platforms
        static TrayIcon? _trayIcon;
        internal static AnalyticsWindow? _analyticsWindow;
        static SettingsWindow? _settingsWindow;
        static NativeMenuItem? _statusItem;
        static NativeMenuItem? _updateItem;
        internal static bool _useNativeTray;

#if WINDOWS
        [DllImport("kernel32.dll")]
        internal static extern bool AttachConsole(int dwProcessId);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_APPWINDOW = 0x00040000;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int ATTACH_PARENT_PROCESS = -1;
#endif

        [STAThread]
        static void Main(string[] args)
        {
#if WINDOWS
            Velopack.VelopackApp.Build().Run();
#endif
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
                    (ex.ToString().Contains("DBus", StringComparison.Ordinal) ||
                     ex.ToString().Contains("Tmds.DBus", StringComparison.Ordinal) ||
                     ex.ToString().Contains("TaskCanceledException", StringComparison.Ordinal)))
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
                if (exceptionStr.Contains("DBus", StringComparison.Ordinal) ||
                    exceptionStr.Contains("Tmds.DBus", StringComparison.Ordinal) ||
                    exceptionStr.Contains("TaskCanceledException", StringComparison.Ordinal))
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
                    (e.Exception.ToString().Contains("DBus", StringComparison.Ordinal) ||
                     e.Exception.ToString().Contains("Tmds.DBus", StringComparison.Ordinal)))
                {
                    // Suppressed - these are expected and handled gracefully
                }
            };

#if WINDOWS
            // Attach to parent console when launched from a terminal.
            // Don't allocate a new one — as a WinExe app, no console window should appear
            // when launched from GUI (Start menu, double-click, etc.)
            AttachConsole(ATTACH_PARENT_PROCESS);
#endif

            // Mirror all console output to ~/.nudge/nudge.log so the Send Feedback
            // flow can attach recent logs. Done after AttachConsole so the parent
            // terminal still receives output on Windows.
            FileLogger.Initialize("tray");

            int interval = 5; // default 5 minutes
            int mlInterval = 60; // default 1 minute

            // Parse arguments
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--interval" && i + 1 < args.Length)
                {
                    _ = int.TryParse(args[i + 1], out interval);
                    i++; // Skip the interval value
                }
                else if (args[i] == "--ml-interval" && i + 1 < args.Length)
                {
                    _ = int.TryParse(args[i + 1], out mlInterval);
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
                    if (i + 1 < args.Length && args[i + 1].StartsWith("--output=", StringComparison.Ordinal))
                    {
                        _uiAuditOutputDir = args[i + 1].Substring("--output=".Length);
                        i++;
                    }
                }
                else if (args[i] == "--staging")
                {
                    _uiAuditStaging = true;
                }
            }

            // ── Load persisted settings (CLI args take precedence) ────────────────
            var savedSettings = LoadSettings();
            bool settingsChanged = false;
            if (savedSettings != null)
            {
                // Restore ML preference unless the user explicitly passed --ml already
                if (!_mlEnabled && savedSettings.MlEnabled)
                {
                    _mlEnabled = true;
                    settingsChanged = true;
                    Console.WriteLine("[INFO] ML re-enabled from saved settings");
                }

                // If intervals weren't passed on CLI, use saved values
                if (interval == 5 && savedSettings.IntervalMinutes != 5 && savedSettings.IntervalMinutes > 0)
                    interval = savedSettings.IntervalMinutes;

                if (mlInterval == 1 && savedSettings.MlCheckIntervalSeconds > 0)
                    mlInterval = savedSettings.MlCheckIntervalSeconds;
            }

            // Persist whatever state we ended up with (only if changed)
            _intervalMinutes = interval;
            _mlCheckIntervalSeconds = mlInterval;
            if (savedSettings is null || settingsChanged ||
                savedSettings.IntervalMinutes != interval ||
                savedSettings.MlCheckIntervalSeconds != mlInterval ||
                savedSettings.MlEnabled != _mlEnabled)
                SaveSettings();

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

            // ML services started asynchronously in AfterSetup so UI appears immediately
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
                        try
                        {
                            StartNudge(interval);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"✗ Fatal: could not start Nudge Harvest on launch: {ex.Message}");
                            Environment.Exit(1);
                        }
#if WINDOWS
                        InitializeNotifications();
#endif
                    }
                    CreateTrayIcon();
                    Task.Run(CheckForUpdateAsync);

                    if (_mlEnabled)
                        Task.Run(StartMLServices);

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

        static AnalyticsData CreateStagingAnalyticsData()
        {
            var data = new AnalyticsData();

            // App usage - realistic developer workflow
            data.AppUsage["Visual Studio Code:"] = 92;
            data.AppUsage["Chrome"] = 64;
            data.AppUsage["Figma"] = 38;
            data.AppUsage["Terminal"] = 31;
            data.AppUsage["Obsidian"] = 24;
            data.AppUsage["Slack"] = 18;
            data.AppUsage["Spotify"] = 14;
            data.AppUsage["Discord"] = 11;
            data.AppUsage["Settings"] = 6;
            data.AppUsage["File Explorer"] = 5;
            data.AppUsage["Notion"] = 4;
            data.AppUsage["Postman"] = 3;

            foreach (var kv in data.AppUsage)
                data.TotalActivityMinutes += kv.Value;

            // Hourly productivity - 9am to 6pm with realistic patterns
            int[] hours = { 9, 10, 11, 12, 13, 14, 15, 16, 17 };
            int[] prodCounts = { 8, 10, 9, 6, 7, 11, 10, 9, 7 };
            int[] unprodCounts = { 2, 1, 2, 4, 3, 1, 2, 2, 3 };

            for (int i = 0; i < hours.Length; i++)
            {
                var stats = new ProductivityStats
                {
                    ProductiveCount = prodCounts[i],
                    UnproductiveCount = unprodCounts[i]
                };
                data.HourlyProductivity[hours[i]] = stats;
                data.ProductiveMinutes += prodCounts[i];
                data.UnproductiveMinutes += unprodCounts[i];
            }

            return data;
        }

        static void InjectStagingData()
        {
            Console.WriteLine("[UI-AUDIT] Injecting staging data...");

            // ── TrainerState ─────────────────────────────────────────────────────
            TrainerState.SampleCount = 187;
            TrainerState.MinSamples = 100;
            TrainerState.LastTrainedCount = 187;
            TrainerState.LastTrained = DateTime.Now.AddDays(-2).AddHours(-3);
            TrainerState.LastAccuracy = 0.87f;
            TrainerState.PreviousAccuracy = 0.84f;
            TrainerState.ModelVersion = 3;
            TrainerState.Architecture = "lightweight";
            TrainerState.IsTraining = false;
            TrainerState.LastError = "";
            TrainerState.LastChecked = DateTime.Now.AddMinutes(-5);

            // ── Dummy model file ─────────────────────────────────────────────────
            string modelDir = Path.Combine(PlatformConfig.DataDirectory, "model");
            Directory.CreateDirectory(modelDir);
            string modelPath = Path.Combine(modelDir, "productivity_model.joblib");
            if (!File.Exists(modelPath))
            {
                // Write a minimal valid joblib-like file (just enough bytes to show a size)
                File.WriteAllBytes(modelPath, new byte[46_080]); // ~45 KB
            }

            // ── LiveAIState ──────────────────────────────────────────────────────
            LiveAIState.NextCheckAt = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds();
            LiveAIState.CurrentApp = "Visual Studio Code:";
            LiveAIState.CurrentDetail = "nudge-tray.cs";
            LiveAIState.LastHarvest = new HarvestSignal
            {
                Quality = "trusted",
                FocusSrc = "kwin_script",
                Category = "Development",
                CategoryConf = 0.95f,
                IdleMs = 1200,
                FocusedMs = 45_000,
                Domain = "",
                Work = 1,
                Ent = 0,
                Comm = 0,
                Browser = 0,
                Afk = 0
            };

            // Add recent ML events
            var now = DateTimeOffset.UtcNow;
            var events = new[]
            {
                (App: "Visual Studio Code:", Score: 0.92, Conf: 0.95, Prod: true, Trig: false, Resp: (bool?)true, Correct: (bool?)true, Src: "ai"),
                (App: "Chrome", Score: 0.15, Conf: 0.88, Prod: false, Trig: true, Resp: (bool?)false, Correct: (bool?)true, Src: "ai"),
                (App: "Figma", Score: 0.78, Conf: 0.72, Prod: true, Trig: false, Resp: (bool?)true, Correct: (bool?)true, Src: "ai"),
                (App: "Terminal", Score: 0.85, Conf: 0.91, Prod: true, Trig: false, Resp: (bool?)true, Correct: (bool?)true, Src: "ai"),
                (App: "Slack", Score: 0.22, Conf: 0.82, Prod: false, Trig: true, Resp: (bool?)false, Correct: (bool?)true, Src: "ai"),
                (App: "Obsidian", Score: 0.68, Conf: 0.76, Prod: true, Trig: false, Resp: (bool?)true, Correct: (bool?)true, Src: "ai"),
                (App: "Chrome", Score: 0.10, Conf: 0.96, Prod: false, Trig: true, Resp: (bool?)false, Correct: (bool?)true, Src: "ai"),
                (App: "Visual Studio Code:", Score: 0.89, Conf: 0.93, Prod: true, Trig: false, Resp: (bool?)true, Correct: (bool?)true, Src: "ai"),
                (App: "Spotify", Score: 0.30, Conf: 0.65, Prod: false, Trig: false, Resp: (bool?)null, Correct: (bool?)null, Src: "int"),
                (App: "Visual Studio Code:", Score: 0.95, Conf: 0.98, Prod: true, Trig: false, Resp: (bool?)null, Correct: (bool?)null, Src: "ai"),
            };

            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                var evt = new MLLiveEvent
                {
                    T = now.AddMinutes(-(events.Length - i) * 6).ToUnixTimeSeconds(),
                    App = e.App,
                    Score = e.Score,
                    Confidence = e.Conf,
                    Productive = e.Prod,
                    Triggered = e.Trig,
                    UserResponse = e.Resp,
                    AiCorrect = e.Correct,
                    TriggerSource = e.Src
                };
                LiveAIState.Add(evt);
            }

            // Enable ML for the audit
            Program._mlEnabled = true;

            Console.WriteLine("[UI-AUDIT] Staging data injected: 187 samples, 87% accuracy, 10 recent events");
        }

        static async Task RunUiAuditAsync()
        {
            if (string.IsNullOrEmpty(_uiAuditOutputDir))
                _uiAuditOutputDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "nudge-ui-audit");

            Directory.CreateDirectory(_uiAuditOutputDir);
            Console.WriteLine($"[UI-AUDIT] Output → {_uiAuditOutputDir}");

            if (_uiAuditStaging)
            {
                InjectStagingData();
            }

            var stagingData = _uiAuditStaging ? CreateStagingAnalyticsData() : null;

            // ── Analytics window ─────────────────────────────────────────────────
            _analyticsWindow = stagingData != null
                ? new AnalyticsWindow(stagingData)
                : new AnalyticsWindow();
            _analyticsWindow.Show();
            await Task.Delay(900);

            CaptureWindow(_analyticsWindow, "analytics_today.png");

            _analyticsWindow.AuditSelectTab(AnalyticsWindow.TimeFilter.ThisWeek, _uiAuditStaging ? CreateStagingAnalyticsData() : null);
            await Task.Delay(300);
            CaptureWindow(_analyticsWindow, "analytics_week.png");

            _analyticsWindow.AuditSelectTab(AnalyticsWindow.TimeFilter.ThisMonth, _uiAuditStaging ? CreateStagingAnalyticsData() : null);
            await Task.Delay(300);
            CaptureWindow(_analyticsWindow, "analytics_month.png");

            _analyticsWindow.AuditSelectTab(AnalyticsWindow.TimeFilter.AllTime, _uiAuditStaging ? CreateStagingAnalyticsData() : null);
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

                var pxSize = new PixelSize(w, h);
                var dpi = new Vector(96, 96);

                using var bmp = new RenderTargetBitmap(pxSize, dpi);
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
            Console.WriteLine($"[SHUTDOWN] Exiting with code {exitCode}");
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
            var mlStep = MlLoadingStep;
            if (!string.IsNullOrEmpty(mlStep))
                return mlStep;
            if (_notificationsPaused)
            {
                return "⏸ Notifications Paused";
            }
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

        static void SetMlStatus(string step)
        {
            MlLoadingStep = step;
            Console.WriteLine($"[ML] {step}");
            if (_statusItem != null)
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_statusItem != null) _statusItem.Header = GetMenuStatusText();
#if WINDOWS
                    if (NativeTray.IsInitialized) NativeTray.SetStatusText(GetMenuStatusText());
#endif
                });
        }

        static void HandleQuitClicked()
        {
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, use native Win32 tray icon instead of Avalonia's buggy one
                // (Avalonia's TrayIcon shows right-click menu at wrong position as a white box).
                // NativeTray is initialized when the hidden window HWND becomes available.
                _useNativeTray = true;
                Console.WriteLine("[INFO] Will create native Win32 tray icon when window opens");
                return;
            }

            try
            {

                _trayIcon = new TrayIcon
                {
                    Icon = CreateCommonIcon(),
                    IsVisible = true,
                    ToolTipText = "Nudge Productivity Tracker",
                    Menu = CreateAvaloniaMenu()
                };

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

        internal static void InitializeNativeTray()
        {
#if WINDOWS
            if (!NativeTray.Initialize())
            {
                Console.WriteLine("[ERROR] Failed to initialize native tray icon");
                return;
            }

            Console.WriteLine("[INFO] ✓ Native Win32 tray icon created");
            Console.WriteLine("[INFO]   • Left-click: Open Analytics Dashboard");
            Console.WriteLine("[INFO]   • Right-click: Native context menu");

            NativeTray.LeftClicked += () => OnTrayIconClicked(null, EventArgs.Empty);
            NativeTray.AnalyticsClicked += () =>
            {
                try { OnTrayIconClicked(null, EventArgs.Empty); }
                catch (Exception ex) { Console.WriteLine($"[ERROR] Analytics handler failed: {ex.Message}"); }
            };
            NativeTray.SettingsClicked += () =>
            {
                try { ShowSettingsWindow(); }
                catch (Exception ex) { Console.WriteLine($"[ERROR] Settings handler failed: {ex.Message}"); }
            };
            NativeTray.LogsClicked += () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(PlatformConfig.DataDirectory) { UseShellExecute = true });
                }
                catch (Exception ex) { Console.WriteLine($"[ERROR] Could not open logs folder: {ex.Message}"); }
            };
            NativeTray.FeedbackClicked += () =>
            {
                try { OpenFeedback(); }
                catch (Exception ex) { Console.WriteLine($"[ERROR] Feedback handler failed: {ex.Message}"); }
            };
            NativeTray.UpdatesClicked += () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(
                        "https://github.com/sguergachi/Nudge/releases/latest") { UseShellExecute = true });
                }
                catch (Exception ex) { Console.WriteLine($"[ERROR] Could not open releases URL: {ex.Message}"); }
            };
            NativeTray.QuitClicked += () =>
            {
                try { HandleQuitClicked(); }
                catch (Exception ex) { Console.WriteLine($"[ERROR] Quit handler failed: {ex.Message}"); }
            };

            // Initial status text
            NativeTray.SetStatusText(GetMenuStatusText());
#endif
        }

#if WINDOWS
        static void InitializeNotifications()
        {
            try
            {
                // Register event handler for notification activation
                // ToastNotificationManagerCompat handles COM registration automatically
                ToastNotificationManagerCompat.OnActivated += OnNotificationActivated;

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

                // Parse the action argument from toast arguments
                var args = ToastArguments.Parse(e.Argument);

                if (args.Contains("action"))
                {
                    var action = args["action"];

                    if (action == "yes")
                    {
                        _waitingForResponse = false;
                        SendResponse(true);
                    }
                    else if (action == "no")
                    {
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

        const string FeedbackBaseUrl = "https://github.com/sguergachi/Nudge/issues/new";

        // Opens a pre-filled GitHub issue containing the most recent log lines.
        // Falls back to a blank issue page if anything goes wrong.
        internal static void OpenFeedback()
        {
            string url = BuildFeedbackUrl();
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Could not open feedback URL: {ex.Message}");
                try
                {
                    Process.Start(new ProcessStartInfo(FeedbackBaseUrl) { UseShellExecute = true });
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[ERROR] Could not open fallback feedback URL: {ex2.Message}");
                }
            }
        }

        // Builds a GitHub "new issue" URL with the last 100 log lines attached in
        // the body. The log block is capped so the resulting URL stays within the
        // length browsers and GitHub reliably accept.
        static string BuildFeedbackUrl()
        {
            try
            {
                var lines = FileLogger.ReadLastLines(100);
                string logText = string.Join("\n", lines);

                const int maxLogChars = 1500;
                if (logText.Length > maxLogChars)
                    logText = string.Concat("…(older lines truncated)…\n",
                              logText.AsSpan(logText.Length - maxLogChars));

                var body = new StringBuilder();
                body.AppendLine("<!-- Describe the problem or feedback here. Recent logs are attached below. -->");
                body.AppendLine();
                body.AppendLine("### What happened?");
                body.AppendLine();
                body.AppendLine();
                body.AppendLine("---");
                body.AppendLine(CultureInfo.InvariantCulture, $"**Version:** {VERSION}");
                body.AppendLine(CultureInfo.InvariantCulture, $"**OS:** {RuntimeInformation.OSDescription}");
                body.AppendLine();
                body.AppendLine("<details><summary>Recent logs (last 100 lines)</summary>");
                body.AppendLine();
                body.AppendLine("```");
                body.AppendLine(logText.Length == 0 ? "(no logs captured yet)" : logText);
                body.AppendLine("```");
                body.AppendLine("</details>");

                return $"{FeedbackBaseUrl}?body={Uri.EscapeDataString(body.ToString())}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to build feedback URL with logs: {ex.Message}");
                return FeedbackBaseUrl;
            }
        }

        static NativeMenu CreateAvaloniaMenu()
        {
            try
            {
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

                // Tick every second to keep the countdown live
                _menuCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _menuCountdownTimer.Tick += (_, _) =>
                {
                    if (_statusItem != null)
                        _statusItem.Header = GetMenuStatusText();
#if WINDOWS
                    if (NativeTray.IsInitialized)
                        NativeTray.SetStatusText(GetMenuStatusText());
#endif
                };
                _menuCountdownTimer.Start();

                // Separator before quit option
                menu.Add(new NativeMenuItemSeparator());

                // Analytics option
                var analyticsItem = new NativeMenuItem { Header = "📊 Analytics" };
                analyticsItem.Click += (s, e) =>
                {
                    try
                    {
                        OnTrayIconClicked(s, e);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Analytics handler failed: {ex.Message}");
                    }
                };
                menu.Add(analyticsItem);

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

                var logsItem = new NativeMenuItem { Header = "View Logs Folder" };
                logsItem.Click += (s, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(PlatformConfig.DataDirectory) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Could not open logs folder: {ex.Message}");
                    }
                };
                menu.Add(logsItem);

                menu.Add(new NativeMenuItemSeparator());

                var feedbackItem = new NativeMenuItem { Header = "Send Feedback" };
                feedbackItem.Click += (s, e) =>
                {
                    try
                    {
                        OpenFeedback();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Feedback handler failed: {ex.Message}");
                    }
                };
                menu.Add(feedbackItem);

                _updateItem = new NativeMenuItem { Header = "Check for Updates" };
                _updateItem.Click += (s, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(
                            "https://github.com/sguergachi/Nudge/releases/latest") { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Could not open releases URL: {ex.Message}");
                    }
                };
                menu.Add(_updateItem);

                menu.Add(new NativeMenuItemSeparator());

                // Quit option
                var quitItem = new NativeMenuItem { Header = "Quit" };
                quitItem.Click += (s, e) =>
                {
                    try
                    {
                        HandleQuitClicked();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Quit handler failed: {ex.Message}");
                    }
                };
                menu.Add(quitItem);

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

        static async Task CheckForUpdateAsync()
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Nudge/{VERSION}");
                var json = await http.GetStringAsync(
                    "https://api.github.com/repos/sguergachi/Nudge/releases/latest");
                using var doc = JsonDocument.Parse(json);
                var tag = doc.RootElement.GetProperty("tag_name").GetString();
                if (tag == null) return;
                var latestStr = tag.TrimStart('v');
                if (Version.TryParse(latestStr, out var latest) &&
                    Version.TryParse(VERSION, out var current) &&
                    latest > current)
                {
                    Console.WriteLine($"[UPDATE] New version available: {tag}");
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_updateItem != null)
                            _updateItem.Header = $"Update available: {tag} ↗";
#if WINDOWS
                        if (NativeTray.IsInitialized)
                            NativeTray.SetUpdateText($"Update available: {tag} ↗");
#endif
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Update check failed: {ex.Message}");
            }
        }

        static void CleanupOldProcesses()
        {
            try
            {

                if (PlatformConfig.IsWindows)
                {
                    // Windows: use taskkill for harvest process
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/F /IM {NudgeExeName}.exe /T",
                            CreateNoWindow = true
                        };
                        Process.Start(psi)?.WaitForExit(2000);
                    }
                    catch { /* Ignore - processes might not exist */ }

                    // Kill any leftover ML Python processes by reference
                    foreach (var mlProc in new[] { _mlInferenceProcess, _mlTrainerProcess })
                    {
                        if (mlProc != null && !mlProc.HasExited)
                        {
                            try { mlProc.Kill(entireProcessTree: true); mlProc.WaitForExit(2000); } catch { }
                        }
                    }
                    _mlInferenceProcess = null;
                    _mlTrainerProcess = null;
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
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Cleanup warning: {ex.Message}");
            }
        }

        static bool EnsurePythonDependencies()
        {
            try
            {
                SetMlStatus("🐍 Checking Python…");
                string systemPython = FindPython();

                // Verify Python actually runs before attempting venv creation
                bool pythonWorks = false;
                try
                {
                    var testProc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = systemPython,
                            Arguments = "--version",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
                    testProc.Start();
                    // Read output streams to prevent deadlock on Windows
                    testProc.BeginOutputReadLine();
                    testProc.BeginErrorReadLine();
                    if (!testProc.WaitForExit(5000))
                    {
                        try { testProc.Kill(entireProcessTree: true); } catch { }
                        MlSetupError = "Python process timed out.\nAntivirus or security software may be blocking Python.\nTry adding an exclusion for the Nudge folder.";
                        Console.WriteLine("  ✗ Python process timed out after 5s");
                        return false;
                    }
                    pythonWorks = testProc.ExitCode == 0;
                }
                catch { }

                if (!pythonWorks)
                {
                    if (string.IsNullOrEmpty(MlSetupError))
                        MlSetupError = "Python runtime is missing or broken.\nPlease re-download Nudge from the releases page or install Python 3.8+ from python.org.";
                    Console.WriteLine("  ✗ Python not found or not working");
                    return false;
                }

                // Create/reuse user-level venv at ~/.nudge/venv/
                if (!File.Exists(PlatformConfig.VenvPythonPath))
                {
                    SetMlStatus("🐍 Creating Python environment…");
                }
                if (PlatformConfig.EnsureVenv(systemPython))
                {
                    string venvPy = PlatformConfig.VenvPythonPath;
                    if (File.Exists(venvPy))
                        Console.WriteLine($"  ✓ User venv ready");
                }

                string python = FindPython(); // returns venv Python if available
                Console.WriteLine($"  Using Python: {python}");

                // Check if required packages are installed inside the venv
                SetMlStatus("🐍 Checking ML packages…");
                var requiredPackages = new[] { "sklearn", "joblib", "pandas", "numpy" };
                bool allInstalled = true;

                foreach (var package in requiredPackages)
                {
                    var checkProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = python,
                            Arguments = $"-c \"import {package}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    checkProcess.Start();
                    // Read output streams to prevent deadlock on Windows
                    checkProcess.BeginOutputReadLine();
                    checkProcess.BeginErrorReadLine();
                    if (!checkProcess.WaitForExit(5000))
                    {
                        try { checkProcess.Kill(entireProcessTree: true); checkProcess.WaitForExit(1000); } catch { }
                        allInstalled = false;
                        break;
                    }

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

                SetMlStatus("📦 Installing ML packages (1–3 min)…");

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
                    MlSetupError = "Requirements file not found.\nPlease reinstall Nudge.";
                    Console.WriteLine("  ✗ No requirements files found");
                    return false;
                }

                // Install into venv — no --user / --break-system-packages needed
                if (!RunPipInstall(python, selectedRequirementsPath, 180_000))
                {
                    // Try minimal fallback
                    if (selectedRequirementsPath != requirementsFiles[2] && File.Exists(requirementsFiles[2]))
                    {
                        SetMlStatus("📦 Full install failed, trying minimal…");
                        if (RunPipInstall(python, requirementsFiles[2], 120_000))
                        {
                            Console.WriteLine("  ✓ Minimal dependencies installed (ML features limited)");
                            return true;
                        }
                    }

                    MlSetupError = $"Failed to install Python packages.\nTry running manually:\n{python} -m pip install -r \"{selectedRequirementsPath}\"";
                    Console.WriteLine("  ✗ Failed to install Python dependencies");
                    Console.WriteLine("  Please try installing manually:");
                    Console.WriteLine($"    {python} -m pip install -r \"{selectedRequirementsPath}\"");
                    return false;
                }

                Console.WriteLine("  ✓ Python dependencies installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                MlSetupError = $"Python error: {ex.Message}";
                Console.WriteLine($"  ⚠ Error checking dependencies: {ex.Message}");
                return false;
            }
        }

        static bool RunPipInstall(string python, string reqPath, int timeoutMs)
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = $"-m pip install -r \"{reqPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine($"    {e.Data}"); };
            proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine($"    {e.Data}"); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            bool finished = proc.WaitForExit(timeoutMs);
            if (!finished)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                Console.WriteLine($"    ✗ pip install timed out after {timeoutMs / 1000}s");
                return false;
            }
            return proc.ExitCode == 0;
        }

        /// <summary>
        /// Copies the bundled V1 pre-trained model (productivity_model.joblib,
        /// scaler.json, trainer_meta.json) from the app directory into the user
        /// data directory so the background trainer and inference server find it.
        /// </summary>
        internal static void DeployBundledModel()
        {
            try
            {
                string userModelPath = Path.Combine(_modelDirPath, "productivity_model.joblib");
                if (File.Exists(userModelPath)) return; // Already deployed

                string bundledDir = Path.Combine(_baseDir, "model");
                string bundledModel = Path.Combine(bundledDir, "productivity_model.joblib");
                if (!File.Exists(bundledModel)) return; // No bundled model to deploy

                Console.WriteLine("[INFO] Deploying bundled V1 model to user data directory…");
                Directory.CreateDirectory(_modelDirPath);
                File.Copy(bundledModel, userModelPath, overwrite: false);
                foreach (var auxFile in new[] { "scaler.json", "trainer_meta.json" })
                {
                    string src = Path.Combine(bundledDir, auxFile);
                    string dst = Path.Combine(_modelDirPath, auxFile);
                    if (File.Exists(src) && !File.Exists(dst))
                        File.Copy(src, dst, overwrite: false);
                }
                Console.WriteLine("  ✓ Bundled model deployed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not deploy bundled model: {ex.Message}");
            }
        }

        internal static void DeployBundledModelExp()
        {
            try
            {
                string userModelPath = Path.Combine(_modelDirPathExp, "productivity_model.joblib");
                if (File.Exists(userModelPath)) return; // Already deployed

                string bundledDir = Path.Combine(_baseDir, "model_exp");
                string bundledModel = Path.Combine(bundledDir, "productivity_model.joblib");
                if (!File.Exists(bundledModel)) return; // No bundled seed to deploy

                Console.WriteLine("[INFO] Deploying bundled V4 seed model to user data directory…");
                Directory.CreateDirectory(_modelDirPathExp);
                File.Copy(bundledModel, userModelPath, overwrite: false);
                foreach (var auxFile in new[] { "scaler.json", "trainer_state.json" })
                {
                    string src = Path.Combine(bundledDir, auxFile);
                    string dst = Path.Combine(_modelDirPathExp, auxFile);
                    if (File.Exists(src) && !File.Exists(dst))
                        File.Copy(src, dst, overwrite: false);
                }
                Console.WriteLine("  ✓ Bundled V4 seed model deployed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not deploy bundled V4 model: {ex.Message}");
            }
        }

        static void StartMLServices()
        {
            try
            {
                SetMlStatus("🧠 Starting AI services…");

                // Ensure Python dependencies are installed
                if (!EnsurePythonDependencies())
                {
                    Console.WriteLine("⚠ Python dependencies not available. ML services will be disabled.");
                    Console.WriteLine("  You can still use Nudge without ML by running without the --ml flag.");
                    MlLoadingStep = "";
                    _mlEnabled = false;
                    return;
                }

                // Deploy bundled V1 model to user data dir so the trainer finds it
                if (_experimentalMode)
                    DeployBundledModelExp();
                else
                    DeployBundledModel();

                string csvPath = _experimentalMode ? PlatformConfig.CsvPathExp : PlatformConfig.CsvPath;
                string modelDir = _experimentalMode ? _modelDirPathExp : _modelDirPath;
                int mlPort = _experimentalMode ? 45003 : 45002;

                // Start ML inference service (TCP on port 45002 / 45003)
                SetMlStatus("🧠 Launching inference server…");
                _mlInferenceProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FindPython(),
                        Arguments = $"\"{FindScript("model_inference.py")}\" --host 127.0.0.1 --port {mlPort} --model-dir \"{modelDir}\"",
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

                // Wait for service to start — poll up to 15s so fast machines don't wait unnecessarily
                SetMlStatus("🧠 Waiting for inference server…");
                bool serverReady = false;
                for (int attempt = 0; attempt < 15 && !serverReady; attempt++)
                {
                    Thread.Sleep(1000);
                    try
                    {
                        using var client = new System.Net.Sockets.TcpClient();
                        client.Connect("127.0.0.1", mlPort);
                        serverReady = true;
                    }
                    catch { }
                }

                if (serverReady)
                    Console.WriteLine($"  ✓ ML inference service started (TCP port {mlPort})");
                else
                    Console.WriteLine("  ⚠ ML inference service may not be ready yet");

                // Start background trainer
                SetMlStatus("🧠 Starting background trainer…");
                string trainerArgs = $"\"{FindScript("background_trainer.py")}\" --seed --csv \"{csvPath}\" --model-dir \"{modelDir}\" --check-interval {TRAINER_CHECK_INTERVAL_SEC}{(_experimentalMode ? " --schema v4" : "")}";
                if (_forceTrainedModel)
                {
                    trainerArgs += " --min-total-samples 1";
                    Console.WriteLine("  Force model enabled: using min-total-samples=1");
                }
                _mlTrainerProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FindPython(),
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

                TrainerState.RefreshFromCsv();

                Console.WriteLine("  ✓ Background trainer started");
                Console.WriteLine($"✓ ML services ready (CSV: {csvPath})");
                _mlEnabled = true;
                MlLoadingStep = "";
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(MlSetupError))
                    MlSetupError = $"Unexpected error: {ex.Message}";
                Console.WriteLine($"⚠ Failed to start ML services: {ex.Message}");
                Console.WriteLine("  Continuing without ML...");
                MlLoadingStep = "";
                _mlEnabled = false;
            }
        }

        public static void TriggerTrainingNow()
        {
            try
            {
                Console.WriteLine("[ML Trainer] Manual training trigger requested...");

                if (_mlTrainerProcess != null && !_mlTrainerProcess.HasExited)
                {
                    try
                    {
                        _mlTrainerProcess.Kill();
                        _mlTrainerProcess.Dispose();
                    }
                    catch { }
                }

                string csvPath = _experimentalMode ? PlatformConfig.CsvPathExp : PlatformConfig.CsvPath;
                string modelDir = _experimentalMode ? _modelDirPathExp : _modelDirPath;
                string trainerArgs = $"\"{FindScript("background_trainer.py")}\" --seed --csv \"{csvPath}\" --model-dir \"{modelDir}\" --check-interval {TRAINER_CHECK_INTERVAL_SEC} --min-total-samples 1 --force --once{(_experimentalMode ? " --schema v4" : "")}";

                _mlTrainerProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FindPython(),
                        Arguments = trainerArgs,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                // Set state BEFORE starting the process so the async stdout
                // reader can't race ahead and set IsTraining=false before we finish.
                TrainerState.IsTraining = true;
                TrainerState.Architecture = "pending";
                TrainerState.LastError = "";
                TrainerState.TrainingProgress = -1f;

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

                TrainerState.RefreshFromCsv();
                _analyticsWindow?.RequestTrainingViewRefresh();

                Console.WriteLine("  ✓ Manual training started");

                // Restart the continuous background trainer once this one-shot exits
                var capturedProcess = _mlTrainerProcess;
                _ = Task.Run(() =>
                {
                    try
                    {
                        capturedProcess.WaitForExit();
                        Thread.Sleep(500);
                        if (_mlTrainerProcess == capturedProcess)
                            StartContinuousTrainer();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠ Failed to restart background trainer: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Failed to start manual training: {ex.Message}");
            }
        }

        static void StartContinuousTrainer()
        {
            string csvPath = _experimentalMode ? PlatformConfig.CsvPathExp : PlatformConfig.CsvPath;
            string modelDir = _experimentalMode ? _modelDirPathExp : _modelDirPath;
            string trainerArgs = $"\"{FindScript("background_trainer.py")}\" --seed --csv \"{csvPath}\" --model-dir \"{modelDir}\" --check-interval {TRAINER_CHECK_INTERVAL_SEC}{(_experimentalMode ? " --schema v4" : "")}";
            if (_forceTrainedModel)
                trainerArgs += " --min-total-samples 1";

            Console.WriteLine("[ML Trainer] Restarting continuous background trainer…");

            _mlTrainerProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = FindPython(),
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

            TrainerState.RefreshFromCsv();
            _analyticsWindow?.RequestTrainingViewRefresh();
        }

        static void StartNudge(int interval)
        {
            _intervalMinutes = interval;
            _nextSnapshotTime = DateTime.Now.AddMinutes(interval);

            try
            {
                // Kill any nudge daemon from a previous session so we start fresh
                if (PlatformConfig.IsWindows)
                {
                    try
                    {
                        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("nudge"))
                        {
                            try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                        }
                    }
                    catch { /* no previous process to kill */ }
                }
                else
                {
                    try
                    {
                        // Kill both native and dotnet-launched daemon variants
                        foreach (var pattern in new[] { "/nudge --interval", "nudge.dll --interval" })
                        {
                            var killOld = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "pkill",
                                    Arguments = $"-9 -f {pattern}",
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                }
                            };
                            killOld.Start();
                            // Fire-and-forget — don't block startup waiting for old process to die
                        }
                    }
                    catch { /* no previous process to kill */ }
                }

                // Prefer self-contained binary (release); fall back to dotnet dll (dev build)
                string nudgeExe = Path.Combine(_baseDir, PlatformConfig.IsWindows ? $"{NudgeExeName}.exe" : NudgeExeName);
                string nudgeDllPath = Path.Combine(_baseDir, NudgeDllName);
                bool useExe = File.Exists(nudgeExe);
                if (!useExe && !File.Exists(nudgeDllPath))
                {
                    Console.WriteLine($"✗ nudge not found (checked: {nudgeExe}, {nudgeDllPath})");
                    Environment.Exit(1);
                }

                // Build arguments
                string args = $"--interval {interval}";
                if (_mlCheckIntervalSeconds > 0)
                {
                    args += $" --ml-interval {_mlCheckIntervalSeconds}";
                }
                if (_mlEnabled)
                {
                    args += " --ml";
                }
                if (_forceTrainedModel)
                {
                    args += " --force-model";
                }
                if (_experimentalMode)
                {
                    args += " --experimental";
                }
                // Start the Nudge Harvest process
                _nudgeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = useExe ? nudgeExe : PlatformConfig.DotnetCommand,
                        Arguments = useExe ? args : $"\"{nudgeDllPath}\" {args}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _nudgeProcess.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    // Only log non-routine lines to keep console output useful
                    bool isHighFrequency =
                        e.Data.StartsWith("APPFOCUS:", StringComparison.Ordinal) ||
                        e.Data.StartsWith("HARVEST:", StringComparison.Ordinal) ||
                        e.Data.StartsWith("MEETING:", StringComparison.Ordinal) ||
                        e.Data.StartsWith("MLNEXT:", StringComparison.Ordinal);

                    if (!isHighFrequency)
                        Console.WriteLine($"[Nudge Harvest] {e.Data}");

                    if (e.Data.Trim() == "SNAPSHOT")
                        HandleSnapshot();
                    else if (e.Data.StartsWith("MLDATA:", StringComparison.Ordinal))
                        HandleMLData(e.Data.AsSpan(7));
                    else if (e.Data.StartsWith("MLRESPONSE:", StringComparison.Ordinal))
                        HandleMLResponse(e.Data.AsSpan(11));
                    else if (e.Data.StartsWith("MLNEXT:", StringComparison.Ordinal))
                        HandleMLNext(e.Data.AsSpan(7));
                    else if (e.Data.StartsWith("APPFOCUS:", StringComparison.Ordinal))
                        HandleAppFocus(e.Data.AsSpan(9));
                    else if (e.Data.StartsWith("HARVEST:", StringComparison.Ordinal))
                        HandleHarvest(e.Data.AsSpan(8));
                    else if (e.Data.StartsWith("MEETING:", StringComparison.Ordinal))
                        HandleMeeting(e.Data.AsSpan(8));
                    else if (e.Data.StartsWith("SUPPRESS:", StringComparison.Ordinal))
                        HandleSuppress(e.Data.AsSpan(9));
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

                Console.WriteLine($"✓ Nudge Harvest started");
                if (_mlEnabled)
                {
                    Console.WriteLine("  ML mode enabled - waiting for inference server connection...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to start Nudge Harvest: {ex.Message}");
                throw;
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // IPC message handlers — dispatched from nudge daemon stdout

        static void HandleSnapshot() => ShowSnapshotNotification();

        static void HandleMLData(ReadOnlySpan<char> payload)
        {
            try
            {
                var evt = JsonSerializer.Deserialize(payload, NudgeJsonContext.Default.MLLiveEvent);
                if (evt != null)
                    LiveAIState.Add(evt);
            }
            catch { /* non-critical, silently ignore parse errors */ }
        }

        static void HandleMLResponse(ReadOnlySpan<char> payload)
        {
            try
            {
                var resp = JsonSerializer.Deserialize(payload, NudgeJsonContext.Default.MLResponseEvent);
                if (resp != null)
                {
                    LiveAIState.UpdateResponse(resp.T, resp.Response);
                    TrainerState.RefreshFromCsv();
                    _analyticsWindow?.RequestTrainingViewRefresh();
                }
            }
            catch { /* non-critical, silently ignore parse errors */ }
        }

        static void HandleMLNext(ReadOnlySpan<char> payload)
        {
            if (long.TryParse(payload, out long ts))
            {
                LiveAIState.NextCheckAt = ts;
                LiveAIState.LastMlNextTick = Environment.TickCount64;
            }
        }

        static void HandleAppFocus(ReadOnlySpan<char> payload)
        {
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

        static void HandleHarvest(ReadOnlySpan<char> payload)
        {
            try
            {
                var sig = JsonSerializer.Deserialize(payload, NudgeJsonContext.Default.HarvestSignal);
                if (sig != null)
                {
                    LiveAIState.LastHarvest = sig;
                    System.Threading.Interlocked.Increment(ref LiveAIState.UpdateVersion);
                    var now = DateTime.UtcNow;
                    if ((now - _lastHarvestRefresh).TotalSeconds >= 1)
                    {
                        _lastHarvestRefresh = now;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            AnalyticsWindow.UpdateLivePulseDot());
                    }
                }
            }
            catch { /* non-critical */ }
        }

        static void HandleMeeting(ReadOnlySpan<char> payload)
        {
            var span = payload;
            int pipe1 = span.IndexOf('|');
            int pipe2 = pipe1 >= 0 ? span.Slice(pipe1 + 1).IndexOf('|') : -1;
            if (pipe1 >= 0 && pipe2 >= 0)
            {
                if (bool.TryParse(span.Slice(0, pipe1), out bool mic))
                    LiveAIState.InMeeting = mic;
                if (bool.TryParse(span.Slice(pipe1 + 1, pipe2), out bool cam))
                    LiveAIState.InMeeting |= cam;
                if (bool.TryParse(span.Slice(pipe1 + pipe2 + 2), out bool sharing))
                    LiveAIState.ScreenSharing = sharing;
                System.Threading.Interlocked.Increment(ref LiveAIState.UpdateVersion);
                _analyticsWindow?.RequestTrainingViewRefresh();
            }
        }

        static void HandleSuppress(ReadOnlySpan<char> payload)
        {
            string reason = payload.ToString();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!SuppressionDeduplication.TryMutateLatest(LiveAIState.Latest, reason, now))
            {
                // Standalone suppression (e.g. meeting detected before ML check ran).
                // No MLDATA was emitted, so create a new placeholder event.
                LiveAIState.Add(new MLLiveEvent
                {
                    T              = now,
                    App            = LiveAIState.CurrentApp,
                    SuppressReason = reason,
                    Score          = 0,
                    Confidence     = 0,
                    Productive     = true,
                    Triggered      = false,
                    TriggerSource  = "sup"
                });
            }
            _analyticsWindow?.RequestTrainingViewRefresh();
        }

        public static void ShowSnapshotNotification()
        {
            // Don't show notification if paused — auto-respond SKIP instead
            if (_notificationsPaused)
            {
                SendSkip();
                return;
            }

            // Don't show notification if already waiting for a response
            if (_waitingForResponse)
            {
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

                _waitingForResponse = true;

                // Capture the app that was focused before the notification appeared
                var previousApp = LiveAIState.CurrentApp ?? "";

                // Create and show custom notification window on Avalonia UI thread (works on all platforms)
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var notifHarvest = LiveAIState.LastHarvest;
                        bool notifIsBrowser = notifHarvest is { Browser: 1 } || BrowserDetector.IsBrowser(LiveAIState.CurrentApp ?? "");
                        string appName = notifIsBrowser
                            ? ((notifHarvest is { Browser: 1 } && !string.IsNullOrEmpty(notifHarvest.Domain))
                                ? notifHarvest.Domain
                                : BrowserDetector.GetBrowserDisplayName(LiveAIState.CurrentApp) ?? LiveAIState.CurrentApp ?? "")
                            : LiveAIState.CurrentApp ?? "";
                        string detail = notifIsBrowser ? "" : LiveAIState.CurrentDetail ?? "";
                        var notificationWindow = new CustomNotificationWindow(appName, detail);
                        notificationWindow.Closed += (s, e) => RestorePreviousAppFocus(previousApp);
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
        // FOCUS RESTORATION
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private static void RestorePreviousAppFocus(string appName)
        {
            if (string.IsNullOrEmpty(appName))
                return;

            try
            {
                if (OperatingSystem.IsLinux())
                {
                    // Strip browser site suffix like "firefox (youtube.com)" -> "firefox"
                    string searchName = appName;
                    int parenIndex = appName.IndexOf(" (", StringComparison.Ordinal);
                    if (parenIndex > 0)
                        searchName = appName[..parenIndex].ToLowerInvariant();

                    // KDE Wayland: use KWin scripting to activate by resourceClass (app_id).
                    // Writes a temp script that KWin loads and evaluates immediately.
                    try
                    {
                        // Sanitize searchName for JS string literal (single-quoted)
                        string safe = searchName.Replace("\\", "\\\\").Replace("'", "\\'");
                        string js = $"var c=workspace.clientList();for(var i=0;i<c.length;i++){{if(c[i].resourceClass&&c[i].resourceClass.toString().toLowerCase()==='{safe}'){{workspace.activeClient=c[i];break;}}}}";
                        string tmp = System.IO.Path.GetTempFileName() + ".js";
                        System.IO.File.WriteAllText(tmp, js);
                        var load = System.Diagnostics.Process.Start("qdbus",
                            $"org.kde.KWin /Scripting org.kde.kwin.Scripting.loadScript \"{tmp}\"");
                        load?.WaitForExit(2000);
                        var unload = System.Diagnostics.Process.Start("qdbus",
                            $"org.kde.KWin /Scripting org.kde.kwin.Scripting.unloadScript \"{System.IO.Path.GetFileNameWithoutExtension(tmp)}\"");
                        unload?.WaitForExit(1000);
                        try { System.IO.File.Delete(tmp); } catch { }
                    }
                    catch
                    {
                        // KWin scripting unavailable — fall through to X11 tools
                    }

                    // Try xdotool (works on X11, not Wayland)
                    using var xd = Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdotool",
                        Arguments = $"search --class --onlyvisible --limit 1 \"{searchName}\" windowactivate",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                    });
                    if (xd != null)
                    {
                        xd.WaitForExit(2000);
                        if (xd.ExitCode == 0)
                            return;
                    }

                    // Fallback: wmctrl
                    Process.Start("wmctrl", $"-a \"{appName}\"")?.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not restore focus to '{appName}': {ex.Message}");
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // COMMON FUNCTIONS
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        public static void SendSkip()
        {
            try
            {
                using var udp = new UdpClient();
                var endpoint = new IPEndPoint(IPAddress.Loopback, UDP_PORT);
                var message = "SKIP";
                var bytes = Encoding.UTF8.GetBytes(message);
                udp.Send(bytes, bytes.Length, endpoint);
                Console.WriteLine($"✓ Sent SKIP (notifications paused)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to send SKIP: {ex.Message}");
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

                // Retrain the model immediately so the next prediction reflects this feedback
                TriggerTrainingNow();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to send response: {ex.Message}");
            }
        }

        public static void Quit()
        {
            Console.WriteLine("[INFO] Quit() called - shutting down Nudge...");

            // Flush prediction history to disk
            LiveAIState.FlushToDisk();

            // Stop menu countdown timer
            _menuCountdownTimer?.Stop();

            // Stop ML services
            if (_mlInferenceProcess != null && !_mlInferenceProcess.HasExited)
            {
                try
                {
                    Console.WriteLine("  Stopping ML inference service...");
                    _mlInferenceProcess.Kill(entireProcessTree: true);
                    _mlInferenceProcess.WaitForExit(3000);
                    _mlInferenceProcess.Dispose();
                }
                catch { }
            }

            if (_mlTrainerProcess != null && !_mlTrainerProcess.HasExited)
            {
                try
                {
                    Console.WriteLine("  Stopping background trainer...");
                    _mlTrainerProcess.Kill(entireProcessTree: true);
                    _mlTrainerProcess.WaitForExit(3000);
                    _mlTrainerProcess.Dispose();
                }
                catch { }
            }

            // Stop main nudge process
            if (_nudgeProcess != null && !_nudgeProcess.HasExited)
            {
                Console.WriteLine($"[INFO] Nudge process PID: {_nudgeProcess.Id}");
                Console.WriteLine("[INFO] Attempting to kill nudge process...");

                try
                {
                    _nudgeProcess.Kill(entireProcessTree: true); // Kill process and all children
                    _nudgeProcess.WaitForExit(5000); // Wait up to 5 seconds

                    if (_nudgeProcess.HasExited)
                    {
                        Console.WriteLine($"[INFO] Nudge process terminated (exit code: {_nudgeProcess.ExitCode})");
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
                Console.WriteLine("[INFO] Nudge process already exited or null");
            }

            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
            }
#if WINDOWS
            if (NativeTray.IsInitialized)
                NativeTray.RemoveIcon();
#endif

            Console.WriteLine("✓ Shutdown complete");
            Console.WriteLine("[INFO] Exiting nudge-tray...");

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
                    MlEnabled              = _mlEnabled,
                    IntervalMinutes        = _intervalMinutes > 0 ? _intervalMinutes : 5,
                    MlCheckIntervalSeconds = _mlCheckIntervalSeconds > 0 ? _mlCheckIntervalSeconds : 60,
                    ExperimentalSignalMode = _experimentalMode,
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
                    var settings = JsonSerializer.Deserialize(json, NudgeJsonContext.Default.TraySettings);
                    if (settings != null)
                    {
                        _experimentalMode = settings.ExperimentalSignalMode;
                    }
                    return settings;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not load settings: {ex.Message}");
            }
            return null;
        }

        internal static void TogglePauseNotifications()
        {
            _notificationsPaused = !_notificationsPaused;
            Console.WriteLine($"[INFO] Notifications {(_notificationsPaused ? "PAUSED" : "RESUMED")}");

            if (_statusItem != null)
                _statusItem.Header = GetMenuStatusText();
#if WINDOWS
            if (NativeTray.IsInitialized)
                NativeTray.SetStatusText(GetMenuStatusText());
#endif
        }

        public static void UpdateSettings(int? mlInterval = null, int? interval = null, bool? experimental = null)
        {
            bool changed = false;
            if (mlInterval.HasValue && mlInterval.Value != _mlCheckIntervalSeconds)
            {
                _mlCheckIntervalSeconds = mlInterval.Value;
                changed = true;
            }
            if (interval.HasValue && interval.Value != _intervalMinutes)
            {
                _intervalMinutes = interval.Value;
                changed = true;
            }
            if (experimental.HasValue && experimental.Value != _experimentalMode)
            {
                _experimentalMode = experimental.Value;
                changed = true;
            }

            if (changed)
            {
                // Immediately update the countdown so the Prediction History bar
                // reflects the new interval without waiting for the daemon restart.
                if (mlInterval.HasValue)
                    LiveAIState.NextCheckAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + mlInterval.Value;
                else if (_mlCheckIntervalSeconds > 0)
                    LiveAIState.NextCheckAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _mlCheckIntervalSeconds;
                LiveAIState.LastMlNextTick = Environment.TickCount64;

                SaveSettings();
                RestartHarvestProcess();
            }
        }

        public static bool RestartWithML()
        {
            try
            {
                Console.WriteLine("[INFO] Starting AI setup...");
                MlSetupError = "";

                // Clean up existing processes before installation begins
                CleanupOldProcesses();

                // Install dependencies and start services.
                // StartMLServices() sets _mlEnabled = true on success, false on failure.
                StartMLServices();

                // Always restart nudge after cleanup, regardless of ML setup result.
                // On failure we still need the nudge process running so the countdown
                // and snapshot cycle keep working.
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

                if (!_mlEnabled)
                {
                    Console.WriteLine("[INFO] AI setup failed — ML not enabled.");
                    return false;
                }

                // Persist only after a successful install so a failed attempt
                // doesn't leave MlEnabled=true in settings on the next startup.
                SaveSettings();

                Console.WriteLine("[INFO] ML mode enabled and nudge restarted");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to enable ML: {ex.Message}");
                _mlEnabled = false;
                // Ensure nudge keeps running even if setup threw
                try { StartNudge(_intervalMinutes); } catch { }
                return false;
            }
        }

        internal static string CurrentVersion => VERSION;

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

            // When experimental mode changes, the sidecars must also swap ports/model dirs.
            if (_mlEnabled)
            {
                foreach (var mlProc in new[] { _mlInferenceProcess, _mlTrainerProcess })
                {
                    if (mlProc != null && !mlProc.HasExited)
                    {
                        try { mlProc.Kill(entireProcessTree: true); mlProc.WaitForExit(2000); } catch { }
                    }
                }
                _mlInferenceProcess = null;
                _mlTrainerProcess = null;
                StartMLServices();
            }

            StartNudge(_intervalMinutes > 0 ? _intervalMinutes : 5);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Trainer state — fed by background_trainer.py stdout, read by AI tab.
    // ─────────────────────────────────────────────────────────────────────────────
    // Avalonia application class - used on all platforms for custom notifications
    public class App : Avalonia.Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Hidden window needed on all platforms:
                // - Linux: keeps the app alive so the tray icon stays registered
                // - Windows: provides the HWND that receives tray icon callback messages
                //   (WM_CONTEXTMENU for right-click, WM_APP for notifications).
                //   Without it, right-click menu won't appear.
                var mainWindow = new Window
                {
                    IsVisible = false,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    CanResize = false,
                    Width = 0,
                    Height = 0,
                    Topmost = false,
                    WindowDecorations = Avalonia.Controls.WindowDecorations.None,
                    Background = Brushes.Transparent,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Position = new PixelPoint(-32000, -32000),
                    Opacity = 0
                };

#if WINDOWS
                // Hide from Alt+Tab by overriding the extended window style.
                // ShowInTaskbar=false removes WS_EX_APPWINDOW but doesn't prevent
                // Alt+Tab appearance on all Windows versions. WS_EX_TOOLWINDOW
                // explicitly excludes the window from the task switcher.
                mainWindow.Opened += (_, _) =>
                {
                    try
                    {
                        var h = mainWindow.TryGetPlatformHandle()?.Handle;
                        if (h.HasValue && h.Value != 0)
                        {
                            var hwnd = h.Value;
                            var exStyle = Program.GetWindowLong(hwnd, Program.GWL_EXSTYLE);
                            exStyle &= ~Program.WS_EX_APPWINDOW;
                            exStyle |= Program.WS_EX_TOOLWINDOW;
                            _ = Program.SetWindowLong(hwnd, Program.GWL_EXSTYLE, exStyle);
                        }
                    }
                    catch { }

                    // Initialize native Win32 tray icon (replaces Avalonia's broken one)
                    if (Program._useNativeTray)
                    {
                        Program.InitializeNativeTray();
                    }
                };
#endif

                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
