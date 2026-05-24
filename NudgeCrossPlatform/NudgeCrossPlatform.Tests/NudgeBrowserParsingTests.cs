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

    [Fact]
    public void ExtractSite_GenericTitleWithoutDomain_ReturnsNull()
    {
        Assert.Null(BrowserDetector.ExtractSite("Quarterly planning notes - Firefox"));
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
}
