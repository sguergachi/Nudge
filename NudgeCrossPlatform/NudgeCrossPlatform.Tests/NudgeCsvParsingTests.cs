using System;
using Xunit;
using NudgeCore;
using NudgeTray;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeCsvParsingTests
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
        Assert.False(NudgeCoreLogic.TryParseHarvestLine("2026-04-26 12,12", out _));
    }

    [Theory]
    [InlineData("Nudge Tray")]
    [InlineData("customnudgewindow")]
    [InlineData("unknown")]
    [InlineData("UNKNOWN")]
    [InlineData("test-mode")]
    [InlineData("kwin_wayland")]
    [InlineData("plasmashell")]
    [InlineData("com.shellyorg.shelly")]
    [InlineData("org.gnome.Files")]
    public void ShouldIgnoreAnalyticsApp_FiltersSystemAndTestApps(string appName)
    {
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp(appName));
    }

    [Theory]
    [InlineData("Firefox")]
    [InlineData("zen")]
    [InlineData("zen-bin")]
    [InlineData("t3code")]
    [InlineData("cursor")]
    public void ShouldIgnoreAnalyticsApp_AllowsRealApps(string appName)
    {
        Assert.False(NudgeCoreLogic.ShouldIgnoreAnalyticsApp(appName));
    }

    // ── Group 4: TryGetCsvField quoted-field state machine tests ────────────────

[Fact]
    public void QuotedField_WithEscapedDoubleQuote_ParsedCorrectly()
    {
        // "say ""hello""" should parse to say "hello" (outer quotes removed, inner quotes preserved)
        bool result = NudgeCoreLogic.TryGetCsvField("\"say \"\"hello\"\"\"", 0, out var field);
        Assert.True(result);
        Assert.Equal(@"say ""hello""", field.ToString());
    }

    [Fact]
    public void QuotedField_EmptyValue_ReturnsEmptyString()
    {
        // "" should parse to empty string
        bool result = NudgeCoreLogic.TryGetCsvField("\"\"", 0, out var field);
        Assert.True(result);
        Assert.Equal(string.Empty, field.ToString());
    }

    [Fact]
    public void QuotedField_LastField_NoTrailingComma()
    {
        // "field1,field2,"value"" should parse last field as "value"
        bool result = NudgeCoreLogic.TryGetCsvField("field1,field2,\"value\"", 2, out var field);
        Assert.True(result);
        Assert.Equal("value", field.ToString());
    }

    [Fact]
    public void UnquotedField_WithComma_StopsAtComma()
    {
        // "value,more" should parse first field as "value" (stops at comma)
        bool result = NudgeCoreLogic.TryGetCsvField("value,more", 0, out var field);
        Assert.True(result);
        Assert.Equal("value", field.ToString());
    }

    [Fact]
    public void CsvLine_WithEmbeddedNewlineInQuotedField()
    {
        // "line1,"embedded\nnewline",line3" - currently treats \n as part of field
        bool result = NudgeCoreLogic.TryGetCsvField("line1,\"embedded\nnewline\",line3", 1, out var field);
        Assert.True(result);
        Assert.Equal("embedded\nnewline", field.ToString());
        // Note: This test verifies current behavior - \n inside quotes is treated as part of the field
        // A more sophisticated parser might reject this or handle it differently
    }

    // ── Group 5: ShouldIgnoreAnalyticsApp edge cases ─────────────────────────────────

    [Fact]
    public void ShouldIgnoreAnalyticsApp_NullOrEmpty_ReturnsFalse()
    {
        // Null throws, empty/whitespace passes through - method doesn't explicitly ignore these
        Assert.False(NudgeCoreLogic.ShouldIgnoreAnalyticsApp(string.Empty));
        Assert.False(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("   "));
    }

    [Theory]
    [InlineData("Firefox")]
    [InlineData("Firefox-beta")]
    public void ShouldIgnoreAnalyticsApp_WithWhitespace_AllowsRealApps(string appName)
    {
        // Whitespace-padded versions of real apps are still allowed
        Assert.False(NudgeCoreLogic.ShouldIgnoreAnalyticsApp($" {appName} "));
    }

    [Fact]
    public void ShouldIgnoreAnalyticsApp_CaseInsensitive_Matches()
    {
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("NUDGE TRAY"));
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("Nudge-Tray"));
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("KWIN_WAYLAND"));
    }

    [Fact]
    public void ShouldIgnoreAnalyticsApp_ReverseDnsBundleId_ReturnsTrue()
    {
        // Bundle IDs with 2+ dots and starting with lowercase letter
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("com.company.app"));
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("org.kde.kwin"));
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("net.example.tool"));
    }

    [Fact]
    public void ShouldIgnoreAnalyticsApp_NudgeInName_ReturnsTrue()
    {
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("nudge"));
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("nudge-tray"));
        Assert.True(NudgeCoreLogic.ShouldIgnoreAnalyticsApp("CustomNudgeWindow"));
    }

    // ── Group 6: TryParseActivityLogLine and TryParseHarvestLine edge cases ──────────

    [Fact]
    public void TryParseActivityLogLine_QuotedAppField_ParsesCorrectly()
    {
        // App name with comma inside quotes
        var ok = NudgeCoreLogic.TryParseActivityLogLine(
            "2026-04-26 12:34:56,12,0,\"Firefox, Developer Edition\",12345,2000",
            out var entry);

        Assert.True(ok);
        Assert.Equal("Firefox, Developer Edition", entry.AppName);
    }

    [Fact]
    public void TryParseActivityLogLine_QuotedAppField_EscapesQuotes()
    {
        // App name with quoted quotes inside
        var ok = NudgeCoreLogic.TryParseActivityLogLine(
            "2026-04-26 12:34:56,12,0,\"say \"\"hello\"\"\",12345,2000",
            out var entry);

        Assert.True(ok);
        Assert.Equal(@"say ""hello""", entry.AppName);
    }

    [Fact]
    public void TryParseHarvestLine_QuotedAppField_ParsesCorrectly()
    {
        // App name with comma inside quotes
        var ok = NudgeCoreLogic.TryParseHarvestLine(
            "2026-04-26 12:34:56,12,0,\"Chrome, Beta\",12345,500,30000,1",
            out var entry);

        Assert.True(ok);
        Assert.Equal("Chrome, Beta", entry.AppName);
        Assert.True(entry.Productive);
    }

    [Fact]
    public void TryParseHarvestLine_ProductiveFlag_Zero_ReturnsFalse()
    {
        var ok = NudgeCoreLogic.TryParseHarvestLine(
            "2026-04-26 12:34:56,12,0,Chrome,12345,500,30000,0",
            out var entry);

        Assert.True(ok);
        Assert.False(entry.Productive);
    }

    [Fact]
    public void TryParseHarvestLine_ProductiveFlag_NonNumeric_ReturnsEntryWithFalse()
    {
        // Non-numeric productive flag - parses correctly with Productive=false
        // (code checks productiveField[0] == '1', not int.TryParse)
        var ok = NudgeCoreLogic.TryParseHarvestLine(
            "2026-04-26 12:34:56,12,0,Chrome,12345,500,30000,X",
            out var entry);

        Assert.True(ok);
        Assert.False(entry.Productive);
    }

    [Fact]
    public void TryParseHarvestLine_V4ExperimentalLine_ReturnsEntry()
    {
        var ok = NudgeCoreLogic.TryParseHarvestLine(
            "2026-04-26 12:34:56,12,0,Cursor,12345,500,30000,1,4,cursor,Code,github.com,win-1,false,60000,30000,2,1,kwin_script,trusted,0,123,456,500,30000,1,3,2,1,1,0.75,0.25,1,0,0,0,1,0,0.9,42,0.8,12",
            out var entry);

        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 4, 26, 12, 34, 56), entry.Timestamp);
        Assert.Equal(12, entry.HourOfDay);
        Assert.Equal("Cursor", entry.AppName);
        Assert.True(entry.Productive);
    }

    [Fact]
    public void AnalyticsData_GetDataPaths_UsesExperimentalFilesWhenEnabled()
    {
        var normal = AnalyticsData.GetDataPaths(experimentalMode: false);
        var experimental = AnalyticsData.GetDataPaths(experimentalMode: true);

        Assert.EndsWith("ACTIVITY_LOG.CSV", normal.ActivityLogPath);
        Assert.EndsWith("HARVEST.CSV", normal.HarvestPath);
        Assert.EndsWith("ACTIVITY_LOG_EXP.CSV", experimental.ActivityLogPath);
        Assert.EndsWith("HARVEST_EXP.CSV", experimental.HarvestPath);
    }

    [Fact]
    public void TryParseActivityLogLine_InvalidTimestamp_ReturnsFalse()
    {
        Assert.False(NudgeCoreLogic.TryParseActivityLogLine(
            "not-a-date,12,0,Firefox,12345,2000", out _));
    }

    [Fact]
    public void TryParseHarvestLine_InvalidTimestamp_ReturnsFalse()
    {
        Assert.False(NudgeCoreLogic.TryParseHarvestLine(
            "not-a-date,12,0,Chrome,12345,500,30000,1", out _));
    }

    [Fact]
    public void TryParseActivityLogLine_EmptyAppField_ReturnsFalse()
    {
        Assert.False(NudgeCoreLogic.TryParseActivityLogLine(
            "2026-04-26 12:34:56,12,0,,12345,2000", out _));
    }

    [Fact]
    public void TryParseHarvestLine_EmptyAppField_ReturnsFalse()
    {
        Assert.False(NudgeCoreLogic.TryParseHarvestLine(
            "2026-04-26 12:34:56,12,0,,12345,500,30000,1", out _));
    }
}
