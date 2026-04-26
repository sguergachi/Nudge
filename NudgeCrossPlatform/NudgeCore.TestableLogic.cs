using System;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    private static readonly string[] BrowserProcessNames =
    [
        "chrome", "chromium", "firefox", "edge", "brave", "opera", "vivaldi",
        "safari", "browser", "chromium-browser", "google-chrome", "mozilla"
    ];

    private static readonly string[] BrowserSuffixes =
    [
        " - Google Chrome", " - Chrome", " - Microsoft Edge",
        " - Mozilla Firefox", " - Firefox", " - Brave",
        " - Opera", " - Vivaldi", " - Chromium",
        " : Google Chrome", " : Chrome", " : Microsoft Edge",
        " : Mozilla Firefox", " : Firefox", " : Brave"
    ];

    private static readonly FrozenSet<string> KnownSiteDomains = new[]
    {
        "github.com", "stackoverflow.com", "stackexchange.com", "gitlab.com",
        "bitbucket.org", "youtube.com", "reddit.com", "twitter.com", "x.com",
        "linkedin.com", "docs.google.com", "drive.google.com", "notion.so",
        "figma.com", "linear.app", "jira.atlassian.com", "confluence.atlassian.com",
        "slack.com", "discord.com", "zoom.us", "meet.google.com", "office.com",
        "outlook.office.com", "mail.google.com", "chat.openai.com", "claude.ai",
        "copilot.microsoft.com", "news.ycombinator.com", "instagram.com",
        "facebook.com", "tiktok.com", "netflix.com", "twitch.tv", "amazon.com", "ebay.com"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CommonWords = new[]
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "can",
        "her", "was", "one", "our", "out", "new", "has", "his", "how",
        "its", "may", "see", "now", "old", "way", "who", "boy", "did",
        "get", "let", "put", "say", "she", "too", "use", "tab", "page",
        "edit", "view", "file", "data", "home", "search", "settings",
        "profile", "account", "dashboard", "overview"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);


    public static bool IsBrowser(string? processName)
    {
        if (string.IsNullOrEmpty(processName))
            return false;

        foreach (var browser in BrowserProcessNames)
        {
            if (processName.Contains(browser, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string? ExtractSite(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        ReadOnlySpan<char> fullTitle = title.AsSpan().Trim();
        ReadOnlySpan<char> cleanedTitle = TrimKnownBrowserSuffix(fullTitle);

        if (TryExtractDomainFromTokens(cleanedTitle, out var domain))
            return domain;

        if (TryNormalizeDomain(cleanedTitle, out domain))
            return domain;

        foreach (var knownSite in KnownSiteDomains)
        {
            if (title.Contains(knownSite, StringComparison.OrdinalIgnoreCase))
                return knownSite;
        }

        if (TryExtractShortestMeaningfulToken(cleanedTitle, out var fallback) &&
            TryNormalizeDomain(fallback, out domain))
        {
            return domain;
        }

        return null;
    }

    public static string GetAppAndSite(string? processName, string title)
    {
        if (!IsBrowser(processName))
            return title;

        string browserName = string.IsNullOrEmpty(processName)
            ? "Browser"
            : char.ToUpperInvariant(processName[0]) + processName[1..];

        var site = ExtractSite(title);
        return string.IsNullOrEmpty(site)
            ? browserName
            : $"{browserName} ({site})";
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

    private static bool IsTokenSeparator(char c) => c is ' ' or '\t' or '-' or '—' or '–' or '|' or '\\';

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
            if (!arg.StartsWith("--", StringComparison.Ordinal) && !arg.StartsWith("-", StringComparison.Ordinal))
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
            return FindFocusedNodeInSwayJson(doc.RootElement);
        }
        catch
        {
            if (json.Contains("\"focused\":true"))
            {
                string app = "unknown";
                string title = "";

                int idx = json.IndexOf("\"app_id\":\"");
                if (idx == -1) idx = json.IndexOf("\"class\":\"");

                if (idx != -1)
                {
                    int start = json.IndexOf('"', idx + 9) + 1;
                    int end = json.IndexOf('"', start);
                    if (end > start)
                        app = json.Substring(start, end - start);
                }

                int nameIdx = json.IndexOf("\"name\":\"");
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

    internal static (string app, string title) FindFocusedNodeInSwayJson(JsonElement node)
    {
        if (node.TryGetProperty("focused", out var focused) && focused.GetBoolean())
        {
            string app = "unknown";
            string title = "";

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

            return (app == "" ? "unknown" : app, title);
        }

        foreach (string arrayProp in new[] { "nodes", "floating_nodes" })
        {
            if (node.TryGetProperty(arrayProp, out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    var result = FindFocusedNodeInSwayJson(child);
                    if (result.app != "unknown")
                        return result;
                }
            }
        }

        return ("unknown", "");
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

    private static bool TryGetCsvField(ReadOnlySpan<char> line, int fieldIndex, out ReadOnlySpan<char> field)
    {
        int currentField = 0;
        int start = 0;

        for (int i = 0; i <= line.Length; i++)
        {
            if (i == line.Length || line[i] == ',')
            {
                if (currentField == fieldIndex)
                {
                    field = line[start..i].Trim();
                    return true;
                }

                currentField++;
                start = i + 1;
            }
        }

        field = default;
        return false;
    }
}
