using System;
using NudgeCore;
using Xunit;

namespace NudgeCrossPlatform.Tests;

// WP8 regression matrix — V4_REDESIGN/08_TESTS_ACCEPTANCE.md §2.
// Each row reproduces a scenario the user reported and asserts the expected trigger at the
// default calibration threshold. Reputation runs through the REAL ReputationAuthority.From;
// focus is supplied as a FocusAssessment (WP1's output type) representing the behavior.
public class V4RegressionTests
{
    private const double DefaultThreshold = 0.60; // Calibrator.Default().Threshold

    private static FeatureVectorV4 Vec(
        double domRate = 0.5, int domCount = 0, double appRate = 0.5, int appCount = 0,
        int browser = 0, int domainHash = 0,
        int audio = 0, int media = 0, int mic = 0, int fullscreen = 0)
        => new FeatureVectorV4(
            HourOfDay: 0, DayOfWeek: 0, FocusedAppHash: 0, FocusedDomainHash: domainHash,
            IdleMs: 0, FocusedSinceMs: 0, TitleStabilityMs: 0,
            SwitchCount60s: 0, SwitchCount300s: 0, DistinctApps300s: 0, DistinctDomains300s: 0,
            ReturnedToAnchorApp300s: 0, CurrentAppShare300s: 0, CurrentDomainShare300s: 0,
            BrowserWindowFlag: browser, AfkFlag: 0, FullscreenFlag: fullscreen, WorkspaceSwitchCount300s: 0,
            AudioPlayingFlag: audio, MediaSessionActiveFlag: media, MicActiveFlag: mic,
            DomainProductiveRate: domRate, DomainLabelCount: domCount,
            AppProductiveRate: appRate, AppLabelCount: appCount);

    private static FocusAssessment Focus(double score, bool drift, bool warm = true)
        => new FocusAssessment(score, drift ? -2.0 : 0.0, drift, warm);

    private static bool Triggers(FeatureVectorV4 v, FocusAssessment focus)
    {
        var inputs = new DecisionInputs(v, ReputationAuthority.From(v), focus, DateTime.UtcNow);
        var cal = new CalibrationState { Threshold = DefaultThreshold };
        return DecisionEngine.Evaluate(inputs, DecisionEngine.DefaultScorer, ref cal).Trigger;
    }

    // R1 — Slack during real collaboration (user labeled Slack productive ×3) ⇒ no nudge.
    [Fact]
    public void R1_SlackLabeledProductive_NoNudge()
        => Assert.False(Triggers(Vec(appRate: 0.90, appCount: 3), Focus(0.4, drift: false)));

    // R2 — Reading StackOverflow/docs in browser (strong shipped prior) ⇒ no nudge.
    [Fact]
    public void R2_DocsInBrowser_NoNudge()
        => Assert.False(Triggers(Vec(domRate: 0.85, domCount: 0, browser: 1, domainHash: 111),
                                 Focus(0.8, drift: false)));

    // R3 — Music while coding (editor productive, audio+media+fullscreen editor) ⇒ no nudge.
    [Fact]
    public void R3_MusicWhileCoding_NoNudge()
        => Assert.False(Triggers(Vec(appRate: 0.92, appCount: 5, audio: 1, media: 1, fullscreen: 1),
                                 Focus(0.85, drift: false)));

    // R4 — Doomscroll Reddit/Twitter (low-value domain) + drift ⇒ nudge.
    [Fact]
    public void R4_DoomscrollWithDrift_Nudge()
        => Assert.True(Triggers(Vec(domRate: 0.12, domCount: 0, browser: 1, domainHash: 222),
                                Focus(0.2, drift: true)));

    // R5 — Fullscreen YouTube (passive media), domain low-evidence ⇒ nudge.
    [Fact]
    public void R5_FullscreenYouTube_Nudge()
        => Assert.True(Triggers(Vec(domRate: 0.5, domCount: 0, browser: 1, domainHash: 333,
                                    audio: 1, media: 1, fullscreen: 1),
                                Focus(0.7, drift: false)));

    // R6 — Background music video while editor is foreground (editor productive) ⇒ no nudge.
    [Fact]
    public void R6_BackgroundMediaEditorForeground_NoNudge()
        => Assert.False(Triggers(Vec(appRate: 0.90, appCount: 5, audio: 1, media: 1, fullscreen: 0),
                                 Focus(0.75, drift: false)));

    // R7 — Unknown new app, deep focus, warm baseline, no drift ⇒ no nudge.
    [Fact]
    public void R7_UnknownAppDeepFocus_NoNudge()
        => Assert.False(Triggers(Vec(appRate: 0.5, appCount: 0), Focus(0.85, drift: false, warm: true)));

    // R8 — Unknown new app, scattered, drift elevated ⇒ nudge.
    [Fact]
    public void R8_UnknownAppScattered_Nudge()
        => Assert.True(Triggers(Vec(appRate: 0.5, appCount: 0), Focus(0.2, drift: true, warm: true)));

    // R9 — Quiet distraction: low-prior feed that behaves like reading (no media) ⇒ nudge (reputation carries it).
    [Fact]
    public void R9_QuietDistraction_Nudge()
        => Assert.True(Triggers(Vec(domRate: 0.12, domCount: 0, browser: 1, domainHash: 444),
                                Focus(0.8, drift: false)));

    // R10 — Cold baseline (first minutes), mild scatter, no sensors ⇒ no nudge (conservative warmup).
    [Fact]
    public void R10_ColdBaselineMildScatter_NoNudge()
        => Assert.False(Triggers(Vec(appRate: 0.5, appCount: 0), Focus(0.35, drift: false, warm: false)));

    // ── Personalization over time: a scattered Slack session that initially nudges stops
    //    nudging after the user labels Slack productive a few times (08 §3). Drives the REAL
    //    DomainReputationStore so this proves instant personalization end to end. ──
    [Fact]
    public void PersonalizationTest_SlackFlips()
    {
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "nudge_test_rep_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new DomainReputationStore(tmp,
                new System.Collections.Generic.Dictionary<string, double>(),
                new System.Collections.Generic.Dictionary<string, double>());

            // Before labels: neutral reputation + elevated drift in a scattered Slack session ⇒ nudge.
            var before = Vec(appRate: store.AppRate("slack"), appCount: store.AppCount("slack"));
            Assert.True(Triggers(before, Focus(0.2, drift: true, warm: true)),
                "scattered unknown Slack should nudge before personalization");

            // User answers YES (productive) three times in Slack.
            for (int i = 0; i < 3; i++) store.Update(domain: "", app: "slack", productive: true);

            // After labels: same scattered behavior, but reputation now confidently productive ⇒ no nudge.
            var after = Vec(appRate: store.AppRate("slack"), appCount: store.AppCount("slack"));
            Assert.Equal(ReputationStance.ConfidentProductive, ReputationAuthority.From(after).Stance);
            Assert.False(Triggers(after, Focus(0.2, drift: true, warm: true)),
                "after labeling Slack productive, the same session must not nudge");
        }
        finally { try { System.IO.File.Delete(tmp); } catch { /* best effort */ } }
    }
}
