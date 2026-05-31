using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
#if !NUDGE_NOTIFY
using NudgeTray;
#endif

[assembly: InternalsVisibleTo("NudgeCrossPlatform.Tests")]

namespace NudgeCore;

internal enum NudgeStartupAction
{
    Run,
    ShowHelp,
    ShowVersion
}

internal sealed class NudgeParsedArgs
{
    public NudgeStartupAction Action { get; init; } = NudgeStartupAction.Run;
    public int? IntervalMinutes { get; init; }
    public int? MlCheckIntervalSeconds { get; init; }
    public bool MlEnabled { get; init; }
    public bool ForceTrainedModel { get; init; }
    public string? CsvPath { get; init; }
    public bool MeetingSuppression { get; init; } = true;
}

internal enum NudgeNotifyAction
{
    SendResponse,
    ShowHelp,
    ShowVersion,
    MissingResponse,
    InvalidResponse
}

internal sealed class NudgeNotifyParsedArgs
{
    public NudgeNotifyAction Action { get; init; }
    public string? Response { get; init; }
    public string? RawInput { get; init; }
}

internal readonly record struct ActivityLogEntry(DateTime Timestamp, string AppName);

internal readonly record struct HarvestEntry(DateTime Timestamp, int HourOfDay, string AppName, bool Productive);

public enum AppCategory
{
    Unknown,
    Development,
    Creative,
    Office,
    Communication,
    Entertainment,
    Utility
}

public enum CategoryConfidence
{
    Unknown  = 0,
    Fallback = 1,  // defaulted to Utility — nothing matched
    Inferred = 2,  // browser inherited anchor app's category
    Semantic = 3,  // keyword token match
    Desktop  = 4,  // XDG .desktop file (OS ground truth)
    Override = 5   // user-defined in app_categories.json
}

internal enum FocusSource
{
    Unknown,
    WindowsApi,
    WaylandActivatedProtocol,
    SwayIpc,
    KWinScript,
    GnomeShell,
    X11Ewmh,
    HeuristicProcessScan
}

internal enum IdleSource
{
    Unknown,
    Win32LastInput,
    KdeKwinIdle,
    FreedesktopScreenSaver,
    GnomeIdleMonitor,
    X11Xprintidle,
    LogindIdleHint,
    WaylandExtIdleNotify
}

internal enum SignalQuality
{
    Poor,
    Usable,
    Trusted
}

internal readonly record struct WindowObservation(
    string AppId,
    string Title,
    string WindowId,
    string WorkspaceId,
    FocusSource FocusSource,
    bool Fullscreen,
    int MappedToplevelCount,
    bool CatalogDisagrees = false);

internal readonly record struct IdleObservation(int IdleMs, IdleSource IdleSource);

internal readonly record struct ActivityContext(
    string FocusedAppId,
    string FocusedTitle,
    string FocusedDomain,
    string FocusedWindowId,
    int IdleMs,
    int IsIdleNow,
    int FocusedSinceMs,
    int TitleUnchangedForMs,
    int MappedToplevelCount,
    string ActiveWorkspaceId,
    FocusSource FocusSource,
    SignalQuality SignalQuality,
    int FullscreenFlag);

internal readonly record struct ActivityContextRecord(
    DateTime Timestamp,
    int HourOfDay,
    int DayOfWeek,
    string AppName,
    int ForegroundApp,
    int IdleTime,
    string FocusedAppId,
    string FocusedTitle,
    string FocusedDomain,
    string FocusedWindowId,
    int IsIdleNow,
    int FocusedSinceMs,
    int TitleUnchangedForMs,
    int MappedToplevelCount,
    string ActiveWorkspaceId,
    string FocusSource,
    string SignalQuality,
    int FullscreenFlag);

internal readonly record struct FeatureVector(
    int HourOfDay,
    int DayOfWeek,
    int FocusedAppHash,
    int FocusedDomainHash,
    int IdleMs,
    int FocusedSinceMs,
    int TitleStabilityMs,
    int SwitchCount60s,
    int SwitchCount300s,
    int DistinctApps300s,
    int DistinctDomains300s,
    int ReturnedToAnchorApp300s,
    double CurrentAppShare300s,
    double CurrentDomainShare300s,
    int BrowserWindowFlag,
    int CommunicationAppFlag,
    int EntertainmentDomainFlag,
    int WorkDomainFlag,
    int AfkFlag,
    int FullscreenFlag,
    int WorkspaceSwitchCount300s,
    int DevAppFlag,
    int CreativeAppFlag,
    int OfficeAppFlag,
    int CommAppFlag,
    int EntAppFlag);

internal readonly record struct ActivityTickResult(
    ActivityContext Context,
    FeatureVector Features,
    AppCategory AppCategory,
    CategoryConfidence AppCategoryConfidence,
    string DisplayAppName,
    string LegacyAppName,
    int LegacyForegroundAppHash,
    int TimeLastRequestMs);

internal sealed class ActivityFeatureTracker
{
    private const int IdleNowThresholdMs = 1000;
    private const int AfkThresholdMs = 60 * 1000;
    private const int BufferSize = 300;
    private const int WindowSeconds = 300;

    private readonly ActivitySample[] _buf = new ActivitySample[BufferSize];
    private int _head;
    private int _count;
    private string _lastFocusKey = "";
    private string _lastTitle = "";
    private DateTime _focusedSince;
    private DateTime _titleSince;
    private readonly Dictionary<string, int> _freqDict = new(16, StringComparer.Ordinal);

    public ActivityTickResult Capture(DateTime now, WindowObservation window, IdleObservation idle)
    {
        string appId = NormalizeRawValue(window.AppId, "unknown");
        string title = NormalizeRawValue(window.Title, "");
        string domain = BrowserDetector.IsBrowser(appId) ? BrowserDetector.ExtractSite(title) ?? "" : "";
        string windowId = NormalizeRawValue(window.WindowId, "");
        string workspaceId = NormalizeRawValue(window.WorkspaceId, "");
        string focusKey = !string.IsNullOrEmpty(windowId) ? windowId : $"{appId}\n{title}";

        if (_focusedSince == DateTime.MinValue || !string.Equals(focusKey, _lastFocusKey, StringComparison.Ordinal))
            _focusedSince = now;

        if (_titleSince == DateTime.MinValue || !string.Equals(title, _lastTitle, StringComparison.Ordinal))
            _titleSince = now;

        var signalQuality = DetermineSignalQuality(window, appId, title);
        // Override to poor if the user is afk (idle > AfkThresholdMs)
        if (idle.IdleMs >= AfkThresholdMs)
        {
            signalQuality = SignalQuality.Poor;
        }

        var context = new ActivityContext(
            FocusedAppId: appId,
            FocusedTitle: title,
            FocusedDomain: domain,
            FocusedWindowId: windowId,
            IdleMs: Math.Max(0, idle.IdleMs),
            IsIdleNow: idle.IdleMs >= IdleNowThresholdMs ? 1 : 0,
            FocusedSinceMs: ClampMilliseconds(now - _focusedSince),
            TitleUnchangedForMs: ClampMilliseconds(now - _titleSince),
            MappedToplevelCount: Math.Max(0, window.MappedToplevelCount),
            ActiveWorkspaceId: workspaceId,
            FocusSource: window.FocusSource,
            SignalQuality: signalQuality,
            FullscreenFlag: window.Fullscreen ? 1 : 0);

        var sample = new ActivitySample(now, focusKey, appId, domain, workspaceId);
        _buf[_head] = sample;
        _head = (_head + 1) % BufferSize;
        if (_count < BufferSize) _count++;
        TrimOld(now);

        var (featureVector, appCategory, appConf) = ComputeFeatures(now, context);
        string legacyAppName = BuildLegacyAppName(appId, title);
        string displayAppName = BuildDisplayAppName(appId, title, domain);

        _lastFocusKey = focusKey;
        _lastTitle = title;

        return new ActivityTickResult(
            Context: context,
            Features: featureVector,
            AppCategory: appCategory,
            AppCategoryConfidence: appConf,
            DisplayAppName: displayAppName,
            LegacyAppName: legacyAppName,
            LegacyForegroundAppHash: NudgeCoreLogic.GetStableHash(legacyAppName),
            TimeLastRequestMs: context.FocusedSinceMs);
    }

    private (FeatureVector Features, AppCategory Category, CategoryConfidence Confidence) ComputeFeatures(DateTime now, ActivityContext context)
    {
        int n = _count;
        string fa = context.FocusedAppId;
        string fd = context.FocusedDomain;
        bool hd = !string.IsNullOrEmpty(fd);
        DateTime c60 = now.AddSeconds(-60);

        int sc60 = 0, sc300 = 0, ws = 0, ca = 0, cd = 0, dd = 0;

        _freqDict.Clear();
        string[] db = System.Buffers.ArrayPool<string>.Shared.Rent(32);
        int dc = 0;
        string? pf = null, pw = null, p60 = null;
        bool f60 = true;

        int o = (_head - n + BufferSize) % BufferSize;
        for (int i = 0; i < n; i++)
        {
            ActivitySample s = _buf[(o + i) % BufferSize];

            if (s.Timestamp >= c60)
            {
                if (f60) { p60 = s.FocusKey; f60 = false; }
                else if (!string.Equals(p60, s.FocusKey, StringComparison.Ordinal)) { sc60++; p60 = s.FocusKey; }
            }
            if (pf == null) pf = s.FocusKey;
            else if (!string.Equals(pf, s.FocusKey, StringComparison.Ordinal)) { sc300++; pf = s.FocusKey; }
            if (!string.IsNullOrEmpty(s.WorkspaceId))
            {
                if (pw == null) pw = s.WorkspaceId;
                else if (!string.Equals(pw, s.WorkspaceId, StringComparison.Ordinal)) { ws++; pw = s.WorkspaceId; }
            }
            if (string.Equals(s.AppId, fa, StringComparison.Ordinal)) ca++;
            if (hd)
            {
                if (string.Equals(s.Domain, fd, StringComparison.Ordinal)) cd++;
                if (!string.IsNullOrEmpty(s.Domain))
                { int ji=0; while(ji<dc&&!string.Equals(db[ji],s.Domain,StringComparison.Ordinal))ji++; if(ji==dc){dd++; if(dc<32)db[dc++]=s.Domain;} }
            }
            CollectionsMarshal.GetValueRefOrAddDefault(_freqDict, s.AppId, out _)++;
        }
        System.Buffers.ArrayPool<string>.Shared.Return(db);

        _freqDict.Remove("unknown");
        int da = _freqDict.Count;

        string aa = ""; bool ra = false;
        if (_freqDict.Count > 0)
        {
            int ac = 0;
            foreach (var (ap, fr) in _freqDict)
                if (fr > ac || (fr == ac && string.Equals(ap, fa, StringComparison.Ordinal))) { ac = fr; aa = ap; }
            if (!string.IsNullOrEmpty(aa) && string.Equals(aa, fa, StringComparison.Ordinal))
            {
                bool sc = false, so = false;
                for (int i = n - 1; i >= 0; i--)
                {
                    ActivitySample s = _buf[(o + i) % BufferSize];
                    if (!sc) sc = string.Equals(s.AppId, fa, StringComparison.Ordinal);
                    else if (!so && !string.Equals(s.AppId, fa, StringComparison.Ordinal)) { so = true; break; }
                }
                ra = sc && so;
            }
        }

        int tt = Math.Max(1, n);
        var ac2 = (!string.IsNullOrEmpty(aa) && !string.Equals(aa, fa, StringComparison.Ordinal))
            ? AppCategoryClassifier.Classify(aa, "").Category : AppCategory.Unknown;
        AppCategory apc; CategoryConfidence co;
        bool ib = BrowserDetector.IsBrowser(fa);
        if (ib && hd)
        {
            AppCategory dc2 = ClassifyBrowserDomain(fd);
            (apc, co) = dc2 != AppCategory.Unknown ? (dc2, CategoryConfidence.Semantic)
                : AppCategoryClassifier.Classify(fa, context.FocusedTitle, ac2);
        }
        else (apc, co) = AppCategoryClassifier.Classify(fa, context.FocusedTitle, ac2);
        var fv = new FeatureVector(HourOfDay: now.Hour, DayOfWeek: (int)now.DayOfWeek,
            FocusedAppHash: NudgeCoreLogic.GetStableHash(fa), FocusedDomainHash: hd ? NudgeCoreLogic.GetStableHash(fd) : 0,
            IdleMs: context.IdleMs, FocusedSinceMs: context.FocusedSinceMs, TitleStabilityMs: context.TitleUnchangedForMs,
            SwitchCount60s: sc60, SwitchCount300s: sc300, DistinctApps300s: da, DistinctDomains300s: dd,
            ReturnedToAnchorApp300s: ra ? 1 : 0, CurrentAppShare300s: ca / (double)tt, CurrentDomainShare300s: cd / (double)tt,
            BrowserWindowFlag: ib ? 1 : 0, CommunicationAppFlag: IsCommunicationContext(context) ? 1 : 0,
            EntertainmentDomainFlag: hd && EntertainmentDomains.Contains(fd) ? 1 : 0,
            WorkDomainFlag: hd && (WorkDomains.Contains(fd) || fd.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) ? 1 : 0,
            AfkFlag: context.IdleMs >= AfkThresholdMs ? 1 : 0, FullscreenFlag: context.FullscreenFlag,
            WorkspaceSwitchCount300s: ws,
            DevAppFlag: apc == AppCategory.Development ? 1 : 0, CreativeAppFlag: apc == AppCategory.Creative ? 1 : 0,
            OfficeAppFlag: apc == AppCategory.Office ? 1 : 0, CommAppFlag: apc == AppCategory.Communication ? 1 : 0,
            EntAppFlag: apc == AppCategory.Entertainment ? 1 : 0);
        return (fv, apc, co);
    }
    private void TrimOld(DateTime now)
    {
        DateTime cutoff = now.AddSeconds(-WindowSeconds);
        int oldest = (_head - _count + BufferSize) % BufferSize;
        int dropped = 0;
        for (int i = 0; i < _count; i++)
        {
            int idx = (oldest + i) % BufferSize;
            if (_buf[idx].Timestamp >= cutoff) break;
            dropped++;
        }
        if (dropped > 0)
            _count -= dropped;
    }

    private static bool FastAddDistinct(string[] buf, ref int count, string value)
    {
        if (string.IsNullOrEmpty(value) || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase))
            return false;
        for (int i = 0; i < count; i++)
            if (string.Equals(buf[i], value, StringComparison.Ordinal))
                return false;
        if (count < buf.Length) buf[count++] = value;
        return true;
    }

    private static SignalQuality DetermineSignalQuality(WindowObservation window, string appId, string title)
    {
        if (string.IsNullOrWhiteSpace(appId) || string.Equals(appId, "unknown", StringComparison.OrdinalIgnoreCase))
            return SignalQuality.Poor;

        if (window.FocusSource is FocusSource.Unknown or FocusSource.HeuristicProcessScan)
            return SignalQuality.Poor;

        if (window.CatalogDisagrees || (BrowserDetector.IsBrowser(appId) && string.IsNullOrWhiteSpace(title)))
            return SignalQuality.Usable;

        return SignalQuality.Trusted;
    }

    internal static int ClampMilliseconds(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return 0;

        double totalMs = duration.TotalMilliseconds;
        return totalMs >= int.MaxValue ? int.MaxValue : (int)totalMs;
    }

    internal static string NormalizeRawValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    internal static string BuildLegacyAppName(string appId, string title)
    {
        if (BrowserDetector.IsBrowser(appId))
            return BrowserDetector.GetAppAndSite(appId, title);

        return !string.IsNullOrWhiteSpace(title) ? title : appId;
    }

    private static string BuildDisplayAppName(string appId, string title, string domain)
    {
        if (BrowserDetector.IsBrowser(appId))
        {
            string browserName = BrowserDetector.GetBrowserDisplayName(appId) ?? "Browser";
            return string.IsNullOrEmpty(domain) ? browserName : $"{browserName} ({domain})";
        }

        return !string.IsNullOrWhiteSpace(title) ? $"{appId} [{title}]" : appId;
    }

    internal static AppCategory ClassifyBrowserDomain(string domain)
    {
        if (EntertainmentDomains.Contains(domain)) return AppCategory.Entertainment;
        if (CommunicationDomains.Contains(domain)) return AppCategory.Communication;
        if (WorkDomains.Contains(domain))          return AppCategory.Development;
        return AppCategory.Unknown;
    }

    internal static bool IsCommunicationContext(ActivityContext context)
    {
        if (CommunicationAppIds.Contains(context.FocusedAppId))
            return true;

        return !string.IsNullOrEmpty(context.FocusedDomain) && CommunicationDomains.Contains(context.FocusedDomain);
    }

    private readonly record struct ActivitySample(
        DateTime Timestamp,
        string FocusKey,
        string AppId,
        string Domain,
        string WorkspaceId);

    private static readonly FrozenSet<string> CommunicationAppIds = new[]
    {
        "discord", "microsoft teams", "outlook", "signal", "slack", "teams", "thunderbird", "whatsapp", "zoom"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CommunicationDomains = new[]
    {
        "calendar.google.com", "discord.com", "mail.google.com", "meet.google.com", "outlook.office.com", "slack.com", "web.whatsapp.com", "zoom.us"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> EntertainmentDomains = new[]
    {
        "facebook.com", "instagram.com", "linkedin.com", "netflix.com", "reddit.com", "tiktok.com", "twitch.tv", "x.com", "youtube.com"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> WorkDomains = new[]
    {
        "bitbucket.org", "chat.openai.com", "chatgpt.com", "claude.ai", "confluence.atlassian.com",
        "copilot.microsoft.com", "docs.google.com", "drive.google.com", "figma.com", "github.com",
        "gitlab.com", "jira.atlassian.com", "linear.app", "localhost", "mail.google.com",
        "meet.google.com", "notion.so", "outlook.office.com", "slack.com", "stackoverflow.com"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}

internal static class FeatureSchema
{
    public const int SchemaVersion = 3;

    public static readonly string[] OrderedFeatureNames =
    [
        "hour_of_day",
        "day_of_week",
        "focused_app_hash",
        "focused_domain_hash",
        "idle_ms",
        "focused_since_ms",
        "title_stability_ms",
        "switch_count_60s",
        "switch_count_300s",
        "distinct_apps_300s",
        "distinct_domains_300s",
        "returned_to_anchor_app_300s",
        "current_app_share_300s",
        "current_domain_share_300s",
        "browser_window_flag",
        "communication_app_flag",
        "entertainment_domain_flag",
        "work_domain_flag",
        "afk_flag",
        "fullscreen_flag",
        "workspace_switch_count_300s",
        "dev_app_flag",
        "creative_app_flag",
        "office_app_flag",
        "comm_app_flag",
        "ent_app_flag"
    ];

    public static readonly string[] ActivityLogHeaders =
    [
        "timestamp",
        "hour_of_day",
        "day_of_week",
        "app_name",
        "foreground_app",
        "idle_time",
        "focused_app_id",
        "focused_title",
        "focused_domain",
        "focused_window_id",
        "is_idle_now",
        "focused_since_ms",
        "title_unchanged_for_ms",
        "mapped_toplevel_count",
        "active_workspace_id",
        "focus_source",
        "signal_quality",
        "fullscreen_flag"
    ];

    public static readonly string[] HarvestHeaders =
    [
        "timestamp",
        "hour_of_day",
        "day_of_week",
        "app_name",
        "foreground_app",
        "idle_time",
        "time_last_request",
        "productive",
        "schema_version",
        "focused_app_id",
        "focused_title",
        "focused_domain",
        "focused_window_id",
        "is_idle_now",
        "focused_since_ms",
        "title_unchanged_for_ms",
        "mapped_toplevel_count",
        "active_workspace_id",
        "focus_source",
        "signal_quality",
        "fullscreen_flag",
        "focused_app_hash",
        "focused_domain_hash",
        "idle_ms",
        "title_stability_ms",
        "switch_count_60s",
        "switch_count_300s",
        "distinct_apps_300s",
        "distinct_domains_300s",
        "returned_to_anchor_app_300s",
        "current_app_share_300s",
        "current_domain_share_300s",
        "browser_window_flag",
        "communication_app_flag",
        "entertainment_domain_flag",
        "work_domain_flag",
        "afk_flag",
        "workspace_switch_count_300s",
        "dev_app_flag",
        "creative_app_flag",
        "office_app_flag",
        "comm_app_flag",
        "ent_app_flag"
    ];

    public static IReadOnlyDictionary<string, double> ToFeatureDictionary(FeatureVector features) =>
        new Dictionary<string, double>(OrderedFeatureNames.Length, StringComparer.Ordinal)
        {
            ["hour_of_day"] = features.HourOfDay,
            ["day_of_week"] = features.DayOfWeek,
            ["focused_app_hash"] = features.FocusedAppHash,
            ["focused_domain_hash"] = features.FocusedDomainHash,
            ["idle_ms"] = features.IdleMs,
            ["focused_since_ms"] = features.FocusedSinceMs,
            ["title_stability_ms"] = features.TitleStabilityMs,
            ["switch_count_60s"] = features.SwitchCount60s,
            ["switch_count_300s"] = features.SwitchCount300s,
            ["distinct_apps_300s"] = features.DistinctApps300s,
            ["distinct_domains_300s"] = features.DistinctDomains300s,
            ["returned_to_anchor_app_300s"] = features.ReturnedToAnchorApp300s,
            ["current_app_share_300s"] = features.CurrentAppShare300s,
            ["current_domain_share_300s"] = features.CurrentDomainShare300s,
            ["browser_window_flag"] = features.BrowserWindowFlag,
            ["communication_app_flag"] = features.CommunicationAppFlag,
            ["entertainment_domain_flag"] = features.EntertainmentDomainFlag,
            ["work_domain_flag"] = features.WorkDomainFlag,
            ["afk_flag"] = features.AfkFlag,
            ["fullscreen_flag"] = features.FullscreenFlag,
            ["workspace_switch_count_300s"] = features.WorkspaceSwitchCount300s,
            ["dev_app_flag"] = features.DevAppFlag,
            ["creative_app_flag"] = features.CreativeAppFlag,
            ["office_app_flag"] = features.OfficeAppFlag,
            ["comm_app_flag"] = features.CommAppFlag,
            ["ent_app_flag"] = features.EntAppFlag
        };
}

internal static class PlatformConfig
{
    private static string? _dataDirectory;

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static string DataDirectory => _dataDirectory ??= EnsureDataDirectory();

    public static string CsvPath => Path.Combine(DataDirectory, "HARVEST.CSV");

    public static string ActivityLogPath => Path.Combine(DataDirectory, "ACTIVITY_LOG.CSV");

    /// <summary>Rolling diagnostic log file (mirrors console output) used by the Send Feedback flow.</summary>
    public static string LogFilePath => Path.Combine(DataDirectory, "nudge.log");

    public static string WhichCommand => IsWindows ? "where" : "which";

    public static string PythonCommand => IsWindows ? "python" : "python3";

    /// <summary>Returns platform-appropriate pip install arguments for a requirements file.</summary>
    public static string PipInstallArgs(string requirementsPath)
    {
        string extra = IsWindows ? "--user" : "--break-system-packages";
        return $"-m pip install {extra} -r \"{requirementsPath}\"";
    }

    /// <summary>User-level venv directory inside DataDirectory.</summary>
    public static string VenvDirectory => Path.Combine(DataDirectory, "venv");

    /// <summary>Path to the Python executable inside the user-level venv, or empty if not yet created.</summary>
    public static string VenvPythonPath =>
        IsWindows
            ? Path.Combine(VenvDirectory, "Scripts", "python.exe")
            : Path.Combine(VenvDirectory, "bin", "python");

    /// <summary>Path to the pip executable inside the user-level venv.</summary>
    public static string VenvPipPath =>
        IsWindows
            ? Path.Combine(VenvDirectory, "Scripts", "pip.exe")
            : Path.Combine(VenvDirectory, "bin", "pip");

    /// <summary>Path to the bundled self-contained Python runtime shipped with the app.</summary>
    public static string BundledPythonPath(string baseDir) =>
        IsWindows
            ? Path.Combine(baseDir, "python-runtime", "python.exe")
            : Path.Combine(baseDir, "python-runtime", "bin", "python3");

    /// <summary>
    /// Find a working Python: check user-level venv, bundled runtime, local venv, dev venv, then system Python.
    /// After EnsureVenv() has run, this returns the venv Python.
    /// </summary>
    public static string FindPython(string baseDir)
    {
        if (File.Exists(VenvPythonPath)) return VenvPythonPath;
        var bundled = BundledPythonPath(baseDir);
        if (File.Exists(bundled)) return bundled;
        if (IsWindows)
        {
            var local = Path.Combine(baseDir, "venv", "Scripts", "python.exe");
            if (File.Exists(local)) return local;
            var srcDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
            var dev = Path.Combine(srcDir, "venv", "Scripts", "python.exe");
            if (File.Exists(dev)) return dev;
            return ProbeSystemPython("py", "python", "python3") ?? "py";
        }
        var localNix = Path.Combine(baseDir, "venv", "bin", "python");
        if (File.Exists(localNix)) return localNix;
        var srcDirNix = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        var devNix = Path.Combine(srcDirNix, "venv", "bin", "python");
        if (File.Exists(devNix)) return devNix;
        return PythonCommand;
    }

    static string? ProbeSystemPython(params string[] candidates)
    {
        foreach (var cmd in candidates)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                p.Start();
                // Read output streams to prevent deadlock on Windows
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(3000))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    continue;
                }
                if (p.ExitCode == 0)
                    return cmd;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Creates the user-level venv at <see cref="VenvDirectory"/> if it does not exist.
    /// Must be called after <see cref="DataDirectory"/> is available.
    /// Returns true if the venv was created or already exists.
    /// </summary>
    public static bool EnsureVenv(string systemPython)
    {
        if (File.Exists(VenvPythonPath)) return true;
        try
        {
            Directory.CreateDirectory(VenvDirectory);
            string args = IsWindows
                ? $"-m venv \"{VenvDirectory}\""
                : $"-m venv \"{VenvDirectory}\"";
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = systemPython,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            proc.Start();
            // Read streams asynchronously to prevent deadlock on Windows
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            if (!proc.WaitForExit(30000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                Console.WriteLine($"[WARN] Failed to create venv: timed out after 30s");
                return false;
            }
            if (proc.ExitCode != 0)
            {
                Console.WriteLine($"[WARN] Failed to create venv (exit code {proc.ExitCode})");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to create venv: {ex.Message}");
            return false;
        }
    }

    public static string DotnetCommand => IsWindows ? "dotnet.exe" : "dotnet";

    private static string EnsureDataDirectory()
    {
        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string nudgeDir = Path.Combine(homeDir, ".nudge");
        Directory.CreateDirectory(nudgeDir);
        return nudgeDir;
    }
}



internal static class AppCategoryClassifier
{
    private const int MaxCacheSize = 1000;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (AppCategory Category, CategoryConfidence Confidence)> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    // Desktop-file lookups are a pure function of appId + filesystem (stable for the
    // session), but the result is NOT folded into _cache for browsers (their final
    // category is domain-dependent). Memoize the raw lookup separately so browser ticks
    // don't re-scan the filesystem every Capture — otherwise each browser tick on Linux
    // walks DesktopSearchPaths, allocating heavily and stalling the harvest loop.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool Found, AppCategory Category)> _desktopFileCache
        = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _loadLock = new();
    private static bool _loaded;
    private static readonly string? _inferredCachePath;

    private static readonly FrozenDictionary<string, AppCategory> XdgCategoryMap =
        new Dictionary<string, AppCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["Development"]      = AppCategory.Development,
            ["IDE"]              = AppCategory.Development,
            ["TextEditor"]       = AppCategory.Development,
            ["TerminalEmulator"] = AppCategory.Development,
            ["WebDevelopment"]   = AppCategory.Development,
            ["Office"]           = AppCategory.Office,
            ["WordProcessor"]    = AppCategory.Office,
            ["Spreadsheet"]      = AppCategory.Office,
            ["Presentation"]     = AppCategory.Office,
            ["Publishing"]       = AppCategory.Office,
            ["Viewer"]           = AppCategory.Office,
            ["Graphics"]         = AppCategory.Creative,
            ["2DGraphics"]       = AppCategory.Creative,
            ["3DGraphics"]       = AppCategory.Creative,
            ["RasterGraphics"]   = AppCategory.Creative,
            ["VectorGraphics"]   = AppCategory.Creative,
            ["AudioVideo"]       = AppCategory.Creative,
            ["Audio"]            = AppCategory.Creative,
            ["Video"]            = AppCategory.Creative,
            ["Midi"]             = AppCategory.Creative,
            ["Sequencer"]        = AppCategory.Creative,
            ["Game"]             = AppCategory.Entertainment,
            ["ActionGame"]       = AppCategory.Entertainment,
            ["Network"]          = AppCategory.Communication,
            ["Chat"]             = AppCategory.Communication,
            ["InstantMessaging"] = AppCategory.Communication,
            ["Email"]            = AppCategory.Communication,
            ["Calendar"]         = AppCategory.Communication,
            ["VideoConference"]  = AppCategory.Communication,
            ["IRCClient"]        = AppCategory.Communication,
            ["System"]           = AppCategory.Utility,
            ["Utility"]          = AppCategory.Utility,
            ["FileManager"]      = AppCategory.Utility,
            ["Monitor"]          = AppCategory.Utility,
            ["Security"]         = AppCategory.Utility,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Ordered: first keyword match wins; more-specific lists come first
    private static readonly (string[] Keywords, AppCategory Category)[] SemanticRules =
    [
        (["konsole", "kitty", "alacritty", "wezterm", "tilix", "hyper", "xterm", "urxvt",
          "neovim", "codium", "helix", "sublime", "antigravity",
          "shell", "git", "debug", "sql", "database", "diff", "term", "terminal", "code"], AppCategory.Development),
        (["openshot", "blender", "gimp", "krita", "inkscape", "darktable", "rawtherapee", "kdenlive", "pitivi",
          "obs", "audacity", "ardour", "resolve",
          "paint", "draw", "sketch", "design", "cad", "render", "synth", "studio"], AppCategory.Creative),
        (["libreoffice", "writer", "calc", "impress", "okular", "evince", "mupdf", "zathura",
          "sheet", "xls", "pdf", "present", "note", "organizer"], AppCategory.Office),
        (["discord", "slack", "teams", "zoom", "signal", "telegram", "thunderbird", "evolution", "whatsapp",
          "chat", "mail", "message", "meet", "call", "collab"], AppCategory.Communication),
        (["spotify", "vlc", "mpv", "mplayer", "totem", "rhythmbox", "clementine", "amarok",
          "steam", "lutris", "heroic",
          "music", "game", "stream", "movie", "player"], AppCategory.Entertainment),
    ];

    private static readonly string[] DesktopSearchPaths;

    static AppCategoryClassifier()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = new List<string>
        {
            Path.Combine(home, ".local", "share", "applications"),
            "/usr/share/applications",
            "/usr/local/share/applications",
        };

        string? xdgDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (!string.IsNullOrWhiteSpace(xdgDirs))
        {
            foreach (var dir in xdgDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                string appDir = Path.Combine(dir.Trim(), "applications");
                if (!paths.Contains(appDir))
                    paths.Add(appDir);
            }
        }

        DesktopSearchPaths = [.. paths];
        _inferredCachePath = Path.Combine(PlatformConfig.DataDirectory, "inferred_categories.json");
    }

    public static (AppCategory Category, CategoryConfidence Confidence) Classify(string appId, string title, AppCategory anchorCategory = AppCategory.Unknown)
    {
        if (string.IsNullOrWhiteSpace(appId) || string.Equals(appId, "unknown", StringComparison.OrdinalIgnoreCase))
            return (AppCategory.Unknown, CategoryConfidence.Unknown);

        // Browser category is domain-dependent and changes every tick — never serve from cache
        bool isBrowser = BrowserDetector.IsBrowser(appId);
        if (!isBrowser)
        {
            if (_cache.TryGetValue(appId, out var cached))
                return cached;

            EnsureLoaded();
            if (_cache.TryGetValue(appId, out cached))
                return cached;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && TryClassifyFromDesktopFile(appId, out var desktopCat))
            return Store(appId, desktopCat, CategoryConfidence.Desktop, persist: true);

        if (TryClassifyFromTokens(appId, title, out var tokenCat))
            return Store(appId, tokenCat, CategoryConfidence.Semantic, persist: true);

        // Browser-anchor temporal fusion: browser inherits anchor context when anchor is a focused work/creative category
        if (BrowserDetector.IsBrowser(appId) && anchorCategory is AppCategory.Development or AppCategory.Creative or AppCategory.Office)
            return Store(appId, anchorCategory, CategoryConfidence.Inferred, persist: false);

        // Fallback: don't persist — nothing was learned, re-classification is fast
        return Store(appId, AppCategory.Utility, CategoryConfidence.Fallback, persist: false);
    }

    public static float GetConfidenceScore(CategoryConfidence conf) => conf switch
    {
        CategoryConfidence.Override => 1.00f,
        CategoryConfidence.Desktop  => 0.95f,
        CategoryConfidence.Semantic => 0.75f,
        CategoryConfidence.Inferred => 0.50f,
        CategoryConfidence.Fallback => 0.20f,
        _                           => 0.00f
    };

    public static string GetConfidenceLabel(CategoryConfidence conf) => conf switch
    {
        CategoryConfidence.Override or CategoryConfidence.Desktop => "Verified",
        CategoryConfidence.Semantic => "Estimated",
        CategoryConfidence.Inferred => "Inferred",
        _ => ""
    };

    public static string GetCategoryName(AppCategory cat) => cat switch
    {
        AppCategory.Development  => "Development",
        AppCategory.Creative     => "Creative & Design",
        AppCategory.Office       => "Office & Writing",
        AppCategory.Communication => "Communication",
        AppCategory.Entertainment => "Entertainment",
        AppCategory.Utility      => "Utility",
        _                        => ""
    };

    // Testable: map a raw XDG Categories= value to AppCategory
    public static AppCategory MapXdgCategories(string categoriesValue)
    {
        if (string.IsNullOrWhiteSpace(categoriesValue))
            return AppCategory.Unknown;

        // Two-pass: prefer non-Utility matches
        AppCategory utilityResult = AppCategory.Unknown;
        foreach (var token in categoriesValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!XdgCategoryMap.TryGetValue(token.Trim(), out var cat))
                continue;
            if (cat != AppCategory.Utility)
                return cat;
            utilityResult = cat;
        }

        return utilityResult;
    }

    // Testable: keyword-based classification
    public static bool TryClassifyFromTokens(string appId, string title, out AppCategory category)
    {
        string combined = $"{appId} {title}".ToLowerInvariant();
        foreach (var (keywords, cat) in SemanticRules)
        {
            foreach (var kw in keywords)
            {
                if (combined.Contains(kw, StringComparison.Ordinal))
                {
                    category = cat;
                    return true;
                }
            }
        }

        category = AppCategory.Unknown;
        return false;
    }

    private static bool TryClassifyFromDesktopFile(string appId, out AppCategory category)
    {
        if (_desktopFileCache.TryGetValue(appId, out var cached))
        {
            category = cached.Category;
            return cached.Found;
        }

        bool found = TryClassifyFromDesktopFileUncached(appId, out category);
        _desktopFileCache[appId] = (found, category);
        return found;
    }

    private static bool TryClassifyFromDesktopFileUncached(string appId, out AppCategory category)
    {
        // Build candidate filenames: try exact, lowercase, and cleaned variants
        string lower = appId.ToLowerInvariant();
        string[] candidates = [$"{appId}.desktop", $"{lower}.desktop"];

        foreach (var dir in DesktopSearchPaths)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var candidate in candidates)
            {
                string path = Path.Combine(dir, candidate);
                if (!File.Exists(path))
                    continue;

                string? cats = ReadDesktopCategories(path);
                if (cats is null)
                    continue;

                var result = MapXdgCategories(cats);
                if (result != AppCategory.Unknown)
                {
                    category = result;
                    return true;
                }
            }
        }

        // Fallback: scan all .desktop files and match on Exec= binary name
        foreach (var dir in DesktopSearchPaths)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.desktop"))
                {
                    if (TryMatchByExec(file, appId, out var execCat))
                    {
                        category = execCat;
                        return true;
                    }
                }
            }
            catch { /* permission errors */ }
        }

        category = AppCategory.Unknown;
        return false;
    }

    private static string? ReadDesktopCategories(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var span = line.AsSpan().TrimStart();
                if (span.StartsWith("Categories=", StringComparison.OrdinalIgnoreCase))
                    return span["Categories=".Length..].ToString();
                // Stop at next section header (not [Desktop Entry])
                if (span.Length > 0 && span[0] == '[' && !span.StartsWith("[Desktop Entry]", StringComparison.OrdinalIgnoreCase))
                    break;
            }
        }
        catch { /* ignore */ }

        return null;
    }

    private static bool TryMatchByExec(string filePath, string appId, out AppCategory category)
    {
        category = AppCategory.Unknown;
        string? cats = null;
        bool matched = false;

        try
        {
            foreach (var line in File.ReadLines(filePath))
            {
                var span = line.AsSpan().TrimStart();
                if (span.StartsWith("Exec=", StringComparison.OrdinalIgnoreCase))
                {
                    string exec = span["Exec=".Length..].ToString();
                    string binary = Path.GetFileNameWithoutExtension(exec.Split(' ')[0]).ToLowerInvariant();
                    if (binary.Contains(appId, StringComparison.OrdinalIgnoreCase) ||
                        appId.Contains(binary, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                    }
                }
                else if (span.StartsWith("Categories=", StringComparison.OrdinalIgnoreCase))
                {
                    cats = span["Categories=".Length..].ToString();
                }

                if (matched && cats != null)
                {
                    var result = MapXdgCategories(cats);
                    if (result != AppCategory.Unknown)
                    {
                        category = result;
                        return true;
                    }
                    return false;
                }
            }
        }
        catch { /* ignore */ }

        return false;
    }

    private static (AppCategory, CategoryConfidence) Store(string appId, AppCategory category, CategoryConfidence confidence, bool persist)
    {
        if (!BrowserDetector.IsBrowser(appId))
        {
            if (_cache.Count >= MaxCacheSize)
                _cache.Clear();
            _cache[appId] = (category, confidence);
        }
        if (persist && !BrowserDetector.IsBrowser(appId) && _inferredCachePath != null)
            PersistAsync(appId, category, confidence);
        return (category, confidence);
    }

    private static void PersistAsync(string appId, AppCategory category, CategoryConfidence confidence)
    {
        // Fire-and-forget: write new entry to the on-disk cache as "Category:confidence"
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string path = _inferredCachePath!;
                Dictionary<string, string> stored;
                if (File.Exists(path))
                {
                    stored = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(path)) ?? [];
                }
                else
                {
                    stored = [];
                }

                stored[appId] = $"{GetCategoryName(category)}:{confidence.ToString().ToLowerInvariant()}";
                File.WriteAllText(path, JsonSerializer.Serialize(stored));
            }
            catch { /* ignore write failures */ }
        });
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            _loaded = true;

            // Load user overrides: ~/.nudge/app_categories.json (highest confidence)
            try
            {
                string overridesPath = Path.Combine(PlatformConfig.DataDirectory, "app_categories.json");
                if (File.Exists(overridesPath))
                {
                    var overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(overridesPath));
                    if (overrides != null)
                    {
                        foreach (var (key, val) in overrides)
                        {
                            if (TryParseCategory(val, out var cat))
                                _cache[key] = (cat, CategoryConfidence.Override);
                        }
                    }
                }
            }
            catch { /* ignore */ }

            // Load previously inferred cache: ~/.nudge/inferred_categories.json
            // Values are stored as "Category:confidence" (new) or plain "Category" (legacy → Semantic)
            if (_inferredCachePath != null && File.Exists(_inferredCachePath))
            {
                try
                {
                    var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(_inferredCachePath));
                    if (saved != null)
                    {
                        foreach (var (key, val) in saved)
                        {
                            if (_cache.ContainsKey(key))
                                continue;

                            int colon = val.IndexOf(':');
                            string catPart = colon > 0 ? val[..colon] : val;
                            string confPart = colon > 0 ? val[(colon + 1)..] : "semantic";

                            if (!TryParseCategory(catPart, out var cat))
                                continue;

                            CategoryConfidence conf = confPart switch
                            {
                                "override" => CategoryConfidence.Override,
                                "desktop"  => CategoryConfidence.Desktop,
                                "inferred" => CategoryConfidence.Inferred,
                                "fallback" => CategoryConfidence.Fallback,
                                _          => CategoryConfidence.Semantic
                            };
                            _cache[key] = (cat, conf);
                        }
                    }
                }
                catch { /* ignore */ }
            }
        }
    }

    internal static bool TryParseCategory(string value, out AppCategory category)
    {
        category = value.ToLowerInvariant() switch
        {
            "development"           => AppCategory.Development,
            "creative" or "creative & design" => AppCategory.Creative,
            "office" or "office & writing"    => AppCategory.Office,
            "communication"         => AppCategory.Communication,
            "entertainment"         => AppCategory.Entertainment,
            "utility"               => AppCategory.Utility,
            _                       => AppCategory.Unknown
        };
        return category != AppCategory.Unknown;
    }
}

internal static class NudgeCoreLogic
{
    internal const string TraySingleInstanceMutexName = "Global\\NudgeTray.SingleInstance";

    internal static NudgeParsedArgs ParseNudgeArgs(string[] args)
    {
        int? intervalMinutes = null;
        int? mlIntervalSeconds = null;
        bool mlEnabled = false;
        bool forceModel = false;
        string? csvPath = null;
        bool meetingSuppression = true;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--help" || arg == "-h")
            {
                return new NudgeParsedArgs { Action = NudgeStartupAction.ShowHelp };
            }
            if (arg == "--version" || arg == "-v")
            {
                return new NudgeParsedArgs { Action = NudgeStartupAction.ShowVersion };
            }
            if (arg == "--interval" || arg == "-i")
            {
                if (i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int minutes))
                        intervalMinutes = minutes;
                    i++;
                }
                continue;
            }
            if (arg == "--ml-interval")
            {
                if (i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int sec))
                        mlIntervalSeconds = sec;
                    i++;
                }
                continue;
            }
            if (arg == "--ml")
            {
                mlEnabled = true;
                continue;
            }
            if (arg == "--force-model")
            {
                forceModel = true;
                continue;
            }
            if (arg == "--no-meeting-suppression")
            {
                meetingSuppression = false;
                continue;
            }
            if (!arg.StartsWith("--", StringComparison.Ordinal) && !arg.StartsWith('-'))
            {
                csvPath = arg;
            }
        }

        return new NudgeParsedArgs
        {
            Action = NudgeStartupAction.Run,
            IntervalMinutes = intervalMinutes,
            MlCheckIntervalSeconds = mlIntervalSeconds,
            MlEnabled = mlEnabled,
            ForceTrainedModel = forceModel,
            CsvPath = csvPath,
            MeetingSuppression = meetingSuppression
        };
    }

    internal static NudgeNotifyParsedArgs ParseNudgeNotifyArgs(string[] args)
    {
        if (args.Length > 0)
        {
            if (args[0] == "--help" || args[0] == "-h")
            {
                return new NudgeNotifyParsedArgs { Action = NudgeNotifyAction.ShowHelp };
            }
            if (args[0] == "--version" || args[0] == "-v")
            {
                return new NudgeNotifyParsedArgs { Action = NudgeNotifyAction.ShowVersion };
            }
        }

        if (args.Length == 0)
        {
            return new NudgeNotifyParsedArgs { Action = NudgeNotifyAction.MissingResponse };
        }

        string response = args[0].ToUpperInvariant();
        if (response != "YES" && response != "NO")
        {
            return new NudgeNotifyParsedArgs
            {
                Action = NudgeNotifyAction.InvalidResponse,
                RawInput = args[0]
            };
        }

        return new NudgeNotifyParsedArgs
        {
            Action = NudgeNotifyAction.SendResponse,
            Response = response
        };
    }

    internal static bool ShouldExitForExistingTrayInstance(bool createdNew) => !createdNew;

    internal static string RunCommand(string cmd, string args, int timeoutMs = 5000)
    {
        try
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

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return "";
            }

            Task.WaitAll(stdoutTask, stderrTask);
            return stdoutTask.Result;
        }
        catch
        {
            return "";
        }
    }

    internal static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "📸 Next snapshot: soon";

        return remaining.TotalSeconds < 60
            ? $"📸 Next snapshot in {(int)remaining.TotalSeconds}s"
            : $"📸 Next snapshot in {(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s";
    }

    internal static (string app, string title) ExtractFocusedAppFromSwayJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return ("unknown", "");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var snapshot = FindFocusedNodeInSwayJson(doc.RootElement);
            return (snapshot.AppId, snapshot.Title);
        }
        catch
        {
            if (json.Contains("\"focused\":true", StringComparison.Ordinal))
            {
                string app = "unknown";
                string title = "";

                int idx = json.IndexOf("\"app_id\":\"", StringComparison.Ordinal);
                if (idx == -1) idx = json.IndexOf("\"class\":\"", StringComparison.Ordinal);

                if (idx != -1)
                {
                    int start = json.IndexOf('"', idx + 9) + 1;
                    int end = json.IndexOf('"', start);
                    if (end > start)
                        app = json.Substring(start, end - start);
                }

                int nameIdx = json.IndexOf("\"name\":\"", StringComparison.Ordinal);
                if (nameIdx != -1)
                {
                    int start = nameIdx + 8;
                    int end = json.IndexOf('"', start);
                    if (end > start)
                        title = json.Substring(start, end - start);
                }

                return (app, title);
            }
            return ("unknown", "");
        }
    }

    internal static SwayFocusSnapshot FindFocusedNodeInSwayJson(JsonElement node) =>
        FindFocusedNodeInSwayJson(node, currentWorkspace: "");

    private static SwayFocusSnapshot FindFocusedNodeInSwayJson(JsonElement node, string currentWorkspace)
    {
        if (node.TryGetProperty("type", out var nodeType) &&
            nodeType.ValueKind == JsonValueKind.String &&
            string.Equals(nodeType.GetString(), "workspace", StringComparison.OrdinalIgnoreCase) &&
            node.TryGetProperty("name", out var workspaceName) &&
            workspaceName.ValueKind == JsonValueKind.String)
        {
            currentWorkspace = workspaceName.GetString() ?? currentWorkspace;
        }

        if (node.TryGetProperty("focused", out var focused) && focused.GetBoolean())
        {
            string app = "unknown";
            string title = "";
            string windowId = "";
            bool fullscreen = false;

            if (node.TryGetProperty("app_id", out var appId) && appId.ValueKind == JsonValueKind.String)
            {
                app = appId.GetString() ?? "unknown";
            }
            else if (node.TryGetProperty("window_properties", out var props) &&
                     props.TryGetProperty("class", out var className))
            {
                app = className.GetString() ?? "unknown";
            }

            if (node.TryGetProperty("name", out var name))
            {
                title = name.GetString() ?? "";
            }

            if (node.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
            {
                windowId = id.GetRawText();
            }

            if (node.TryGetProperty("fullscreen_mode", out var fullscreenMode) &&
                fullscreenMode.ValueKind == JsonValueKind.Number)
            {
                fullscreen = fullscreenMode.GetInt32() > 0;
            }

            return new SwayFocusSnapshot(
                AppId: app == "" ? "unknown" : app,
                Title: title,
                WindowId: windowId,
                WorkspaceId: currentWorkspace,
                Fullscreen: fullscreen);
        }

        foreach (string arrayProp in new[] { "nodes", "floating_nodes" })
        {
            if (node.TryGetProperty(arrayProp, out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    var result = FindFocusedNodeInSwayJson(child, currentWorkspace);
                    if (result.AppId != "unknown")
                        return result;
                }
            }
        }

        return SwayFocusSnapshot.Empty;
    }

    internal static string ExtractQuotedString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "unknown";

        foreach (char q in new[] { '"', '\'' })
        {
            int start = input.IndexOf(q) + 1;
            if (start > 0)
            {
                int end = input.IndexOf(q, start);
                if (end > start)
                    return input.Substring(start, end - start);
            }
        }

        return "unknown";
    }

    internal static bool IsNudgeForegroundWindow(string appName, string title)
    {
        return MatchesNudgeWindowMarker(appName) ||
               title.Contains("Were you productive?", StringComparison.OrdinalIgnoreCase);
    }

    internal static int GetStableHash(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        const uint fnvPrime = 16777619;
        uint hash = 2166136261;

        foreach (char c in text)
        {
            hash ^= c;
            hash *= fnvPrime;
        }

        return unchecked((int)hash);
    }

    internal static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        string sanitized = value.Replace('\r', ' ').Replace('\n', ' ');
        if (sanitized.IndexOfAny([',', '"']) >= 0)
            return $"\"{sanitized.Replace("\"", "\"\"")}\"";

        return sanitized;
    }

    internal static string GetFocusSourceName(FocusSource source) => source switch
    {
        FocusSource.WindowsApi => "windows_api",
        FocusSource.WaylandActivatedProtocol => "wayland_activated_protocol",
        FocusSource.SwayIpc => "sway_ipc",
        FocusSource.KWinScript => "kwin_script",
        FocusSource.GnomeShell => "gnome_shell",
        FocusSource.X11Ewmh => "x11_ewmh",
        FocusSource.HeuristicProcessScan => "heuristic_process_scan",
        _ => "unknown"
    };

    internal static string GetIdleSourceName(IdleSource source) => source switch
    {
        IdleSource.Win32LastInput => "win32_last_input",
        IdleSource.KdeKwinIdle => "kde_kwin_idle",
        IdleSource.WaylandExtIdleNotify => "wayland_ext_idle_notify",
        IdleSource.FreedesktopScreenSaver => "freedesktop_screensaver",
        IdleSource.GnomeIdleMonitor => "gnome_idle_monitor",
        IdleSource.X11Xprintidle => "x11_xprintidle",
        IdleSource.LogindIdleHint => "logind_idle_hint",
        _ => "unknown"
    };

    internal static string GetSignalQualityName(SignalQuality quality) => quality switch
    {
        SignalQuality.Trusted => "trusted",
        SignalQuality.Usable => "usable",
        _ => "poor"
    };

    private static readonly FrozenSet<string> s_systemProcessDenylist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Window managers / compositors
            "kwin_wayland", "kwin_x11", "plasmashell", "gnome-shell",
            "xfwm4", "openbox", "mutter", "compiz", "i3",
            // System bars / panels
            "waybar", "polybar", "lxpanel", "xfce4-panel",
            // Test artifacts
            "unknown", "test-mode",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static bool ShouldIgnoreAnalyticsApp(string appName)
    {
        // Self — never count the tracker itself
        if (appName.Contains("nudge", StringComparison.OrdinalIgnoreCase))
            return true;
        // Known system/WM/test names
        if (s_systemProcessDenylist.Contains(appName))
            return true;
        // Reverse-DNS bundle IDs (e.g. com.company.app) — raw IDs, not display names
        if (appName.Length > 5 && char.IsLower(appName[0]) && CountDots(appName) >= 2)
            return true;
        return false;
    }

    private static int CountDots(string s)
    {
        int n = 0;
        foreach (char c in s) if (c == '.') n++;
        return n;
    }

    internal static bool TryParseActivityLogLine(string line, out ActivityLogEntry entry)
    {
        entry = default;
        if (!TryGetCsvField(line.AsSpan(), 0, out var timestampField) ||
            !TryGetCsvField(line.AsSpan(), 3, out var appField) ||
            !DateTime.TryParse(timestampField, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            return false;
        }

        string appName = appField.ToString();
        if (string.IsNullOrWhiteSpace(appName))
            return false;

        entry = new ActivityLogEntry(timestamp, appName);
        return true;
    }

    internal static bool TryParseHarvestLine(string line, out HarvestEntry entry)
    {
        entry = default;
        if (!TryGetCsvField(line.AsSpan(), 0, out var timestampField) ||
            !TryGetCsvField(line.AsSpan(), 1, out var hourField) ||
            !TryGetCsvField(line.AsSpan(), 3, out var appField) ||
            !TryGetCsvField(line.AsSpan(), 7, out var productiveField) ||
            !DateTime.TryParse(timestampField, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) ||
            !int.TryParse(hourField, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour))
        {
            return false;
        }

        string appName = appField.ToString();
        if (string.IsNullOrWhiteSpace(appName))
            return false;

        entry = new HarvestEntry(timestamp, hour, appName, productiveField.Length == 1 && productiveField[0] == '1');
        return true;
    }

    internal static bool MatchesNudgeWindowMarker(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Equals("Window", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("CustomNotification", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("nudge-tray", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("NudgeTray", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Nudge Analytics", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Productivity Check", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryGetCsvField(ReadOnlySpan<char> line, int fieldIndex, out ReadOnlySpan<char> field)
    {
        int currentField = 0;
        int start = 0;
        bool inQuotes = false;

        for (int i = 0; i <= line.Length; i++)
        {
            if (i < line.Length && line[i] == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (i == line.Length || (!inQuotes && line[i] == ','))
            {
                if (currentField == fieldIndex)
                {
                    field = TrimCsvField(line[start..i]);
                    return true;
                }

                currentField++;
                start = i + 1;
            }
        }

        field = default;
        return false;
    }

    private static ReadOnlySpan<char> TrimCsvField(ReadOnlySpan<char> field)
    {
        field = field.Trim();
        if (field.Length >= 2 && field[0] == '"' && field[^1] == '"')
        {
            // Remove the outer quotes
            field = field[1..^1];
            
            // Check for and unescape escaped quotes ("" -> ")
            for (int i = 0; i < field.Length - 1; i++)
            {
                if (field[i] == '"' && field[i + 1] == '"')
                {
                    // Found an escaped quote, we need to create a new string to unescape
                    var unescaped = field.ToString().Replace("\"\"", "\"");
                    return unescaped.AsSpan();
                }
            }
        }

        return field;
    }

    internal readonly record struct SwayFocusSnapshot(
        string AppId,
        string Title,
        string WindowId,
        string WorkspaceId,
        bool Fullscreen)
    {
        public static readonly SwayFocusSnapshot Empty = new("unknown", "", "", "", false);
    }
}

// ── Presence Detection ────────────────────────────────────────────────────────

internal enum PresenceSource
{
    None,             // Detection not available on this platform — gate fails open
    PipeWire,
    PulseAudio,
    WindowsRegistry,
    ProcessList       // Process-scan fallback — weak signal, used when hw detection unavailable
}

internal readonly record struct PresenceState(
    bool IsMicActive,
    bool IsCameraActive,
    bool IsScreenSharing,
    PresenceSource Source)
{
    public static readonly PresenceState Unavailable = new(false, false, false, PresenceSource.None);
    public bool InMeeting => IsMicActive || IsCameraActive;
}

internal enum SuppressionReason { None, PoorSignal, Afk, InMeeting, ScreenSharing }

internal readonly record struct GateDecision(bool Suppress, SuppressionReason Reason)
{
    public static readonly GateDecision Allow = new(false, SuppressionReason.None);
    public static GateDecision Because(SuppressionReason reason) => new(true, reason);
}

internal static class SnapshotGate
{
    // Authoritative suppression check applied before every snapshot, both ML and interval paths.
    // Always gates on AFK/poor-signal (fixes the latent interval-path bypass).
    // Gates on presence only when Source != None (fail-open: unknown → never suppress).
    public static GateDecision Evaluate(ActivityTickResult? tick, PresenceState presence)
    {
        if (tick is ActivityTickResult t)
        {
            if (t.Context.SignalQuality == SignalQuality.Poor)
                return GateDecision.Because(SuppressionReason.PoorSignal);
            if (t.Features.AfkFlag == 1)
                return GateDecision.Because(SuppressionReason.Afk);
        }

        if (presence.Source != PresenceSource.None)
        {
            if (presence.IsScreenSharing)
                return GateDecision.Because(SuppressionReason.ScreenSharing);
            if (presence.InMeeting)
                return GateDecision.Because(SuppressionReason.InMeeting);
        }

        return GateDecision.Allow;
    }
}

internal static class PulseAudioParser
{
    // Returns true if `pactl list source-outputs` output has any RUNNING stream.
    // Works on PulseAudio and PipeWire-pulse (pipewire-alsa compatibility layer).
    public static bool HasActiveCaptureStream(string pactlOutput)
    {
        if (string.IsNullOrEmpty(pactlOutput)) return false;
        return pactlOutput.Contains("State: RUNNING", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class PipeWireParser
{
    // Parses `pw-dump` JSON to determine mic, camera, and screen-sharing state.
    // Nodes with state "running" and the matching media.class are considered active.
    // Screen sharing is detected via Stream/Output/Video nodes owned by the XDG portal.
    public static PresenceState Parse(string pwDumpJson)
    {
        if (string.IsNullOrWhiteSpace(pwDumpJson)) return PresenceState.Unavailable;

        bool micActive = false;
        bool camActive = false;
        bool screenSharing = false;

        try
        {
            using var doc = JsonDocument.Parse(pwDumpJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return PresenceState.Unavailable;

            foreach (var node in doc.RootElement.EnumerateArray())
            {
                if (!node.TryGetProperty("type", out var typeEl) ||
                    typeEl.GetString() != "PipeWire:Interface:Node") continue;

                if (!node.TryGetProperty("info", out var info)) continue;

                if (!info.TryGetProperty("state", out var stateEl) ||
                    !string.Equals(stateEl.GetString(), "running", StringComparison.OrdinalIgnoreCase)) continue;

                if (!info.TryGetProperty("props", out var props)) continue;
                if (!props.TryGetProperty("media.class", out var classEl)) continue;

                string? mediaClass = classEl.GetString();
                switch (mediaClass)
                {
                    case "Stream/Input/Audio":
                        micActive = true;
                        break;
                    case "Stream/Input/Video":
                        camActive = true;
                        break;
                    case "Stream/Output/Video":
                        if (IsScreenCastPortalNode(props))
                            screenSharing = true;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return PresenceState.Unavailable;
        }

        return new PresenceState(micActive, camActive, screenSharing, PresenceSource.PipeWire);
    }

    // XDG desktop portal screen-cast nodes are identifiable by their node/application name.
    internal static bool IsScreenCastPortalNode(JsonElement props)
    {
        foreach (string propName in PortalNodeProps)
        {
            if (props.TryGetProperty(propName, out var val) && val.GetString() is string s &&
                (s.Contains("portal", StringComparison.OrdinalIgnoreCase) ||
                 s.Contains("xdp", StringComparison.OrdinalIgnoreCase) ||
                 s.Contains("screencast", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static readonly string[] PortalNodeProps =
        ["node.name", "application.name", "pipewire.access.portal.app_id"];
}

/// <summary>
/// Title-keyword detection used by the Windows meeting presence sensor.
/// Kept here (in NudgeCore, no NudgeTray dependency) so tests can verify that
/// process names are NOT included in the title check — that was the double-counting
/// bug fixed in commit 3dec73d.
/// </summary>
internal static class MeetingTitleDetector
{
    internal static readonly FrozenSet<string> TitleKeywords = new[]
    {
        "zoom meeting", "zoom video", "google meet", "microsoft teams",
        "skype for business", "webex meeting", "gotomeeting", "bluejeans",
        "slack call", "slack huddle", "discord voice", "ringcentral meeting",
        "whereby", "lark meeting", "dingtalk meeting",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static readonly FrozenSet<string> ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "teams", "zoom", "skype", "webex", "slack", "discord",
        "gotomeeting", "bluejeans", "ringcentral", "whereby",
        "ms-teams", "cisco webex meeting", "lark", "dingtalk",
        "tencent meeting", "wemeet", "voov meeting",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static bool IsMeetingTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        foreach (string kw in TitleKeywords)
        {
            if (title.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static bool IsMeetingProcessRunning()
    {
        Process[]? procs = null;
        try
        {
            procs = Process.GetProcesses();
            foreach (var p in procs)
            {
                try
                {
                    if (ProcessNames.Contains(p.ProcessName))
                        return true;
                }
                catch { }
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (procs != null)
            {
                foreach (var p in procs)
                    p.Dispose();
            }
        }
    }
}

#if !NUDGE_NOTIFY
internal static class SuppressionDeduplication
{
    private const int RecentWindowSeconds = 5;

    internal static bool TryMutateLatest(MLLiveEvent? latest, string reason, long nowEpochSec)
    {
        if (latest == null || nowEpochSec - latest.T > RecentWindowSeconds) return false;
        latest.SuppressReason = reason;
        latest.Triggered = false;
        return true;
    }
}

internal static class PredictionChartHelper
{
    internal static List<MLLiveEvent> FilterToAiOnly(IReadOnlyList<MLLiveEvent> events)
    {
        var result = new List<MLLiveEvent>(events.Count);
        foreach (var e in events)
            if (e.TriggerSource == "ai" && e.SuppressReason is null) result.Add(e);
        return result;
    }
}
#endif
