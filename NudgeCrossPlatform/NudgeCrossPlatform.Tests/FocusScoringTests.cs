using System;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

public sealed class FocusScoringTests
{
    // FeatureVectorV4 is a 25-field positional record struct. Most fields are irrelevant to
    // WP1; this helper builds readable instances varying only the behavioral fields the focus
    // score reads, with neutral defaults for the rest.
    private static FeatureVectorV4 Feature(
        double appShare = 0.5,
        int focusedSinceMs = 0,
        int titleStabilityMs = 0,
        int switch300 = 0,
        int distinctApps = 1,
        int workspaceSwitch = 0,
        int returnedToAnchor = 0,
        int afk = 0)
        => new FeatureVectorV4(
            HourOfDay: 12,
            DayOfWeek: 3,
            FocusedAppHash: 0,
            FocusedDomainHash: 0,
            IdleMs: 0,
            FocusedSinceMs: focusedSinceMs,
            TitleStabilityMs: titleStabilityMs,
            SwitchCount60s: 0,
            SwitchCount300s: switch300,
            DistinctApps300s: distinctApps,
            DistinctDomains300s: distinctApps,
            ReturnedToAnchorApp300s: returnedToAnchor,
            CurrentAppShare300s: appShare,
            CurrentDomainShare300s: appShare,
            BrowserWindowFlag: 0,
            AfkFlag: afk,
            FullscreenFlag: 0,
            WorkspaceSwitchCount300s: workspaceSwitch,
            AudioPlayingFlag: 0,
            MediaSessionActiveFlag: 0,
            MicActiveFlag: 0,
            DomainProductiveRate: 0.5,
            DomainLabelCount: 0,
            AppProductiveRate: 0.5,
            AppLabelCount: 0);

    private static FeatureVectorV4 DeepWork() => Feature(
        appShare: 0.95, focusedSinceMs: 300_000, titleStabilityMs: 300_000,
        switch300: 1, distinctApps: 1, workspaceSwitch: 0);

    private static FeatureVectorV4 ChannelSurf() => Feature(
        appShare: 0.15, focusedSinceMs: 3_000, titleStabilityMs: 2_000,
        switch300: 25, distinctApps: 8, workspaceSwitch: 4);

    private static readonly DateTime T0 = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

    // ── §2 focus score ──

    [Fact]
    public void FocusScore_DeepWork_HighScore()
    {
        Assert.True(FocusScoring.FocusScore(DeepWork()) >= 0.8);
    }

    [Fact]
    public void FocusScore_ChannelSurfing_LowScore()
    {
        Assert.True(FocusScoring.FocusScore(ChannelSurf()) <= 0.3);
    }

    [Fact]
    public void FocusScore_AlwaysInUnitInterval()
    {
        Assert.InRange(FocusScoring.FocusScore(DeepWork()), 0.0, 1.0);
        Assert.InRange(FocusScoring.FocusScore(ChannelSurf()), 0.0, 1.0);
        // pathological extreme inputs still clamp
        var extreme = Feature(appShare: 5.0, focusedSinceMs: int.MaxValue,
            titleStabilityMs: int.MaxValue, switch300: 0, distinctApps: 1);
        Assert.InRange(FocusScoring.FocusScore(extreme), 0.0, 1.0);
    }

    // ── §3 baseline / drift ──

    [Fact]
    public void Baseline_Warmup_NoDriftBeforeWarmupMin()
    {
        var baseline = default(BaselineState);
        var calm = DeepWork();
        var t = T0;
        // Feed 29 calm ticks (warmup is 30), then a hard scatter tick.
        for (int i = 0; i < 29; i++)
        {
            var a = FocusScoring.Assess(in calm, ref baseline, t);
            Assert.False(a.BaselineWarm);
            Assert.False(a.DriftElevated);
            t = t.AddSeconds(60);
        }
        var surf = ChannelSurf();
        var res = FocusScoring.Assess(in surf, ref baseline, t);
        // Still not warm (this is the 30th fold; warmth is evaluated pre-fold), so no drift
        // can be reported even though the score collapsed.
        Assert.False(res.BaselineWarm);
        Assert.False(res.DriftElevated);
    }

    [Fact]
    public void Drift_SuddenScatterAfterCalmHistory_Elevated()
    {
        var baseline = default(BaselineState);
        var calm = DeepWork();
        var t = T0;
        for (int i = 0; i < 60; i++)
        {
            FocusScoring.Assess(in calm, ref baseline, t);
            t = t.AddSeconds(60);
        }
        var surf = ChannelSurf();
        var res = FocusScoring.Assess(in surf, ref baseline, t);
        Assert.True(res.BaselineWarm);
        Assert.True(res.DriftElevated);
        Assert.True(res.DriftZ < -1.0);
    }

    [Fact]
    public void Drift_ConsistentlyScatteredUser_NotAlwaysElevated()
    {
        // The personalization proof: a user whose normal IS scattered should not be
        // permanently flagged. The baseline adapts to their low-but-stable focus.
        var baseline = default(BaselineState);
        var surf = ChannelSurf();
        var t = T0;
        for (int i = 0; i < 200; i++)
        {
            FocusScoring.Assess(in surf, ref baseline, t);
            t = t.AddSeconds(60);
        }
        // Baseline mean has settled near the surfing score; another surf tick is normal.
        var res = FocusScoring.Assess(in surf, ref baseline, t);
        Assert.True(res.BaselineWarm);
        Assert.False(res.DriftElevated);
        Assert.InRange(res.DriftZ, -1.0, 1.0);
    }

    [Fact]
    public void Baseline_AfkTicksExcluded()
    {
        var baseline = default(BaselineState);
        var calm = DeepWork();
        var t = T0;
        for (int i = 0; i < 40; i++)
        {
            FocusScoring.Assess(in calm, ref baseline, t);
            t = t.AddSeconds(60);
        }
        double meanBefore = baseline.Mean;
        long countBefore = baseline.Count;

        // An AFK tick (even with a wildly different focus score) must not move the baseline.
        var afk = Feature(appShare: 0.0, switch300: 50, distinctApps: 10, afk: 1);
        var res = FocusScoring.Assess(in afk, ref baseline, t);

        Assert.Equal(meanBefore, baseline.Mean, 12);
        Assert.Equal(countBefore, baseline.Count);
        Assert.False(res.DriftElevated);
        Assert.Equal(0.0, res.DriftZ, 12);
    }

    [Fact]
    public void Baseline_SessionGap_NoFalseDrift()
    {
        var baseline = default(BaselineState);
        var calm = DeepWork();
        var t = T0;
        for (int i = 0; i < 60; i++)
        {
            FocusScoring.Assess(in calm, ref baseline, t);
            t = t.AddSeconds(60);
        }
        // Machine asleep overnight, then resume with a scattered tick. The first post-gap
        // tick must NOT be reported as drift even though the score collapsed.
        var afterGap = t.AddHours(10);
        var surf = ChannelSurf();
        var res = FocusScoring.Assess(in surf, ref baseline, afterGap);
        Assert.False(res.DriftElevated);
        Assert.Equal(0.0, res.DriftZ, 12);
        // ...but the tick was still folded in (count advances).
        Assert.True(baseline.Count > 60);
    }

    [Fact]
    public void Determinism_SameInputsSameOutputs()
    {
        BaselineState b1 = default, b2 = default;
        var calm = DeepWork();
        var surf = ChannelSurf();
        var t = T0;
        for (int i = 0; i < 50; i++)
        {
            FocusScoring.Assess(in calm, ref b1, t);
            FocusScoring.Assess(in calm, ref b2, t);
            t = t.AddSeconds(60);
        }
        var r1 = FocusScoring.Assess(in surf, ref b1, t);
        var r2 = FocusScoring.Assess(in surf, ref b2, t);
        Assert.Equal(r1.FocusScore, r2.FocusScore, 15);
        Assert.Equal(r1.DriftZ, r2.DriftZ, 15);
        Assert.Equal(r1.DriftElevated, r2.DriftElevated);
        Assert.Equal(r1.BaselineWarm, r2.BaselineWarm);
        Assert.Equal(b1.Mean, b2.Mean, 15);
        Assert.Equal(b1.Var, b2.Var, 15);
    }
}
