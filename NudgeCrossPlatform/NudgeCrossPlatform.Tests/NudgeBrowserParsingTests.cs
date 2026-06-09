using System;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeBrowserParsingTests
{
    [Theory]
    [InlineData("https://www.github.com/openai/nudge - Google Chrome", "github.com")]
    [InlineData("docs.google.com/document/d/123/edit | Chrome", "docs.google.com")]
    [InlineData("linear.app — Issue Board - Chromium", "linear.app")]
    public void ExtractSite_DomainLikeTitles_ReturnExpectedDomain(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.ExtractSite(title));
    }

    [Theory]
    [InlineData("openai/nudge: README.md at main · openai/nudge · GitHub - Google Chrome", "github.com")]
    [InlineData("c# - How do spans work? - Stack Overflow - Mozilla Firefox", "stackoverflow.com")]
    [InlineData("YouTube - Mozilla Firefox", "youtube.com")]
    public void ExtractSite_KnownSiteTitles_ReturnExpectedDomain(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.ExtractSite(title));
    }

    [Theory]
    // Issue #131/#130: Chromium/Edge prefix titles with an unread/tab count "(N)" which
    // previously broke the site-name match, leaving the domain empty on Windows.
    [InlineData("(2) Reddit - Dive into anything - Google Chrome", "reddit.com")]
    [InlineData("(1) Inbox - Gmail - Google Chrome", "mail.google.com")]
    [InlineData("(15) GitHub - Microsoft Edge", "github.com")]
    [InlineData("(99+) Messenger - Google Chrome", "messenger.com")]
    public void ExtractSite_TabCountPrefix_StrippedAndResolved(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.ExtractSite(title));
    }

    [Fact]
    public void ExtractSite_NonNumericParenthetical_NotTreatedAsTabCount()
    {
        // "(Draft)" is not a tab count, so it is left in place and yields no false domain.
        Assert.Null(BrowserDetector.ExtractSite("(Draft) Meeting notes - Firefox"));
    }

    [Theory]
    // The UIA URL path must produce the same keys as title parsing — "www.x.com" and
    // "x.com" would otherwise split a domain's reputation across two entries.
    [InlineData("https://www.x.com/home", "x.com")]
    [InlineData("https://news.ycombinator.com/item?id=1", "news.ycombinator.com")]
    [InlineData("http://localhost:3000/app", null)]          // localhost excluded, like title path
    public void ExtractSite_UiaUrl_NormalizedLikeTitlePath(string url, string? expected)
    {
        BrowserDetector.TryGetBrowserUrl = () => url;
        try
        {
            Assert.Equal(expected, BrowserDetector.ExtractSite("Some Unrelated Title - Google Chrome"));
        }
        finally
        {
            BrowserDetector.TryGetBrowserUrl = null;
        }
    }

    [Fact]
    public void ExtractSite_UiaReaderThrows_DegradesToTitleParse()
    {
        BrowserDetector.TryGetBrowserUrl = () => throw new InvalidOperationException("UIA unavailable");
        try
        {
            Assert.Equal("reddit.com", BrowserDetector.ExtractSite("(2) Reddit - Dive into anything - Google Chrome"));
        }
        finally
        {
            BrowserDetector.TryGetBrowserUrl = null;
        }
    }

    [Fact]
    public void ExtractSite_GenericTitleWithoutDomain_ReturnsNull()
    {
        Assert.Null(BrowserDetector.ExtractSite("Quarterly planning notes - Firefox"));
    }

    [Fact]
    public void ExtractSite_TryGetBrowserUrl_InjectedReaderTakesPrecedence()
    {
        // Tier 2: when a platform URL reader is injected, its result is preferred over title parsing.
        BrowserDetector.TryGetBrowserUrl = () => "https://docs.python.org/tutorial/";
        try
        {
            Assert.Equal("docs.python.org", BrowserDetector.ExtractSite("Python docs - Chrome"));
        }
        finally
        {
            BrowserDetector.TryGetBrowserUrl = null;
        }
    }

    [Fact]
    public void ExtractSite_TryGetBrowserUrl_ReturnsNull_FallsBackToTitle()
    {
        BrowserDetector.TryGetBrowserUrl = () => null;
        try
        {
            Assert.Equal("github.com", BrowserDetector.ExtractSite("openai/nudge - GitHub - Chrome"));
        }
        finally
        {
            BrowserDetector.TryGetBrowserUrl = null;
        }
    }

    [Fact]
    public void GetAppAndSite_ForBrowserTitle_ReturnsFormattedLabel()
    {
        var label = BrowserDetector.GetAppAndSite("chrome", "chat.openai.com - Google Chrome");
        Assert.Equal("Chrome (chat.openai.com)", label);
    }

    [Fact]
    public void GetAppAndSite_UsesFriendlyBrowserNameWhenNoSiteDetected()
    {
        var label = BrowserDetector.GetAppAndSite("msedge", "Quarterly planning notes - Microsoft Edge");
        Assert.Equal("Edge", label);
    }

    [Theory]
    [InlineData("google-chrome", true)]
    [InlineData("Navigator", true)]
    [InlineData("zen-browser", true)]
    [InlineData("zen", true)]
    [InlineData("librewolf", true)]
    [InlineData("iexplore", true)]
    // Windows File Explorer (explorer.exe) is NOT a web browser (issue #135): treating it as
    // one flagged folder windows as browser tabs and ran (empty) domain extraction on them.
    [InlineData("explorer", false)]
    [InlineData("code", false)]
    [InlineData("konsole", false)]
    [InlineData("", false)]
    public void IsBrowser_VariousProcessNames_CorrectlyIdentified(string name, bool expected)
    {
        Assert.Equal(expected, BrowserDetector.IsBrowser(name));
    }

    // ── TrimBrowserSuffix ─────────────────────────────────────────────────────

    [Fact]
    public void TrimBrowserSuffix_LongestSuffixWins_GoogleChromeNotJustChrome()
    {
        // " - Google Chrome" must be stripped in full, not just " - Chrome"
        string result = BrowserDetector.TrimBrowserSuffix("Some Page - Google Chrome");
        Assert.Equal("Some Page", result);
    }

    [Theory]
    [InlineData("Page - Mozilla Firefox", "Page")]
    [InlineData("Page | Chromium", "Page")]
    [InlineData("Page — Microsoft Edge", "Page")]
    [InlineData("Page · Brave", "Page")]
    public void TrimBrowserSuffix_VariousSeparators_AllStripped(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.TrimBrowserSuffix(title));
    }

    // ── Domain normalisation edge cases ───────────────────────────────────────

    [Theory]
    [InlineData("github.com:443 - Chrome", "github.com")]
    [InlineData("myapp.com:8080 - Firefox", "myapp.com")]
    public void ExtractSite_DomainWithPort_PortIsStripped(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.ExtractSite(title));
    }

    [Fact]
    public void ExtractSite_Localhost_ReturnsNull()
    {
        // "localhost" should not be treated as a valid domain for productivity classification
        Assert.Null(BrowserDetector.ExtractSite("localhost - Chrome"));
    }

    [Theory]
    [InlineData("notes.pdf - Chrome")]
    [InlineData("report.docx - Firefox")]
    [InlineData("archive.zip - Chromium")]
    public void ExtractSite_FileExtensionToken_ReturnsNull(string title)
    {
        // Tokens that look like file names (ending in a common extension) must not be treated as domains
        Assert.Null(BrowserDetector.ExtractSite(title));
    }

    [Theory]
    [InlineData("my-site.co.uk - Chrome", "my-site.co.uk")]
    [InlineData("sub.my-company.io - Firefox", "sub.my-company.io")]
    public void ExtractSite_DomainWithHyphens_Recognized(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.ExtractSite(title));
    }

    [Fact]
    public void ExtractSite_HttpsUrl_DomainExtracted()
    {
        Assert.Equal("example.com", BrowserDetector.ExtractSite("https://example.com/path - Chrome"));
    }

    // ── New domain aliases ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Adam Billal Guergachi | Messenger — Zen Browser", "messenger.com")]
    [InlineData("Inbox - Messenger - Google Chrome", "messenger.com")]
    [InlineData("Someone | Messenger — Zen Browser", "messenger.com")]
    public void ExtractSite_MessengerPages_ReturnsMessengerDotCom(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.ExtractSite(title));
    }

    [Theory]
    [InlineData("How to focus better - Medium - Mozilla Firefox", "medium.com")]
    [InlineData("JavaScript Weekly - Medium - Google Chrome", "medium.com")]
    public void ExtractSite_MediumPages_ReturnsMediumDotCom(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.ExtractSite(title));
    }

    [Theory]
    [InlineData("Group Chat — Telegram — Zen Browser", "telegram.org")]
    [InlineData("Telegram - Mozilla Firefox", "telegram.org")]
    public void ExtractSite_TelegramPages_ReturnsTelegramDotOrg(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.ExtractSite(title));
    }

    [Theory]
    [InlineData("Family Group - WhatsApp - Google Chrome", "whatsapp.com")]
    [InlineData("WhatsApp - Mozilla Firefox", "whatsapp.com")]
    public void ExtractSite_WhatsAppPages_ReturnsWhatsAppDotCom(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.ExtractSite(title));
    }

    [Theory]
    [InlineData("Adam Billal Guergachi | Messenger — Zen Browser", "Zen (messenger.com)")]
    [InlineData("YouTube - Zen Browser", "Zen (youtube.com)")]
    public void GetAppAndSite_ZenBrowser_ReturnsFormattedLabel(string title, string expected)
    {
        Assert.Equal(expected, BrowserDetector.GetAppAndSite("zen-browser", title));
    }

    [Theory]
    [InlineData("firefox", "Firefox")]
    [InlineData("msedge", "Edge")]
    [InlineData("google-chrome", "Chrome")]
    [InlineData("chromium", "Chromium")]
    [InlineData("brave-browser", "Brave")]
    [InlineData("opera", "Opera")]
    [InlineData("zen-browser", "Zen")]
    public void GetBrowserDisplayName_KnownBrowser_ReturnsDisplayName(string processName, string expected)
    {
        Assert.Equal(expected, BrowserDetector.GetBrowserDisplayName(processName));
    }

    [Theory]
    [InlineData("code")]
    [InlineData("terminal")]
    [InlineData("")]
    [InlineData(null)]
    public void GetBrowserDisplayName_NonBrowser_ReturnsNull(string? processName)
    {
        Assert.Null(BrowserDetector.GetBrowserDisplayName(processName));
    }
}

public sealed class NudgeDomainPipelineTests
{
    [Theory]
    [InlineData("github.com", true)]
    [InlineData("sub.example.com", true)]
    [InlineData("www.example.co.uk", true)]
    [InlineData("a.b", false)]
    [InlineData("", false)]
    [InlineData("a..b", false)]
    [InlineData(".com", false)]
    [InlineData("site.", false)]
    [InlineData("report.pdf", false)]
    [InlineData("localhost", false)]
    [InlineData("no-dot", false)]
    [InlineData("site", false)]
    public void IsLikelyDomain(string value, bool expected)
    {
        Assert.Equal(expected, BrowserDetector.IsLikelyDomain(value));
    }

    [Theory]
    [InlineData("https://github.com/path", true, "github.com")]
    [InlineData("http://example.com/page", true, "example.com")]
    [InlineData("www.example.com", true, "example.com")]
    [InlineData("site.com:8080", true, "site.com")]
    [InlineData("GITHUB.COM/path", true, "github.com")]
    [InlineData("bad", false, null)]
    [InlineData("no dots here", false, null)]
    public void TryNormalizeDomain(string input, bool expectedResult, string? expectedDomain)
    {
        bool result = BrowserDetector.TryNormalizeDomain(input, out string? domain);
        Assert.Equal(expectedResult, result);
        if (expectedResult)
            Assert.Equal(expectedDomain, domain);
        else
            Assert.Null(domain);
    }

    [Theory]
    [InlineData("GitHub", "github.com")]
    [InlineData("YouTube", "youtube.com")]
    [InlineData("Stack Overflow", "stackoverflow.com")]
    [InlineData("Notion", "notion.so")]
    [InlineData("Figma", "figma.com")]
    [InlineData("Linear", "linear.app")]
    [InlineData("Jira", "jira.atlassian.com")]
    [InlineData("NonsenseSite", null)]
    public void TryMatchKnownSiteAlias(string input, string? expectedDomain)
    {
        bool result = BrowserDetector.TryMatchKnownSiteAlias(input, out string? domain);
        if (expectedDomain != null)
        {
            Assert.True(result);
            Assert.Equal(expectedDomain, domain);
        }
        else
        {
            Assert.False(result);
        }
    }

    [Fact]
    public void TryMatchKnownSiteAlias_EmptyInput_ReturnsFalse()
    {
        Assert.False(BrowserDetector.TryMatchKnownSiteAlias("", out _));
    }

    [Fact]
    public void TryExtractShortestMeaningfulToken_PicksShortest()
    {
        Assert.True(BrowserDetector.TryExtractShortestMeaningfulToken(
            "the quick brown fox", out var token));
        Assert.Equal("fox", token.ToString());
    }

    [Fact]
    public void TryExtractShortestMeaningfulToken_AllCommonWords_ReturnsFalse()
    {
        Assert.False(BrowserDetector.TryExtractShortestMeaningfulToken(
            "the and for but", out _));
    }

    [Fact]
    public void TryExtractShortestMeaningfulToken_ShortTokensFiltered()
    {
        Assert.False(BrowserDetector.TryExtractShortestMeaningfulToken("a b c", out _));
    }
}
