using System;
using Xunit;

public class NudgeSignalFusionTests
{
    [Fact]
    public void ActivityFeatureTracker_BrowserDomainWorkVsEntertainmentFlagsDiffer()
    {
        var tracker = new ActivityFeatureTracker();
        var start = new DateTime(2026, 5, 3, 9, 0, 0);

        var workTick = tracker.Capture(
            start,
            new WindowObservation("firefox", "openai/nudge pull request - GitHub - Mozilla Firefox", "1", "", FocusSource.SwayIpc, false, 0),
            new IdleObservation(0, IdleSource.Unknown));

        var entertainmentTick = tracker.Capture(
            start.AddSeconds(1),
            new WindowObservation("firefox", "YouTube - Mozilla Firefox", "2", "", FocusSource.SwayIpc, false, 0),
            new IdleObservation(0, IdleSource.Unknown));

        Assert.Equal("github.com", workTick.Context.FocusedDomain);
        Assert.Equal(1, workTick.Features.WorkDomainFlag);
        Assert.Equal(0, workTick.Features.EntertainmentDomainFlag);

        Assert.Equal("youtube.com", entertainmentTick.Context.FocusedDomain);
        Assert.Equal(1, entertainmentTick.Features.EntertainmentDomainFlag);
        Assert.Equal(0, entertainmentTick.Features.WorkDomainFlag);
    }

    [Fact]
    public void ActivityFeatureTracker_ReturnToAnchorAndSwitchCountsAreTracked()
    {
        var tracker = new ActivityFeatureTracker();
        var start = new DateTime(2026, 5, 3, 10, 0, 0);

        for (int i = 0; i < 120; i++)
        {
            tracker.Capture(
                start.AddSeconds(i),
                new WindowObservation("code", "main.cs", "code", "1", FocusSource.SwayIpc, false, 0),
                new IdleObservation(0, IdleSource.Unknown));
        }

        for (int i = 120; i < 180; i++)
        {
            tracker.Capture(
                start.AddSeconds(i),
                new WindowObservation("slack", "Slack", "slack", "1", FocusSource.SwayIpc, false, 0),
                new IdleObservation(0, IdleSource.Unknown));
        }

        ActivityTickResult result = default;
        for (int i = 180; i < 240; i++)
        {
            result = tracker.Capture(
                start.AddSeconds(i),
                new WindowObservation("code", "main.cs", "code", "1", FocusSource.SwayIpc, false, 0),
                new IdleObservation(0, IdleSource.Unknown));
        }

        Assert.Equal("code", result.Context.FocusedAppId);
        Assert.Equal(1, result.Features.ReturnedToAnchorApp300s);
        Assert.True(result.Features.SwitchCount300s >= 2);
        Assert.True(result.Features.CurrentAppShare300s > 0.5);
        Assert.Equal(2, result.Features.DistinctApps300s);
    }

    [Fact]
    public void ActivityFeatureTracker_AfkAndPoorSignalAreMarked()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 3, 11, 0, 0),
            new WindowObservation("firefox", "", "browser", "", FocusSource.HeuristicProcessScan, false, 0),
            new IdleObservation(75_000, IdleSource.Unknown));

        Assert.Equal(SignalQuality.Poor, tick.Context.SignalQuality);
        Assert.Equal(1, tick.Features.AfkFlag);
    }

    [Fact]
    public void TryParseHarvestLine_HandlesQuotedAppNames()
    {
        string line = "2026-05-03 12:00:00,12,0,\"Chrome (github.com, docs)\",123,0,1000,1";

        bool parsed = NudgeCoreLogic.TryParseHarvestLine(line, out var entry);

        Assert.True(parsed);
        Assert.Equal("Chrome (github.com, docs)", entry.AppName);
        Assert.True(entry.Productive);
    }
}
