using System;
using System.Collections.Frozen;

namespace NudgeCore;

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
        ("zen-browser", "Zen"),
        ("zen", "Zen"),
        ("iexplore", "Explorer"),
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
        " - LibreWolf", " | LibreWolf", " — LibreWolf", " – LibreWolf", " · LibreWolf", " : LibreWolf",
        " - Zen Browser", " | Zen Browser", " — Zen Browser", " – Zen Browser", " · Zen Browser", " : Zen Browser",
        " - Zen", " | Zen", " — Zen", " – Zen", " · Zen", " : Zen",
        " - Internet Explorer", " | Internet Explorer", " — Internet Explorer", " – Internet Explorer", " · Internet Explorer", " : Internet Explorer",
        " - Explorer", " | Explorer", " — Explorer", " – Explorer", " · Explorer", " : Explorer"
    ];

    private static readonly FrozenSet<string> KnownSiteDomains = new[]
    {
        "amazon.com", "bitbucket.org", "chat.openai.com", "chatgpt.com", "claude.ai",
"confluence.atlassian.com", "copilot.microsoft.com", "discord.com", "docs.google.com",
        "drive.google.com", "ebay.com", "facebook.com", "figma.com", "github.com",
        "gitlab.com", "instagram.com", "jira.atlassian.com", "linear.app", "linkedin.com",
        "mail.google.com", "medium.com", "meet.google.com", "messenger.com", "netflix.com",
        "news.ycombinator.com", "notion.so", "office.com", "outlook.office.com",
        "reddit.com", "slack.com", "stackoverflow.com", "stackexchange.com",
        "telegram.org", "tiktok.com", "twitch.tv", "whatsapp.com", "x.com",
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
        ("Medium", "medium.com"),
        ("Messenger", "messenger.com"),
        ("Netflix", "netflix.com"),
        ("Notion", "notion.so"),
        ("Outlook", "outlook.office.com"),
        ("Reddit", "reddit.com"),
        ("Slack", "slack.com"),
        ("Stack Exchange", "stackexchange.com"),
        ("Stack Overflow", "stackoverflow.com"),
        ("Telegram", "telegram.org"),
        ("TikTok", "tiktok.com"),
        ("Twitch", "twitch.tv"),
        ("WhatsApp", "whatsapp.com"),
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

        ReadOnlySpan<char> cleanedTitle = TrimKnownBrowserSuffix(TrimTabCountPrefix(title.AsSpan().Trim()));
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

    public static string TrimBrowserSuffix(string title) =>
        TrimKnownBrowserSuffix(title.AsSpan()).ToString();

    private static ReadOnlySpan<char> TrimKnownBrowserSuffix(ReadOnlySpan<char> title)
    {
        foreach (var suffix in BrowserSuffixes)
        {
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return title[..^suffix.Length].TrimEnd();
        }

        return title;
    }

    // Chromium/Edge prefix browser titles with an unread/tab count, e.g. "(2) Reddit - …" or
    // "(99+) Messenger - …". Strip a leading "(<digits>[+])" so the site name/domain that
    // follows can be matched. A non-numeric parenthetical (e.g. "(Draft)") is left untouched.
    internal static ReadOnlySpan<char> TrimTabCountPrefix(ReadOnlySpan<char> title)
    {
        if (title.Length < 3 || title[0] != '(')
            return title;

        int i = 1;
        while (i < title.Length && char.IsDigit(title[i]))
            i++;
        if (i == 1)                                 // no digits after '('
            return title;
        if (i < title.Length && title[i] == '+')    // "(99+)"
            i++;
        if (i >= title.Length || title[i] != ')')
            return title;
        i++;                                        // skip ')'
        while (i < title.Length && char.IsWhiteSpace(title[i]))
            i++;
        return title[i..];
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
            while (end < title.Length && !IsTokenSeparatorBoundary(title, end))
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
            {
                // Hyphen within alphanumeric context is part of a word, not a separator
                if (title[start] == '-' && start > 0 && start + 1 < title.Length && 
                    char.IsLetterOrDigit(title[start - 1]) && char.IsLetterOrDigit(title[start + 1]))
                {
                    start++;
                    continue;
                }
                start++;
            }

            if (start >= title.Length)
                break;

            int end = start;
            while (end < title.Length)
            {
                if (IsSegmentSeparator(title[end]) && !(title[end] == '-' && 
                    end > 0 && end + 1 < title.Length && 
                    char.IsLetterOrDigit(title[end - 1]) && char.IsLetterOrDigit(title[end + 1])))
                {
                    break;
                }
                end++;
            }

            var candidate = TrimToken(title[start..end]);
            if (TryMatchKnownSiteAlias(candidate, out domain) || TryNormalizeDomain(candidate, out domain))
                return true;

            start = end + 1;
        }

        domain = null;
        return false;
    }

    internal static bool TryMatchKnownSiteAlias(ReadOnlySpan<char> value, out string? domain)
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

    internal static bool TryExtractShortestMeaningfulToken(ReadOnlySpan<char> title, out ReadOnlySpan<char> token)
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

    internal static bool TryNormalizeDomain(ReadOnlySpan<char> value, out string? normalizedDomain)
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

    internal static bool IsLikelyDomain(ReadOnlySpan<char> value)
    {
        // localhost is explicitly excluded - not a valid productivity domain
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return false;

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

    private static bool IsTokenSeparator(char c) => c is ' ' or '\t' or '—' or '–' or '|' or '\\' or '·' or '•';

    private static bool IsTokenSeparatorBoundary(ReadOnlySpan<char> text, int index)
    {
        // A hyphen is a separator only when surrounded by spaces (like " - ") to preserve hyphenated domains
        if (index < text.Length && text[index] == '-')
        {
            bool leftSpace = index > 0 && char.IsWhiteSpace(text[index - 1]);
            bool rightSpace = index + 1 < text.Length && char.IsWhiteSpace(text[index + 1]);
            return leftSpace && rightSpace;
        }
        return IsTokenSeparator(text[index]);
    }

    private static bool IsTrimCharacter(char c) => c is ' ' or '\t' or '—' or '–' or '|' or '/' or '\\' or '[' or ']' or '(' or ')' or '{' or '}' or '<' or '>' or ',' or ';' or ':' or '!' or '?' or '"' or '\'' or '`';
}