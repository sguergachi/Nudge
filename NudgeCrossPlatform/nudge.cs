#!/usr/bin/env dotnet run
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Nudge - ML-Powered Productivity Tracker
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// Tracks your activity, learns from your responses, nudges you to stay productive.
// Built with obsessive attention to detail.
//
// Usage:
//   nudge [options] [csv-path]
//
// Options:
//   --help, -h        Show this help
//   --version, -v     Show version info
//   --interval N      Snapshot interval in minutes (default: 5)
//
// Example:
//   nudge                    # Use defaults
//   nudge /data/harvest.csv  # Custom CSV path
//   nudge --interval 2       # Snapshot every 2 minutes
//
// Requirements:
//   - Wayland compositor (Sway, GNOME, or KDE Plasma)
//   - .NET 8.0 or later
//
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

class Nudge
{
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // VERSION & CONSTANTS
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    const string VERSION = "1.0.0";
    const int CYCLE_MS = 1000;           // 1 second monitoring cycle
    const int UDP_PORT = 45001;          // UDP listener port
    const int RESPONSE_TIMEOUT_MS = 60000; // 60 seconds to respond

    static int SNAPSHOT_INTERVAL_MS = 5 * 60 * 1000;  // 5 minutes (configurable)

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ANSI COLORS - Professional terminal output
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static class Color
    {
        public const string RESET = "\u001b[0m";
        public const string BOLD = "\u001b[1m";
        public const string DIM = "\u001b[2m";

        public const string RED = "\u001b[31m";
        public const string GREEN = "\u001b[32m";
        public const string YELLOW = "\u001b[33m";
        public const string BLUE = "\u001b[34m";
        public const string MAGENTA = "\u001b[35m";
        public const string CYAN = "\u001b[36m";
        public const string WHITE = "\u001b[37m";

        public const string BRED = "\u001b[1;31m";      // Bold red
        public const string BGREEN = "\u001b[1;32m";    // Bold green
        public const string BYELLOW = "\u001b[1;33m";   // Bold yellow
        public const string BCYAN = "\u001b[1;36m";     // Bold cyan
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // STATE - Application state
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static string _compositor = "";
    static string _csvPath = "/tmp/HARVEST.CSV";
    static StreamWriter? _csvFile;

    // Activity tracking
    static string _currentApp = "";
    static int _attentionSpanMs = 0;
    static bool _waitingForResponse = false;
    static int _totalSnapshots = 0;

    // Snapshot state (captured when snapshot is taken)
    static string _snapshotApp = "";
    static int _snapshotIdle = 0;
    static int _snapshotAttention = 0;
    static System.Threading.Timer? _responseTimer;

    // Performance caching (avoid excessive process spawning)
    static string _cachedApp = "";
    static DateTime _appCacheExpiry = DateTime.MinValue;
    static int _cachedIdle = 0;
    static DateTime _idleCacheExpiry = DateTime.MinValue;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MAIN - Entry point with professional argument parsing
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void Main(string[] args)
    {
        // Parse arguments
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--help" || arg == "-h")
            {
                ShowHelp();
                return;
            }
            if (arg == "--version" || arg == "-v")
            {
                Console.WriteLine($"Nudge v{VERSION}");
                return;
            }
            if (arg == "--interval" || arg == "-i")
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int minutes))
                {
                    SNAPSHOT_INTERVAL_MS = minutes * 60 * 1000;
                    i++; // Skip the interval value
                }
                continue;
            }
            if (!arg.StartsWith("--") && !arg.StartsWith("-"))
            {
                _csvPath = arg;
            }
        }

        // Welcome banner
        PrintBanner();

        // System validation
        if (!ValidateEnvironment())
        {
            Error("Environment validation failed. Cannot continue.");
            return;
        }

        // Initialize
        InitializeCSV();
        StartUDPListener();

        // Main event loop
        Success("✓ Nudge is running");
        Info($"  Taking snapshots every {SNAPSHOT_INTERVAL_MS/1000/60} minutes");
        Info($"  Respond with: {Color.BCYAN}nudge-notify YES{Color.RESET} or {Color.BCYAN}nudge-notify NO{Color.RESET}");
        Console.WriteLine();

        RunMainLoop();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ENVIRONMENT VALIDATION - Check all requirements before running
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static bool ValidateEnvironment()
    {
        bool valid = true;

        // Check Wayland session
        Info("Checking environment...");

        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (sessionType != "wayland")
        {
            Warning($"Not running on Wayland (detected: {sessionType ?? "none"})");
            Warning("Nudge requires Wayland. Some features may not work.");
            valid = false;
        }

        // Detect compositor
        _compositor = DetectCompositor();
        if (_compositor == "unknown")
        {
            Error("Could not detect compositor");
            Error("Supported: Sway, GNOME, KDE Plasma");
            return false;
        }

        Success($"✓ Compositor: {_compositor}");

        // Check required commands
        var (cmd, desc) = _compositor switch
        {
            "sway" => ("swaymsg", "Sway IPC"),
            "gnome" => ("gdbus", "D-Bus communication"),
            "kde" => ("qdbus", "Qt D-Bus"),
            _ => ("", "")
        };

        if (!string.IsNullOrEmpty(cmd) && !CommandExists(cmd))
        {
            Error($"Required command not found: {cmd}");
            Error($"Install: {GetInstallCommand(cmd)}");
            return false;
        }

        Success($"✓ {desc} available");

        // Test window detection
        Info("Testing window detection...");
        var testApp = GetForegroundApp();
        if (testApp == "unknown" || string.IsNullOrEmpty(testApp))
        {
            Warning("Could not detect foreground window");
            Warning("Please ensure compositor is running correctly");
        }
        else
        {
            Success($"✓ Detected window: {Color.CYAN}{testApp}{Color.RESET}");
        }

        // Test idle time detection
        Info("Testing idle time detection...");
        var testIdle = GetIdleTime();
        if (testIdle >= 0)
        {
            Success($"✓ Idle time: {testIdle}ms");
        }
        else
        {
            Warning("Could not detect idle time");
        }

        Console.WriteLine();
        return valid;
    }

    static string GetInstallCommand(string cmd) => cmd switch
    {
        "swaymsg" => "sway (should be pre-installed with Sway)",
        "gdbus" => "glib2.0-bin (apt install glib2.0-bin)",
        "qdbus" => "qttools5-dev-tools (apt install qttools5-dev-tools)",
        _ => "check your package manager"
    };

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MAIN LOOP - Core event loop with professional status updates
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void RunMainLoop()
    {
        int elapsed = 0;
        int lastMinute = -1;

        while (true)
        {
            // Get current activity
            string app = GetForegroundApp();
            int idle = GetIdleTime();

            // Track attention span
            if (app != _currentApp)
            {
                if (!string.IsNullOrEmpty(_currentApp))
                {
                    Dim($"  Switched: {_currentApp} → {app}");
                }
                _currentApp = app;
                _attentionSpanMs = 0;
            }
            else
            {
                _attentionSpanMs += CYCLE_MS;
            }

            // Time for snapshot?
            elapsed += CYCLE_MS;
            if (elapsed >= SNAPSHOT_INTERVAL_MS && !_waitingForResponse)
            {
                TakeSnapshot(app, idle, _attentionSpanMs);
                elapsed = 0;
                lastMinute = -1; // Reset progress indicator
            }

            // Show progress every minute
            int currentMinute = elapsed / 60000;
            if (currentMinute > lastMinute && currentMinute < SNAPSHOT_INTERVAL_MS/60000 && !_waitingForResponse)
            {
                lastMinute = currentMinute;
                int remaining = (SNAPSHOT_INTERVAL_MS - elapsed) / 60000;
                Dim($"  {remaining} min until next snapshot  ({Color.CYAN}{app}{Color.RESET}, idle: {idle}ms)");
            }

            Thread.Sleep(CYCLE_MS);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // WAYLAND INTEGRATION - Professional compositor interaction
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static string DetectCompositor()
    {
        if (CommandExists("swaymsg"))
            return "sway";

        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        if (desktop?.Contains("GNOME") == true)
            return "gnome";
        if (desktop?.Contains("KDE") == true)
            return "kde";

        return "unknown";
    }

    static string GetForegroundApp()
    {
        if (DateTime.Now < _appCacheExpiry)
            return _cachedApp;

        string app = _compositor switch
        {
            "sway" => GetSwayFocusedApp(),
            "gnome" => GetGnomeFocusedApp(),
            "kde" => GetKDEFocusedApp(),
            _ => "unknown"
        };

        _cachedApp = app;
        _appCacheExpiry = DateTime.Now.AddMilliseconds(500);
        return app;
    }

    static string GetSwayFocusedApp()
    {
        try
        {
            var json = RunCommand("swaymsg", "-t get_tree");
            return ExtractFocusedAppFromSwayTree(json);
        }
        catch (Exception ex)
        {
            Dim($"  Sway error: {ex.Message}");
            return "unknown";
        }
    }

    static string ExtractFocusedAppFromSwayTree(string json)
    {
        // Parse JSON properly using System.Text.Json
        try
        {
            using var doc = JsonDocument.Parse(json);
            return FindFocusedNode(doc.RootElement);
        }
        catch
        {
            // Fallback to simple string parsing if JSON fails
            if (json.Contains("\"focused\":true"))
            {
                int idx = json.IndexOf("\"app_id\":\"");
                if (idx != -1)
                {
                    idx += 10;
                    int end = json.IndexOf("\"", idx);
                    if (end > idx)
                        return json.Substring(idx, end - idx);
                }
            }
            return "unknown";
        }
    }

    static string FindFocusedNode(JsonElement node)
    {
        // Recursive search for focused window in Sway tree
        if (node.TryGetProperty("focused", out var focused) && focused.GetBoolean())
        {
            if (node.TryGetProperty("app_id", out var appId))
            {
                var id = appId.GetString();
                return string.IsNullOrEmpty(id) ? "unknown" : id;
            }
        }

        if (node.TryGetProperty("nodes", out var nodes))
        {
            foreach (var child in nodes.EnumerateArray())
            {
                var result = FindFocusedNode(child);
                if (result != "unknown")
                    return result;
            }
        }

        if (node.TryGetProperty("floating_nodes", out var floatingNodes))
        {
            foreach (var child in floatingNodes.EnumerateArray())
            {
                var result = FindFocusedNode(child);
                if (result != "unknown")
                    return result;
            }
        }

        return "unknown";
    }

    static string GetGnomeFocusedApp()
    {
        try
        {
            var output = RunCommand("gdbus", "call --session --dest org.gnome.Shell " +
                "--object-path /org/gnome/Shell " +
                "--method org.gnome.Shell.Eval " +
                "\"global.display.focus_window.get_wm_class()\"");

            return ExtractQuotedString(output);
        }
        catch (Exception ex)
        {
            Dim($"  GNOME error: {ex.Message}");
            return "unknown";
        }
    }

    static string GetKDEFocusedApp()
    {
        try
        {
            // Get list of all windows
            var windows = RunCommand("qdbus", "org.kde.KWin /KWin org.kde.KWin.getWindowInfo 1");

            if (string.IsNullOrWhiteSpace(windows))
            {
                // Fallback: try to get active window via different method
                var activeWindow = RunCommand("xdotool", "getactivewindow getwindowname");
                if (!string.IsNullOrWhiteSpace(activeWindow))
                {
                    return activeWindow.Trim().Split('\n')[0];
                }
                return "unknown";
            }

            // Parse window info - look for the active window
            var lines = windows.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("resourceClass") || line.Contains("caption"))
                {
                    var parts = line.Split(':');
                    if (parts.Length > 1)
                    {
                        return parts[1].Trim();
                    }
                }
            }

            return "unknown";
        }
        catch (Exception ex)
        {
            // Don't log errors in cached function - too spammy
            return "unknown";
        }
    }

    static int GetIdleTime()
    {
        if (DateTime.Now < _idleCacheExpiry)
            return _cachedIdle;

        // Universal idle detection - tries multiple methods for cross-compositor support
        int idle = GetUniversalIdleTime();

        _cachedIdle = idle;
        _idleCacheExpiry = DateTime.Now.AddMilliseconds(100);
        return idle;
    }

    static int GetUniversalIdleTime()
    {
        // Method 1: Try org.freedesktop.ScreenSaver (universal - works on KDE, Sway, most compositors)
        // This is the most compatible method for both X11 and Wayland
        int idle = GetFreedesktopIdleTime();
        if (idle > 0) return idle;

        // Method 2: Try GNOME-specific Mutter idle monitor
        idle = GetGnomeIdleTime();
        if (idle > 0) return idle;

        return 0;
    }

    static int GetFreedesktopIdleTime()
    {
        try
        {
            // Try with qdbus first (KDE/Qt environments)
            var output = RunCommand("qdbus",
                "org.freedesktop.ScreenSaver " +
                "/org/freedesktop/ScreenSaver " +
                "org.freedesktop.ScreenSaver.GetSessionIdleTime");

            // Output is in seconds, convert to milliseconds
            if (int.TryParse(output.Trim(), out int seconds))
            {
                return seconds * 1000;
            }

            // Try with gdbus as fallback (GNOME/GTK environments)
            output = RunCommand("gdbus",
                "call --session " +
                "--dest org.freedesktop.ScreenSaver " +
                "--object-path /org/freedesktop/ScreenSaver " +
                "--method org.freedesktop.ScreenSaver.GetSessionIdleTime");

            // Parse output like "(uint32 123,)"
            var cleaned = output.Trim()
                .Replace("(", "")
                .Replace(")", "")
                .Replace("uint32", "")
                .Replace(",", "")
                .Trim();

            if (int.TryParse(cleaned, out seconds))
            {
                return seconds * 1000;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    static int GetGnomeIdleTime()
    {
        try
        {
            var output = RunCommand("gdbus",
                "call --session " +
                "--dest org.gnome.Mutter.IdleMonitor " +
                "--object-path /org/gnome/Mutter/IdleMonitor/Core " +
                "--method org.gnome.Mutter.IdleMonitor.GetIdletime");

            // Parse output like "(uint64 12345,)"
            var cleaned = output.Trim()
                .Replace("(", "")
                .Replace(")", "")
                .Replace("uint64", "")
                .Replace(",", "")
                .Trim();

            return int.TryParse(cleaned, out int ms) ? ms : 0;
        }
        catch
        {
            return 0;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // DATA COLLECTION - CSV management with professional error handling
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void InitializeCSV()
    {
        try
        {
            bool exists = File.Exists(_csvPath);
            var dir = Path.GetDirectoryName(_csvPath);

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _csvFile = new StreamWriter(_csvPath, append: true);
            _csvFile.AutoFlush = true; // Ensure data is written immediately

            if (!exists)
            {
                _csvFile.WriteLine("foreground_app,idle_time,time_last_request,productive");
                Info($"Created new CSV: {_csvPath}");
            }
            else
            {
                Info($"Appending to: {_csvPath}");
            }
        }
        catch (Exception ex)
        {
            Error($"Failed to initialize CSV: {ex.Message}");
            throw;
        }
    }

    static void TakeSnapshot(string app, int idle, int attention)
    {
        int appHash = GetHash(app);
        _totalSnapshots++;

        // Capture snapshot state to avoid race conditions
        _snapshotApp = app;
        _snapshotIdle = idle;
        _snapshotAttention = attention;

        Console.WriteLine();
        Console.WriteLine($"{Color.BYELLOW}━━━ SNAPSHOT #{_totalSnapshots} ━━━{Color.RESET}");
        Console.WriteLine($"  {Color.BOLD}App:{Color.RESET}       {Color.CYAN}{app}{Color.RESET}");
        Console.WriteLine($"  {Color.BOLD}Hash:{Color.RESET}      {appHash}");
        Console.WriteLine($"  {Color.BOLD}Idle:{Color.RESET}      {FormatTime(idle)}");
        Console.WriteLine($"  {Color.BOLD}Attention:{Color.RESET} {FormatTime(attention)}");
        Console.WriteLine();
        Console.WriteLine($"  {Color.MAGENTA}❯{Color.RESET} Waiting for response...");
        Console.WriteLine($"  {Color.DIM}Run: {Color.BCYAN}nudge-notify YES{Color.DIM} or {Color.BCYAN}nudge-notify NO{Color.RESET}");
        Console.WriteLine();

        // Notify tray application that a snapshot was taken
        Console.WriteLine("SNAPSHOT");

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
        }, null, RESPONSE_TIMEOUT_MS, Timeout.Infinite);
    }

    static void SaveSnapshot(string app, int idle, int attention, bool productive)
    {
        int appHash = GetHash(app);
        int productiveInt = productive ? 1 : 0;

        try
        {
            _csvFile?.WriteLine($"{appHash},{idle},{attention},{productiveInt}");

            var label = productive ?
                $"{Color.BGREEN}PRODUCTIVE{Color.RESET}" :
                $"{Color.YELLOW}NOT PRODUCTIVE{Color.RESET}";

            Success($"✓ Saved as {label}");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Error($"Failed to save: {ex.Message}");
        }
        finally
        {
            _waitingForResponse = false;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // UDP LISTENER - Network communication with detailed logging
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void StartUDPListener()
    {
        var thread = new Thread(RunUDPListener);
        thread.IsBackground = true;
        thread.Start();

        Success($"✓ UDP listener started on port {UDP_PORT}");
    }

    static void RunUDPListener()
    {
        UdpClient? listener = null;
        try
        {
            listener = new UdpClient(UDP_PORT);

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

                // Use captured snapshot state (not current state)
                string app = _snapshotApp;
                int idle = _snapshotIdle;
                int attention = _snapshotAttention;

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

                    default:
                        Warning($"Unknown message: '{message}'");
                        Warning("Expected: YES or NO");
                        break;
                }
            }
        }
        catch (SocketException ex) when (ex.ErrorCode == 10048 || ex.Message.Contains("already in use"))
        {
            Error($"UDP port {UDP_PORT} is already in use");
            Error("Another instance of Nudge may be running");
            Error("Please close the other instance or choose a different port");
        }
        catch (Exception ex)
        {
            Error($"UDP listener crashed: {ex.Message}");
            Error("Restart Nudge to resume data collection");
        }
        finally
        {
            listener?.Dispose();
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // UTILITIES - Helper functions
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static string RunCommand(string cmd, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return "";

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    static bool CommandExists(string cmd)
    {
        try
        {
            var output = RunCommand("which", cmd);
            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    static int GetHash(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // FNV-1a hash: fast, deterministic, good distribution
        const uint fnvPrime = 16777619;
        uint hash = 2166136261;

        foreach (char c in text)
        {
            hash ^= c;
            hash *= fnvPrime;
        }

        return unchecked((int)hash);
    }

    static string ExtractQuotedString(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("\""))
            return "unknown";

        int start = input.IndexOf("\"") + 1;
        int end = input.IndexOf("\"", start);

        return end > start ? input.Substring(start, end - start) : "unknown";
    }

    static string FormatTime(int ms)
    {
        if (ms < 1000)
            return $"{ms}ms";
        if (ms < 60000)
            return $"{ms/1000}s";

        int minutes = ms / 60000;
        int seconds = (ms % 60000) / 1000;
        return seconds > 0 ? $"{minutes}m {seconds}s" : $"{minutes}m";
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // CONSOLE OUTPUT - Professional logging with colors
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void Success(string msg) =>
        Console.WriteLine($"{Color.BGREEN}{msg}{Color.RESET}");

    static void Info(string msg) =>
        Console.WriteLine($"{Color.CYAN}{msg}{Color.RESET}");

    static void Warning(string msg) =>
        Console.WriteLine($"{Color.BYELLOW}{msg}{Color.RESET}");

    static void Error(string msg) =>
        Console.WriteLine($"{Color.BRED}{msg}{Color.RESET}");

    static void Dim(string msg) =>
        Console.WriteLine($"{Color.DIM}{msg}{Color.RESET}");

    static void PrintBanner()
    {
        Console.WriteLine();
        Console.WriteLine($"{Color.BCYAN}╔═══════════════════════════════════════════════╗{Color.RESET}");
        Console.WriteLine($"{Color.BCYAN}║{Color.RESET}  {Color.BOLD}Nudge{Color.RESET} - ML-Powered Productivity Tracker  {Color.BCYAN}║{Color.RESET}");
        Console.WriteLine($"{Color.BCYAN}║{Color.RESET}  {Color.DIM}Version {VERSION,-36}{Color.RESET}{Color.BCYAN}║{Color.RESET}");
        Console.WriteLine($"{Color.BCYAN}╚═══════════════════════════════════════════════╝{Color.RESET}");
        Console.WriteLine();
    }

    static void ShowHelp()
    {
        Console.WriteLine($"{Color.BOLD}Nudge{Color.RESET} - ML-Powered Productivity Tracker");
        Console.WriteLine($"Version {VERSION}");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}USAGE:{Color.RESET}");
        Console.WriteLine($"  nudge [options] [csv-path]");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}OPTIONS:{Color.RESET}");
        Console.WriteLine($"  {Color.CYAN}--help, -h{Color.RESET}         Show this help");
        Console.WriteLine($"  {Color.CYAN}--version, -v{Color.RESET}      Show version information");
        Console.WriteLine($"  {Color.CYAN}--interval, -i N{Color.RESET}   Snapshot interval in minutes (default: 5)");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}ARGUMENTS:{Color.RESET}");
        Console.WriteLine($"  {Color.CYAN}csv-path{Color.RESET}           Path to CSV file (default: /tmp/HARVEST.CSV)");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}EXAMPLES:{Color.RESET}");
        Console.WriteLine($"  nudge                          # Use defaults");
        Console.WriteLine($"  nudge /data/harvest.csv        # Custom CSV path");
        Console.WriteLine($"  nudge --interval 2             # Snapshot every 2 minutes");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}RESPONDING TO SNAPSHOTS:{Color.RESET}");
        Console.WriteLine($"  In another terminal, run:");
        Console.WriteLine($"    {Color.BGREEN}nudge-notify YES{Color.RESET}   # I was productive");
        Console.WriteLine($"    {Color.YELLOW}nudge-notify NO{Color.RESET}    # I was not productive");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}REQUIREMENTS:{Color.RESET}");
        Console.WriteLine($"  - Wayland compositor (Sway, GNOME, or KDE Plasma)");
        Console.WriteLine($"  - .NET 8.0 or later");
        Console.WriteLine();
    }
}
