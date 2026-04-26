using Xunit;

public class NudgeCliParserTests
{
    // ── nudge arg parser ──────────────────────────────────────────────────────

    [Fact]
    public void ParseNudgeArgs_LongHelpFlag_ReturnsHelpAction()
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["--help"]);
        Assert.Equal(NudgeStartupAction.ShowHelp, parsed.Action);
    }

    [Fact]
    public void ParseNudgeArgs_ShortHelpFlag_ReturnsHelpAction()
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["-h"]);
        Assert.Equal(NudgeStartupAction.ShowHelp, parsed.Action);
    }

    [Fact]
    public void ParseNudgeArgs_LongVersionFlag_ReturnsVersionAction()
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["--version"]);
        Assert.Equal(NudgeStartupAction.ShowVersion, parsed.Action);
    }

    [Fact]
    public void ParseNudgeArgs_ShortVersionFlag_ReturnsVersionAction()
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["-v"]);
        Assert.Equal(NudgeStartupAction.ShowVersion, parsed.Action);
    }

    [Fact]
    public void ParseNudgeArgs_RecognizesIntervalMlForceAndCsvPath()
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["--interval", "2", "--ml", "--force-model", "/tmp/data.csv"]);
        Assert.Equal(NudgeStartupAction.Run, parsed.Action);
        Assert.Equal(2, parsed.IntervalMinutes);
        Assert.True(parsed.MlEnabled);
        Assert.True(parsed.ForceTrainedModel);
        Assert.Equal("/tmp/data.csv", parsed.CsvPath);
    }

    [Fact]
    public void ParseNudgeArgs_ShortIntervalFlag_SetsInterval()
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["-i", "3"]);
        Assert.Equal(3, parsed.IntervalMinutes);
    }

    [Fact]
    public void ParseNudgeArgs_IntervalAtEndOfArgs_DoesNotSetInterval()
    {
        // --interval with no following value: interval stays null
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["--interval"]);
        Assert.Null(parsed.IntervalMinutes);
    }

    [Fact]
    public void ParseNudgeArgs_IntervalWithNonNumericValue_DoesNotSetIntervalAndDoesNotTreatValueAsCsvPath()
    {
        // "bad" should NOT become the CSV path – it is consumed as the interval value
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["--interval", "bad"]);
        Assert.Null(parsed.IntervalMinutes);
        Assert.Null(parsed.CsvPath);
    }

    [Fact]
    public void ParseNudgeArgs_ForceModelAlone_SetsFlag()
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["--force-model"]);
        Assert.Equal(NudgeStartupAction.Run, parsed.Action);
        Assert.True(parsed.ForceTrainedModel);
    }

    [Fact]
    public void ParseNudgeArgs_NoArgs_ReturnsRunWithDefaults()
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs([]);
        Assert.Equal(NudgeStartupAction.Run, parsed.Action);
        Assert.Null(parsed.IntervalMinutes);
        Assert.False(parsed.MlEnabled);
        Assert.False(parsed.ForceTrainedModel);
        Assert.Null(parsed.CsvPath);
    }

    [Fact]
    public void ParseNudgeArgs_HelpTakesPrecedenceOverOtherFlags()
    {
        // --help should short-circuit, even when other flags precede it
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["--ml", "--help"]);
        Assert.Equal(NudgeStartupAction.ShowHelp, parsed.Action);
    }

    // ── nudge-notify arg parser ───────────────────────────────────────────────

    [Fact]
    public void ParseNudgeNotifyArgs_LongHelpAndVersionFlags_Work()
    {
        Assert.Equal(NudgeNotifyAction.ShowHelp,    NudgeCoreLogic.ParseNudgeNotifyArgs(["--help"]).Action);
        Assert.Equal(NudgeNotifyAction.ShowVersion, NudgeCoreLogic.ParseNudgeNotifyArgs(["--version"]).Action);
    }

    [Fact]
    public void ParseNudgeNotifyArgs_ShortHelpAndVersionFlags_Work()
    {
        Assert.Equal(NudgeNotifyAction.ShowHelp,    NudgeCoreLogic.ParseNudgeNotifyArgs(["-h"]).Action);
        Assert.Equal(NudgeNotifyAction.ShowVersion, NudgeCoreLogic.ParseNudgeNotifyArgs(["-v"]).Action);
    }

    [Fact]
    public void ParseNudgeNotifyArgs_MissingOrInvalidResponse_AreRejected()
    {
        Assert.Equal(NudgeNotifyAction.MissingResponse, NudgeCoreLogic.ParseNudgeNotifyArgs([]).Action);

        var invalid = NudgeCoreLogic.ParseNudgeNotifyArgs(["MAYBE"]);
        Assert.Equal(NudgeNotifyAction.InvalidResponse, invalid.Action);
        Assert.Equal("MAYBE", invalid.RawInput);
    }

    [Fact]
    public void ParseNudgeNotifyArgs_YesResponse_IsNormalized()
    {
        var parsed = NudgeCoreLogic.ParseNudgeNotifyArgs(["yes"]);
        Assert.Equal(NudgeNotifyAction.SendResponse, parsed.Action);
        Assert.Equal("YES", parsed.Response);
    }

    [Fact]
    public void ParseNudgeNotifyArgs_NoResponse_IsNormalized()
    {
        var parsed = NudgeCoreLogic.ParseNudgeNotifyArgs(["no"]);
        Assert.Equal(NudgeNotifyAction.SendResponse, parsed.Action);
        Assert.Equal("NO", parsed.Response);
    }

    [Fact]
    public void ParseNudgeNotifyArgs_MixedCaseResponse_IsNormalized()
    {
        // "yEs" and "nO" should both be accepted and uppercased
        Assert.Equal("YES", NudgeCoreLogic.ParseNudgeNotifyArgs(["yEs"]).Response);
        Assert.Equal("NO",  NudgeCoreLogic.ParseNudgeNotifyArgs(["nO"]).Response);
    }

    [Fact]
    public void ParseNudgeNotifyArgs_OnlyFirstArgIsInspected()
    {
        // Extra args beyond the first are silently ignored
        var parsed = NudgeCoreLogic.ParseNudgeNotifyArgs(["YES", "extra", "args"]);
        Assert.Equal(NudgeNotifyAction.SendResponse, parsed.Action);
        Assert.Equal("YES", parsed.Response);
    }
}
