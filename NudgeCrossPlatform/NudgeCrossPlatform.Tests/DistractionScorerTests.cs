using System;
using NudgeCore;
using Xunit;

namespace NudgeCrossPlatform.Tests;

// WP3 unit tests — V4_REDESIGN/03_DECISION_FUSION_SCORER.md §4.
public class DistractionScorerTests
{
    private static readonly IDistractionScorer Scorer = new RuleBasedScorer();

    // Minimal vector: only the fields the scorer reads (sensors + fullscreen). Everything else 0.
    private static FeatureVectorV4 Vec(int audio = 0, int media = 0, int mic = 0, int fullscreen = 0)
        => new FeatureVectorV4(
            HourOfDay: 0, DayOfWeek: 0, FocusedAppHash: 0, FocusedDomainHash: 0,
            IdleMs: 0, FocusedSinceMs: 0, TitleStabilityMs: 0,
            SwitchCount60s: 0, SwitchCount300s: 0, DistinctApps300s: 0, DistinctDomains300s: 0,
            ReturnedToAnchorApp300s: 0, CurrentAppShare300s: 0, CurrentDomainShare300s: 0,
            BrowserWindowFlag: 0, AfkFlag: 0, FullscreenFlag: fullscreen, WorkspaceSwitchCount300s: 0,
            AudioPlayingFlag: audio, MediaSessionActiveFlag: media, MicActiveFlag: mic,
            DomainProductiveRate: 0.5, DomainLabelCount: 0, AppProductiveRate: 0.5, AppLabelCount: 0);

    private static ReputationVerdict Rep(ReputationStance stance)
        => new ReputationVerdict(0.5, 0, 0.5, 0, stance);

    private static FocusAssessment Focus(double score, bool driftElevated, bool warm = true)
        => new FocusAssessment(score, driftElevated ? -2.0 : 0.0, driftElevated, warm);

    private static DecisionInputs In(ReputationStance stance, FocusAssessment focus, FeatureVectorV4 v)
        => new DecisionInputs(v, Rep(stance), focus, DateTime.UtcNow);

    [Fact]
    public void Score_ConfidentProductive_LowRegardlessOfDrift()
    {
        // Rep productive but drift elevated AND fullscreen media → must still be low.
        var s = Scorer.Score(In(ReputationStance.ConfidentProductive,
            Focus(0.1, driftElevated: true), Vec(audio: 1, media: 1, fullscreen: 1)));
        Assert.True(s.Value < 0.5, $"expected low, got {s.Value}");
        Assert.Contains("productive", s.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Score_PassiveMedia_High()
    {
        var s = Scorer.Score(In(ReputationStance.LowEvidence,
            Focus(0.9, driftElevated: false), Vec(audio: 1, media: 1, fullscreen: 1)));
        Assert.True(s.Value >= 0.7, $"expected high, got {s.Value}");
    }

    [Fact]
    public void Score_PassiveMediaWithMic_NotTreatedAsPassiveVideo()
    {
        // Mic active ⇒ a call, not passive video. Should NOT hit the 0.85 passive-media branch.
        var s = Scorer.Score(In(ReputationStance.LowEvidence,
            Focus(0.8, driftElevated: false), Vec(audio: 1, media: 1, mic: 1, fullscreen: 1)));
        Assert.True(s.Value < 0.85);
    }

    [Fact]
    public void Score_ConfidentLowValue_PlusDrift_High()
    {
        var s = Scorer.Score(In(ReputationStance.ConfidentLowValue,
            Focus(0.2, driftElevated: true), Vec()));
        Assert.True(s.Value >= 0.8, $"expected high, got {s.Value}");
    }

    [Fact]
    public void Score_ConfidentLowValue_StableFocus_StillElevatedByReputation()
    {
        // Quiet distraction: behaves like reading, but reputation carries it over threshold.
        var s = Scorer.Score(In(ReputationStance.ConfidentLowValue,
            Focus(0.8, driftElevated: false), Vec()));
        Assert.True(s.Value >= 0.6, $"reputation should carry it, got {s.Value}");
    }

    [Fact]
    public void Score_LowEvidence_DeepFocus_Low()
    {
        var s = Scorer.Score(In(ReputationStance.LowEvidence,
            Focus(0.85, driftElevated: false), Vec()));
        Assert.True(s.Value < 0.5, $"expected low, got {s.Value}");
    }

    [Fact]
    public void Score_LowEvidence_Drift_High()
    {
        var s = Scorer.Score(In(ReputationStance.LowEvidence,
            Focus(0.2, driftElevated: true), Vec()));
        Assert.True(s.Value >= 0.6, $"expected high, got {s.Value}");
    }

    [Fact]
    public void Score_Warmup_Conservative()
    {
        // Cold baseline + scattered behavior but no sensors/reputation ⇒ must not trigger high.
        var s = Scorer.Score(In(ReputationStance.LowEvidence,
            Focus(0.1, driftElevated: false, warm: false), Vec()));
        Assert.True(s.Value < 0.6, $"warmup must stay conservative, got {s.Value}");
    }

    [Fact]
    public void Evaluate_RespectsCalibratedThreshold()
    {
        var inputs = In(ReputationStance.ConfidentLowValue, Focus(0.5, driftElevated: false), Vec());
        var low = new CalibrationState { Threshold = 0.50 };
        var high = new CalibrationState { Threshold = 0.95 };
        Assert.True(DecisionEngine.Evaluate(inputs, Scorer, ref low).Trigger);
        Assert.False(DecisionEngine.Evaluate(inputs, Scorer, ref high).Trigger);
    }

    [Fact]
    public void Evaluate_PopulatesResultFields()
    {
        var inputs = In(ReputationStance.LowEvidence, Focus(0.9, driftElevated: false), Vec());
        var cal = Calibrator.Default();
        var r = DecisionEngine.Evaluate(inputs, Scorer, ref cal);
        Assert.Equal(1.0 - r.DistractionValue, r.ProductivityScore, 6);
        Assert.Equal(cal.Threshold, r.EffectiveThreshold);
        Assert.False(string.IsNullOrEmpty(r.Rationale));
    }

    [Fact]
    public void Score_RationaleAlwaysPopulated()
    {
        foreach (var stance in new[] { ReputationStance.ConfidentProductive,
                                       ReputationStance.ConfidentLowValue, ReputationStance.LowEvidence })
        {
            var s = Scorer.Score(In(stance, Focus(0.5, false), Vec()));
            Assert.False(string.IsNullOrWhiteSpace(s.Rationale));
        }
    }
}
