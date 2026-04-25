using Xunit;

public class NudgeCliParserTests
{
    [Fact]
    public void ParseNudgeArgs_HelpFlag_ReturnsHelpAction()
    {
        var parsed = NudgeCoreLogic.ParseNudgeArgs(["--help"]);
        Assert.Equal(NudgeStartupAction.ShowHelp, parsed.Action);
    }

    [Fact]
    public void ParseNudgeArgs_VersionFlag_ReturnsVersionAction()
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
    public void ParseNudgeNotifyArgs_HelpAndVersionFlags_Work()
    {
        Assert.Equal(NudgeNotifyAction.ShowHelp, NudgeCoreLogic.ParseNudgeNotifyArgs(["-h"]).Action);
        Assert.Equal(NudgeNotifyAction.ShowVersion, NudgeCoreLogic.ParseNudgeNotifyArgs(["--version"]).Action);
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
    public void ParseNudgeNotifyArgs_ValidResponse_IsUppercased()
    {
        var parsed = NudgeCoreLogic.ParseNudgeNotifyArgs(["yes"]);
        Assert.Equal(NudgeNotifyAction.SendResponse, parsed.Action);
        Assert.Equal("YES", parsed.Response);
    }
}
