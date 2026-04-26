using Xunit;

public class NudgeBrowserParsingTests
{
    [Theory]
    [InlineData("https://www.github.com/openai/nudge - Google Chrome", "github.com")]
    [InlineData("docs.google.com/document/d/123/edit | Chrome", "docs.google.com")]
    [InlineData("linear.app — Issue Board - Chromium", "linear.app")]
    [InlineData("localhost:3000 - Google Chrome", "localhost")]
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

    [Fact]
    public void IsBrowser_KnowsBrowserAndNonBrowserProcesses()
    {
        Assert.True(BrowserDetector.IsBrowser("google-chrome"));
        Assert.True(BrowserDetector.IsBrowser("Navigator"));
        Assert.False(BrowserDetector.IsBrowser("code"));
    }
}
