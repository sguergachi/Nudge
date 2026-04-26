using System;
using Xunit;

public class NudgeCsvParsingTests
{
    [Fact]
    public void TryParseActivityLogLine_ValidLine_ReturnsEntry()
    {
        var ok = NudgeCoreLogic.TryParseActivityLogLine(
            "2026-04-26 12:34:56,12,0,Firefox,12345,2000",
            out var entry);

        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 4, 26, 12, 34, 56), entry.Timestamp);
        Assert.Equal("Firefox", entry.AppName);
    }

    [Fact]
    public void TryParseActivityLogLine_MalformedLine_ReturnsFalse()
    {
        Assert.False(NudgeCoreLogic.TryParseActivityLogLine("bad,line", out _));
    }

    [Fact]
    public void TryParseHarvestLine_ValidLine_ReturnsEntry()
    {
        var ok = NudgeCoreLogic.TryParseHarvestLine(
            "2026-04-26 12:34:56,12,0,Chrome,12345,500,30000,1",
            out var entry);

        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 4, 26, 12, 34, 56), entry.Timestamp);
        Assert.Equal(12, entry.HourOfDay);
        Assert.Equal("Chrome", entry.AppName);
        Assert.True(entry.Productive);
    }

    [Fact]
    public void TryParseHarvestLine_MalformedLine_ReturnsFalse()
    {
        Assert.False(NudgeCoreLogic.TryParseHarvestLine("2026-04-26 12:34:56,12", out _));
    }

    [Theory]
    [InlineData("Nudge Tray")]
    [InlineData("customnudgewindow")]
    public void ShouldIgnoreAnalyticsApp_IsCaseInsensitive(string appName)
    {
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp(appName));
    }
}
