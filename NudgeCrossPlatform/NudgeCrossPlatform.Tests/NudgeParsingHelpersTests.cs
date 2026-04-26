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
    public void ExtractFocusedApp_WithFocusedNodeAtRoot_ReturnsAppAndTitle()
    {
        const string json = """
            {
              "focused": true,
              "app_id": "firefox",
              "name": "Mozilla Firefox",
              "nodes": []
            }
            """;
        var (app, title) = NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json);
        Assert.Equal("firefox", app);
        Assert.Equal("Mozilla Firefox", title);
    }

    [Fact]
    public void ExtractFocusedApp_WithFocusedNodeNested_ReturnsAppAndTitle()
    {
        const string json = """
            {
              "focused": false,
              "nodes": [
                {
                  "focused": false,
                  "nodes": [
                    { "focused": true, "app_id": "kitty", "name": "terminal", "nodes": [] }
                  ]
                }
              ]
            }
            """;
        var (app, title) = NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json);
        Assert.Equal("kitty", app);
        Assert.Equal("terminal", title);
    }

    [Fact]
    public void ExtractFocusedApp_WithFocusedInFloatingNodes_ReturnsAppAndTitle()
    {
        const string json = """
            {
              "focused": false,
              "nodes": [],
              "floating_nodes": [
                { "focused": true, "app_id": "pavucontrol", "name": "Volume Control", "nodes": [] }
              ]
            }
            """;
        var (app, title) = NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json);
        Assert.Equal("pavucontrol", app);
        Assert.Equal("Volume Control", title);
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
        var (app, title) = NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json);
        Assert.Equal("unknown", app);
        Assert.Equal("", title);
    }

    [Fact]
    public void ExtractFocusedApp_EmptyAppId_ReturnsUnknown()
    {
        const string json = """{ "focused": true, "app_id": "", "nodes": [] }""";
        var (app, title) = NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json);
        Assert.Equal("unknown", app);
    }

    [Fact]
    public void ExtractFocusedApp_NullAppId_ReturnsUnknown()
    {
        const string json = """{ "focused": true, "app_id": null, "nodes": [] }""";
        var (app, title) = NudgeCoreLogic.ExtractFocusedAppFromSwayJson(json);
        Assert.Equal("unknown", app);
    }

    [Fact]
    public void ExtractFocusedApp_EmptyString_ReturnsUnknown()
    {
        var (app, title) = NudgeCoreLogic.ExtractFocusedAppFromSwayJson("");
        Assert.Equal("unknown", app);
        Assert.Equal("", title);
    }

    [Fact]
    public void ExtractFocusedApp_MalformedJson_FallsBackToStringScan()
    {
        // Not valid JSON but contains the key markers
        const string raw = """...,"focused":true,"app_id":"alacritty","name":"term",...""";
        var (app, title) = NudgeCoreLogic.ExtractFocusedAppFromSwayJson(raw);
        Assert.Equal("alacritty", app);
        Assert.Equal("term", title);
    }

    [Fact]
    public void ExtractFocusedApp_MalformedJsonNoMarkers_ReturnsUnknown()
    {
        var (app, title) = NudgeCoreLogic.ExtractFocusedAppFromSwayJson("not json at all");
        Assert.Equal("unknown", app);
        Assert.Equal("", title);
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
