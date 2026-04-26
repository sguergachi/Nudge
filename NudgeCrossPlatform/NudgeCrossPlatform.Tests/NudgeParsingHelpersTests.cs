using System;
using Xunit;

public class NudgeParsingHelpersTests
{
    // ── FormatCountdown ───────────────────────────────────────────────────────

    [Fact]
    public void FormatCountdown_MoreThanOneMinute_ShowsMinutesAndSeconds()
    {
        var result = NudgeCoreLogic.FormatCountdown(TimeSpan.FromSeconds(90));
        Assert.Equal("📸 Next snapshot in 1m 30s", result);
    }

    [Fact]
    public void FormatCountdown_ExactlyOneMinute_ShowsOneMinuteZeroSeconds()
    {
        var result = NudgeCoreLogic.FormatCountdown(TimeSpan.FromSeconds(60));
        Assert.Equal("📸 Next snapshot in 1m 00s", result);
    }

    [Fact]
    public void FormatCountdown_LessThanOneMinute_ShowsSecondsOnly()
    {
        var result = NudgeCoreLogic.FormatCountdown(TimeSpan.FromSeconds(45));
        Assert.Equal("📸 Next snapshot in 45s", result);
    }

    [Fact]
    public void FormatCountdown_OneSecond_ShowsOneSecond()
    {
        var result = NudgeCoreLogic.FormatCountdown(TimeSpan.FromSeconds(1));
        Assert.Equal("📸 Next snapshot in 1s", result);
    }

    [Fact]
    public void FormatCountdown_Zero_ShowsSoon()
    {
        var result = NudgeCoreLogic.FormatCountdown(TimeSpan.Zero);
        Assert.Equal("📸 Next snapshot: soon", result);
    }

    [Fact]
    public void FormatCountdown_Negative_ShowsSoon()
    {
        var result = NudgeCoreLogic.FormatCountdown(TimeSpan.FromSeconds(-5));
        Assert.Equal("📸 Next snapshot: soon", result);
    }

    [Fact]
    public void FormatCountdown_FiveMinutes_ShowsCorrectFormat()
    {
        var result = NudgeCoreLogic.FormatCountdown(TimeSpan.FromMinutes(5));
        Assert.Equal("📸 Next snapshot in 5m 00s", result);
    }

    [Fact]
    public void FormatCountdown_SecondsArePaddedToTwoDigits()
    {
        var result = NudgeCoreLogic.FormatCountdown(TimeSpan.FromSeconds(61));
        // 61s → 1m 01s
        Assert.Equal("📸 Next snapshot in 1m 01s", result);
    }

    // ── ExtractFocusedAppFromSwayJson ─────────────────────────────────────────

    [Fact]
    public void ExtractFocusedApp_WithFocusedNodeAtRoot_ReturnsAppId()
    {
        const string json = """
            {
              "focused": true,
              "app_id": "firefox",
              "nodes": []
            }
            """;
        Assert.Equal("firefox", NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json));
    }

    [Fact]
    public void ExtractFocusedApp_WithFocusedNodeNested_ReturnsAppId()
    {
        const string json = """
            {
              "focused": false,
              "nodes": [
                {
                  "focused": false,
                  "nodes": [
                    { "focused": true, "app_id": "kitty", "nodes": [] }
                  ]
                }
              ]
            }
            """;
        Assert.Equal("kitty", NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json));
    }

    [Fact]
    public void ExtractFocusedApp_WithFocusedInFloatingNodes_ReturnsAppId()
    {
        const string json = """
            {
              "focused": false,
              "nodes": [],
              "floating_nodes": [
                { "focused": true, "app_id": "pavucontrol", "nodes": [] }
              ]
            }
            """;
        Assert.Equal("pavucontrol", NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json));
    }

    [Fact]
    public void ExtractFocusedApp_NoFocusedNode_ReturnsUnknown()
    {
        const string json = """
            {
              "focused": false,
              "nodes": [
                { "focused": false, "app_id": "unfocused", "nodes": [] }
              ]
            }
            """;
        Assert.Equal("unknown", NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json));
    }

    [Fact]
    public void ExtractFocusedApp_EmptyAppId_ReturnsUnknown()
    {
        const string json = """{ "focused": true, "app_id": "", "nodes": [] }""";
        Assert.Equal("unknown", NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json));
    }

    [Fact]
    public void ExtractFocusedApp_NullAppId_ReturnsUnknown()
    {
        const string json = """{ "focused": true, "app_id": null, "nodes": [] }""";
        Assert.Equal("unknown", NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json));
    }

    [Fact]
    public void ExtractFocusedApp_EmptyString_ReturnsUnknown()
    {
        Assert.Equal("unknown", NudgeCoreLogic.ExtractFocusedAppFromSwayJson(""));
    }

    [Fact]
    public void ExtractFocusedApp_MalformedJson_FallsBackToStringScan()
    {
        // Not valid JSON but contains the key markers
        const string raw = """...,"focused":true,"app_id":"alacritty",...""";
        Assert.Equal("alacritty", NudgeCoreLogic.ExtractFocusedAppFromSwayJson(raw));
    }

    [Fact]
    public void ExtractFocusedApp_MalformedJsonNoMarkers_ReturnsUnknown()
    {
        Assert.Equal("unknown", NudgeCoreLogic.ExtractFocusedAppFromSwayJson("not json at all"));
    }

    // ── ExtractQuotedString ───────────────────────────────────────────────────

    [Fact]
    public void ExtractQuotedString_DoubleQuotes_ReturnsValue()
    {
        Assert.Equal("firefox", NudgeCoreLogic.ExtractQuotedString("(true, \"firefox\")"));
    }

    [Fact]
    public void ExtractQuotedString_SingleQuotes_ReturnsValue()
    {
        // gdbus output uses single quotes: (true, 'nautilus')
        Assert.Equal("nautilus", NudgeCoreLogic.ExtractQuotedString("(true, 'nautilus')"));
    }

    [Fact]
    public void ExtractQuotedString_EmptyQuotedValue_ReturnsUnknown()
    {
        // An empty-quoted token ('') produces no usable app name → "unknown"
        // because end == start (the close-quote immediately follows the open-quote).
        Assert.Equal("unknown", NudgeCoreLogic.ExtractQuotedString("(false, '')"));
    }

    [Fact]
    public void ExtractQuotedString_NoQuotes_ReturnsUnknown()
    {
        Assert.Equal("unknown", NudgeCoreLogic.ExtractQuotedString("no quotes here"));
    }

    [Fact]
    public void ExtractQuotedString_EmptyInput_ReturnsUnknown()
    {
        Assert.Equal("unknown", NudgeCoreLogic.ExtractQuotedString(""));
    }

    [Fact]
    public void ExtractQuotedString_NullInput_ReturnsUnknown()
    {
        Assert.Equal("unknown", NudgeCoreLogic.ExtractQuotedString(null!));
    }

    [Fact]
    public void ExtractQuotedString_PrefersDoubleQuotesOverSingle()
    {
        // Input has both; double-quote content should win
        Assert.Equal("double", NudgeCoreLogic.ExtractQuotedString("\"double\" and 'single'"));
    }
}
