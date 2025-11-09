#!/usr/bin/env dotnet run
// Nudge - Productivity tracker with ML
// Single file, no abstractions, just code that works
//
// Build: csc -out:nudge nudge.cs /r:System.Net.Sockets.dll
// Run:   ./nudge
//
// Requirements:
// - Wayland compositor (Sway, GNOME, KDE)
// - CsvHelper nuget (or inline CSV writing)

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// ====================================================================================
// MAIN PROGRAM - Everything in one place, no jumping around
// ====================================================================================

class Nudge
{
    const int SNAPSHOT_INTERVAL_MS = 5 * 60 * 1000;  // 5 minutes
    const int CYCLE_MS = 1000;                        // 1 second
    const int UDP_PORT = 45001;

    // State
    static string _currentApp = "";
    static int _attentionSpanMs = 0;
    static string _csvPath = "/tmp/HARVEST.CSV";
    static StreamWriter? _csvFile;
    static bool _waitingForResponse = false;

    // Wayland compositor detection
    static string _compositor = "";

    // Caching (because process spawning is expensive)
    static string _cachedApp = "";
    static DateTime _appCacheExpiry = DateTime.MinValue;
    static int _cachedIdle = 0;
    static DateTime _idleCacheExpiry = DateTime.MinValue;

    static void Main(string[] args)
    {
        Console.WriteLine("=== Nudge - Productivity Tracker ===");
        Console.WriteLine("Jon Blow edition: No abstractions, just code\n");

        // Detect compositor
        _compositor = DetectCompositor();
        Console.WriteLine($"Compositor: {_compositor}");

        // Override CSV path if provided
        if (args.Length > 0)
            _csvPath = args[0];

        // Initialize CSV
        InitializeCSV();

        // Start UDP listener in background
        var udpThread = new Thread(RunUDPListener);
        udpThread.IsBackground = true;
        udpThread.Start();

        // Main loop - this is the actual program
        int elapsed = 0;
        while (true)
        {
            // Get current state from Wayland
            string app = GetForegroundApp();
            int idle = GetIdleTime();

            // Track attention span
            if (app != _currentApp)
            {
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
            }

            Thread.Sleep(CYCLE_MS);
        }
    }

    // ====================================================================================
    // WAYLAND - Get actual system state
    // ====================================================================================

    static string DetectCompositor()
    {
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (session != "wayland")
        {
            Console.WriteLine("WARNING: Not running on Wayland!");
            return "unknown";
        }

        // Check which compositor
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
        // Cache check - don't spawn processes every second
        if (DateTime.Now < _appCacheExpiry)
            return _cachedApp;

        string app = _compositor switch
        {
            "sway" => GetSwayFocusedApp(),
            "gnome" => GetGnomeFocusedApp(),
            "kde" => GetKDEFocusedApp(),
            _ => "unknown"
        };

        // Cache for 500ms
        _cachedApp = app;
        _appCacheExpiry = DateTime.Now.AddMilliseconds(500);
        return app;
    }

    static string GetSwayFocusedApp()
    {
        try
        {
            var output = RunCommand("swaymsg", "-t get_tree");
            // Parse JSON to find focused window
            // For now, simple approach: look for "focused":true and extract app_id
            if (output.Contains("\"focused\":true"))
            {
                int idx = output.IndexOf("\"app_id\":\"");
                if (idx != -1)
                {
                    idx += 10;
                    int end = output.IndexOf("\"", idx);
                    if (end != -1)
                        return output.Substring(idx, end - idx);
                }
            }
        }
        catch { }
        return "unknown";
    }

    static string GetGnomeFocusedApp()
    {
        try
        {
            // Use gdbus to query GNOME Shell
            var output = RunCommand("gdbus", "call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval \"global.display.focus_window.get_wm_class()\"");
            // Parse output
            if (output.Contains("\""))
            {
                int start = output.IndexOf("\"") + 1;
                int end = output.IndexOf("\"", start);
                if (end > start)
                    return output.Substring(start, end - start);
            }
        }
        catch { }
        return "unknown";
    }

    static string GetKDEFocusedApp()
    {
        try
        {
            var output = RunCommand("qdbus", "org.kde.KWin /KWin org.kde.KWin.currentDesktop");
            // KDE plasma integration - simplified
            // TODO: Proper implementation
        }
        catch { }
        return "unknown";
    }

    static int GetIdleTime()
    {
        // Cache check
        if (DateTime.Now < _idleCacheExpiry)
            return _cachedIdle;

        int idle = _compositor switch
        {
            "sway" => GetSwayIdleTime(),
            "gnome" => GetGnomeIdleTime(),
            _ => 0
        };

        // Cache for 100ms
        _cachedIdle = idle;
        _idleCacheExpiry = DateTime.Now.AddMilliseconds(100);
        return idle;
    }

    static int GetSwayIdleTime()
    {
        try
        {
            // Sway doesn't expose idle time directly
            // Use swayidle or check last input via libinput
            // For now, fallback to checking D-Bus
            return GetDBusIdleTime();
        }
        catch { }
        return 0;
    }

    static int GetGnomeIdleTime()
    {
        return GetDBusIdleTime();
    }

    static int GetDBusIdleTime()
    {
        try
        {
            var output = RunCommand("gdbus", "call --session --dest org.gnome.Mutter.IdleMonitor --object-path /org/gnome/Mutter/IdleMonitor/Core --method org.gnome.Mutter.IdleMonitor.GetIdletime");
            // Parse output like "(uint64 12345,)"
            var cleaned = output.Trim().Replace("(", "").Replace(")", "").Replace("uint64", "").Replace(",", "").Trim();
            if (int.TryParse(cleaned, out int ms))
                return ms;
        }
        catch { }
        return 0;
    }

    // ====================================================================================
    // DATA COLLECTION - Write to CSV when user responds
    // ====================================================================================

    static void InitializeCSV()
    {
        bool exists = File.Exists(_csvPath);
        _csvFile = new StreamWriter(_csvPath, append: true);

        if (!exists)
        {
            // Write header
            _csvFile.WriteLine("foreground_app,idle_time,time_last_request,productive");
        }

        Console.WriteLine($"CSV: {_csvPath}\n");
    }

    static void TakeSnapshot(string app, int idle, int attention)
    {
        int appHash = GetHash(app);

        Console.WriteLine("\n=== SNAPSHOT ===");
        Console.WriteLine($"App: {app}");
        Console.WriteLine($"App Hash: {appHash}");
        Console.WriteLine($"Idle: {idle}ms");
        Console.WriteLine($"Attention: {attention}ms");
        Console.WriteLine("\nWaiting for YES/NO response via UDP...");

        _waitingForResponse = true;

        // Start timeout thread - if no response in 60s, skip
        var timeout = new Thread(() =>
        {
            Thread.Sleep(60000);
            if (_waitingForResponse)
            {
                Console.WriteLine("\n⏱️  Timeout - skipping this snapshot");
                _waitingForResponse = false;
            }
        });
        timeout.IsBackground = true;
        timeout.Start();
    }

    static void SaveSnapshot(string app, int idle, int attention, bool productive)
    {
        int appHash = GetHash(app);
        int productiveInt = productive ? 1 : 0;

        // Write to CSV
        _csvFile?.WriteLine($"{appHash},{idle},{attention},{productiveInt}");
        _csvFile?.Flush();

        Console.WriteLine($"\n✓ Saved: productive={productiveInt}\n");
        _waitingForResponse = false;
    }

    // ====================================================================================
    // UDP LISTENER - Receive YES/NO from notifier
    // ====================================================================================

    static void RunUDPListener()
    {
        var listener = new UdpClient(UDP_PORT);
        Console.WriteLine($"UDP listening on port {UDP_PORT}");

        while (true)
        {
            try
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = listener.Receive(ref remote);
                string message = Encoding.UTF8.GetString(data).Trim().ToUpper();

                if (!_waitingForResponse)
                    continue;

                // Get current state
                string app = _cachedApp;
                int idle = _cachedIdle;
                int attention = _attentionSpanMs;

                if (message == "YES")
                {
                    Console.WriteLine("✓ Received: YES (productive)");
                    SaveSnapshot(app, idle, attention, productive: true);
                }
                else if (message == "NO")
                {
                    Console.WriteLine("✓ Received: NO (not productive)");
                    SaveSnapshot(app, idle, attention, productive: false);
                }
                else
                {
                    Console.WriteLine($"? Unknown response: {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP error: {ex.Message}");
            }
        }
    }

    // ====================================================================================
    // UTILITIES - Minimal helper functions
    // ====================================================================================

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

        // FNV-1a hash - simple, fast, deterministic
        const uint fnvPrime = 16777619;
        uint hash = 2166136261;

        foreach (char c in text)
        {
            hash ^= c;
            hash *= fnvPrime;
        }

        return unchecked((int)hash);
    }
}
