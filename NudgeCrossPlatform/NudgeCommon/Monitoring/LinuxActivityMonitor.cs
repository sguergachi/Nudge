using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NudgeCommon.Monitoring;

/// <summary>
/// Linux-specific activity monitor using X11 tools and /proc
/// Requires: xdotool, xprintidle packages
/// </summary>
public class LinuxActivityMonitor : IActivityMonitor
{
    private string _lastForegroundApp = string.Empty;
    private int _attentionSpanMs = 0;
    private readonly bool _hasXdotool;
    private readonly bool _hasXprintidle;

    // Performance optimization: Cache foreground app to reduce process spawning
    private string _cachedForegroundApp = string.Empty;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private const int CACHE_DURATION_MS = 500; // Cache for 500ms

    // Performance optimization: Use compiled regex
    private static readonly Regex WindowNamePattern =
        new Regex(@"[-:]?\s*([^-:]+)$", RegexOptions.Compiled);

    public LinuxActivityMonitor()
    {
        // Check if required tools are available
        _hasXdotool = CheckCommandExists("xdotool");
        _hasXprintidle = CheckCommandExists("xprintidle");

        if (!_hasXdotool)
        {
            Console.WriteLine("WARNING: xdotool not found. Install with: sudo apt-get install xdotool");
        }

        if (!_hasXprintidle)
        {
            Console.WriteLine("WARNING: xprintidle not found. Install with: sudo apt-get install xprintidle");
        }
    }

    public string GetForegroundApp()
    {
        // Performance optimization: Return cached value if still valid
        if (DateTime.Now < _cacheExpiry)
        {
            return _cachedForegroundApp;
        }

        if (!_hasXdotool)
        {
            return CacheAndReturn("unknown");
        }

        try
        {
            // Use xdotool to get the active window PID
            var pidOutput = ExecuteCommand("xdotool", "getactivewindow getwindowpid");
            if (int.TryParse(pidOutput.Trim(), out int pid))
            {
                // Get process name from /proc
                var commPath = $"/proc/{pid}/comm";
                if (File.Exists(commPath))
                {
                    var processName = File.ReadAllText(commPath).Trim();
                    return CacheAndReturn(processName);
                }
            }

            // Fallback: get window name
            var windowName = ExecuteCommand("xdotool", "getactivewindow getwindowname");
            return CacheAndReturn(ExtractAppFromWindowName(windowName));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting foreground app: {ex.Message}");
            return CacheAndReturn("unknown");
        }
    }

    private string CacheAndReturn(string appName)
    {
        _cachedForegroundApp = appName;
        _cacheExpiry = DateTime.Now.AddMilliseconds(CACHE_DURATION_MS);
        return appName;
    }

    public int GetKeyboardInactivityMs()
    {
        // X11 idle time covers both keyboard and mouse
        return GetIdleTimeMs();
    }

    public int GetMouseInactivityMs()
    {
        // X11 idle time covers both keyboard and mouse
        return GetIdleTimeMs();
    }

    public int GetAttentionSpanMs()
    {
        return _attentionSpanMs;
    }

    public void ResetAttentionSpan()
    {
        _attentionSpanMs = 0;
    }

    public void Update(int cycleMs)
    {
        var currentApp = GetForegroundApp();

        if (currentApp != _lastForegroundApp)
        {
            // App changed, reset attention span
            _attentionSpanMs = 0;
            _lastForegroundApp = currentApp;
        }
        else
        {
            // Same app, increment attention span
            _attentionSpanMs += cycleMs;
        }
    }

    private int GetIdleTimeMs()
    {
        if (!_hasXprintidle)
        {
            return 0;
        }

        try
        {
            // xprintidle returns idle time in milliseconds
            var output = ExecuteCommand("xprintidle", "");
            if (int.TryParse(output.Trim(), out int idleMs))
            {
                return idleMs;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting idle time: {ex.Message}");
        }

        return 0;
    }

    private string ExecuteCommand(string command, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return string.Empty;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private bool CheckCommandExists(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    private string ExtractAppFromWindowName(string windowName)
    {
        if (string.IsNullOrWhiteSpace(windowName))
        {
            return "unknown";
        }

        // Try to extract application name from window title
        // Common patterns: "Document - AppName", "AppName: Document", etc.
        // Using compiled regex for better performance
        var match = WindowNamePattern.Match(windowName);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return windowName.Trim();
    }
}
