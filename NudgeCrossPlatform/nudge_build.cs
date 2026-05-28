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
//   --help, -h          Show this help
//   --version, -v       Show version info
//   --interval N        Snapshot interval in minutes (default: 5)
//   --ml                Enable ML-powered adaptive notifications
//   --force-model       Force use of trained model even if below 100 sample threshold
//
// Example:
//   nudge                    # Use defaults

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#if !WINDOWS
using Tmds.DBus.Protocol;
using WaylandDotnet;
using WaylandDotnet.Staging;
#endif
using NudgeCore;
using NudgeTray;

interface IPlatformService
{
    string PlatformName { get; }
    IdleSource LastIdleSource { get; }
    bool Initialize();
    string GetForegroundApp();
    (string app, string title) GetForegroundAppWithTitle();
    int GetIdleTime();
}

sealed class Nudge
{
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // VERSION & CONSTANTS
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    const string ProgramVersion = "2.0.4";
    const string VersionSuffix = "dev";
    static readonly string VERSION = $"{ProgramVersion}-{VersionSuffix}";

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ANSI COLORS - Professional terminal output
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static class Color
    {
        public const string RESET   = "\u001b[0m";
        public const string BOLD    = "\u001b[1m";
        public const string DIM     = "\u001b[2m";
        public const string RED     = "\u001b[31m";
        public const string GREEN   = "\u001b[32m";
        public const string YELLOW  = "\u001b[33m";
        public const string CYAN    = "\u001b[36m";
        public const string MAGENTA = "\u001b[35m";
        public const string BRED    = "\u001b[1;31m";
        public const string BGREEN  = "\u001b[1;32m";
        public const string BYELLOW = "\u001b[1;33m";
        public const string BCYAN   = "\u001b[1;36m";
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // CONSTANTS
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    const int CYCLE_MS = 1000;
    const int UDP_PORT = 45001;
    const int RESPONSE_TIMEOUT_MS = 60000;
    const double ML_CONFIDENCE_THRESHOLD = 0.85;
    const int MIN_SAMPLES_THRESHOLD = 100;
    static int ML_CHECK_INTERVAL_MS = 60000;
    const int ACTIVITY_LOG_INTERVAL_MS = 60000;
    const string ML_HOST = "127.0.0.1";
    const int ML_PORT = 45002;
    static int SNAPSHOT_INTERVAL_MS = 5 * 60 * 1000;
    static bool _customInterval;
    static bool _mlEnabled;
    static bool _forceTrainedModel;
    static bool _mlAvailable;

#if !WINDOWS
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // KWIN WINDOW TRACKER — KWin script + D-Bus listener for KDE Wayland focus
    //
    // Writes a KWin script to ~/.local/share/kwin/scripts/nudge-window-tracker/
    // that listens for windowActivated and captionChanged events, then publishes
    // (app, title) to a D-Bus method "Update" on org.nudge.WindowTracker.
    // This class owns that bus name, handles incoming Updates, and caches the
    // latest (app, title, updatedAt) for idle calculation.
    //
    // Used as the primary KDE Wayland detection path in LinuxPlatformService,
    // falling back to xprop on XWayland only when the tracker isn't ready.
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    sealed class KWinWindowTracker : IPathMethodHandler, IDisposable
    {
        private const string DBusName       = "org.nudge.WindowTracker";
        private const string DBusInterface  = "org.nudge.WindowTracker";
        private const string DBusObjectPath = "/";
        private const string PluginName     = "nudge-window-tracker";

        private static readonly string ScriptDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "kwin", "scripts", PluginName);
        private static readonly string ScriptMainJsPath =
            System.IO.Path.Combine(ScriptDir, "contents", "code", "main.js");
        private static readonly string ScriptMetadataPath =
            System.IO.Path.Combine(ScriptDir, "metadata.json");

        private DBusConnection? _connection;
        private readonly object _lock = new();
        private string _cachedApp = "";
        private string _cachedTitle = "";
        private DateTime _lastUpdate;
        private volatile bool _ready;

        public string Path => DBusObjectPath;
        public bool IsReady => _ready;
        public bool HandlesChildPaths => false;

        public void Dispose()
        {
            _connection?.Dispose();
        }

        public (string app, string title, DateTime updatedAt) Snapshot()
        {
            lock (_lock) return (_cachedApp, _cachedTitle, _lastUpdate);
        }

        public int GetIdleMs()
        {
            lock (_lock)
            {
                if (_lastUpdate == DateTime.MinValue) return -1;
                return Math.Max(0, (int)(DateTime.UtcNow - _lastUpdate).TotalMilliseconds);
            }
        }

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var msg = context.Request;
            if (msg.InterfaceAsString == DBusInterface && msg.MemberAsString == "Update")
            {
                try
                {
                    var reader = msg.GetBodyReader();
                    var app = reader.ReadString().ToString();
                    var title = reader.ReadString().ToString();
                    lock (_lock)
                    {
                        _cachedApp = app;
                        _cachedTitle = title;
                        _lastUpdate = DateTime.UtcNow;
                    }
                }
                catch { }
            }
            return ValueTask.CompletedTask;
        }

        public static bool RunMethodHandlerSynchronously(Message message) => true;

        public async Task<bool> StartAsync()
        {
            try
            {
                EnsureScriptInstalled();

                _connection = new DBusConnection(DBusAddress.Session!);
                await _connection.ConnectAsync().ConfigureAwait(false);

                bool got = await _connection.TryRequestNameAsync(DBusName, RequestNameOptions.None)
                                            .ConfigureAwait(false);
                if (!got)
                {
                    _connection.Dispose();
                    _connection = null;
                    Console.Error.WriteLine($"[kwin-tracker] could not own bus name {DBusName} (already taken)");
                    return false;
                }

                _connection.AddMethodHandler(this);

                LoadAndStartScript();
                _ready = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[kwin-tracker] start failed: {ex.Message}");
                return false;
            }
        }

        private static void EnsureScriptInstalled()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScriptMainJsPath)!);
            if (!File.Exists(ScriptMetadataPath) ||
                File.ReadAllText(ScriptMetadataPath) != KWinScripts.MetadataJson)
            {
                File.WriteAllText(ScriptMetadataPath, KWinScripts.MetadataJson);
            }
            if (!File.Exists(ScriptMainJsPath) ||
                File.ReadAllText(ScriptMainJsPath) != KWinScripts.MainJs)
            {
                File.WriteAllText(ScriptMainJsPath, KWinScripts.MainJs);
            }
        }

        private static string ResolveQdbus()
        {
            foreach (var candidate in new[] { "qdbus6", "qdbus-qt6", "qdbus" })
            {
                if (CommandExists(candidate)) return candidate;
            }
            return "";
        }

        private static void LoadAndStartScript()
        {
            var qdbus = ResolveQdbus();
            if (string.IsNullOrEmpty(qdbus)) return;

            RunCommand(qdbus,
                $"org.kde.KWin /Scripting org.kde.kwin.Scripting.unloadScript {PluginName}");
            RunCommand(qdbus,
                $"org.kde.KWin /Scripting org.kde.kwin.Scripting.loadScript " +
                $"\"{ScriptMainJsPath}\" {PluginName}");
            RunCommand(qdbus,
                "org.kde.KWin /Scripting org.kde.kwin.Scripting.start");
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // WAYLAND IDLE MONITOR — ext-idle-notify-v1 native idle detection
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    sealed class WaylandIdleMonitor : IDisposable
    {
        private WlDisplay? _display;
        private ExtIdleNotifierV1? _notifier;
        private ExtIdleNotificationV1? _notification;
        private WlSeat? _seat;
        private readonly Stopwatch _idleSw = new();
        private bool _disposed;
        private Thread? _dispatchThread;

        public bool IsAvailable => _notifier != null;
        public int LastIdleMs => _idleSw.IsRunning ? (int)_idleSw.ElapsedMilliseconds : 0;

        public bool Initialize()
        {
            try
            {
                _display = WlDisplay.Connect(null);
                var registry = _display.GetRegistry();

                registry.OnGlobal += (name, interfaceName, version) =>
                {
                    if (interfaceName == ExtIdleNotifierV1.InterfaceName)
                        _notifier = registry.Bind<ExtIdleNotifierV1>((uint)name, version);
                    else if (interfaceName == WlSeat.InterfaceName)
                        _seat = registry.Bind<WlSeat>((uint)name, version);
                };

                _display.Roundtrip();

                if (_notifier == null || _seat == null)
                {
                    _display = null;
                    return false;
                }

                _notification = _notifier.GetIdleNotification(0u, _seat);

                Console.Error.WriteLine($"[wayland-idle] using ext-idle-notify-v1");

                _notification.OnIdled += () =>
                {
                    _idleSw.Restart();
                };

                _notification.OnResumed += () =>
                {
                    _idleSw.Reset();
                };

                _dispatchThread = new Thread(DispatchLoop)
                {
                    IsBackground = true,
                    Name = "wayland-dispatch"
                };
                _dispatchThread.Start();

                return true;
            }
            catch
            {
                Dispose();
                return false;
            }
        }

        private void DispatchLoop()
        {
            try
            {
                while (!_disposed && _display != null)
                    _display.Dispatch();
            }
            catch
            {
                // Thread exit on disposal
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _notification?.Destroy();
            _notifier?.Destroy();
            if (_display != null)
                _display.Disconnect();
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // LINUX PLATFORM SERVICE — Focus + idle detection per compositor
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    sealed class LinuxPlatformService : IPlatformService, IDisposable
    {
        private string _compositor = "";
        private string _cachedApp = "";
        private string _cachedTitle = "";
        private DateTime _appCacheExpiry;
        private int _cachedIdle;
        private DateTime _idleCacheExpiry;
        private IdleSource _lastIdleSource;
        private KWinWindowTracker? _kwinTracker;
        private WaylandIdleMonitor? _waylandIdle;

        private static readonly char[] Separators = [' ', '\n', '\r', '\t'];

        public void Dispose()
        {
            _kwinTracker?.Dispose();
            _waylandIdle?.Dispose();
        }

        private static readonly FrozenSet<string> SystemProcessNames = new[]
        {
            "kwin_wayland", "kwin_x11", "plasmashell", "kded5", "kded6",
            "kglobalaccel", "ksmserver", "systemd", "dbus-daemon",
            "kwalletd5", "kwalletd6", "baloo_file", "agent", "polkit",
            "xdg-desktop-portal", "xdg-document-portal", "xdg-permission-store"
        }.ToFrozenSet(StringComparer.Ordinal);

        private static readonly byte[][] NudgeProcessPatternBytes =
        [
            "background_trainer"u8.ToArray(),
            "model_inference"u8.ToArray(),
            "nudge-tray"u8.ToArray(),
            "/Nudge/"u8.ToArray(),
            "NudgeCrossPlatform"u8.ToArray()
        ];

        private static readonly byte[] WaylandDisplayBytes = "WAYLAND_DISPLAY"u8.ToArray();
        private static readonly byte[] DisplayBytes = "DISPLAY="u8.ToArray();

        public string PlatformName => _compositor;
        public IdleSource LastIdleSource => _lastIdleSource;

        public bool Initialize()
        {
            _compositor = DetectCompositor();
            if (_compositor == "unknown")
                return false;

            // Check required commands
            var (cmd, _) = _compositor switch
            {
                "sway" => ("swaymsg", "Sway IPC"),
                "gnome" => ("gdbus", "D-Bus communication"),
                "kde" => ("", ""),  // KDE: handled via KWin script + D-Bus listener (see KWinWindowTracker)
                _ => ("", "")
            };

            if (!string.IsNullOrEmpty(cmd) && !CommandExists(cmd))
            {
                return false;
            }

            // Try ext-idle-notify-v1 first (works across compositors that support it)
            _waylandIdle = new WaylandIdleMonitor();
            if (_waylandIdle.Initialize())
            {
                Console.Error.WriteLine($"[linux-platform] wayland idle: ext-idle-notify-v1 available");
            }
            else
            {
                Console.Error.WriteLine($"[linux-platform] wayland idle: ext-idle-notify-v1 not advertised");
                _waylandIdle = null;
            }

            if (_compositor == "kde")
            {
                // Spin up the KWin-script-driven tracker. Failure is non-fatal: we
                // fall back to xprop on XWayland in GetKDEFocusedAppWithTitle.
                _kwinTracker = new KWinWindowTracker();
                // Block briefly so the first GetForegroundApp() call doesn't race
                // ahead of the bus name being claimed and the script publishing.
                try { _kwinTracker.StartAsync().Wait(TimeSpan.FromSeconds(2)); } catch { }
            }

            return true;
        }

        private static string DetectCompositor()
        {
            if (CommandExists("swaymsg"))
                return "sway";

            var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
            if (desktop?.Contains("GNOME", StringComparison.Ordinal) == true)
                return "gnome";
            if (desktop?.Contains("KDE", StringComparison.Ordinal) == true)
                return "kde";
            if (desktop?.Contains("X-Cinnamon", StringComparison.Ordinal) == true)
                return "cinnamon";

            // Fallback: check for cinnamon-session process
            if (CommandExists("pgrep") && !string.IsNullOrWhiteSpace(RunCommand("pgrep", "-x cinnamon-session")))
                return "cinnamon";

            return "unknown";
        }

        public string GetForegroundApp()
        {
            var (app, _) = GetForegroundAppWithTitle();
            return app;
        }

        public (string app, string title) GetForegroundAppWithTitle()
        {
            if (DateTime.Now < _appCacheExpiry)
                return (_cachedApp, _cachedTitle);

            (string app, string title) result = _compositor switch
            {
                "sway" => GetSwayFocusedAppWithTitle(),
                "gnome" => GetGnomeFocusedAppWithTitle(),
                "kde" => GetKDEFocusedAppWithTitle(),
                "cinnamon" => GetX11FocusedAppWithTitle(),
                _ => ("unknown", "")
            };

            string fallbackApp = !string.IsNullOrWhiteSpace(result.app)
                ? result.app
                : !string.IsNullOrWhiteSpace(result.title)
                    ? result.title
                    : "unknown";
            string app = BrowserDetector.IsBrowser(result.app)
                ? BrowserDetector.GetAppAndSite(result.app, result.title)
                : fallbackApp;

            _cachedApp = app;
            _cachedTitle = result.title;
            _appCacheExpiry = DateTime.Now.AddMilliseconds(500);
            return (app, result.title);
        }

        public int GetIdleTime()
        {
            if (DateTime.Now < _idleCacheExpiry)
                return _cachedIdle;

            int idle;

            // Primary: ext-idle-notify-v1 (native Wayland, cross-compositor).
            // When available, this is the authoritative source — it tracks
            // actual seat input (mouse + keyboard), not window switches.
            if (_waylandIdle != null && _waylandIdle.IsAvailable)
            {
                idle = _waylandIdle.LastIdleMs;
                _lastIdleSource = IdleSource.WaylandExtIdleNotify;
                _cachedIdle = idle;
                _idleCacheExpiry = DateTime.Now.AddMilliseconds(100);
                return idle;
            }

            // On KDE Wayland, GetSessionIdleTime is not supported.
            // Use the KWin script tracker: it timestamps every windowActivated
            // and captionChanged event, so idle = time since last such event.
            // Only used when ext-idle-notify is unavailable on the compositor.
            if (_compositor == "kde" && _kwinTracker != null && _kwinTracker.IsReady)
            {
                idle = _kwinTracker.GetIdleMs();
                if (idle >= 0)
                {
                    _lastIdleSource = IdleSource.KdeKwinIdle;
                    _cachedIdle = idle;
                    _idleCacheExpiry = DateTime.Now.AddMilliseconds(100);
                    return idle;
                }
            }

            // Standard cross-compositor methods
            idle = GetFreedesktopIdleTime();
            if (idle > 0)
            {
                _lastIdleSource = IdleSource.FreedesktopScreenSaver;
                _cachedIdle = idle;
                _idleCacheExpiry = DateTime.Now.AddMilliseconds(100);
                return idle;
            }

            idle = GetGnomeIdleTime();
            if (idle > 0)
            {
                _lastIdleSource = IdleSource.GnomeIdleMonitor;
                _cachedIdle = idle;
                _idleCacheExpiry = DateTime.Now.AddMilliseconds(100);
                return idle;
            }

            idle = GetX11IdleTime();
            if (idle > 0)
            {
                _lastIdleSource = IdleSource.X11Xprintidle;
                _cachedIdle = idle;
                _idleCacheExpiry = DateTime.Now.AddMilliseconds(100);
                return idle;
            }

            // Final fallback: logind idle hint (fires at screensaver timeout,
            // so only catches prolonged idle but better than nothing).
            idle = GetLogindIdleTime();
            if (idle > 0)
            {
                _lastIdleSource = IdleSource.LogindIdleHint;
                _cachedIdle = idle;
                _idleCacheExpiry = DateTime.Now.AddMilliseconds(100);
                return idle;
            }

            _lastIdleSource = IdleSource.Unknown;
            return 0;
        }

        // Linux-specific window detection methods
        private static (string app, string title) GetSwayFocusedAppWithTitle()
        {
            try
            {
                var json = RunCommand("swaymsg", "-t get_tree");
                return ExtractFocusedAppFromSwayTree(json);
            }
            catch
            {
                return ("unknown", "");
            }
        }

        private static (string app, string title) GetGnomeFocusedAppWithTitle()
        {
            try
            {
                // Get window class (app_id equivalent)
                var classOutput = RunCommand("gdbus", "call --session --dest org.gnome.Shell " +
                    "--object-path /org/gnome/Shell " +
                    "--method org.gnome.Shell.Eval " +
                    "\"global.display.focus_window.get_wm_class()\"");

                string appClass = ExtractQuotedString(classOutput);

                // Get window title
                var titleOutput = RunCommand("gdbus", "call --session --dest org.gnome.Shell " +
                    "--object-path /org/gnome/Shell " +
                    "--method org.gnome.Shell.Eval " +
                    "\"global.display.focus_window.get_title()\"");

                string title = ExtractQuotedString(titleOutput);
                if (title == "unknown")
                    title = "";

                if (string.IsNullOrEmpty(appClass) || appClass == "unknown")
                    return ("unknown", "");

                return (appClass, title);
            }
            catch
            {
                return ("unknown", "");
            }
        }

        private (string app, string title) GetKDEFocusedAppWithTitle()
        {
            // Primary: KWin-script-driven tracker (invisible, covers native Wayland too).
            if (_kwinTracker != null && _kwinTracker.IsReady)
            {
                var (app, title, updatedAt) = _kwinTracker.Snapshot();
                if (updatedAt != DateTime.MinValue && !string.IsNullOrEmpty(app))
                    return (app, title);
            }
            // Fallback: xprop on XWayland (only sees X11 apps; misses Wayland-native).
            // Strictly invisible — _NET_ACTIVE_WINDOW is just a property read.
            var x = ReadActiveX11Window();
            if (!string.IsNullOrEmpty(x.app))
                return x;
            return ("unknown", "");
        }

        private static (string app, string title) GetX11FocusedAppWithTitle()
        {
            // EWMH-based read of _NET_ACTIVE_WINDOW. Invisible and accurate.
            var x = ReadActiveX11Window();
            if (!string.IsNullOrEmpty(x.app))
                return x;
            // Last-resort: heuristic /proc scan (not focus-aware; better than nothing).
            return (DetectActiveProcessKDE(), "");
        }

        private static (string app, string title) ReadActiveX11Window()
        {
            try
            {
                if (!CommandExists("xprop")) return ("", "");

                var rootOut = RunCommand("xprop", "-root _NET_ACTIVE_WINDOW");
                if (string.IsNullOrWhiteSpace(rootOut)) return ("", "");

                // Format: "_NET_ACTIVE_WINDOW(WINDOW): window id # 0x3a00007"
                int hashIdx = rootOut.IndexOf('#');
                if (hashIdx < 0) return ("", "");
                var idTok = rootOut[(hashIdx + 1)..].Trim().Split(Separators, 2)[0];
                if (string.IsNullOrEmpty(idTok) || idTok == "0x0" || idTok == "0") return ("", "");

                var winOut = RunCommand("xprop", $"-id {idTok} WM_CLASS _NET_WM_NAME WM_NAME");
                if (string.IsNullOrWhiteSpace(winOut)) return ("", "");

                string app = "", title = "";
                foreach (var rawLine in winOut.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("WM_CLASS", StringComparison.Ordinal))
                    {
                        // WM_CLASS(STRING) = "instance", "Class"
                        var quotes = ExtractQuotedTokens(line);
                        if (quotes.Count > 0)
                            app = quotes[^1]; // Class (second) preferred; falls back to instance if only one
                    }
                    else if (string.IsNullOrEmpty(title) &&
                             (line.StartsWith("_NET_WM_NAME", StringComparison.Ordinal) || line.StartsWith("WM_NAME", StringComparison.Ordinal)))
                    {
                        var quotes = ExtractQuotedTokens(line);
                        if (quotes.Count > 0) title = quotes[0];
                    }
                }
                return (app, title);
            }
            catch
            {
                return ("", "");
            }
        }

        private static List<string> ExtractQuotedTokens(string line)
        {
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                int start = line.IndexOf('"', i);
                if (start < 0) break;
                int end = start + 1;
                var sb = new StringBuilder();
                while (end < line.Length)
                {
                    char c = line[end];
                    if (c == '\\' && end + 1 < line.Length) { sb.Append(line[end + 1]); end += 2; continue; }
                    if (c == '"') break;
                    sb.Append(c); end++;
                }
                result.Add(sb.ToString());
                i = end + 1;
            }
            return result;
        }

        // Linux-specific idle time detection methods
        private static int GetFreedesktopIdleTime()
        {
            try
            {
                // Try qdbus first (KDE/Qt environments)
                var output = RunCommand("qdbus",
                    "org.freedesktop.ScreenSaver " +
                    "/org/freedesktop/ScreenSaver " +
                    "org.freedesktop.ScreenSaver.GetSessionIdleTime");

                if (int.TryParse(output.Trim(), out int seconds))
                {
                    return seconds * 1000;
                }

                // Try gdbus as fallback (GNOME/GTK environments)
                output = RunCommand("gdbus",
                    "call --session " +
                    "--dest org.freedesktop.ScreenSaver " +
                    "--object-path /org/freedesktop/ScreenSaver " +
                    "--method org.freedesktop.ScreenSaver.GetSessionIdleTime");

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

        private static int GetGnomeIdleTime()
        {
            try
            {
                var output = RunCommand("gdbus",
                    "call --session " +
                    "--dest org.gnome.Mutter.IdleMonitor " +
                    "--object-path /org/gnome/Mutter/IdleMonitor/Core " +
                    "--method org.gnome.Mutter.IdleMonitor.GetIdletime");

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

        private static int GetX11IdleTime()
        {
            try
            {
                var output = RunCommand("xprintidle", "");
                if (int.TryParse(output.Trim(), out int ms))
                {
                    return ms;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // Queries logind for idle hint. Only fires when the screensaver/power
        // management considers the session idle (typically 5+ minutes), but
        // it's a reliable cross-compositor fallback for prolonged AFK periods.
        private static int GetLogindIdleTime()
        {
            try
            {
                var output = RunCommand("loginctl", "show-session -p IdleHint -p IdleSinceHint");
                bool idleHint = false;
                long idleSinceUs = 0;

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("IdleHint=yes", StringComparison.OrdinalIgnoreCase))
                        idleHint = true;
                    else if (line.StartsWith("IdleSinceHint=", StringComparison.Ordinal) &&
                             long.TryParse(line["IdleSinceHint=".Length..], out long us))
                        idleSinceUs = us;
                }

                if (!idleHint || idleSinceUs == 0) return 0;

                long nowUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
                long idleMs = (nowUs - idleSinceUs) / 1000L;
                return (int)Math.Max(0, idleMs);
            }
            catch
            {
                return 0;
            }
        }

        private static string DetectActiveProcessKDE()
        {
            try
            {
                string bestProcessName = "";
                long bestActivityScore = long.MinValue;

                foreach (var procDir in Directory.EnumerateDirectories("/proc"))
                {
                    if (!int.TryParse(Path.GetFileName(procDir), out _))
                        continue;

                    try
                    {
                        var environPath = Path.Combine(procDir, "environ");
                        if (!File.Exists(environPath) || !HasDisplayEnvironment(environPath))
                            continue;

                        var cmdlinePath = Path.Combine(procDir, "cmdline");
                        if (!File.Exists(cmdlinePath) || !TryGetProcessNameFromCmdline(cmdlinePath, out var processName))
                            continue;

                        if (SystemProcessNames.Contains(processName))
                            continue;

                        long activityScore = CalculateActivityScore(procDir);
                        if (activityScore > bestActivityScore)
                        {
                            bestActivityScore = activityScore;
                            bestProcessName = processName;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(bestProcessName))
                    return bestProcessName;
            }
            catch { }

            return "kde-wayland-window";
        }

        private static bool HasDisplayEnvironment(string environPath)
        {
            var environmentData = File.ReadAllBytes(environPath);
            return environmentData.AsSpan().IndexOf(WaylandDisplayBytes) >= 0 ||
                   environmentData.AsSpan().IndexOf(DisplayBytes) >= 0;
        }

        private static bool TryGetProcessNameFromCmdline(string cmdlinePath, out string processName)
        {
            processName = "";
            var commandLineData = File.ReadAllBytes(cmdlinePath);
            var commandLine = commandLineData.AsSpan();
            if (commandLine.IsEmpty || ContainsAnyPattern(commandLine, NudgeProcessPatternBytes))
                return false;

            int firstArgumentEnd = commandLine.IndexOf((byte)0);
            if (firstArgumentEnd < 0)
                firstArgumentEnd = commandLine.Length;
            if (firstArgumentEnd == 0)
                return false;

            string processPath = Encoding.UTF8.GetString(commandLine[..firstArgumentEnd]);
            processName = Path.GetFileName(processPath.Trim());
            return !string.IsNullOrEmpty(processName);
        }

        private static bool ContainsAnyPattern(ReadOnlySpan<byte> data, byte[][] patterns)
        {
            foreach (var pattern in patterns)
            {
                if (data.IndexOf(pattern) >= 0)
                    return true;
            }

            return false;
        }

        private static long CalculateActivityScore(string procDir)
        {
            var statPath = Path.Combine(procDir, "stat");
            if (!File.Exists(statPath))
                return 0;

            try
            {
                return CalculateActivityScore(File.ReadAllText(statPath).AsSpan());
            }
            catch
            {
                return 0;
            }
        }

        private static long CalculateActivityScore(ReadOnlySpan<char> stat)
        {
            int processNameEnd = stat.LastIndexOf(')');
            if (processNameEnd < 0 || processNameEnd + 2 >= stat.Length)
                return 0;

            var fields = stat[(processNameEnd + 2)..];
            long pgrp = 0;
            long tpgid = 0;
            long minFaults = 0;
            long numThreads = 0;
            int fieldNumber = 3;
            int index = 0;

            while (index < fields.Length && fieldNumber <= 20)
            {
                while (index < fields.Length && fields[index] == ' ')
                    index++;

                if (index >= fields.Length)
                    break;

                int nextSpace = fields[index..].IndexOf(' ');
                ReadOnlySpan<char> token = nextSpace >= 0 ? fields.Slice(index, nextSpace) : fields[index..];

                switch (fieldNumber)
                {
                    case 5:
                        _ = long.TryParse(token, out pgrp);
                        break;
                    case 8:
                        _ = long.TryParse(token, out tpgid);
                        break;
                    case 10:
                        _ = long.TryParse(token, out minFaults);
                        break;
                    case 20:
                        _ = long.TryParse(token, out numThreads);
                        break;
                }

                if (nextSpace < 0)
                    break;

                index += nextSpace + 1;
                fieldNumber++;
            }

            long activityScore = 0;
            if (tpgid > 0 && tpgid == pgrp)
                activityScore += 1000;

            activityScore += numThreads * 10;
            activityScore += minFaults / 1000;
            return activityScore;
        }

        private static (string app, string title) ExtractFocusedAppFromSwayTree(string json) =>
            NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json);

        private static string ExtractQuotedString(string input) =>
            NudgeCoreLogic.ExtractQuotedString(input);
    }
#endif

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // WINDOWS PLATFORM SERVICE — Win32 idle + foreground detection
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    sealed class WindowsPlatformService : IPlatformService, IDisposable
    {
        public string PlatformName => "Windows";
        public IdleSource LastIdleSource => IdleSource.Win32LastInput;

        public bool Initialize() => true;

        public string GetForegroundApp()
        {
            var (app, _) = GetForegroundAppWithTitle();
            return app;
        }

        public (string app, string title) GetForegroundAppWithTitle()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return ("unknown", "");

            var buf = new char[512];
            var len = GetWindowText(hwnd, buf, buf.Length);
            var title = len > 0 ? new string(buf, 0, len) : "";

            uint tid = GetWindowThreadProcessId(hwnd, out uint pid);
            if (tid == 0)
                return ("unknown", title);

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                return (proc.ProcessName.ToLowerInvariant(), title);
            }
            catch
            {
                return ("unknown", title);
            }
        }

        public int GetIdleTime()
        {
            LASTINPUTINFO info = new();
            info.cbSize = Marshal.SizeOf<LASTINPUTINFO>();
            if (GetLastInputInfo(ref info))
                return (int)(Environment.TickCount64 - info.dwTime);
            return 0;
        }

        public void Dispose() { }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, char[] text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private struct LASTINPUTINFO
        {
            public int cbSize;
            public uint dwTime;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // STATE - Application state
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static IPlatformService? _platformService;
    static string _csvPath = PlatformConfig.CsvPath;
    static StreamWriter? _csvFile;
    static StreamWriter? _activityLogFile;
    static readonly ActivityFeatureTracker FeatureTracker = new();

    // Activity tracking
    static string _currentApp = "";
    static string _currentTitle = "";
    static int _attentionSpanMs;
    static bool _waitingForResponse;
    static int _totalSnapshots;
    static int _activityLogElapsed;  // Track elapsed time for activity logging

    // Snapshot state (captured when snapshot is taken)
    static string _snapshotApp = "";
    static string _snapshotTitle = "";
    static int _snapshotIdle;
    static int _snapshotAttention;
    static ActivityTickResult _snapshotTick;
    static System.Threading.Timer? _responseTimer;

    // ML statistics tracking
    static int _mlPredictions;
    static int _mlTriggeredSnapshots;
    static int _mlSkippedAlerts;
    static int _mlLowConfidenceSkips;
    static int _intervalTriggeredSnapshots;
    static bool _productivityConfirmed;
    static bool _mlLowConfidence;  // Set when model predicts unproductive below confidence threshold
    static int _mlSampleCount;     // Cached from trainer_meta.json, refreshed in CheckMLAvailability
    static int _mlProductiveSamples;
    static int _mlUnproductiveSamples;
    static List<double> _mlConfidenceScores = new List<double>();
    static long _lastMLTriggerT;  // Unix timestamp of last ML-triggered snapshot (0=none/interval)

    // Log message formats
    private const string LogPredictionFormat = "📊 Request #{0}: {1} (confidence: {2:F1}%, {3:F1}ms)";
    private const string LogIdleFormat = "  {0} min until next snapshot{1}  ({2}{3}{4}, idle: {5}ms)";
    private const string LogAppSwitchFormat = "  Switched: {0} → {1}";

    private static readonly CompositeFormat LogAppSwitchComposite = CompositeFormat.Parse(LogAppSwitchFormat);

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // RANDOM INTERVAL - Generate random snapshot interval between 5-10 minutes
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void SetRandomInterval()
    {
        if (!_customInterval)
        {
            // Random interval between 5 and 10 minutes (in milliseconds)
            int randomMinutes = Random.Shared.Next(5, 11);  // 5-10 inclusive
            SNAPSHOT_INTERVAL_MS = randomMinutes * 60 * 1000;
            Dim($"  Next random interval: {randomMinutes} minutes");
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MAIN - Entry point with professional argument parsing
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void Main(string[] args)
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs(args);
        if (parsed.Action == NudgeStartupAction.ShowHelp)
        {
            ShowHelp();
            return;
        }
        if (parsed.Action == NudgeStartupAction.ShowVersion)
        {
            Console.WriteLine($"Nudge Harvest v{VERSION}");
            return;
        }
        if (parsed.IntervalMinutes is int minutes)
        {
            SNAPSHOT_INTERVAL_MS = minutes * 60 * 1000;
            _customInterval = true;
        }
        if (parsed.MlCheckIntervalSeconds is int mlSeconds)
        {
            ML_CHECK_INTERVAL_MS = mlSeconds * 1000;
        }
        _mlEnabled = parsed.MlEnabled;
        _forceTrainedModel = parsed.ForceTrainedModel;
        if (!string.IsNullOrWhiteSpace(parsed.CsvPath))
        {
            _csvPath = parsed.CsvPath;
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
        if (_customInterval)
        {
            Info($"  Taking snapshots every {SNAPSHOT_INTERVAL_MS/1000/60} minutes");
        }
        else
        {
            Info($"  Taking snapshots every 5-10 minutes (random)");
            // Set initial random interval if not custom
            SetRandomInterval();
        }
        if (_mlEnabled)
        {
            Info($"  {Color.BGREEN}ML-powered adaptive notifications enabled{Color.RESET}");
            Info($"  Confidence threshold: {ML_CONFIDENCE_THRESHOLD*100:F0}%");
            Info($"  AI check frequency: {ML_CHECK_INTERVAL_MS/1000} seconds");
            if (_forceTrainedModel)
            {
                Warning($"  {Color.BYELLOW}Force trained model: enabled{Color.RESET} (ignoring sample threshold)");
            }
            else
            {
                Info($"  Minimum samples required: {MIN_SAMPLES_THRESHOLD}");
            }
        }
        Info($"  Respond with: {Color.BCYAN}nudge-notify YES{Color.RESET} or {Color.BCYAN}nudge-notify NO{Color.RESET}");
        Console.WriteLine();

        // Let the tray AI Brain tab show a countdown to the first ML check
        if (_mlEnabled)
        {
            Console.WriteLine($"MLNEXT:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ML_CHECK_INTERVAL_MS / 1000}");
        }

        RunMainLoop();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ENVIRONMENT VALIDATION - Check all requirements before running
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static bool ValidateEnvironment()
    {
        bool valid = true;

        Info("Checking environment...");

        // Initialize platform service
        if (PlatformConfig.IsWindows)
        {
            _platformService = new WindowsPlatformService();
            Success($"✓ Platform: Windows");
        }
#if !WINDOWS
        else if (PlatformConfig.IsLinux)
        {
            var linuxService = new LinuxPlatformService();
            if (!linuxService.Initialize())
            {
                Error("Could not detect compositor or desktop environment");
                Error("Supported: Sway, GNOME, KDE Plasma, Cinnamon");
                return false;
            }
            _platformService = linuxService;

            // Check session type (Wayland or X11)
            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (sessionType == "wayland")
            {
                Success($"✓ Session: Wayland");
            }
            else if (sessionType == "x11")
            {
                Success($"✓ Session: X11");
            }
            else
            {
                Warning($"Unknown session type: {sessionType ?? "none"}");
            }

            Success($"✓ Desktop Environment: {_platformService.PlatformName}");
        }
#endif
        else if (PlatformConfig.IsMacOS)
        {
            _platformService = new WindowsPlatformService(); // Placeholder for now
            Success($"✓ Platform: macOS");
            Warning("macOS support is experimental");
        }

        if (_platformService == null)
        {
            Error("Unsupported platform");
            return false;
        }

        // Test window detection
        Info("Testing window detection...");
        var testApp = _platformService.GetForegroundApp();
        if (testApp == "unknown" || string.IsNullOrEmpty(testApp))
        {
            Warning("Could not detect foreground window");
            if (!PlatformConfig.IsWindows)
            {
                Warning("Please ensure compositor is running correctly");
            }
        }
        else
        {
            Success($"✓ Detected window: {Color.CYAN}{testApp}{Color.RESET}");
        }

        // Test idle time detection
        Info("Testing idle time detection...");
        var testIdle = _platformService.GetIdleTime();
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
        "xprintidle" => "xprintidle (apt install xprintidle)",
        _ => "check your package manager"
    };

    static FocusSource GetFocusSource()
    {
        if (PlatformConfig.IsWindows)
            return FocusSource.WindowsApi;

        string platformName = _platformService?.PlatformName ?? "";
        if (platformName.Contains("sway", StringComparison.OrdinalIgnoreCase))
            return FocusSource.SwayIpc;
        if (platformName.Contains("kde", StringComparison.OrdinalIgnoreCase) ||
            platformName.Contains("plasma", StringComparison.OrdinalIgnoreCase))
            return FocusSource.KWinScript;
        if (platformName.Contains("gnome", StringComparison.OrdinalIgnoreCase))
            return FocusSource.GnomeShell;
        if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "x11", StringComparison.OrdinalIgnoreCase))
            return FocusSource.X11Ewmh;

        return FocusSource.Unknown;
    }

    static ActivityTickResult CaptureActivityTick(DateTime now, string app, string title, int idle) =>
        FeatureTracker.Capture(
            now,
            new WindowObservation(
                AppId: app,
                Title: title,
                WindowId: "",
                WorkspaceId: "",
                FocusSource: GetFocusSource(),
                Fullscreen: false,
                MappedToplevelCount: 0),
            new IdleObservation(idle, _platformService?.LastIdleSource ?? IdleSource.Unknown));

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MAIN LOOP - Core event loop with professional status updates
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void RunMainLoop()
    {
        int elapsed = 0;
        int mlElapsed = 0;
        int harvestElapsed = 0;
        int lastMinute = -1;
        int lastStatsSnapshot = 0;

        while (true)
        {
            var sw = Stopwatch.StartNew();

            // Get current activity
            var now = DateTime.Now;
            var (app, title) = _platformService?.GetForegroundAppWithTitle() ?? ("unknown", "");
            int idle = _platformService?.GetIdleTime() ?? 0;

            // Ignore Nudge notification window in app tracking
            // This prevents the notification from polluting analytics data
            bool isNudgeWindow = NudgeCoreLogic.IsNudgeForegroundWindow(app, title);

            // Track attention span (skip if it's our notification window)
            if (!isNudgeWindow && app != _currentApp)
            {
                if (!string.IsNullOrEmpty(_currentApp))
                {
                    // Reuse the shared format string to keep the hot path quiet on allocations.
                    Dim(string.Format(CultureInfo.InvariantCulture, LogAppSwitchComposite, _currentApp, app));
                }
                _currentApp = app;
                _currentTitle = title;
                _attentionSpanMs = 0;
                // Keep the tray AI Brain tab aware of which app is in focus (tab-separated: app\ttitle)
                Console.WriteLine($"APPFOCUS:{app}\t{BrowserDetector.TrimBrowserSuffix(title)}");
            }
            // Broadcast title changes within the same app (e.g. browser tab switches)
            else if (!isNudgeWindow && title != _currentTitle)
            {
                _currentTitle = title;
                _attentionSpanMs += CYCLE_MS;
                Console.WriteLine($"APPFOCUS:{app}\t{BrowserDetector.TrimBrowserSuffix(title)}");
            }
            else if (!isNudgeWindow)
            {
                _currentTitle = title;
                _attentionSpanMs += CYCLE_MS;
            }

            // If notification window is focused, use the last valid app for data collection
            if (isNudgeWindow && !string.IsNullOrEmpty(_currentApp))
            {
                app = _currentApp; // Use previous app instead of notification window
                title = _currentTitle;
            }

            ActivityTickResult? tick = CaptureActivityTick(now, app, title, idle);

            // Broadcast harvest sensor signals to AI Brain tab every 2 seconds
            harvestElapsed += CYCLE_MS;
            if (harvestElapsed >= 2000)
            {
                harvestElapsed = 0;
                if (tick is ActivityTickResult t)
                {
                    var ctx = t.Context;
                    var feat = t.Features;
                    var sig = new HarvestSignal
                    {
                        Quality   = ctx.SignalQuality.ToString().ToLowerInvariant(),
                        FocusSrc  = ctx.FocusSource.ToString(),
                        Category  = AppCategoryClassifier.GetCategoryName(t.AppCategory),
                        CategoryConf = AppCategoryClassifier.GetConfidenceScore(t.AppCategoryConfidence),
                        IdleMs    = ctx.IdleMs,
                        FocusedMs = ctx.FocusedSinceMs,
                        Domain    = ctx.FocusedDomain,
                        Work      = feat.WorkDomainFlag,
                        Ent       = feat.EntertainmentDomainFlag,
                        Comm      = feat.CommunicationAppFlag,
                        Browser   = feat.BrowserWindowFlag,
                        Afk       = feat.AfkFlag,
                        Fullscreen = feat.FullscreenFlag,
                        Sw300     = feat.SwitchCount300s,
                        Share     = feat.CurrentAppShare300s,
                        Apps300   = feat.DistinctApps300s,
                    };
                    Console.WriteLine($"HARVEST:{JsonSerializer.Serialize(sig, NudgeJsonContext.Default.HarvestSignal)}");
                }
            }

            // Check for snapshot triggers
            elapsed += CYCLE_MS;
            mlElapsed += CYCLE_MS;
            _activityLogElapsed += CYCLE_MS;
            bool intervalReached = elapsed >= SNAPSHOT_INTERVAL_MS;
            bool mlTriggered = false;

            // Log activity every minute
            if (_activityLogElapsed >= ACTIVITY_LOG_INTERVAL_MS)
            {
                LogActivity(app, idle, tick);
                _activityLogElapsed = 0;
            }

            // Always reset ML timer when interval fires — even if waiting for a response —
            // so the AI Brain countdown never gets stuck at "Checking now..."
            bool mlCheckDue = _mlEnabled && mlElapsed >= ML_CHECK_INTERVAL_MS;
            if (mlCheckDue)
            {
                mlElapsed = 0;
                Console.WriteLine($"MLNEXT:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ML_CHECK_INTERVAL_MS / 1000}");
            }

            if (!_waitingForResponse)
            {
                if (mlCheckDue)
                {
                    if (ShouldTriggerSnapshot(app, idle, _attentionSpanMs, tick))
                        mlTriggered = true;
                    else if (_productivityConfirmed)
                    {
                        _productivityConfirmed = false;
                        elapsed = 0;
                        intervalReached = false;
                    }
                }

                // Trigger snapshot if:
                // 1. ML triggered with high confidence (checked every minute)
                // 2. Interval reached (fallback when ML is disabled or unavailable)
                bool useIntervalFallback = !_mlEnabled || !_mlAvailable || _mlLowConfidence;
                if (mlTriggered || (useIntervalFallback && intervalReached))
                {
                    if (mlTriggered)
                    {
                        Info($"  {Color.BGREEN}✓ ML-TRIGGERED SNAPSHOT{Color.RESET} (detected unproductive)");
                    }
                    else if (intervalReached)
                    {
                        _intervalTriggeredSnapshots++;
                        string intervalReason = !_mlEnabled ? "ML disabled"
                            : _mlLowConfidence ? $"ML below {ML_CONFIDENCE_THRESHOLD*100:F0}% confidence threshold"
                            : "ML unavailable";
                        Info($"  {Color.BYELLOW}⏰ INTERVAL SNAPSHOT{Color.RESET} ({intervalReason})");

                        // Broadcast interval-triggered event for Recent Checks display
                        var intEvt = new MLLiveEvent
                        {
                            T             = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            App           = app,
                            Score         = 0.5,
                            Confidence    = 0,
                            Productive    = false,
                            Triggered     = true,
                            TriggerSource = "int"
                        };
                        Console.WriteLine($"MLDATA:{JsonSerializer.Serialize(intEvt, NudgeJsonContext.Default.MLLiveEvent)}");
                    }

                    TakeSnapshot(app, title, idle, _attentionSpanMs, tick);
                    _mlLowConfidence = false;
                    elapsed = 0;
                    mlElapsed = 0;
                    SetRandomInterval();  // Set new random interval for next snapshot
                    lastMinute = -1; // Reset progress indicator

                    // Show ML stats every 10 snapshots
                    if (_mlEnabled && (_totalSnapshots - lastStatsSnapshot) >= 10)
                    {
                        ShowMLStats();
                        lastStatsSnapshot = _totalSnapshots;
                    }
                }
            }

            // Show progress every minute
            int currentMinute = elapsed / 60000;
            if (currentMinute > lastMinute && currentMinute < SNAPSHOT_INTERVAL_MS/60000 && !_waitingForResponse)
            {
                lastMinute = currentMinute;
                int remaining = (SNAPSHOT_INTERVAL_MS - elapsed) / 60000;
                string mlStatus = _mlEnabled ? (_mlAvailable ? (_mlLowConfidence ? " [ML: low conf]" : " [ML: active]") : " [ML: fallback]") : "";
                Dim($"  {remaining} min until next snapshot{mlStatus}  ({Color.CYAN}{app}{Color.RESET}, idle: {idle}ms)");
            }

            sw.Stop();
            if (sw.ElapsedMilliseconds > 10)
            {
                Dim($"  PERF: Monitoring cycle took {sw.ElapsedMilliseconds}ms");
            }

            Thread.Sleep(CYCLE_MS);
        }
    }

    static void ShowMLStats()
    {
        Console.WriteLine();
        Console.WriteLine($"{Color.BCYAN}━━━ ML PERFORMANCE SUMMARY ━━━{Color.RESET}");

        double avgConfidence = _mlConfidenceScores.Count > 0 ? _mlConfidenceScores.Average() : 0;
        int totalMLDecisions = _mlTriggeredSnapshots + _mlSkippedAlerts;

        Console.WriteLine($"  {Color.BOLD}Predictions Made:{Color.RESET}        {_mlPredictions}");
        Console.WriteLine($"  {Color.BOLD}Average Confidence:{Color.RESET}     {avgConfidence*100:F1}%");
        Console.WriteLine();
        Console.WriteLine($"  {Color.BOLD}ML Triggered Alerts:{Color.RESET}    {_mlTriggeredSnapshots} {Color.DIM}(detected unproductive){Color.RESET}");
        Console.WriteLine($"  {Color.BOLD}ML Skipped Alerts:{Color.RESET}      {_mlSkippedAlerts} {Color.DIM}(detected productive){Color.RESET}");
        Console.WriteLine($"  {Color.BOLD}ML Low-Confidence:{Color.RESET}     {_mlLowConfidenceSkips} {Color.DIM}(deferred to interval){Color.RESET}");
        Console.WriteLine($"  {Color.BOLD}Interval Fallbacks:{Color.RESET}     {_intervalTriggeredSnapshots} {Color.DIM}(ML disabled/unavailable/low conf){Color.RESET}");
        Console.WriteLine();
        Console.WriteLine($"  {Color.BOLD}Training Samples:{Color.RESET}       {_mlSampleCount} total ({_mlProductiveSamples} productive, {_mlUnproductiveSamples} unproductive) {Color.DIM}(min required: {MIN_SAMPLES_THRESHOLD}){Color.RESET}");
        Console.WriteLine();

        if (totalMLDecisions > 0)
        {
            double mlEfficiency = (_mlSkippedAlerts / (double)totalMLDecisions) * 100;
            Console.WriteLine($"  {Color.BOLD}Alerts Prevented:{Color.RESET}       {Color.BGREEN}{mlEfficiency:F1}%{Color.RESET} {Color.DIM}(interruptions avoided){Color.RESET}");
        }

        Console.WriteLine($"{Color.BCYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━{Color.RESET}");
        Console.WriteLine();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // DATA COLLECTION - CSV management with professional error handling
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void InitializeCSV()
    {
        try
        {
            var dir = Path.GetDirectoryName(_csvPath);

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            bool harvestExists = File.Exists(_csvPath);
            EnsureCsvSchema(_csvPath, FeatureSchema.HarvestHeaders);
            _csvFile = new StreamWriter(_csvPath, append: true);
            _csvFile.AutoFlush = true; // Ensure data is written immediately

            Info($"{(harvestExists ? "Appending to" : "Created new CSV")}: {_csvPath}");

            // Initialize activity log
            var activityLogPath = Path.Combine(Path.GetDirectoryName(_csvPath) ?? "", "ACTIVITY_LOG.CSV");
            bool activityExists = File.Exists(activityLogPath);
            EnsureCsvSchema(activityLogPath, FeatureSchema.ActivityLogHeaders);
            _activityLogFile = new StreamWriter(activityLogPath, append: true);
            _activityLogFile.AutoFlush = true;
            Dim($"  {(activityExists ? "Activity log" : "Created activity log")}: {activityLogPath}");
        }
        catch (Exception ex)
        {
            Error($"Failed to initialize CSV: {ex.Message}");
            throw;
        }
    }

    static void EnsureCsvSchema(string path, string[] expectedHeaders)
    {
        string expectedHeader = string.Join(',', expectedHeaders);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, expectedHeader + Environment.NewLine, Encoding.UTF8);
            return;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            File.WriteAllText(path, expectedHeader + Environment.NewLine, Encoding.UTF8);
            return;
        }

        if (string.Equals(lines[0], expectedHeader, StringComparison.Ordinal))
            return;

        lines[0] = expectedHeader;
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    static void TakeSnapshot(string app, string title, int idle, int attention, ActivityTickResult? tick)
    {
        int appHash = GetHash(app);
        _totalSnapshots++;

        // Capture snapshot state to avoid race conditions
        _snapshotApp = app;
        _snapshotTitle = title;
        _snapshotIdle = idle;
        _snapshotAttention = attention;
        _snapshotTick = tick ?? default;

        Console.WriteLine();
        Console.WriteLine($"{Color.BYELLOW}━━━ SNAPSHOT #{_totalSnapshots} ━━━{Color.RESET}");
        Console.WriteLine($"  {Color.BOLD}App:{Color.RESET}       {Color.CYAN}{app}{Color.RESET}");
        Console.WriteLine($"  {Color.BOLD}Hash:{Color.RESET}      {appHash}");
        Console.WriteLine($"  {Color.BOLD}Idle:{Color.RESET}      {FormatTime(idle)}");
        Console.WriteLine($"  {Color.BOLD}Attention:{Color.RESET} {FormatTime(attention)}");
        if (tick is ActivityTickResult fusedTick)
        {
            Console.WriteLine($"  {Color.BOLD}Title:{Color.RESET}     {fusedTick.Context.FocusedTitle}");
            Console.WriteLine($"  {Color.BOLD}Domain:{Color.RESET}    {fusedTick.Context.FocusedDomain}");
            Console.WriteLine($"  {Color.BOLD}Signal:{Color.RESET}    {NudgeCoreLogic.GetSignalQualityName(fusedTick.Context.SignalQuality)}");
        }
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
                _lastMLTriggerT = 0;
            }
        }, null, RESPONSE_TIMEOUT_MS, Timeout.Infinite);
    }

    static void LogActivity(string app, int idle, ActivityTickResult? tick)
    {
        try
        {
            var now = DateTime.Now;
            int appHash = GetHash(app);
            int hourOfDay = now.Hour;  // 0-23
            int dayOfWeek = (int)now.DayOfWeek;  // 0-6 (Sunday=0)
            string timestamp = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            if (tick is ActivityTickResult fusedTick)
            {
                if (fusedTick.Context.SignalQuality == SignalQuality.Poor)
                    return;

                WriteCsvRow(
                    _activityLogFile,
                    timestamp,
                    hourOfDay,
                    dayOfWeek,
                    fusedTick.DisplayAppName,
                    fusedTick.LegacyForegroundAppHash,
                    fusedTick.Context.IdleMs,
                    fusedTick.Context.FocusedAppId,
                    fusedTick.Context.FocusedTitle,
                    fusedTick.Context.FocusedDomain,
                    fusedTick.Context.FocusedWindowId,
                    fusedTick.Context.IsIdleNow,
                    fusedTick.Context.FocusedSinceMs,
                    fusedTick.Context.TitleUnchangedForMs,
                    fusedTick.Context.MappedToplevelCount,
                    fusedTick.Context.ActiveWorkspaceId,
                    NudgeCoreLogic.GetFocusSourceName(fusedTick.Context.FocusSource),
                    NudgeCoreLogic.GetSignalQualityName(fusedTick.Context.SignalQuality),
                    fusedTick.Context.FullscreenFlag);
            }
        }
        catch (Exception ex)
        {
            // Silent failure for activity logging - don't interrupt main flow
            Dim($"  Activity log error: {ex.Message}");
        }
    }

    static void SaveSnapshot(string app, int idle, int attention, bool? productive, ActivityTickResult? tick)
    {
        int appHash = GetHash(app);
        bool wroteRow = false;

        var now = DateTime.Now;
        int hourOfDay = now.Hour;  // 0-23
        int dayOfWeek = (int)now.DayOfWeek;  // 0-6 (Sunday=0)
        string timestamp = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        // null (SKIP) means no training signal at all.
        // Sample weighting (3x for productive) is handled in train_model.py
        int? productiveInt = productive.HasValue ? (productive.Value ? 1 : 0) : null;

        try
        {
            if (tick is ActivityTickResult fusedTick)
            {
                if (fusedTick.Context.SignalQuality == SignalQuality.Poor)
                {
                    Warning("  Skipping labeled row because signal quality is poor");
                }
                else
                {

                    WriteCsvRow(
                        _csvFile,
                        timestamp,
                        hourOfDay,
                        dayOfWeek,
                        fusedTick.DisplayAppName,
                        fusedTick.LegacyForegroundAppHash,
                        fusedTick.Context.IdleMs,
                        fusedTick.TimeLastRequestMs,
                        (object?)productiveInt,
                        FeatureSchema.SchemaVersion,
                        fusedTick.Context.FocusedAppId,
                        fusedTick.Context.FocusedTitle,
                        fusedTick.Context.FocusedDomain,
                        fusedTick.Context.FocusedWindowId,
                        fusedTick.Context.IsIdleNow,
                        fusedTick.Context.FocusedSinceMs,
                        fusedTick.Context.TitleUnchangedForMs,
                        fusedTick.Context.MappedToplevelCount,
                        fusedTick.Context.ActiveWorkspaceId,
                        NudgeCoreLogic.GetFocusSourceName(fusedTick.Context.FocusSource),
                        NudgeCoreLogic.GetSignalQualityName(fusedTick.Context.SignalQuality),
                        fusedTick.Context.FullscreenFlag,
                        fusedTick.Features.FocusedAppHash,
                        fusedTick.Features.FocusedDomainHash,
                        fusedTick.Features.IdleMs,
                        fusedTick.Features.TitleStabilityMs,
                        fusedTick.Features.SwitchCount60s,
                        fusedTick.Features.SwitchCount300s,
                        fusedTick.Features.DistinctApps300s,
                        fusedTick.Features.DistinctDomains300s,
                        fusedTick.Features.ReturnedToAnchorApp300s,
                        fusedTick.Features.CurrentAppShare300s,
                        fusedTick.Features.CurrentDomainShare300s,
                        fusedTick.Features.BrowserWindowFlag,
                        fusedTick.Features.CommunicationAppFlag,
                        fusedTick.Features.EntertainmentDomainFlag,
                        fusedTick.Features.WorkDomainFlag,
                        fusedTick.Features.AfkFlag,
                        fusedTick.Features.WorkspaceSwitchCount300s,
                        fusedTick.Features.DevAppFlag,
                        fusedTick.Features.CreativeAppFlag,
                        fusedTick.Features.OfficeAppFlag,
                        fusedTick.Features.CommAppFlag,
                        fusedTick.Features.EntAppFlag);
                    wroteRow = true;
                }
            }

            if (!productive.HasValue)
            {
                Dim("  Snapshot skipped — notifications paused");
            }
            else if (wroteRow)
            {
                var label = productive.Value ?
                    $"{Color.BGREEN}PRODUCTIVE{Color.RESET}" :
                    $"{Color.YELLOW}NOT PRODUCTIVE{Color.RESET}";
                Success($"✓ Saved as {label}");
            }
            else
            {
                Warning("  Snapshot label recorded, but no training row was written");
            }
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

    static void WriteCsvRow(StreamWriter? writer, params object?[] fields)
    {
        if (writer == null)
            return;

        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
                writer.Write(',');

            object? field = fields[i];
            if (field is null)
                continue;

            if (field is string text)
            {
                writer.Write(NudgeCoreLogic.EscapeCsv(text));
            }
            else if (field is IFormattable formattable)
            {
                writer.Write(formattable.ToString(null, CultureInfo.InvariantCulture));
            }
            else
            {
                writer.Write(NudgeCoreLogic.EscapeCsv(field.ToString()));
            }
        }

        writer.WriteLine();
    }

    static void BroadcastMLResponse(bool productive)
    {
        if (_lastMLTriggerT <= 0) return;

        var resp = new MLResponseEvent
        {
            T = _lastMLTriggerT,
            Response = productive
        };
        Console.WriteLine($"MLRESPONSE:{JsonSerializer.Serialize(resp, NudgeJsonContext.Default.MLResponseEvent)}");
        _lastMLTriggerT = 0;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // UDP LISTENER - Network communication with detailed logging
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void StartUDPListener()
    {
        var thread = new Thread(RunUDPListener);
        thread.IsBackground = true;
        thread.Start();

        // Success message is now printed inside RunUDPListener after successful binding
    }

    static void RunUDPListener()
    {
        UdpClient? listener = null;
        try
        {
            listener = new UdpClient(UDP_PORT);
            Success($"✓ UDP listener started on port {UDP_PORT}");

            while (true)
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = listener.Receive(ref remote);
                string message = Encoding.UTF8.GetString(data).Trim().ToUpper(CultureInfo.InvariantCulture);

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
                        SaveSnapshot(app, idle, attention, productive: true, _snapshotTick);
                        BroadcastMLResponse(true);
                        break;

                    case "NO":
                        Info($"  Received: {Color.YELLOW}NO{Color.RESET} (not productive)");
                        SaveSnapshot(app, idle, attention, productive: false, _snapshotTick);
                        BroadcastMLResponse(false);
                        break;

                    case "SKIP":
                        Dim($"  Received: SKIP (notifications paused)");
                        SaveSnapshot(app, idle, attention, productive: null, _snapshotTick);
                        break;

                    default:
                        Warning($"Unknown message: '{message}'");
                        Warning("Expected: YES or NO");
                        break;
                }
            }
        }
        catch (SocketException ex) when (ex.ErrorCode == 10048 || ex.Message.Contains("already in use", StringComparison.Ordinal))
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
    // ML INFERENCE - Communicate with ML prediction service
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static MLPrediction? QueryMLModel(string app, int idle, int attention, ActivityTickResult? tick)
    {
        try
        {
            // Create TCP socket client
            using var client = new TcpClient();
            client.SendTimeout = 1000;  // 1 second
            client.ReceiveTimeout = 1000;

            // Connect to ML service
            client.Connect(ML_HOST, ML_PORT);
            using var stream = client.GetStream();

            string requestJson;
            if (tick is ActivityTickResult fusedTick)
            {
                var request = new MLPredictionRequest
                {
                    SchemaVersion = FeatureSchema.SchemaVersion,
                    FeatureOrder = FeatureSchema.OrderedFeatureNames,
                    Features = FeatureSchema.ToFeatureDictionary(fusedTick.Features),
                    FocusSource = NudgeCoreLogic.GetFocusSourceName(fusedTick.Context.FocusSource),
                    SignalQuality = NudgeCoreLogic.GetSignalQualityName(fusedTick.Context.SignalQuality)
                };
                requestJson = JsonSerializer.Serialize(request, NudgeJsonContext.Default.MLPredictionRequest) + "\n";
            }
            else
            {
                return null;
            }

            byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson);

            // Send request
            stream.Write(requestBytes, 0, requestBytes.Length);

            // Receive response
            byte[] buffer = new byte[4096];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            string responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            var response = JsonSerializer.Deserialize(responseJson, NudgeJsonContext.Default.MLPrediction);
            return response;
        }
        catch (Exception ex)
        {
            // Silent failure - ML is optional
            if (_mlEnabled)
            {
                Dim($"  ML query failed: {ex.Message}");
            }
            return null;
        }
    }

    static bool CheckMLAvailability()
    {
        // Try a quick TCP connection test to inference server
        try
        {
            using var client = new TcpClient();
            client.Connect(ML_HOST, ML_PORT);
            client.Close();

            if (!_mlAvailable)
            {
                Success($"✓ ML inference server connected (TCP {ML_HOST}:{ML_PORT})");
                _mlAvailable = true;
            }

            // Refresh sample count from latest training metadata
            CheckTrainingProgress();

            return true;
        }
        catch
        {
            if (_mlAvailable)
            {
                Warning("ML inference server unreachable - falling back to interval-based");
                _mlAvailable = false;
            }
            return false;
        }
    }

    static void CheckTrainingProgress()
    {
        try
        {
            string metaPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nudge", "model", "trainer_meta.json");
            if (File.Exists(metaPath))
            {
                var json = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize(json, NudgeJsonContext.Default.TrainerMeta);
                if (meta != null)
                {
                    _mlSampleCount = meta.SampleCount;
                    _mlProductiveSamples = meta.NProductive;
                    _mlUnproductiveSamples = meta.NUnproductive;
                }
            }
        }
        catch { }
    }

    static bool ShouldTriggerSnapshot(string app, int idle, int attention, ActivityTickResult? tick)
    {
        // If ML not enabled, always use interval-based
        if (!_mlEnabled)
        {
            return true;  // Will be gated by elapsed time in main loop
        }

        // Check ML availability every time (this function is called once per minute)
        CheckMLAvailability();

        // ML not available — let the regular interval fallback handle notification timing
        if (!_mlAvailable)
        {
            return false;
        }

        // Require minimum training samples before trusting ML predictions
        if (!_forceTrainedModel && _mlSampleCount < MIN_SAMPLES_THRESHOLD)
        {
            Dim($"  ML: {_mlSampleCount} samples < {MIN_SAMPLES_THRESHOLD} minimum — using interval fallback");
            return false;
        }

        if (tick is ActivityTickResult fusedTick)
        {
            if (fusedTick.Context.SignalQuality == SignalQuality.Poor)
            {
                Dim("  ML: Skipping prediction because signal quality is poor");
                return false;
            }

            if (fusedTick.Features.AfkFlag == 1)
            {
                Dim("  ML: Skipping prediction because user is AFK");
                return false;
            }
        }

        // Query ML model
        var prediction = QueryMLModel(app, idle, attention, tick);

        if (prediction == null || !prediction.ModelAvailable)
        {
            // ML failed, use interval-based
            return true;
        }

        // Track statistics
        _mlPredictions++;
        _mlConfidenceScores.Add(prediction.Confidence);

        // Keep last 100 confidence scores for running average
        if (_mlConfidenceScores.Count > 100)
        {
            _mlConfidenceScores.RemoveAt(0);
        }

        // Calculate average confidence
        double avgConfidence = _mlConfidenceScores.Average();

        // ── Broadcast live state to nudge-tray AI Brain tab ──────────────────
        {
            bool willTrigger = prediction.Prediction == 0 && prediction.Confidence >= ML_CONFIDENCE_THRESHOLD;
            bool isProductive = prediction.Prediction == 1;
            // Score: 1.0 = AI very confident you ARE productive; 0.0 = very confident NOT productive
            double productivityScore = isProductive
                ? prediction.Confidence
                : (1.0 - prediction.Confidence);
            var liveEvt = new MLLiveEvent
            {
                T             = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                App           = app,
                Score         = productivityScore,
                Confidence    = prediction.Confidence,
                Productive    = isProductive,
                Triggered     = willTrigger,
                TriggerSource = "ai"
            };
            Console.WriteLine($"MLDATA:{JsonSerializer.Serialize(liveEvt, NudgeJsonContext.Default.MLLiveEvent)}");

            // Store timestamp for response correlation
            if (willTrigger)
                _lastMLTriggerT = liveEvt.T;
        }

        // Trigger based on prediction with confidence threshold enforcement
        if (prediction.Prediction == 0)
        {
            if (prediction.Confidence >= ML_CONFIDENCE_THRESHOLD)
            {
                // HIGH confidence NOT productive — trigger snapshot!
                _mlTriggeredSnapshots++;
                _mlLowConfidence = false;
                Info($"  {Color.BRED}ML TRIGGER{Color.RESET}: NOT productive (confidence: {Color.BYELLOW}{prediction.Confidence*100:F1}%{Color.RESET}, avg: {avgConfidence*100:F1}%)");
                Dim($"  {Color.DIM}Stats: {_mlPredictions} predictions, {_mlTriggeredSnapshots} triggered, {_mlSkippedAlerts} skipped{Color.RESET}");
                return true;
            }

            // Low confidence NOT productive — defer to interval-based fallback
            _mlLowConfidence = true;
            _mlLowConfidenceSkips++;
            Dim($"  {Color.DIM}ML DEFER{Color.RESET}: NOT productive (confidence: {prediction.Confidence*100:F1}% < {ML_CONFIDENCE_THRESHOLD*100:F0}% threshold) — deferring to interval");
            return false;
        }
        else
        {
            // User IS productive — skip snapshot, reset interval
            _mlSkippedAlerts++;
            _productivityConfirmed = true;
            _mlLowConfidence = false;
            Info($"  {Color.BGREEN}ML SKIP{Color.RESET}: Productive (confidence: {Color.BYELLOW}{prediction.Confidence*100:F1}%{Color.RESET}, avg: {avgConfidence*100:F1}%)");
            Dim($"  {Color.DIM}Stats: {_mlPredictions} predictions, {_mlTriggeredSnapshots} triggered, {_mlSkippedAlerts} skipped{Color.RESET}");
            return false;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // UTILITIES - Helper functions
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static string RunCommand(string cmd, string args)
    {
        return NudgeCoreLogic.RunCommand(cmd, args, timeoutMs: 5000);
    }

    static bool CommandExists(string cmd)
    {
        try
        {
            var output = RunCommand(PlatformConfig.WhichCommand, cmd);
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
        Console.WriteLine($"{Color.BCYAN}║{Color.RESET}  {Color.BOLD}Nudge Harvest{Color.RESET} - Activity Collector     {Color.BCYAN}║{Color.RESET}");
        Console.WriteLine($"{Color.BCYAN}║{Color.RESET}  {Color.DIM}Version {VERSION,-36}{Color.RESET}{Color.BCYAN}║{Color.RESET}");
        Console.WriteLine($"{Color.BCYAN}╚═══════════════════════════════════════════════╝{Color.RESET}");
        Console.WriteLine();
    }

    static void ShowHelp()
    {
        Console.WriteLine($"{Color.BOLD}Nudge Harvest{Color.RESET} - Activity Collector");
        Console.WriteLine($"Version {VERSION}");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}USAGE:{Color.RESET}");
        Console.WriteLine($"  nudge [options] [csv-path]");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}OPTIONS:{Color.RESET}");
        Console.WriteLine($"  {Color.CYAN}--help, -h{Color.RESET}          Show this help");
        Console.WriteLine($"  {Color.CYAN}--version, -v{Color.RESET}       Show version information");
        Console.WriteLine($"  {Color.CYAN}--interval, -i N{Color.RESET}    Snapshot interval in minutes (default: 5)");
        Console.WriteLine($"  {Color.CYAN}--ml{Color.RESET}                Enable ML-powered adaptive notifications");
        Console.WriteLine($"  {Color.CYAN}--force-model{Color.RESET}       Force use of trained model even if below 100 samples");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}ARGUMENTS:{Color.RESET}");
        Console.WriteLine($"  {Color.CYAN}csv-path{Color.RESET}            Path to CSV file (default: {PlatformConfig.CsvPath})");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}EXAMPLES:{Color.RESET}");
        Console.WriteLine($"  nudge                          # Use defaults");
        Console.WriteLine($"  nudge /data/harvest.csv        # Custom CSV path");
        Console.WriteLine($"  nudge --interval 2             # Snapshot every 2 minutes");
        Console.WriteLine($"  nudge --ml                     # Enable ML predictions");
        Console.WriteLine($"  nudge --ml --force-model       # Force trained model usage");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}RESPONDING TO SNAPSHOTS:{Color.RESET}");
        Console.WriteLine($"  In another terminal, run:");
        Console.WriteLine($"    {Color.BGREEN}nudge-notify YES{Color.RESET}   # I was productive");
        Console.WriteLine($"    {Color.YELLOW}nudge-notify NO{Color.RESET}    # I was not productive");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}REQUIREMENTS:{Color.RESET}");
        Console.WriteLine($"  - Windows 10+, or Linux with Wayland/X11 (Sway, GNOME, KDE, Cinnamon)");
        Console.WriteLine($"  - .NET 10.0 SDK/runtime");
        Console.WriteLine();
    }
}
