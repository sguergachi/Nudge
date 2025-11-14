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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
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
using System.Management;
#endif

namespace NudgeTray
{
    // Shared platform configuration (same as in nudge.cs)
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

    // Simple ICommand implementation for TrayIcon menu items
    class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter)
        {
            Console.WriteLine($"[DEBUG] RelayCommand.Execute called with parameter: {parameter}");
            _execute(parameter);
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
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
        static System.Threading.Timer? _menuRefreshTimer;

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
                Console.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");
                if (e.ExceptionObject is Exception ex)
                {
                    Console.WriteLine($"[FATAL] Message: {ex.Message}");
                    Console.WriteLine($"[FATAL] Stack: {ex.StackTrace}");
                }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Console.WriteLine($"[FATAL] Unobserved task exception: {e.Exception.Message}");
                e.SetObserved();
            };

#if WINDOWS
            // Attach to parent console for logging (when run from terminal)
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }
#endif

            // Kill any existing nudge-tray processes to ensure single instance
            KillExistingInstances();

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

            // Start ML services if enabled
            if (_mlEnabled)
            {
                StartMLServices();
            }

            _intervalMinutes = interval;

            // Use Avalonia for cross-platform tray icon on all platforms
            BuildAvaloniaApp(interval).StartWithClassicDesktopLifetime(args);
        }

        static void KillExistingInstances()
        {
            int totalKilled = 0;

            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var currentProcessId = currentProcess.Id;

                // 1. Kill other nudge-tray instances
                Console.WriteLine("[CLEANUP] Checking for existing nudge-tray processes...");
                totalKilled += KillProcessesByName("nudge-tray", currentProcessId);

                // 2. Kill main nudge process
                Console.WriteLine("[CLEANUP] Checking for existing nudge processes...");
                totalKilled += KillProcessesByName("nudge", -1);

                // 3. Kill Python ML processes
                Console.WriteLine("[CLEANUP] Checking for Python ML processes...");
                totalKilled += KillPythonProcesses("model_inference.py");
                totalKilled += KillPythonProcesses("train_model.py");

                if (totalKilled > 0)
                {
                    Console.WriteLine($"[CLEANUP] ✓ Killed {totalKilled} existing process(es) total");
                    Thread.Sleep(1000); // Give OS time to clean up
                }
                else
                {
                    Console.WriteLine("[CLEANUP] ✓ No existing instances found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLEANUP] Error during cleanup: {ex.Message}");
            }
        }

        static int KillProcessesByName(string processName, int excludeProcessId)
        {
            int killedCount = 0;
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    if (process.Id != excludeProcessId)
                    {
                        try
                        {
                            Console.WriteLine($"[CLEANUP]   Killing {processName} (PID {process.Id})...");
                            process.Kill();
                            process.WaitForExit(2000);
                            killedCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[CLEANUP]   Failed to kill PID {process.Id}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLEANUP]   Error killing {processName}: {ex.Message}");
            }
            return killedCount;
        }

        static int KillPythonProcesses(string scriptName)
        {
            int killedCount = 0;
            try
            {
                var pythonProcesses = Process.GetProcessesByName("python")
                    .Concat(Process.GetProcessesByName("python3"));

                foreach (var process in pythonProcesses)
                {
                    try
                    {
                        string commandLine = GetProcessCommandLine(process);
                        if (commandLine.Contains(scriptName))
                        {
                            Console.WriteLine($"[CLEANUP]   Killing Python {scriptName} (PID {process.Id})...");
                            process.Kill();
                            process.WaitForExit(2000);
                            killedCount++;
                        }
                    }
                    catch { /* Ignore if we can't read command line or kill */ }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLEANUP]   Error killing Python {scriptName}: {ex.Message}");
            }
            return killedCount;
        }

        static string GetProcessCommandLine(Process process)
        {
            try
            {
                #if WINDOWS
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
                {
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        return obj["CommandLine"]?.ToString() ?? "";
                    }
                }
                #else
                // On Linux, read from /proc/[pid]/cmdline
                string cmdlinePath = $"/proc/{process.Id}/cmdline";
                if (File.Exists(cmdlinePath))
                {
                    return File.ReadAllText(cmdlinePath).Replace('\0', ' ');
                }
                #endif
            }
            catch { }
            return "";
        }

        static AppBuilder BuildAvaloniaApp(int interval)
        {
            return AppBuilder.Configure(() => new App(interval))
                .UsePlatformDetect()
                .LogToTrace();
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

        public static void CreateTrayIconForApp(Avalonia.Application app)
        {
            Console.WriteLine("\n═══════════════════════════════════════════════════════════");
            Console.WriteLine("  CRITICAL FIX VALIDATION TEST");
            Console.WriteLine("  Click events CRASH - Testing ICommand as fix");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("  🔴 RED    = NO menu");
            Console.WriteLine("  🟢 GREEN  = Empty menu - ✓ WORKS");
            Console.WriteLine("  🔵 BLUE   = Menu without handler - ✓ WORKS");
            Console.WriteLine("  🟡 YELLOW = Click event - ⚠️  CRASHES");
            Console.WriteLine("  🟣 PURPLE = ICommand - 🧪 TESTING FIX");
            Console.WriteLine("═══════════════════════════════════════════════════════════\n");

            var icons = new TrayIcons();

            // TEST 1: RED - NO menu at all (control test)
            Console.WriteLine("[TEST 1 - RED] Creating icon with NO menu...");
            try
            {
                var icon1 = new TrayIcon
                {
                    Icon = CreateColoredIcon(0xFFFF0000), // RED
                    ToolTipText = "RED: No Menu",
                    IsVisible = true
                    // NO Menu property set!
                };
                icons.Add(icon1);
                Console.WriteLine("[TEST 1 - RED] ✓ Success - Icon with no menu created");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST 1 - RED] ✗ FAILED: {ex.Message}");
                Console.WriteLine($"                Stack: {ex.StackTrace}");
            }

            // TEST 2: GREEN - Empty menu (zero items)
            Console.WriteLine("\n[TEST 2 - GREEN] Creating icon with EMPTY menu...");
            try
            {
                var icon2 = new TrayIcon
                {
                    Icon = CreateColoredIcon(0xFF00FF00), // GREEN
                    ToolTipText = "GREEN: Empty Menu",
                    Menu = new NativeMenu(), // Empty menu, no items
                    IsVisible = true
                };
                icons.Add(icon2);
                Console.WriteLine("[TEST 2 - GREEN] ✓ Success - Icon with empty menu created");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST 2 - GREEN] ✗ FAILED: {ex.Message}");
                Console.WriteLine($"                 Stack: {ex.StackTrace}");
            }

            // TEST 3: BLUE - One menu item, NO Click handler
            Console.WriteLine("\n[TEST 3 - BLUE] Creating icon with ONE menu item, NO handler...");
            try
            {
                var menu3 = new NativeMenu();
                menu3.Items.Add(new NativeMenuItem("Test Item"));

                var icon3 = new TrayIcon
                {
                    Icon = CreateColoredIcon(0xFF0000FF), // BLUE
                    ToolTipText = "BLUE: 1 Item, No Handler",
                    Menu = menu3,
                    IsVisible = true
                };
                icons.Add(icon3);
                Console.WriteLine("[TEST 3 - BLUE] ✓ Success - Icon with 1 item (no handler) created");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST 3 - BLUE] ✗ FAILED: {ex.Message}");
                Console.WriteLine($"                Stack: {ex.StackTrace}");
            }

            // TEST 4: YELLOW - One menu item, WITH Click handler (CRASHES!)
            Console.WriteLine("\n[TEST 4 - YELLOW] Creating icon with ONE menu item, WITH Click handler...");
            Console.WriteLine("[TEST 4 - YELLOW] ⚠️  WARNING: This uses Click events and will CRASH!");
            try
            {
                var menuItem4 = new NativeMenuItem("Click Me (CRASH)");
                menuItem4.Click += (s, e) =>
                {
                    Console.WriteLine("[TEST 4 - YELLOW] >>> Menu item clicked! <<<");
                };

                var menu4 = new NativeMenu();
                menu4.Items.Add(menuItem4);

                var icon4 = new TrayIcon
                {
                    Icon = CreateColoredIcon(0xFFFFFF00), // YELLOW
                    ToolTipText = "YELLOW: Click Event (CRASHES)",
                    Menu = menu4,
                    IsVisible = true
                };
                icons.Add(icon4);
                Console.WriteLine("[TEST 4 - YELLOW] ✓ Created (will crash on right-click)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST 4 - YELLOW] ✗ FAILED: {ex.Message}");
                Console.WriteLine($"                  Stack: {ex.StackTrace}");
            }

            // TEST 5: PURPLE - Using ICommand instead of Click events (SHOULD WORK!)
            Console.WriteLine("\n[TEST 5 - PURPLE] Creating icon with ICommand (FIX ATTEMPT)...");
            try
            {
                // Create a command for menu actions
                var command = new RelayCommand(param =>
                {
                    var action = param as string;
                    Console.WriteLine($"[TEST 5 - PURPLE] >>> Command executed: {action} <<<");

                    if (action == "quit")
                    {
                        HandleQuitClicked();
                    }
                });

                var menuItem5a = new NativeMenuItem("Test ICommand")
                {
                    Command = command,
                    CommandParameter = "test"
                };

                var menuItem5b = new NativeMenuItem("Quit")
                {
                    Command = command,
                    CommandParameter = "quit"
                };

                var menu5 = new NativeMenu();
                menu5.Items.Add(menuItem5a);
                menu5.Items.Add(menuItem5b);

                var icon5 = new TrayIcon
                {
                    Icon = CreateColoredIcon(0xFFFF00FF), // PURPLE
                    ToolTipText = "PURPLE: ICommand (SHOULD WORK)",
                    Menu = menu5,
                    IsVisible = true
                };
                icons.Add(icon5);
                Console.WriteLine("[TEST 5 - PURPLE] ✓ Created with ICommand pattern");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST 5 - PURPLE] ✗ FAILED: {ex.Message}");
                Console.WriteLine($"                  Stack: {ex.StackTrace}");
            }

            // Set all icons
            Console.WriteLine("\n─────────────────────────────────────────────────────────");
            Console.WriteLine($"Setting {icons.Count} icons on Application...");
            TrayIcon.SetIcons(app, icons);
            Console.WriteLine("✓ All icons set successfully");
            Console.WriteLine("─────────────────────────────────────────────────────────");
            Console.WriteLine("\nTEST INSTRUCTIONS:");
            Console.WriteLine("1. You should see 5 colored circles in your system tray");
            Console.WriteLine("2. Right-click each one and observe:");
            Console.WriteLine("   - RED (no menu) - nothing happens");
            Console.WriteLine("   - GREEN (empty menu) - ✓ WORKS (no crash)");
            Console.WriteLine("   - BLUE (menu, no handler) - ✓ WORKS (shows menu)");
            Console.WriteLine("   - YELLOW (Click event) - ⚠️  CRASHES (confirms bug)");
            Console.WriteLine("   - PURPLE (ICommand) - 🧪 DOES IT WORK?");
            Console.WriteLine("\n3. If PURPLE works, we've found the solution!");
            Console.WriteLine("   Report: Does PURPLE icon crash or work?\n");

            // Store first icon for compatibility
            _trayIcon = icons.Count > 0 ? icons[0] : null;
        }

#if WINDOWS
        public static void InitializeNotifications()
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

                        // Refresh tray menu on UI thread
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (_trayIcon != null)
                            {
                                _trayIcon.Menu = CreateMenu();
                            }
                        });
                    }
                    else if (action == "no")
                    {
                        Console.WriteLine("[DEBUG] User clicked NO from notification");
                        _waitingForResponse = false;
                        SendResponse(false);

                        // Refresh tray menu on UI thread
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (_trayIcon != null)
                            {
                                _trayIcon.Menu = CreateMenu();
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to handle notification action: {ex.Message}");
            }
        }
#endif

        static NativeMenu CreateMenu()
        {
            // Default menu - use Commands
            return CreateMenuWithCommands("DEFAULT");
        }

        static NativeMenu CreateMenuWithCommands(string label)
        {
            Console.WriteLine($"[{label}] CreateMenuWithCommands() called");

            var menuCommand = new RelayCommand(parameter =>
            {
                var action = parameter as string;
                Console.WriteLine($"[{label}] Command executed: {action}");

                if (action?.Contains("quit") == true)
                {
                    HandleQuitClicked();
                }
                else
                {
                    Console.WriteLine($"[{label}] Test action: {action}");
                }
            });

            var menu = new NativeMenu
            {
                new NativeMenuItem($"Test Item [{label}]")
                {
                    Command = menuCommand,
                    CommandParameter = $"{label}-test"
                },
                new NativeMenuItem("Quit")
                {
                    Command = menuCommand,
                    CommandParameter = $"{label}-quit"
                }
            };

            Console.WriteLine($"[{label}] Menu created with {menu.Items.Count} items");
            return menu;
        }

        static NativeMenu CreateMenuWithClickEvents(string label)
        {
            Console.WriteLine($"[{label}] CreateMenuWithClickEvents() called");

            var testItem = new NativeMenuItem($"Test Item [{label}]");
            testItem.Click += (s, e) =>
            {
                Console.WriteLine($"[{label}] Test item clicked!");
            };

            var quitItem = new NativeMenuItem("Quit");
            quitItem.Click += (s, e) =>
            {
                Console.WriteLine($"[{label}] Quit clicked!");
                HandleQuitClicked();
            };

            var menu = new NativeMenu { testItem, quitItem };
            Console.WriteLine($"[{label}] Menu created with {menu.Items.Count} items");
            return menu;
        }

        static WindowIcon CreateColoredIcon(uint argbColor)
        {
            // Create a simple 16x16 icon with specified color
            var width = 16;
            var height = 16;
            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Premul);

            using (var fb = bitmap.Lock())
            {
                unsafe
                {
                    var ptr = (uint*)fb.Address.ToPointer();
                    var stride = fb.RowBytes / 4;

                    // Convert ARGB to BGRA
                    var a = (argbColor >> 24) & 0xFF;
                    var r = (argbColor >> 16) & 0xFF;
                    var g = (argbColor >> 8) & 0xFF;
                    var b = (argbColor >> 0) & 0xFF;
                    var bgraColor = (a << 24) | (r << 0) | (g << 8) | (b << 16);

                    // Draw a filled circle
                    var cx = width / 2;
                    var cy = height / 2;
                    var radius = 6;

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int dx = x - cx;
                            int dy = y - cy;

                            if (dx * dx + dy * dy <= radius * radius)
                            {
                                ptr[y * stride + x] = bgraColor;
                            }
                            else
                            {
                                // Transparent
                                ptr[y * stride + x] = 0x00000000;
                            }
                        }
                    }
                }
            }

            // Save to PNG stream
            var stream = new MemoryStream();
            bitmap.Save(stream);
            stream.Position = 0;

            return new WindowIcon(stream);
        }

        static WindowIcon CreateCommonIcon()
        {
            // Default: Blue icon
            return CreateColoredIcon(0xFF0088FF);
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

        public static void StartNudge(int interval)
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

        private static void ShowCustomNotification()
        {
            try
            {
                Console.WriteLine("[DEBUG] ShowCustomNotification called");

                _waitingForResponse = true;

                // Create and show custom notification window on Avalonia UI thread (works on all platforms)
                Dispatcher.UIThread.Post(() =>
                {
                    var notificationWindow = new CustomNotificationWindow();
                    notificationWindow.ShowWithAnimation((productive) =>
                    {
                        _waitingForResponse = false;
                        SendResponse(productive);

                        // Refresh tray menu
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (_trayIcon != null)
                            {
                                _trayIcon.Menu = CreateMenu();
                            }
                        });
                    });
                });

                Console.WriteLine("✓ Custom notification shown with animation");

                // Refresh tray menu to show waiting state
                Dispatcher.UIThread.Post(() =>
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.Menu = CreateMenu();
                    }
                });
            }
            catch (Exception ex)
            {
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

                // Refresh tray menu to show Yes/No options
                Dispatcher.UIThread.Post(() =>
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.Menu = CreateMenu();
                    }
                });
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

            if (_menuRefreshTimer != null)
            {
                _menuRefreshTimer.Dispose();
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
        private readonly int _interval;

        public App(int interval)
        {
            _interval = interval;
        }

        public override void Initialize()
        {
            // No XAML needed for headless tray app
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Console.WriteLine("[DEBUG] App.OnFrameworkInitializationCompleted called");

            // Initialize services and tray icon AFTER framework is fully initialized
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Console.WriteLine("[DEBUG] Classic desktop lifetime detected");

                // Start the nudge process
                Program.StartNudge(_interval);

#if WINDOWS
                // Initialize Windows toast notifications
                Program.InitializeNotifications();
#endif

                // Create the tray icon - use 'this' instead of Application.Current
                Program.CreateTrayIconForApp(this);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
