using Xunit;

public class NudgeStartupGuardTests
{
    [Fact]
    public void ShouldExitForExistingTrayInstance_TrueWhenMutexNotCreated()
    {
        Assert.True(NudgeCoreLogic.ShouldExitForExistingTrayInstance(createdNew: false));
    }

    [Fact]
    public void ShouldExitForExistingTrayInstance_FalseWhenFirstInstance()
    {
        Assert.False(NudgeCoreLogic.ShouldExitForExistingTrayInstance(createdNew: true));
    }

    [Fact]
    public void TrayMutexName_IsStable()
    {
        Assert.Equal("Global\\NudgeTray.SingleInstance", NudgeCoreLogic.TraySingleInstanceMutexName);
    }
}
