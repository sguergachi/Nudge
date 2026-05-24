using System;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

public class NudgeCommandExecutionTests
{
    [Fact]
    public void RunCommand_ReturnsStdout_ForSuccessfulCommand()
    {
        if (!OperatingSystem.IsLinux()) return;
        var output = NudgeCoreLogic.RunCommand("/bin/sh", "-c \"printf 'ok'\"", timeoutMs: 2000);
        Assert.Equal("ok", output);
    }

    [Fact]
    public void RunCommand_ReturnsEmpty_WhenCommandTimesOut()
    {
        if (!OperatingSystem.IsLinux()) return;
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
        if (!OperatingSystem.IsLinux()) return;
        var output = NudgeCoreLogic.RunCommand("/bin/sh", "-c \"printf 'err' >&2\"", timeoutMs: 2000);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void RunCommand_ReturnsStdout_EvenOnNonZeroExitCode()
    {
        if (!OperatingSystem.IsLinux()) return;
        var output = NudgeCoreLogic.RunCommand("/bin/sh", "-c \"printf 'out'; exit 1\"", timeoutMs: 2000);
        Assert.Equal("out", output);
    }

    [Fact]
    public void RunCommand_ReturnsBothLines_WhenCommandOutputsMultipleLines()
    {
        if (!OperatingSystem.IsLinux()) return;
        var output = NudgeCoreLogic.RunCommand("/bin/sh", "-c \"printf 'a\\nb'\"", timeoutMs: 2000);
        Assert.Equal("a\nb", output);
    }
}
