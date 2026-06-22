using System;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

public sealed class GetStableHashTests
{
    [Fact]
    public void EmptyOrNull_ReturnsZero()
    {
        Assert.Equal(0, NudgeCoreLogic.GetStableHash(""));
        Assert.Equal(0, NudgeCoreLogic.GetStableHash(null!));
    }

    [Fact]
    public void SameInput_ProducesSameHash()
    {
        Assert.Equal(NudgeCoreLogic.GetStableHash("hello"), NudgeCoreLogic.GetStableHash("hello"));
    }

    [Fact]
    public void DifferentInputs_ProduceDifferentHashes()
    {
        Assert.NotEqual(NudgeCoreLogic.GetStableHash("hello"), NudgeCoreLogic.GetStableHash("world"));
    }

    [Fact]
    public void CaseSensitive()
    {
        Assert.NotEqual(NudgeCoreLogic.GetStableHash("Hello"), NudgeCoreLogic.GetStableHash("hello"));
    }

    [Fact]
    public void DeterministicAcrossCalls()
    {
        int a = NudgeCoreLogic.GetStableHash("test");
        int b = NudgeCoreLogic.GetStableHash("test");
        int c = NudgeCoreLogic.GetStableHash("test");
        Assert.Equal(a, b);
        Assert.Equal(b, c);
    }
}

public sealed class EscapeCsvTests
{
    [Fact]
    public void NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", NudgeCoreLogic.EscapeCsv(null));
        Assert.Equal("", NudgeCoreLogic.EscapeCsv(""));
    }

    [Fact]
    public void PlainValue_ReturnsUnchanged()
    {
        Assert.Equal("hello", NudgeCoreLogic.EscapeCsv("hello"));
    }

    [Fact]
    public void ValueWithComma_WrapsInQuotes()
    {
        Assert.Equal("\"a,b\"", NudgeCoreLogic.EscapeCsv("a,b"));
    }

    [Fact]
    public void ValueWithQuote_EscapesQuote()
    {
        Assert.Equal("\"\"\"hello\"\"\"", NudgeCoreLogic.EscapeCsv("\"hello\""));
    }

    [Fact]
    public void ValueWithCommaAndQuote_EscapesQuoteInsideQuotes()
    {
        Assert.Equal("\"a\"\"b,c\"", NudgeCoreLogic.EscapeCsv("a\"b,c"));
    }

    [Fact]
    public void Newlines_ReplacedWithSpace()
    {
        Assert.Equal("a b", NudgeCoreLogic.EscapeCsv("a\rb"));
        Assert.Equal("a b", NudgeCoreLogic.EscapeCsv("a\nb"));
    }

    [Fact]
    public void MixedNewlineAndComma_WrapsAfterSanitize()
    {
        Assert.Equal("\"a b,c\"", NudgeCoreLogic.EscapeCsv("a\rb,c"));
    }
}

public sealed class GetFocusSourceNameTests
{
    [Fact]
    public void WindowsApi_ReturnsExpectedName() =>
        Assert.Equal("windows_api", NudgeCoreLogic.GetFocusSourceName(FocusSource.WindowsApi));

    [Fact]
    public void WaylandActivatedProtocol_ReturnsExpectedName() =>
        Assert.Equal("wayland_activated_protocol", NudgeCoreLogic.GetFocusSourceName(FocusSource.WaylandActivatedProtocol));

    [Fact]
    public void SwayIpc_ReturnsExpectedName() =>
        Assert.Equal("sway_ipc", NudgeCoreLogic.GetFocusSourceName(FocusSource.SwayIpc));

    [Fact]
    public void KWinScript_ReturnsExpectedName() =>
        Assert.Equal("kwin_script", NudgeCoreLogic.GetFocusSourceName(FocusSource.KWinScript));

    [Fact]
    public void GnomeShell_ReturnsExpectedName() =>
        Assert.Equal("gnome_shell", NudgeCoreLogic.GetFocusSourceName(FocusSource.GnomeShell));

    [Fact]
    public void X11Ewmh_ReturnsExpectedName() =>
        Assert.Equal("x11_ewmh", NudgeCoreLogic.GetFocusSourceName(FocusSource.X11Ewmh));

    [Fact]
    public void HeuristicProcessScan_ReturnsExpectedName() =>
        Assert.Equal("heuristic_process_scan", NudgeCoreLogic.GetFocusSourceName(FocusSource.HeuristicProcessScan));

    [Fact]
    public void UnknownValue_ReturnsUnknown() =>
        Assert.Equal("unknown", NudgeCoreLogic.GetFocusSourceName((FocusSource)999));
}

public sealed class GetSignalQualityNameTests
{
    [Fact]
    public void Trusted_ReturnsExpectedName() =>
        Assert.Equal("trusted", NudgeCoreLogic.GetSignalQualityName(SignalQuality.Trusted));

    [Fact]
    public void Usable_ReturnsExpectedName() =>
        Assert.Equal("usable", NudgeCoreLogic.GetSignalQualityName(SignalQuality.Usable));

    [Fact]
    public void Poor_ReturnsExpectedName() =>
        Assert.Equal("poor", NudgeCoreLogic.GetSignalQualityName(SignalQuality.Poor));

    [Fact]
    public void UnknownValue_ReturnsPoor() =>
        Assert.Equal("poor", NudgeCoreLogic.GetSignalQualityName((SignalQuality)999));
}

public sealed class ClampMillisecondsTests
{
    [Fact]
    public void Zero_ReturnsZero() =>
        Assert.Equal(0, ActivityFeatureTracker.ClampMilliseconds(TimeSpan.Zero));

    [Fact]
    public void Negative_ReturnsZero() =>
        Assert.Equal(0, ActivityFeatureTracker.ClampMilliseconds(TimeSpan.FromMilliseconds(-100)));

    [Fact]
    public void NormalDuration_ReturnsMilliseconds() =>
        Assert.Equal(5000, ActivityFeatureTracker.ClampMilliseconds(TimeSpan.FromSeconds(5)));

    [Fact]
    public void EnormousDuration_ClampsToIntMax() =>
        Assert.Equal(int.MaxValue, ActivityFeatureTracker.ClampMilliseconds(TimeSpan.MaxValue));
}

public sealed class NormalizeRawValueTests
{
    [Fact]
    public void Null_ReturnsFallback() =>
        Assert.Equal("fallback", ActivityFeatureTracker.NormalizeRawValue(null, "fallback"));

    [Fact]
    public void Whitespace_ReturnsFallback() =>
        Assert.Equal("fallback", ActivityFeatureTracker.NormalizeRawValue("   ", "fallback"));

    [Fact]
    public void Normal_ReturnsUnchanged() =>
        Assert.Equal("hello", ActivityFeatureTracker.NormalizeRawValue("hello", "fallback"));

    [Fact]
    public void CrLf_ReplacedWithSpace() =>
        Assert.Equal("a b", ActivityFeatureTracker.NormalizeRawValue("a\rb", "fallback"));

    [Fact]
    public void Newline_ReplacedWithSpace() =>
        Assert.Equal("a b", ActivityFeatureTracker.NormalizeRawValue("a\nb", "fallback"));

    [Fact]
    public void SurroundingWhitespace_Trimmed() =>
        Assert.Equal("val", ActivityFeatureTracker.NormalizeRawValue("  val  ", "fallback"));
}

public sealed class BuildLegacyAppNameTests
{
    [Fact]
    public void NonBrowser_WithTitle_ReturnsTitle()
    {
        Assert.Equal("main.py", ActivityFeatureTracker.BuildLegacyAppName("code", "main.py"));
    }

    [Fact]
    public void NonBrowser_NoTitle_ReturnsAppId()
    {
        Assert.Equal("terminal", ActivityFeatureTracker.BuildLegacyAppName("terminal", ""));
    }
}

public sealed class ClassifyBrowserDomainTests
{
    [Fact]
    public void YouTube_ReturnsEntertainment() =>
        Assert.Equal(AppCategory.Entertainment, ActivityFeatureTracker.ClassifyBrowserDomain("youtube.com"));

    [Fact]
    public void GitHub_ReturnsDevelopment() =>
        Assert.Equal(AppCategory.Development, ActivityFeatureTracker.ClassifyBrowserDomain("github.com"));

    [Fact]
    public void Slack_ReturnsCommunication() =>
        Assert.Equal(AppCategory.Communication, ActivityFeatureTracker.ClassifyBrowserDomain("slack.com"));

    [Fact]
    public void UnknownDomain_ReturnsUnknown() =>
        Assert.Equal(AppCategory.Unknown, ActivityFeatureTracker.ClassifyBrowserDomain("randomsite.xyz"));
}

public sealed class IsCommunicationContextTests
{
    [Fact]
    public void AppIdInCommunicationApps_ReturnsTrue()
    {
        var ctx = new ActivityContext(
            FocusedAppId: "slack", FocusedTitle: "", FocusedDomain: "",
            FocusedWindowId: "", IdleMs: 0, IsIdleNow: 0,
            FocusedSinceMs: 0, TitleUnchangedForMs: 0,
            MappedToplevelCount: 1, ActiveWorkspaceId: "",
            FocusSource: FocusSource.Unknown, SignalQuality: SignalQuality.Trusted,
            FullscreenFlag: 0);
        Assert.True(ActivityFeatureTracker.IsCommunicationContext(ctx));
    }

    [Fact]
    public void DomainInCommunicationDomains_ReturnsTrue()
    {
        var ctx = new ActivityContext(
            FocusedAppId: "firefox", FocusedTitle: "", FocusedDomain: "slack.com",
            FocusedWindowId: "", IdleMs: 0, IsIdleNow: 0,
            FocusedSinceMs: 0, TitleUnchangedForMs: 0,
            MappedToplevelCount: 1, ActiveWorkspaceId: "",
            FocusSource: FocusSource.Unknown, SignalQuality: SignalQuality.Trusted,
            FullscreenFlag: 0);
        Assert.True(ActivityFeatureTracker.IsCommunicationContext(ctx));
    }

    [Fact]
    public void NonCommunicationApp_ReturnsFalse()
    {
        var ctx = new ActivityContext(
            FocusedAppId: "code", FocusedTitle: "", FocusedDomain: "github.com",
            FocusedWindowId: "", IdleMs: 0, IsIdleNow: 0,
            FocusedSinceMs: 0, TitleUnchangedForMs: 0,
            MappedToplevelCount: 1, ActiveWorkspaceId: "",
            FocusSource: FocusSource.Unknown, SignalQuality: SignalQuality.Trusted,
            FullscreenFlag: 0);
        Assert.False(ActivityFeatureTracker.IsCommunicationContext(ctx));
    }
}

public sealed class GetCategoryNameTests
{
    [Fact]
    public void Development_ReturnsCorrectLabel() =>
        Assert.Equal("Development", AppCategoryClassifier.GetCategoryName(AppCategory.Development));

    [Fact]
    public void Entertainment_ReturnsCorrectLabel() =>
        Assert.Equal("Entertainment", AppCategoryClassifier.GetCategoryName(AppCategory.Entertainment));

    [Fact]
    public void Communication_ReturnsCorrectLabel() =>
        Assert.Equal("Communication", AppCategoryClassifier.GetCategoryName(AppCategory.Communication));
}

public sealed class TryParseCategoryTests
{
    [Theory]
    [InlineData("development", AppCategory.Development)]
    [InlineData("creative", AppCategory.Creative)]
    [InlineData("creative & design", AppCategory.Creative)]
    [InlineData("office", AppCategory.Office)]
    [InlineData("office & writing", AppCategory.Office)]
    [InlineData("communication", AppCategory.Communication)]
    [InlineData("entertainment", AppCategory.Entertainment)]
    [InlineData("utility", AppCategory.Utility)]
    [InlineData("DeVeLoPmEnT", AppCategory.Development)]
    public void KnownCategory_ReturnsTrue(string input, AppCategory expected)
    {
        Assert.True(AppCategoryClassifier.TryParseCategory(input, out var category));
        Assert.Equal(expected, category);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public void UnknownOrEmpty_ReturnsFalse(string input)
    {
        Assert.False(AppCategoryClassifier.TryParseCategory(input, out var category));
        Assert.Equal(AppCategory.Unknown, category);
    }

    [Fact]
    public void MixedCase_ReturnsCorrectCategory()
    {
        Assert.True(AppCategoryClassifier.TryParseCategory("DEVELOPMENT", out var cat));
        Assert.Equal(AppCategory.Development, cat);
    }
}

public sealed class DisplayAppNameTests
{
    [Fact]
    public void DomainPresent_ReturnsDomain()
    {
        Assert.Equal("reddit.com", NudgeCoreLogic.DisplayAppName("chrome", "reddit.com"));
        Assert.Equal("outlook.office.com", NudgeCoreLogic.DisplayAppName("msedge", "outlook.office.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoDomain_ReturnsApp(string? domain)
    {
        Assert.Equal("Code", NudgeCoreLogic.DisplayAppName("Code", domain));
    }

    // #174: a browser with no extractable domain shows its friendly name,
    // not the raw process name.
    [Theory]
    [InlineData("chrome", "Chrome")]
    [InlineData("msedge", "Edge")]
    [InlineData("firefox", "Firefox")]
    public void NoDomain_Browser_ReturnsFriendlyBrowserName(string processName, string expected)
    {
        Assert.Equal(expected, NudgeCoreLogic.DisplayAppName(processName, ""));
        Assert.Equal(expected, NudgeCoreLogic.DisplayAppName(processName, null));
    }
}
