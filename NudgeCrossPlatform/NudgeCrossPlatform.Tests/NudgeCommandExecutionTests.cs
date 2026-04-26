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

    [Fact]
    public void RunCommand_ReturnsStdout_NotStderr()
    {
        // Command writes only to stderr; stdout should be empty
        var output = NudgeCoreLogic.RunCommand("/bin/sh", "-c \"printf 'err' >&2\"", timeoutMs: 2000);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void RunCommand_ReturnsStdout_EvenOnNonZeroExitCode()
    {
        // Command emits stdout then exits with code 1
        var output = NudgeCoreLogic.RunCommand("/bin/sh", "-c \"printf 'out'; exit 1\"", timeoutMs: 2000);
        Assert.Equal("out", output);
    }

    [Fact]
    public void RunCommand_ReturnsBothLines_WhenCommandOutputsMultipleLines()
    {
        var output = NudgeCoreLogic.RunCommand("/bin/sh", "-c \"printf 'a\\nb'\"", timeoutMs: 2000);
        Assert.Equal("a\nb", output);
    }
}
