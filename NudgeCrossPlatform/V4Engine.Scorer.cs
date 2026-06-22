using System;
using System.Globalization;

namespace NudgeCore;

// WP3 — Distraction fusion scorer + decision engine.
// SPEC: V4_REDESIGN/03_DECISION_FUSION_SCORER.md. Fuses reputation verdict + focus/drift +
// sensors into a DistractionScore, then applies the calibrated threshold. Pure, allocation-
// light, fully unit-tested (DistractionScorerTests.cs) + regression matrix (V4RegressionTests.cs).
internal sealed class RuleBasedScorer : IDistractionScorer
{
    // ── Fusion constants (03 §1). Tuned so the 08 regression matrix passes at the default
    //    calibration threshold (0.60). All values are points on the distraction scale [0,1]. ──
    private const double ConfidentProductiveValue = 0.10; // rep says productive ⇒ never nudge
    private const double PassiveMediaValue        = 0.85; // audio+media+fullscreen video

    private const double LowValueBase   = 0.65; // ConfidentLowValue floor (already ≥ threshold)
    private const double LowValueDrift  = 0.20; // + when behavior also drifts
    private const double LowValueLowFocus = 0.10; // + when focus is very low

    private const double NeutralStart   = 0.40; // LowEvidence starting point
    private const double DriftAdj       = 0.30; // + when warm & drift elevated
    private const double DeepFocusAdj   = 0.20; // − when warm & clearly focused
    private const double BackgroundMediaAdj = 0.25; // + audio+media (non-fullscreen) background video

    private const double VeryLowFocus   = 0.25; // focus below this counts as "very low"
    private const double DeepFocus      = 0.70; // focus above this counts as deep focus

    public DistractionScore Score(in DecisionInputs inputs)
    {
        FeatureVectorV4 b = inputs.Behavioral;
        FocusAssessment focus = inputs.Focus;
        ReputationStance stance = inputs.Reputation.Stance;

        bool audio = b.AudioPlayingFlag != 0;
        bool media = b.MediaSessionActiveFlag != 0;
        bool mic = b.MicActiveFlag != 0;
        bool fullscreen = b.FullscreenFlag != 0;

        // 1. Reputation is authoritative when confidently productive — kills the Slack/docs/
        //    music-while-coding false positives regardless of drift or media.
        if (stance == ReputationStance.ConfidentProductive)
            return new DistractionScore(ConfidentProductiveValue,
                Rationale("reputation: confidently productive", inputs.Reputation));

        // 2. Passive-media pattern: fullscreen audio+media without a mic ⇒ video consumption.
        //    (Mic-active sessions are calls, suppressed upstream by the meeting gate.)
        if (audio && media && fullscreen && !mic)
            return new DistractionScore(PassiveMediaValue, "passive media (audio+media+fullscreen)");

        // 3. ConfidentLowValue app/domain ⇒ start high; behavior raises it further.
        if (stance == ReputationStance.ConfidentLowValue)
        {
            double v = LowValueBase;
            string why = "low-value app/domain";
            if (focus.DriftElevated) { v += LowValueDrift; why += " + focus drift"; }
            if (focus.FocusScore <= VeryLowFocus) { v += LowValueLowFocus; why += " + very low focus"; }
            return new DistractionScore(Clamp01(v), why);
        }

        // 4. LowEvidence ⇒ reputation abstains; behavior + sensors decide.
        double value = NeutralStart;
        string rationale;
        if (!focus.BaselineWarm)
        {
            // Cold baseline: do not trust behavior/drift yet — only sensors may move it.
            if (audio && media) { value += BackgroundMediaAdj; rationale = "warmup: background media"; }
            else rationale = "warmup: insufficient baseline (neutral)";
        }
        else if (focus.DriftElevated)
        {
            value += DriftAdj;
            rationale = "low-evidence app + focus drift from baseline";
            if (focus.FocusScore <= VeryLowFocus) value += LowValueLowFocus;
            if (audio && media && !fullscreen) value += BackgroundMediaAdj;
        }
        else if (focus.FocusScore >= DeepFocus)
        {
            value -= DeepFocusAdj;
            rationale = "low-evidence app but deep focus";
        }
        else
        {
            rationale = "low-evidence app, neutral behavior";
            if (audio && media && !fullscreen) { value += BackgroundMediaAdj; rationale = "low-evidence app + background media"; }
        }

        return new DistractionScore(Clamp01(value), rationale);
    }

    private static string Rationale(string head, in ReputationVerdict r)
        => string.Create(CultureInfo.InvariantCulture,
            $"{head} (domain={r.DomainRate:F2}/{r.DomainCount}, app={r.AppRate:F2}/{r.AppCount})");

    private static double Clamp01(double x) => x <= 0.0 ? 0.0 : (x >= 1.0 ? 1.0 : x);
}

internal static class DecisionEngine
{
    // Stateless scorer — shared, allocation-free per decision.
    public static readonly IDistractionScorer DefaultScorer = new RuleBasedScorer();

    // Runs the scorer and applies the calibrated threshold (03 §2). Side-effect-free on
    // `calibration` — observation happens on YES/NO via Calibrator.Observe (WP5). `ref` is
    // kept for a stable signature should a future scorer need to read more state.
    public static DecisionResult Evaluate(in DecisionInputs inputs,
                                          IDistractionScorer scorer,
                                          ref CalibrationState calibration)
    {
        DistractionScore s = scorer.Score(inputs);
        bool trigger = Calibrator.ShouldTrigger(s, calibration);
        return new DecisionResult(
            Trigger: trigger,
            DistractionValue: s.Value,
            ProductivityScore: 1.0 - s.Value,
            EffectiveThreshold: calibration.Threshold,
            Rationale: s.Rationale);
    }
}
