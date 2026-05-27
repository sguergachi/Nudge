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
    public void BrowserDomain_YouTube_AlwaysEntertainmentRegardlessOfDevAnchor()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        for (int i = 0; i < 60; i++)
            tracker.Capture(t.AddSeconds(i), Win("konsole", "~/Dev", "k1"), Active());

        var tick = tracker.Capture(
            t.AddSeconds(61),
            new WindowObservation("firefox", "Cats - YouTube - Mozilla Firefox", "f1", "1", FocusSource.KWinScript, false, 1),
            Active());

        Assert.Equal("youtube.com", tick.Context.FocusedDomain);
        Assert.Equal(AppCategory.Entertainment, tick.AppCategory);
        Assert.Equal(1, tick.Features.EntAppFlag);
        Assert.Equal(0, tick.Features.DevAppFlag);
    }

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

    // ── NEW TESTS FROM THE PLAN ───────────────────────────────────────────────

    // ── Browser anchor fusion ─────────────────────────────────────────────────

    [Fact]
    public void BrowserAnchorFusion_DevBrowserInheritsDevCategory()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        // Establish a dev anchor app (code) with 5 observations
        for (int i = 0; i < 5; i++)
            tracker.Capture(t.AddSeconds(i), Win("code", "file.cs"), Active());

        // Browser observation with unknown domain should inherit dev category via anchor
        // Use a non-desktop appId to avoid desktop file classification interference
        var tick = tracker.Capture(
            t.AddSeconds(6),
            new WindowObservation("my-browser", "Some Page - MyBrowser", "f1", "1", FocusSource.KWinScript, false, 1),
            Active());

        Assert.Equal(AppCategory.Development, tick.AppCategory);
        Assert.Equal(CategoryConfidence.Inferred, tick.AppCategoryConfidence);
        Assert.Equal(string.Empty, tick.Context.FocusedDomain); // No domain detected
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

    // ── Windows app classification ────────────────────────────────────────────

    [Theory]
    [InlineData("winword")]
    [InlineData("excel")]
    [InlineData("powerpnt")]
    [InlineData("onenote")]
    public void WindowsOfficeApps_ClassifiedAsOffice(string processName)
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win(processName, "Document1"),
            Active());

        Assert.Equal(AppCategory.Office, tick.AppCategory);
        Assert.Equal(1, tick.Features.OfficeAppFlag);
    }

    [Theory]
    [InlineData("devenv")]
    [InlineData("idea")]
    [InlineData("pycharm")]
    [InlineData("rider")]
    [InlineData("webstorm")]
    [InlineData("clion")]
    [InlineData("goland")]
    [InlineData("datagrip")]
    [InlineData("eclipse")]
    public void WindowsDevApps_ClassifiedAsDevelopment(string processName)
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win(processName, "Project"),
            Active());

        Assert.Equal(AppCategory.Development, tick.AppCategory);
        Assert.Equal(1, tick.Features.DevAppFlag);
    }

    [Theory]
    [InlineData("skype")]
    [InlineData("whatsapp")]
    [InlineData("msteams")]
    public void WindowsCommunicationApps_CommunicationFlagSet(string processName)
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            Win(processName, "Chat"),
            Active());

        Assert.Equal(1, tick.Features.CommAppFlag);
    }

    [Theory]
    [InlineData("teams.microsoft.com")]
    [InlineData("web.whatsapp.com")]
    [InlineData("web.telegram.org")]
    public void CommunicationDomains_BrowserFocused_ClassifiedAsCommunication(string domain)
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 10, 0, 0),
            new WindowObservation("firefox", $"{domain} - Mozilla Firefox", "f1", "1", FocusSource.KWinScript, false, 1),
            Active());

        Assert.Equal(AppCategory.Communication, tick.AppCategory);
    }
}