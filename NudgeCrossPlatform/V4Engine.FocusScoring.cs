using System;

namespace NudgeCore;

// WP1 — Behavioral focus score + personal drift baseline.
// SPEC: V4_REDESIGN/01_FOCUS_DRIFT.md. Implement against V4Engine.Types.cs.
// Pure logic, fully unit-tested (FocusScoringTests.cs). No new sensors, no IO.
internal static class FocusScoring
{
    // ── Focus-score tuning (01 §2). squash(x) = min(1, x): cheap, saturating, monotonic. ──
    private const double DwellRefMs = 180_000.0; // ~3 min continuous dwell → full dwell credit
    private const double TitleRefMs = 120_000.0; // ~2 min stable title → full title credit
    private const double SwitchRef  = 18.0;      // switches/5min that saturate the scatter penalty
    private const double AppsRef    = 6.0;       // distinct apps/5min that saturate the penalty
    private const double WsRef       = 6.0;      // virtual-desktop hops/5min that saturate

    // Positive weights (focus). Sum = 1.0 so an all-positive deep-work tick can reach ~1.0.
    private const double WShare  = 0.45;
    private const double WDwell  = 0.30;
    private const double WTitle  = 0.25;
    // Negative weights (scatter). Subtracted; can pull a surfing tick toward 0.
    private const double WSwitch = 0.45;
    private const double WApps   = 0.30;
    private const double WWs      = 0.15;
    // Small recovery bonus: a bounce back to an anchor app is not sustained scatter.
    private const double WAnchor = 0.05;

    // ── Baseline / drift tuning (01 §3). ──
    private const double EwmaAlpha    = 0.02;   // ~50-sample memory
    private const long   WarmupMin    = 30;     // samples before drift is trustworthy
    private const double DriftZTrigger = -1.0;  // z at/below this (when warm) ⇒ elevated
    private const double VarFloor     = 0.01;   // variance floor → sd ≥ 0.1, avoids early div-by-zero
    private const long   SessionGapSeconds = 1800; // ≥30 min since last update ⇒ treat as fresh session

    /// <summary>
    /// Computes the instantaneous focus score, evaluates drift vs the CURRENT baseline
    /// (pre-update mean/sd), then folds this tick into the baseline. Mutates <paramref name="baseline"/>.
    /// Caller contract (01 §4): only invoked on non-AFK, non-poor-signal ticks. As a defensive
    /// guard we additionally skip the fold for AFK ticks, returning a neutral assessment so an
    /// accidental AFK tick cannot pollute "normal".
    /// </summary>
    public static FocusAssessment Assess(in FeatureVectorV4 f, ref BaselineState baseline, DateTime now)
    {
        double focus = FocusScore(in f);
        bool warm = baseline.Count >= WarmupMin;

        // Defensive: an AFK tick must never move the baseline (01 §1). Return neutral, no fold.
        if (f.AfkFlag != 0)
            return new FocusAssessment(focus, 0.0, false, warm);

        long nowUnix = ToUnixSeconds(now);
        bool sessionGap = baseline.Count > 0
            && baseline.UpdatedUnix > 0
            && (nowUnix - baseline.UpdatedUnix) >= SessionGapSeconds;

        // Drift is assessed against the PRE-update mean/sd (01 §3 ordering decision).
        // After a long gap (machine asleep), suppress the z-eval for one tick: the first
        // post-gap tick is folded into the baseline but never reported as drift.
        double z = 0.0;
        bool elevated = false;
        if (!sessionGap)
        {
            double sd = Math.Sqrt(Math.Max(baseline.Var, VarFloor));
            z = (focus - baseline.Mean) / sd;
            elevated = warm && z <= DriftZTrigger;
        }

        // Fold this tick in (exponentially-weighted Welford).
        double delta = focus - baseline.Mean;
        baseline.Mean += EwmaAlpha * delta;
        baseline.Var = (1.0 - EwmaAlpha) * (baseline.Var + EwmaAlpha * delta * delta);
        baseline.Count += 1;
        baseline.UpdatedUnix = nowUnix;

        return new FocusAssessment(focus, z, elevated, warm);
    }

    /// <summary>
    /// Instantaneous focus score in [0,1] from the behavioral fields (01 §2). A bounded,
    /// monotonic, hand-auditable blend. Exposed for unit tests / 08 fixtures.
    /// </summary>
    internal static double FocusScore(in FeatureVectorV4 f)
    {
        double focus =
              WShare * Clamp01(f.CurrentAppShare300s)
            + WDwell * Squash(f.FocusedSinceMs / DwellRefMs)
            + WTitle * Squash(f.TitleStabilityMs / TitleRefMs)
            - WSwitch * Squash(f.SwitchCount300s / SwitchRef)
            - WApps  * Squash(f.DistinctApps300s / AppsRef)
            - WWs     * Squash(f.WorkspaceSwitchCount300s / WsRef)
            + WAnchor * (f.ReturnedToAnchorApp300s > 0 ? 1.0 : 0.0);

        return Clamp01(focus);
    }

    // squash: cheap, saturating, monotonic; clamps negatives (defensive) to 0.
    private static double Squash(double x) => x <= 0.0 ? 0.0 : (x >= 1.0 ? 1.0 : x);

    private static double Clamp01(double x) => x <= 0.0 ? 0.0 : (x >= 1.0 ? 1.0 : x);

    // Treat Unspecified/Local uniformly as UTC so the gap guard is deterministic and never
    // throws on edge DateTimes (e.g. DateTime.MinValue near the epoch in some zones).
    private static long ToUnixSeconds(DateTime now)
        => new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
