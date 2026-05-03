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
    public bool MlEnabled { get; init; }
    public bool ForceTrainedModel { get; init; }
    public string? CsvPath { get; init; }
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
    WaylandExtIdleNotify,
    FreedesktopScreenSaver,
    GnomeIdleMonitor,
    X11Xprintidle
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

internal readonly record struct FeatureVectorV2(
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
    int WorkspaceSwitchCount300s);

internal readonly record struct ActivityTickResult(
    ActivityContext Context,
    FeatureVectorV2 Features,
    string DisplayAppName,
    string LegacyAppName,
    int LegacyForegroundAppHash,
    int TimeLastRequestMs);

internal sealed class ActivityFeatureTracker
{
    private const int IdleNowThresholdMs = 1000;
    private const int AfkThresholdMs = 60 * 1000;
    private const int WindowSeconds = 300;

    private readonly Queue<ActivitySample> _samples = new();
    private string _lastFocusKey = "";
    private string _lastTitle = "";
    private DateTime _focusedSince = DateTime.MinValue;
    private DateTime _titleSince = DateTime.MinValue;

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
            SignalQuality: DetermineSignalQuality(window, appId, title),
            FullscreenFlag: window.Fullscreen ? 1 : 0);

        _samples.Enqueue(new ActivitySample(now, focusKey, appId, domain, workspaceId));
        TrimSamples(now);

        var featureVector = BuildFeatureVector(now, context);
        string legacyAppName = BuildLegacyAppName(appId, title);
        string displayAppName = BuildDisplayAppName(appId, title, domain);

        _lastFocusKey = focusKey;
        _lastTitle = title;

        return new ActivityTickResult(
            Context: context,
            Features: featureVector,
            DisplayAppName: displayAppName,
            LegacyAppName: legacyAppName,
            LegacyForegroundAppHash: NudgeCoreLogic.GetStableHash(legacyAppName),
            TimeLastRequestMs: context.FocusedSinceMs);
    }

    private FeatureVectorV2 BuildFeatureVector(DateTime now, ActivityContext context)
    {
        ActivitySample[] last300 = GetSamplesSince(now.AddSeconds(-299));
        ActivitySample[] last60 = GetSamplesSince(now.AddSeconds(-59));

        int total300 = Math.Max(1, last300.Length);
        int currentAppCount = last300.Count(sample => string.Equals(sample.AppId, context.FocusedAppId, StringComparison.Ordinal));
        int currentDomainCount = string.IsNullOrEmpty(context.FocusedDomain)
            ? 0
            : last300.Count(sample => string.Equals(sample.Domain, context.FocusedDomain, StringComparison.Ordinal));

        string anchorApp = GetMostCommonApp(last300, context.FocusedAppId);
        bool returnedToAnchorApp = !string.IsNullOrEmpty(anchorApp) &&
                                   string.Equals(anchorApp, context.FocusedAppId, StringComparison.Ordinal) &&
                                   HasInterveningSwitch(last300, context.FocusedAppId);

        return new FeatureVectorV2(
            HourOfDay: now.Hour,
            DayOfWeek: (int)now.DayOfWeek,
            FocusedAppHash: NudgeCoreLogic.GetStableHash(context.FocusedAppId),
            FocusedDomainHash: NudgeCoreLogic.GetStableHash(context.FocusedDomain),
            IdleMs: context.IdleMs,
            FocusedSinceMs: context.FocusedSinceMs,
            TitleStabilityMs: context.TitleUnchangedForMs,
            SwitchCount60s: CountFocusSwitches(last60),
            SwitchCount300s: CountFocusSwitches(last300),
            DistinctApps300s: CountDistinct(last300.Select(sample => sample.AppId)),
            DistinctDomains300s: CountDistinct(last300.Select(sample => sample.Domain)),
            ReturnedToAnchorApp300s: returnedToAnchorApp ? 1 : 0,
            CurrentAppShare300s: currentAppCount / (double)total300,
            CurrentDomainShare300s: currentDomainCount / (double)total300,
            BrowserWindowFlag: BrowserDetector.IsBrowser(context.FocusedAppId) ? 1 : 0,
            CommunicationAppFlag: IsCommunicationContext(context) ? 1 : 0,
            EntertainmentDomainFlag: IsEntertainmentDomain(context.FocusedDomain) ? 1 : 0,
            WorkDomainFlag: IsWorkDomain(context.FocusedDomain) ? 1 : 0,
            AfkFlag: context.IdleMs >= AfkThresholdMs ? 1 : 0,
            FullscreenFlag: context.FullscreenFlag,
            WorkspaceSwitchCount300s: CountWorkspaceSwitches(last300));
    }

    private ActivitySample[] GetSamplesSince(DateTime threshold) =>
        _samples.Where(sample => sample.Timestamp >= threshold).ToArray();

    private void TrimSamples(DateTime now)
    {
        var cutoff = now.AddSeconds(-WindowSeconds);
        while (_samples.Count > 0 && _samples.Peek().Timestamp < cutoff)
            _samples.Dequeue();
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

    private static int ClampMilliseconds(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return 0;

        double totalMs = duration.TotalMilliseconds;
        return totalMs >= int.MaxValue ? int.MaxValue : (int)totalMs;
    }

    private static string NormalizeRawValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static string BuildLegacyAppName(string appId, string title)
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

    private static int CountFocusSwitches(IReadOnlyList<ActivitySample> samples)
    {
        if (samples.Count <= 1)
            return 0;

        int switches = 0;
        string previousKey = samples[0].FocusKey;
        for (int i = 1; i < samples.Count; i++)
        {
            if (!string.Equals(previousKey, samples[i].FocusKey, StringComparison.Ordinal))
            {
                switches++;
                previousKey = samples[i].FocusKey;
            }
        }

        return switches;
    }

    private static int CountWorkspaceSwitches(IReadOnlyList<ActivitySample> samples)
    {
        if (samples.Count <= 1)
            return 0;

        int switches = 0;
        string previousWorkspace = samples[0].WorkspaceId;
        for (int i = 1; i < samples.Count; i++)
        {
            string workspace = samples[i].WorkspaceId;
            if (!string.IsNullOrEmpty(workspace) &&
                !string.Equals(previousWorkspace, workspace, StringComparison.Ordinal))
            {
                switches++;
            }

            if (!string.IsNullOrEmpty(workspace))
                previousWorkspace = workspace;
        }

        return switches;
    }

    private static int CountDistinct(IEnumerable<string> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase))
              .Distinct(StringComparer.Ordinal)
              .Count();

    private static string GetMostCommonApp(IEnumerable<ActivitySample> samples, string currentApp)
    {
        return samples.Where(sample => !string.IsNullOrWhiteSpace(sample.AppId))
                      .GroupBy(sample => sample.AppId, StringComparer.Ordinal)
                      .OrderByDescending(group => group.Count())
                      .ThenByDescending(group => string.Equals(group.Key, currentApp, StringComparison.Ordinal))
                      .Select(group => group.Key)
                      .FirstOrDefault() ?? "";
    }

    private static bool HasInterveningSwitch(IReadOnlyList<ActivitySample> samples, string currentApp)
    {
        if (samples.Count <= 1 || string.IsNullOrWhiteSpace(currentApp))
            return false;

        int currentSegmentStart = samples.Count - 1;
        while (currentSegmentStart > 0 &&
               string.Equals(samples[currentSegmentStart - 1].AppId, currentApp, StringComparison.Ordinal))
        {
            currentSegmentStart--;
        }

        return samples.Take(currentSegmentStart)
                      .Any(sample => !string.Equals(sample.AppId, currentApp, StringComparison.Ordinal));
    }

    private static bool IsCommunicationContext(ActivityContext context)
    {
        if (CommunicationAppIds.Contains(context.FocusedAppId))
            return true;

        return !string.IsNullOrEmpty(context.FocusedDomain) && CommunicationDomains.Contains(context.FocusedDomain);
    }

    private static bool IsEntertainmentDomain(string domain) =>
        !string.IsNullOrEmpty(domain) && EntertainmentDomains.Contains(domain);

    private static bool IsWorkDomain(string domain) =>
        !string.IsNullOrEmpty(domain) &&
        (WorkDomains.Contains(domain) || domain.EndsWith(".internal", StringComparison.OrdinalIgnoreCase));

    private readonly record struct ActivitySample(
        DateTime Timestamp,
        string FocusKey,
        string AppId,
        string Domain,
        string WorkspaceId);

    private static readonly FrozenSet<string> CommunicationAppIds = new[]
    {
        "discord", "microsoft teams", "outlook", "signal", "slack", "teams", "thunderbird", "zoom"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CommunicationDomains = new[]
    {
        "calendar.google.com", "discord.com", "mail.google.com", "meet.google.com", "outlook.office.com", "slack.com", "zoom.us"
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

internal static class FeatureSchemaV2
{
    public const int SchemaVersion = 2;

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
        "workspace_switch_count_300s"
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
        "workspace_switch_count_300s"
    ];

    public static IReadOnlyDictionary<string, double> ToFeatureDictionary(FeatureVectorV2 features) =>
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
            ["workspace_switch_count_300s"] = features.WorkspaceSwitchCount300s
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

    public static string WhichCommand => IsWindows ? "where" : "which";

    public static string PythonCommand => IsWindows ? "python" : "python3";

    public static string DotnetCommand => IsWindows ? "dotnet.exe" : "dotnet";

    private static string EnsureDataDirectory()
    {
        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string nudgeDir = Path.Combine(homeDir, ".nudge");
        Directory.CreateDirectory(nudgeDir);
        return nudgeDir;
    }
}

internal static class BrowserDetector
{
    private static readonly (string Match, string DisplayName)[] BrowserProcessNames =
    [
        ("google-chrome", "Chrome"),
        ("chrome", "Chrome"),
        ("chromium-browser", "Chromium"),
        ("chromium", "Chromium"),
        ("msedge", "Edge"),
        ("microsoft-edge", "Edge"),
        ("edge", "Edge"),
        ("firefox", "Firefox"),
        ("navigator", "Firefox"),
        ("librewolf", "LibreWolf"),
        ("brave-browser", "Brave"),
        ("brave", "Brave"),
        ("opera", "Opera"),
        ("vivaldi", "Vivaldi"),
        ("safari", "Safari"),
        ("browser", "Browser")
    ];

    private static readonly string[] BrowserSuffixes =
    [
        " - Google Chrome", " | Google Chrome", " — Google Chrome", " – Google Chrome", " · Google Chrome", " : Google Chrome",
        " - Chrome", " | Chrome", " — Chrome", " – Chrome", " · Chrome", " : Chrome",
        " - Chromium", " | Chromium", " — Chromium", " – Chromium", " · Chromium", " : Chromium",
        " - Microsoft Edge", " | Microsoft Edge", " — Microsoft Edge", " – Microsoft Edge", " · Microsoft Edge", " : Microsoft Edge",
        " - Edge", " | Edge", " — Edge", " – Edge", " · Edge", " : Edge",
        " - Mozilla Firefox", " | Mozilla Firefox", " — Mozilla Firefox", " – Mozilla Firefox", " · Mozilla Firefox", " : Mozilla Firefox",
        " - Firefox", " | Firefox", " — Firefox", " – Firefox", " · Firefox", " : Firefox",
        " - Brave Browser", " | Brave Browser", " — Brave Browser", " – Brave Browser", " · Brave Browser", " : Brave Browser",
        " - Brave", " | Brave", " — Brave", " – Brave", " · Brave", " : Brave",
        " - Opera", " | Opera", " — Opera", " – Opera", " · Opera", " : Opera",
        " - Vivaldi", " | Vivaldi", " — Vivaldi", " – Vivaldi", " · Vivaldi", " : Vivaldi",
        " - Safari", " | Safari", " — Safari", " – Safari", " · Safari", " : Safari",
        " - LibreWolf", " | LibreWolf", " — LibreWolf", " – LibreWolf", " · LibreWolf", " : LibreWolf"
    ];

    private static readonly FrozenSet<string> KnownSiteDomains = new[]
    {
        "amazon.com", "bitbucket.org", "chat.openai.com", "chatgpt.com", "claude.ai",
        "confluence.atlassian.com", "copilot.microsoft.com", "discord.com", "docs.google.com",
        "drive.google.com", "ebay.com", "facebook.com", "figma.com", "github.com",
        "gitlab.com", "instagram.com", "jira.atlassian.com", "linear.app", "linkedin.com",
        "localhost", "mail.google.com", "meet.google.com", "netflix.com", "news.ycombinator.com",
        "notion.so", "office.com", "outlook.office.com", "reddit.com", "slack.com",
        "stackoverflow.com", "stackexchange.com", "tiktok.com", "twitch.tv", "x.com",
        "youtube.com", "zoom.us"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Alias, string Domain)[] KnownSiteAliases =
    [
        ("Amazon", "amazon.com"),
        ("Bitbucket", "bitbucket.org"),
        ("ChatGPT", "chat.openai.com"),
        ("Claude", "claude.ai"),
        ("Confluence", "confluence.atlassian.com"),
        ("Copilot", "copilot.microsoft.com"),
        ("Discord", "discord.com"),
        ("Figma", "figma.com"),
        ("Facebook", "facebook.com"),
        ("Gmail", "mail.google.com"),
        ("GitHub", "github.com"),
        ("GitLab", "gitlab.com"),
        ("Google Docs", "docs.google.com"),
        ("Google Drive", "drive.google.com"),
        ("Google Meet", "meet.google.com"),
        ("Hacker News", "news.ycombinator.com"),
        ("Instagram", "instagram.com"),
        ("Jira", "jira.atlassian.com"),
        ("Linear", "linear.app"),
        ("LinkedIn", "linkedin.com"),
        ("Netflix", "netflix.com"),
        ("Notion", "notion.so"),
        ("Outlook", "outlook.office.com"),
        ("Reddit", "reddit.com"),
        ("Slack", "slack.com"),
        ("Stack Exchange", "stackexchange.com"),
        ("Stack Overflow", "stackoverflow.com"),
        ("TikTok", "tiktok.com"),
        ("Twitch", "twitch.tv"),
        ("YouTube", "youtube.com"),
        ("Zoom", "zoom.us")
    ];

    private static readonly FrozenSet<string> CommonWords = new[]
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "can",
        "her", "was", "one", "our", "out", "new", "has", "his", "how",
        "its", "may", "see", "now", "old", "way", "who", "boy", "did",
        "get", "let", "put", "say", "she", "too", "use", "tab", "page",
        "edit", "view", "file", "data", "home", "search", "settings",
        "profile", "account", "dashboard", "overview"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CommonFileExtensions = new[]
    {
        "7z", "avi", "bmp", "csv", "doc", "docx", "gif", "gz", "jpeg", "jpg",
        "json", "md", "mov", "mp3", "mp4", "pdf", "png", "ppt", "pptx", "rar",
        "svg", "tar", "txt", "wav", "webp", "xlsx", "xml", "yaml", "yml", "zip"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsBrowser(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        foreach (var browser in BrowserProcessNames)
        {
            if (processName.Contains(browser.Match, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string? ExtractSite(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        ReadOnlySpan<char> cleanedTitle = TrimKnownBrowserSuffix(title.AsSpan().Trim());
        if (cleanedTitle.IsEmpty)
            return null;

        if (TryExtractKnownDomain(cleanedTitle, out var domain) ||
            TryExtractKnownSiteFromSegments(cleanedTitle, out domain) ||
            TryExtractDomainFromTokens(cleanedTitle, out domain) ||
            TryNormalizeDomain(cleanedTitle, out domain))
        {
            return domain;
        }

        if (TryExtractShortestMeaningfulToken(cleanedTitle, out var fallback) &&
            (TryNormalizeDomain(fallback, out domain) || TryMatchKnownSiteAlias(fallback, out domain)))
        {
            return domain;
        }

        return null;
    }

    public static string GetAppAndSite(string? processName, string title)
    {
        string fallbackApp = string.IsNullOrWhiteSpace(title)
            ? processName?.Trim() ?? "unknown"
            : title;

        if (!IsBrowser(processName))
            return fallbackApp;

        string browserName = GetBrowserDisplayName(processName) ?? "Browser";
        var site = ExtractSite(title);
        return string.IsNullOrEmpty(site)
            ? browserName
            : $"{browserName} ({site})";
    }

    public static string? GetBrowserDisplayName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        foreach (var browser in BrowserProcessNames)
        {
            if (processName.Contains(browser.Match, StringComparison.OrdinalIgnoreCase))
                return browser.DisplayName;
        }

        return null;
    }

    private static ReadOnlySpan<char> TrimKnownBrowserSuffix(ReadOnlySpan<char> title)
    {
        foreach (var suffix in BrowserSuffixes)
        {
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return title[..^suffix.Length].TrimEnd();
        }

        return title;
    }

    private static bool TryExtractKnownDomain(ReadOnlySpan<char> title, out string? domain)
    {
        string? bestMatch = null;
        foreach (var knownSite in KnownSiteDomains)
        {
            if (title.Contains(knownSite, StringComparison.OrdinalIgnoreCase) &&
                (bestMatch == null || knownSite.Length > bestMatch.Length))
            {
                bestMatch = knownSite;
            }
        }

        domain = bestMatch;
        return bestMatch != null;
    }

    private static bool TryExtractDomainFromTokens(ReadOnlySpan<char> title, out string? domain)
    {
        int start = 0;
        while (start < title.Length)
        {
            while (start < title.Length && IsTokenSeparator(title[start]))
                start++;

            if (start >= title.Length)
                break;

            int end = start;
            while (end < title.Length && !IsTokenSeparator(title[end]))
                end++;

            if (TryNormalizeDomain(title[start..end], out domain))
                return true;

            start = end + 1;
        }

        domain = null;
        return false;
    }

    private static bool TryExtractKnownSiteFromSegments(ReadOnlySpan<char> title, out string? domain)
    {
        if (TryMatchKnownSiteAlias(title, out domain))
            return true;

        int start = 0;
        while (start < title.Length)
        {
            while (start < title.Length && IsSegmentSeparator(title[start]))
                start++;

            if (start >= title.Length)
                break;

            int end = start;
            while (end < title.Length && !IsSegmentSeparator(title[end]))
                end++;

            var candidate = TrimToken(title[start..end]);
            if (TryMatchKnownSiteAlias(candidate, out domain) || TryNormalizeDomain(candidate, out domain))
                return true;

            start = end + 1;
        }

        domain = null;
        return false;
    }

    private static bool TryMatchKnownSiteAlias(ReadOnlySpan<char> value, out string? domain)
    {
        value = TrimToken(value);
        if (value.IsEmpty)
        {
            domain = null;
            return false;
        }

        foreach (var site in KnownSiteAliases)
        {
            if (value.Equals(site.Alias, StringComparison.OrdinalIgnoreCase))
            {
                domain = site.Domain;
                return true;
            }
        }

        domain = null;
        return false;
    }

    private static bool TryExtractShortestMeaningfulToken(ReadOnlySpan<char> title, out ReadOnlySpan<char> token)
    {
        token = default;
        int bestLength = int.MaxValue;
        int start = 0;

        while (start < title.Length)
        {
            while (start < title.Length && IsTokenSeparator(title[start]))
                start++;

            if (start >= title.Length)
                break;

            int end = start;
            while (end < title.Length && !IsTokenSeparator(title[end]))
                end++;

            var candidate = TrimToken(title[start..end]);
            if (candidate.Length > 2 && candidate.Length < bestLength && !IsCommonWord(candidate))
            {
                token = candidate;
                bestLength = candidate.Length;
            }

            start = end + 1;
        }

        return bestLength != int.MaxValue;
    }

    private static bool TryNormalizeDomain(ReadOnlySpan<char> value, out string? normalizedDomain)
    {
        value = TrimToken(value);
        if (value.Length < 4 || value.Length > 100)
        {
            normalizedDomain = null;
            return false;
        }

        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = value[8..];
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            value = value[7..];

        int slashIndex = value.IndexOf('/');
        if (slashIndex > 0)
            value = value[..slashIndex];

        int colonIndex = value.IndexOf(':');
        if (colonIndex > 0)
            value = value[..colonIndex];

        value = TrimToken(value);
        if (!IsLikelyDomain(value))
        {
            normalizedDomain = null;
            return false;
        }

        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            value = value[4..];

        normalizedDomain = value.ToString().ToLowerInvariant();
        return true;
    }

    private static bool IsLikelyDomain(ReadOnlySpan<char> value)
    {
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Length < 4 || value.Length > 100)
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            if (value[i - 1] == '.' && value[i] == '.')
                return false;
        }

        if (value[0] == '.' || value[^1] == '.')
            return false;

        int dotIndex = value.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == value.Length - 1)
            return false;

        foreach (char c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-')
                return false;
        }

        int lastDotIndex = value.LastIndexOf('.');
        if (lastDotIndex == dotIndex && lastDotIndex > 0)
        {
            var trailingSegment = value[(lastDotIndex + 1)..];
            if (CommonFileExtensions.Contains(trailingSegment.ToString()))
                return false;
        }

        return true;
    }

    private static bool IsCommonWord(ReadOnlySpan<char> value) => CommonWords.Contains(value.ToString());

    private static ReadOnlySpan<char> TrimToken(ReadOnlySpan<char> value)
    {
        int start = 0;
        int end = value.Length - 1;

        while (start <= end && IsTrimCharacter(value[start]))
            start++;

        while (end >= start && IsTrimCharacter(value[end]))
            end--;

        return start <= end ? value[start..(end + 1)] : ReadOnlySpan<char>.Empty;
    }

    private static bool IsSegmentSeparator(char c) => c is '|' or '-' or '—' or '–' or '·' or '•';

    private static bool IsTokenSeparator(char c) => c is ' ' or '\t' or '-' or '—' or '–' or '|' or '\\' or '·' or '•';

    private static bool IsTrimCharacter(char c) => c is ' ' or '\t' or '-' or '—' or '–' or '|' or '/' or '\\' or '[' or ']' or '(' or ')' or '{' or '}' or '<' or '>' or ',' or ';' or ':' or '!' or '?' or '"' or '\'' or '`';
}

internal static class NudgeCoreLogic
{
    internal const string TraySingleInstanceMutexName = "Global\\NudgeTray.SingleInstance";

    internal static NudgeParsedArgs ParseNudgeArgs(string[] args)
    {
        int? intervalMinutes = null;
        bool mlEnabled = false;
        bool forceModel = false;
        string? csvPath = null;

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
            if (!arg.StartsWith("--", StringComparison.Ordinal) && !arg.StartsWith('-'))
            {
                csvPath = arg;
            }
        }

        return new NudgeParsedArgs
        {
            Action = NudgeStartupAction.Run,
            IntervalMinutes = intervalMinutes,
            MlEnabled = mlEnabled,
            ForceTrainedModel = forceModel,
            CsvPath = csvPath
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
            if (json.Contains("\"focused\":true"))
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
        IdleSource.WaylandExtIdleNotify => "wayland_ext_idle_notify",
        IdleSource.FreedesktopScreenSaver => "freedesktop_screensaver",
        IdleSource.GnomeIdleMonitor => "gnome_idle_monitor",
        IdleSource.X11Xprintidle => "x11_xprintidle",
        _ => "unknown"
    };

    internal static string GetSignalQualityName(SignalQuality quality) => quality switch
    {
        SignalQuality.Trusted => "trusted",
        SignalQuality.Usable => "usable",
        _ => "poor"
    };

    internal static bool ShouldIgnoreAnalyticsApp(string appName) =>
        appName.Contains("nudge", StringComparison.OrdinalIgnoreCase);

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

    private static bool MatchesNudgeWindowMarker(string value)
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

    private static bool TryGetCsvField(ReadOnlySpan<char> line, int fieldIndex, out ReadOnlySpan<char> field)
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
            field = field[1..^1];

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
