using Xunit;

public class NudgeCommandExecutionTests
{
    [Fact]
    public void RunCommand_ReturnsStdout_ForSuccessfulCommand()
    {
        var output = NudgeCoreLogic.RunCommand("/bin/sh", "-c \"printf 'ok'\"", timeoutMs: 2000);
        Assert.Equal("ok", output);
    }

    [Fact]
    public void RunCommand_ReturnsEmpty_WhenCommandTimesOut()
    {
        var output = NudgeCoreLogic.RunCommand("/bin/sh", "-c \"sleep 2; printf 'late'\"", timeoutMs: 100);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void RunCommand_ReturnsEmpty_WhenCommandCannotStart()
    {
        var output = NudgeCoreLogic.RunCommand("definitely-not-a-real-command", "");
        Assert.Equal(string.Empty, output);
    }
}
