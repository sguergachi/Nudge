using System;

namespace NudgeCore;

// ─────────────────────────────────────────────────────────────────────────────
// V4 decision-engine shared types (frozen contract — see V4_REDESIGN/00_ARCHITECTURE.md §4).
// These decouple the work packages: producers (FocusScoring, ReputationAuthority,
// RuleBasedScorer, Calibrator) implement against these and may be built independently.
// Do not change a type here without updating 00_ARCHITECTURE.md and all consumers.
// ─────────────────────────────────────────────────────────────────────────────

// ── WP1 (FocusScoring) ───────────────────────────────────────────────────────
/// <summary>
/// Result of assessing the current tick's focus against the user's personal baseline.
/// <para>FocusScore: 0 = fully scattered, 1 = deep focus (higher = better).</para>
/// <para>DriftZ: signed z-score of the current focus score vs the personal baseline.
/// Negative = below personal norm (drifting).</para>
/// <para>DriftElevated: true when drift is below the trigger threshold AND the baseline is warm.</para>
/// <para>BaselineWarm: false during warmup — drift is not yet trustworthy.</para>
/// </summary>
internal readonly record struct FocusAssessment(
    double FocusScore,
    double DriftZ,
    bool DriftElevated,
    bool BaselineWarm);

/// <summary>
/// Persisted personal baseline: EWMA mean/variance of the focus score. Mutated by ref each
/// tick by <see cref="FocusScoring.Assess"/>. Persisted to ~/.nudge/exp_baseline.json (WP6).
/// </summary>
internal struct BaselineState
{
    public double Mean;        // EWMA mean of focus score
    public double Var;         // EWMA variance of focus score
    public long   Count;       // samples folded in (warmup gate)
    public long   UpdatedUnix; // last update (epoch seconds) — staleness / session-gap guard
}

// ── WP2 (ReputationAuthority) ────────────────────────────────────────────────
internal enum ReputationStance
{
    ConfidentProductive,
    ConfidentLowValue,
    LowEvidence,
}

internal readonly record struct ReputationVerdict(
    double DomainRate,
    int DomainCount,
    double AppRate,
    int AppCount,
    ReputationStance Stance);

// ── WP3 (RuleBasedScorer / DecisionEngine) ───────────────────────────────────
internal readonly record struct DecisionInputs(
    FeatureVectorV4 Behavioral,   // reputation + sensor fields already populated by the daemon
    ReputationVerdict Reputation,
    FocusAssessment Focus,
    DateTime Now);

/// <summary>Distraction score in [0,1] (1 = clearly distracted) plus a human-readable rationale.</summary>
internal readonly record struct DistractionScore(double Value, string Rationale);

internal interface IDistractionScorer
{
    DistractionScore Score(in DecisionInputs inputs);
}

/// <summary>Final engine output. Carries everything <c>MLLiveEvent</c> needs (00 §7).</summary>
internal readonly record struct DecisionResult(
    bool Trigger,
    double DistractionValue,   // 0..1
    double ProductivityScore,  // 1 - DistractionValue
    double EffectiveThreshold, // calibrated threshold at decision time
    string Rationale);

// ── WP4 (Calibrator) ─────────────────────────────────────────────────────────
/// <summary>
/// Persisted closed-loop calibration state. Persisted to ~/.nudge/exp_calibration.json (WP6).
/// </summary>
internal struct CalibrationState
{
    public double Threshold;            // current distraction-score trigger threshold (0..1)
    public double TargetNudgesPerHour;  // desired cadence
    public double NudgesEwma;           // EWMA of realized nudges/hour
    public int    FalsePositiveStreak;  // consecutive "nudged → user said productive"
    public long   LastNudgeUnix;        // epoch seconds of last engine-triggered nudge
    public long   UpdatedUnix;          // epoch seconds of last observation
}
