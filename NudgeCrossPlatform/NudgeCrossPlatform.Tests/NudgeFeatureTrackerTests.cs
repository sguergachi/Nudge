using System;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeFeatureTrackerTests
{
    private static WindowObservation Win(string appId, string title = "", string windowId = "w1", string workspaceId = "1") =>
        new(appId, title, windowId, workspaceId, FocusSource.KWinScript, false, 1);

    private static IdleObservation Active() => new(0, IdleSource.Unknown);

    // ── Single observation ────────────────────────────────────────────────────

    [Fact]
    public void SingleObservation_ProducesExpectedFeatureVector()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code", "main.cs"),
            Active());

        Assert.Equal(1.0, tick.Features.CurrentAppShare300s);
        Assert.Equal(0, tick.Features.SwitchCount300s);
        Assert.Equal(0, tick.Features.SwitchCount60s);
        Assert.Equal(1, tick.Features.DistinctApps300s);
        Assert.Equal(SignalQuality.Trusted, tick.Context.SignalQuality);
    }

    // ── Focus switches ────────────────────────────────────────────────────────

    [Fact]
    public void SameWindowId_TwoObservations_NoSwitchCounted()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        tracker.Capture(t, Win("code", "main.cs", "w1"), Active());
        var tick = tracker.Capture(t.AddSeconds(1), Win("code", "other.cs", "w1"), Active());

        Assert.Equal(0, tick.Features.SwitchCount300s);
    }

    [Fact]
    public void DifferentWindowIds_OneSwitchCounted()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        tracker.Capture(t, Win("code", "main.cs", "w1"), Active());
        var tick = tracker.Capture(t.AddSeconds(1), Win("firefox", "GitHub", "w2"), Active());

        Assert.Equal(1, tick.Features.SwitchCount300s);
        Assert.Equal(2, tick.Features.DistinctApps300s);
    }

    [Fact]
    public void SwitchCount60s_OnlyCountsSwitchesWithin60Seconds()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        // One switch at t=0→1 (within 60s window)
        tracker.Capture(t, Win("code", "", "w1"), Active());
        tracker.Capture(t.AddSeconds(1), Win("slack", "", "w2"), Active());

        // Advance 90s — now the first switch is outside the 60s window
        var tick = tracker.Capture(t.AddSeconds(91), Win("code", "", "w1"), Active());

        Assert.Equal(0, tick.Features.SwitchCount60s);
        Assert.Equal(2, tick.Features.SwitchCount300s);  // both switches still in 300s window
    }

    // ── Window ID fallback (app+title as focus key) ───────────────────────────

    [Fact]
    public void NoWindowId_SameAppAndTitle_NoSwitch()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        tracker.Capture(t, new WindowObservation("code", "main.cs", "", "1", FocusSource.KWinScript, false, 1), Active());
        var tick = tracker.Capture(t.AddSeconds(1), new WindowObservation("code", "main.cs", "", "1", FocusSource.KWinScript, false, 1), Active());

        Assert.Equal(0, tick.Features.SwitchCount300s);
    }

    [Fact]
    public void NoWindowId_TitleChange_CountsAsSwitch()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        tracker.Capture(t, new WindowObservation("code", "main.cs", "", "1", FocusSource.KWinScript, false, 1), Active());
        var tick = tracker.Capture(t.AddSeconds(1), new WindowObservation("code", "other.cs", "", "1", FocusSource.KWinScript, false, 1), Active());

        Assert.Equal(1, tick.Features.SwitchCount300s);
    }

    // ── TrimSamples (300s rolling window) ────────────────────────────────────

    [Fact]
    public void OldSamplesDropped_AppShareReflectsOnlyRecentWindow()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        for (int i = 0; i < 50; i++)
            tracker.Capture(t.AddSeconds(i), Win("old-app", "", "w-old"), Active());

        // Jump forward past 300s so all old samples are pruned
        var tick = tracker.Capture(t.AddSeconds(400), Win("new-app", "", "w-new"), Active());

        Assert.Equal(1.0, tick.Features.CurrentAppShare300s);
        Assert.Equal(1, tick.Features.DistinctApps300s);
    }

    // ── Workspace switches ────────────────────────────────────────────────────

    [Fact]
    public void ThreeWorkspaces_TwoSwitchesCounted()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        tracker.Capture(t, Win("code", "", "w1", "1"), Active());
        tracker.Capture(t.AddSeconds(1), Win("slack", "", "w2", "2"), Active());
        var tick = tracker.Capture(t.AddSeconds(2), Win("code", "", "w1", "3"), Active());

        Assert.Equal(2, tick.Features.WorkspaceSwitchCount300s);
    }

    [Fact]
    public void EmptyWorkspaceId_NotCountedAsSwitch()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        tracker.Capture(t, Win("code", "", "w1", "1"), Active());
        tracker.Capture(t.AddSeconds(1), new WindowObservation("code", "", "w1", "", FocusSource.KWinScript, false, 1), Active());
        var tick = tracker.Capture(t.AddSeconds(2), Win("code", "", "w1", "1"), Active());

        Assert.Equal(0, tick.Features.WorkspaceSwitchCount300s);
    }

    // ── Signal quality ────────────────────────────────────────────────────────

    [Fact]
    public void SignalQuality_HeuristicSource_IsPoor()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("code", "main.cs", "w1", "1", FocusSource.HeuristicProcessScan, false, 1),
            Active());

        Assert.Equal(SignalQuality.Poor, tick.Context.SignalQuality);
    }

    [Fact]
    public void SignalQuality_UnknownSource_IsPoor()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("code", "main.cs", "w1", "1", FocusSource.Unknown, false, 1),
            Active());

        Assert.Equal(SignalQuality.Poor, tick.Context.SignalQuality);
    }

    [Fact]
    public void SignalQuality_KWinSource_IsTrusted()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("code", "main.cs", "w1", "1", FocusSource.KWinScript, false, 1),
            Active());

        Assert.Equal(SignalQuality.Trusted, tick.Context.SignalQuality);
    }

    [Fact]
    public void SignalQuality_BrowserWithNoTitle_IsUsable()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("firefox", "", "w1", "1", FocusSource.KWinScript, false, 1),
            Active());

        Assert.Equal(SignalQuality.Usable, tick.Context.SignalQuality);
    }

    // ── AFK flag ──────────────────────────────────────────────────────────────

    [Fact]
    public void AfkFlag_SetWhenIdleExceeds60Seconds()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code"),
            new IdleObservation(60_001, IdleSource.Unknown));

        Assert.Equal(1, tick.Features.AfkFlag);
    }

    [Fact]
    public void AfkFlag_NotSetJustBelowThreshold()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code"),
            new IdleObservation(59_999, IdleSource.Unknown));

        Assert.Equal(0, tick.Features.AfkFlag);
    }

    // ── FocusedSinceMs clamping ───────────────────────────────────────────────

    [Fact]
    public void FocusedSinceMs_ClampedToIntMax_ForVeryLongDwell()
    {
        var tracker = new ActivityFeatureTracker();
        var past = new DateTime(2000, 1, 1);
        var now = new DateTime(2026, 5, 23, 10, 0, 0);

        tracker.Capture(past, Win("code", "", "w1"), Active());
        // Same window ID → focusedSince stays at past
        var tick = tracker.Capture(now, Win("code", "", "w1"), Active());

        Assert.Equal(int.MaxValue, tick.Features.FocusedSinceMs);
    }

    // ── Browser domain classification ─────────────────────────────────────────

    [Fact]
    public void BrowserDomain_GitHub_ClassifiedAsDevelopment()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("firefox", "openai/nudge - GitHub - Mozilla Firefox", "f1", "1", FocusSource.KWinScript, false, 1),
            Active());

        Assert.Equal("github.com", tick.Context.FocusedDomain);
        Assert.Equal(AppCategory.Development, tick.AppCategory);
        Assert.Equal(1, tick.Features.DevAppFlag);
    }

    [Fact]
    public void BrowserDomain_WorkDomainFlag_SetForKnownWorkDomain()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("firefox", "openai/nudge - GitHub - Mozilla Firefox", "f1", "1", FocusSource.KWinScript, false, 1),
            Active());

        Assert.Equal(1, tick.Features.WorkDomainFlag);
        Assert.Equal(0, tick.Features.EntertainmentDomainFlag);
    }

    [Fact]
    public void BrowserDomain_EntertainmentDomainFlag_SetForYouTube()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("firefox", "Cats - YouTube - Mozilla Firefox", "f1", "1", FocusSource.KWinScript, false, 1),
            Active());

        Assert.Equal(1, tick.Features.EntertainmentDomainFlag);
        Assert.Equal(0, tick.Features.WorkDomainFlag);
    }

    // ── DisplayAppName ────────────────────────────────────────────────────────

    [Fact]
    public void DisplayAppName_NonBrowser_IsAppIdBracketTitle()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code", "main.cs"),
            Active());

        Assert.Equal("code [main.cs]", tick.DisplayAppName);
    }

    [Fact]
    public void DisplayAppName_NonBrowserNoTitle_IsAppIdOnly()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code", ""),
            Active());

        Assert.Equal("code", tick.DisplayAppName);
    }

    [Fact]
    public void DisplayAppName_Browser_IsBrowserNameParenDomain()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("firefox", "openai/nudge - GitHub - Mozilla Firefox", "f1", "1", FocusSource.KWinScript, false, 1),
            Active());

        Assert.Equal("Firefox (github.com)", tick.DisplayAppName);
    }

    // ── Fullscreen flag ───────────────────────────────────────────────────────

    [Fact]
    public void FullscreenFlag_SetWhenWindowIsFullscreen()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("mpv", "video.mkv", "w1", "1", FocusSource.SwayIpc, true, 1),
            Active());

        Assert.Equal(1, tick.Features.FullscreenFlag);
        Assert.Equal(1, tick.Context.FullscreenFlag);
    }

    // ── IsIdleNow ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsIdleNow_SetWhenIdleExceeds1Second()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code"),
            new IdleObservation(1001, IdleSource.Unknown));

        Assert.Equal(1, tick.Context.IsIdleNow);
    }

    [Fact]
    public void IsIdleNow_NotSetBelowThreshold()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code"),
            new IdleObservation(999, IdleSource.Unknown));

        Assert.Equal(0, tick.Context.IsIdleNow);
    }

    // ── AFK and signal quality ────────────────────────────────────────────────

    [Fact]
    public void AFK_WhenAllObservationsIdle_SignalQualityIsPoor()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        // All observations are idle (>60s idle time)
        for (int i = 0; i < 5; i++)
        {
            tracker.Capture(
                t.AddSeconds(i * 10),
                Win("code", "file.cs"),
                new IdleObservation(61_000, IdleSource.Unknown)); // 61 seconds idle
        }

        var tick = tracker.Capture(
            t.AddSeconds(50),
            Win("code", "file.cs"),
            new IdleObservation(61_000, IdleSource.Unknown));

        // Should have Poor signal quality and AFK flag set
        Assert.Equal(SignalQuality.Poor, tick.Context.SignalQuality);
        Assert.Equal(1, tick.Features.AfkFlag);
    }
}

public sealed class HarvestEngineRobustnessTests
{
    private static WindowObservation Win(string appId, string title = "", string windowId = "w1", string workspaceId = "1") =>
        new(appId, title, windowId, workspaceId, FocusSource.KWinScript, false, 1);

    private static IdleObservation Active() => new(0, IdleSource.Unknown);

    // ── Buffer wrap-around and trim correctness ────────────────────────────────

    [Fact]
    public void WrapBuffer_After300Captures_SharesCorrect()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        for (int i = 0; i < 150; i++)
        {
            tracker.Capture(t.AddSeconds(i), Win("code", "a.cs", "w1"), Active());
            tracker.Capture(t.AddSeconds(i + 0.5), Win("firefox", "b", "w2"), Active());
        }
        var tick = tracker.Capture(t.AddSeconds(300), Win("code", "a.cs", "w1"), Active());

        Assert.Equal(2, tick.Features.DistinctApps300s);
        Assert.InRange(tick.Features.CurrentAppShare300s, 0.49, 0.51);
    }

    [Fact]
    public void TrimOld_AllSamplesBeyond300s_DropsAll()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        for (int i = 0; i < 50; i++)
            tracker.Capture(t.AddSeconds(i), Win($"app{i % 5}", $"t{i}", $"w{i % 3}"), Active());

        var tick = tracker.Capture(t.AddSeconds(500), Win("code", "a.cs"), Active());

        Assert.Equal(1, tick.Features.DistinctApps300s);
        Assert.Equal(1.0, tick.Features.CurrentAppShare300s);
        Assert.Equal(0, tick.Features.SwitchCount300s);
    }

    // ── Anchor return — all edge cases ─────────────────────────────────────────

    [Fact]
    public void ReturnedToAnchor_AllSameApp_Zero()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        for (int i = 0; i < 60; i++)
            tracker.Capture(t.AddSeconds(i), Win("code", "a.cs"), Active());

        var tick = tracker.Capture(t.AddSeconds(61), Win("code", "a.cs"), Active());
        Assert.Equal(0, tick.Features.ReturnedToAnchorApp300s);
    }

    [Fact]
    public void ReturnedToAnchor_FirstAppearance_Zero()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        for (int i = 0; i < 10; i++)
            tracker.Capture(t.AddSeconds(i), Win("code", "a.cs"), Active());

        var tick = tracker.Capture(t.AddSeconds(11), Win("firefox", "site"), Active());
        Assert.Equal(0, tick.Features.ReturnedToAnchorApp300s);
    }

    [Fact]
    public void ReturnedToAnchor_BriefSwitchAndReturn_One()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        for (int i = 0; i < 50; i++)
            tracker.Capture(t.AddSeconds(i), Win("code", "a.cs"), Active());

        tracker.Capture(t.AddSeconds(51), Win("firefox", "lookup"), Active());
        tracker.Capture(t.AddSeconds(52), Win("code", "a.cs"), Active());

        var tick = tracker.Capture(t.AddSeconds(53), Win("code", "a.cs"), Active());
        Assert.Equal(1, tick.Features.ReturnedToAnchorApp300s);
    }

    // ── 60s switch count — edge cases ──────────────────────────────────────────

    [Fact]
    public void SwitchCount60s_OneSample_Zero()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        for (int i = 0; i < 10; i++)
            tracker.Capture(t.AddSeconds(i), Win("code", "a.cs"), Active());

        var tick = tracker.Capture(t.AddSeconds(70), Win("code", "a.cs"), Active());
        Assert.Equal(0, tick.Features.SwitchCount60s);
    }

    [Fact]
    public void SwitchCount60s_NoSamplesInWindow_Zero()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        for (int i = 0; i < 5; i++)
            tracker.Capture(t.AddSeconds(i), Win("code", "a.cs"), Active());

        var tick = tracker.Capture(t.AddSeconds(70), Win("code", "a.cs"), Active());
        Assert.Equal(0, tick.Features.SwitchCount60s);
    }

    [Fact]
    public void SwitchCount60s_ThreeSamples_TwoAlternatingSwitches_CorrectCount()
    {
        // Verifies no spurious first-comparison bug (oldest vs newest).
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        tracker.Capture(t, Win("code", "a.cs", "w1"), Active());
        tracker.Capture(t.AddSeconds(5), Win("slack", "msg", "w2"), Active());
        tracker.Capture(t.AddSeconds(10), Win("code", "a.cs", "w1"), Active());

        var tick = tracker.Capture(t.AddSeconds(15), Win("code", "a.cs", "w1"), Active());
        Assert.Equal(2, tick.Features.SwitchCount60s);
    }

    // ── Signal quality — all paths ─────────────────────────────────────────────

    [Fact]
    public void SignalQuality_EmptyAppId_Poor()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation(null!, "", "w1", "1", FocusSource.KWinScript, false, 1),
            Active());
        Assert.Equal(SignalQuality.Poor, tick.Context.SignalQuality);
    }

    [Fact]
    public void SignalQuality_UnknownAppId_Poor()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("unknown", "", "w1", "1", FocusSource.Unknown, false, 1),
            Active());
        Assert.Equal(SignalQuality.Poor, tick.Context.SignalQuality);
    }

    [Fact]
    public void SignalQuality_CatalogDisagrees_Usable()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("code", "a.cs", "w1", "1", FocusSource.KWinScript, false, 1, CatalogDisagrees: true),
            Active());
        Assert.Equal(SignalQuality.Usable, tick.Context.SignalQuality);
    }

    [Fact]
    public void SignalQuality_BrowserEmptyTitle_Usable()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("firefox", "", "w1", "1", FocusSource.KWinScript, false, 1),
            Active());
        Assert.Equal(SignalQuality.Usable, tick.Context.SignalQuality);
    }

    [Fact]
    public void SignalQuality_AfkOverridesUsable_Poor()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("firefox", "", "w1", "1", FocusSource.KWinScript, false, 1),
            new IdleObservation(IdleMs: 61_000, IdleSource: IdleSource.Unknown));
        Assert.Equal(SignalQuality.Poor, tick.Context.SignalQuality);
    }

    [Fact]
    public void SignalQuality_HeuristicSource_Poor()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("code", "a.cs", "w1", "1", FocusSource.HeuristicProcessScan, false, 1),
            Active());
        Assert.Equal(SignalQuality.Poor, tick.Context.SignalQuality);
    }

    // ── Feature flags — boundaries ─────────────────────────────────────────────

    [Fact]
    public void FullscreenFlag_NotSet_Zero()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code", "a.cs"), Active());
        Assert.Equal(0, tick.Context.FullscreenFlag);
        Assert.Equal(0, tick.Features.FullscreenFlag);
    }

    [Fact]
    public void FullscreenFlag_Set_One()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("code", "a.cs", "w1", "1", FocusSource.KWinScript, true, 1),
            Active());
        Assert.Equal(1, tick.Context.FullscreenFlag);
        Assert.Equal(1, tick.Features.FullscreenFlag);
    }

    [Fact]
    public void MappedToplevelCount_Negative_ClampedZero()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("code", "a.cs", "w1", "1", FocusSource.KWinScript, false, -5),
            Active());
        Assert.Equal(0, tick.Context.MappedToplevelCount);
    }

    [Fact]
    public void IsIdleNow_Exactly1000ms_Set()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code", "a.cs"),
            new IdleObservation(IdleMs: 1000, IdleSource: IdleSource.Unknown));
        Assert.Equal(1, tick.Context.IsIdleNow);
    }

    [Fact]
    public void WorkDomainFlag_InternalTld_Set()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("firefox", "wiki.internal - Mozilla Firefox", "w1", "1", FocusSource.KWinScript, false, 1),
            Active());
        Assert.Equal(1, tick.Features.WorkDomainFlag);
    }

    // ── Empty buffer / first-call paths ────────────────────────────────────────

    [Fact]
    public void FirstCapture_FocusedSinceMs_Zero()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win("code", "a.cs"), Active());
        Assert.Equal(0, tick.Context.FocusedSinceMs);
    }

    [Fact]
    public void SecondCapture_FocusedSinceMs_Positive()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        tracker.Capture(t, Win("code", "a.cs", "w1"), Active());
        var tick = tracker.Capture(t.AddSeconds(5), Win("code", "a.cs", "w1"), Active());
        Assert.True(tick.Context.FocusedSinceMs >= 4000);
    }

    // ── Determinism ────────────────────────────────────────────────────────────

    [Fact]
    public void IdenticalInputs_ProduceSameFeatures()
    {
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        var obs = new WindowObservation("code", "a.cs", "w1", "1", FocusSource.KWinScript, false, 1);
        var idle = Active();

        var t1 = new ActivityFeatureTracker();
        var t2 = new ActivityFeatureTracker();
        for (int i = 0; i < 30; i++)
        {
            t1.Capture(t.AddSeconds(i), obs, idle);
            t2.Capture(t.AddSeconds(i), obs, idle);
        }

        var r1 = t1.Capture(t.AddSeconds(31), obs, idle);
        var r2 = t2.Capture(t.AddSeconds(31), obs, idle);

        Assert.Equal(r1.Features.SwitchCount300s, r2.Features.SwitchCount300s);
        Assert.Equal(r1.Features.DistinctApps300s, r2.Features.DistinctApps300s);
        Assert.Equal(r1.Features.CurrentAppShare300s, r2.Features.CurrentAppShare300s);
        Assert.Equal(r1.Features.ReturnedToAnchorApp300s, r2.Features.ReturnedToAnchorApp300s);
    }

    // ── Continuity — rapid captures, no crash ──────────────────────────────────

    [Fact]
    public void RapidCaptures_NoCrash()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        var apps = new[] { "code", "firefox", "slack", "terminal", "figma" };
        for (int i = 0; i < 1000; i++)
        {
            int idx = i % apps.Length;
            tracker.Capture(t.AddMilliseconds(i * 100),
                new WindowObservation(apps[idx], $"title{idx}", $"w{idx % 3}", $"{idx % 4}", FocusSource.KWinScript, false, 1),
                new IdleObservation(IdleMs: i % 2000, IdleSource: IdleSource.Unknown));
        }
        // If we reach here without exception, the engine didn't crash under load.
    }

    [Fact]
    public void SameMillisecondCaptures_NoCrash()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        for (int i = 0; i < 100; i++)
            tracker.Capture(t, Win("code", $"t{i}", $"w{i % 3}"), Active());
    }

    // ── Distinct apps — "unknown" handling ─────────────────────────────────────

    [Fact]
    public void DistinctApps_AllUnknown_ReturnsZero()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        for (int i = 0; i < 5; i++)
            tracker.Capture(t.AddSeconds(i),
                new WindowObservation("unknown", "", $"w{i}", "1", FocusSource.Unknown, false, 1), Active());

        var tick = tracker.Capture(t.AddSeconds(6),
            new WindowObservation("unknown", "", "w6", "1", FocusSource.Unknown, false, 1), Active());

        Assert.Equal(0, tick.Features.DistinctApps300s);
    }
}