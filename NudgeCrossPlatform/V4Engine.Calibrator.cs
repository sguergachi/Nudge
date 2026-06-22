using System;

namespace NudgeCore;

// WP4 — Closed-loop calibration.
// SPEC: V4_REDESIGN/04_CALIBRATION.md. Implement against V4Engine.Types.cs.
// Pure logic, fully unit-tested incl. a convergence simulation (CalibrationTests.cs).
internal static class Calibrator
{
    // First-run defaults (04 §1). Used by WP6 persistence and tests.
    public static CalibrationState Default() => new CalibrationState
    {
        Threshold           = 0.60,
        TargetNudgesPerHour = 1.0,
        NudgesEwma          = 1.0,
        FalsePositiveStreak = 0,
        LastNudgeUnix       = 0,
        UpdatedUnix         = 0,
    };

    // ── Bounds (04 §1) ───────────────────────────────────────────────────────
    // Threshold is hard-clamped to this band on every adjustment: never so low it
    // spams, never so high it never fires.
    public const double THRESHOLD_MIN = 0.40;
    public const double THRESHOLD_MAX = 0.90;

    // ── Control-law constants (04 §3) ────────────────────────────────────────
    // All adjustments are ADDITIVE and small so the loop is obviously stable —
    // no multiplicative spirals. Picked conservatively; tune against 08 sims.

    // 3a precision: base step the threshold rises per false positive. The streak
    // term multiplies this by (1 + 0.5*(streak-1)), so a run of FPs accelerates
    // the climb but a single FP only nudges by FP_STEP.
    private const double FP_STEP = 0.02;

    // 3a precision: small relaxation per true positive — gently regains sensitivity
    // when the user agrees they were distracted. Much smaller than FP_STEP so
    // recovery is slow and the loop does not oscillate around a noisy boundary.
    private const double TP_RELAX = 0.005;

    // 3b recall: drop per interval-floor miss (engine didn't trigger but the user
    // said they were NOT productive). Comparable to FP_STEP so misses and false
    // positives have symmetric authority over the threshold.
    private const double MISS_STEP = 0.02;

    // 3c rate governor: EWMA smoothing for realized nudges/hour. Small alpha ⇒ the
    // rate estimate is slow/stable and the governor never jerks the threshold.
    private const double EWMA_ALPHA = 0.20;

    // 3c rate governor: gain applied to the (ewma - target) error. Deliberately
    // tiny so 3a/3b dominate; the governor only bounds slow runaway in cadence.
    private const double GOVERNOR_GAIN = 0.01;

    // Clamp helper for the rate estimate so a long idle gap (huge elapsed hours ⇒
    // ~0 instantaneous rate) or a burst can't produce a wild EWMA.
    private const double NUDGES_PER_HOUR_MAX = 60.0;

    // 04 §2: pure comparison. All adaptation lives in Observe.
    public static bool ShouldTrigger(in DistractionScore score, in CalibrationState s)
        => score.Value >= s.Threshold;

    // 04 §3: closed-loop control law. Additive, bounded, never throws.
    public static void Observe(ref CalibrationState s, bool triggered, bool userSaidProductive, DateTime now)
    {
        long nowUnix = ToUnix(now);

        if (triggered)
        {
            // ── 3c. Rate governor — only engine triggers count as "nudges". ──
            // Update the EWMA of realized nudges/hour from the gap since the last
            // nudge, then apply a mild pull toward the target cadence.
            UpdateRateGovernor(ref s, nowUnix);
            s.LastNudgeUnix = nowUnix;

            // ── 3a. Precision feedback (main loop — attacks false positives). ──
            if (userSaidProductive)
            {
                // FALSE POSITIVE: we nudged, user was productive. Raise the bar,
                // accelerating with the streak.
                s.FalsePositiveStreak += 1;
                s.Threshold += FP_STEP * (1.0 + 0.5 * (s.FalsePositiveStreak - 1));
            }
            else
            {
                // TRUE POSITIVE: we nudged, user agreed they were distracted.
                // Reset the streak and gently allow more sensitivity.
                s.FalsePositiveStreak = 0;
                s.Threshold -= TP_RELAX;
            }
        }
        else if (!userSaidProductive)
        {
            // ── 3b. Recall feedback — interval-floor MISS. ──
            // Engine didn't trigger but the user said they were NOT productive:
            // the bar is too high, lower it.
            s.Threshold -= MISS_STEP;
        }
        // else: interval snapshot where the user was productive — informative but
        // intentionally moves the threshold none (avoids double-counting 3a).

        s.Threshold = Clamp(s.Threshold, THRESHOLD_MIN, THRESHOLD_MAX);
        s.UpdatedUnix = nowUnix;
    }

    // 3c helper: fold this nudge into the nudges/hour EWMA and pull the threshold
    // a little toward keeping that rate near TargetNudgesPerHour.
    private static void UpdateRateGovernor(ref CalibrationState s, long nowUnix)
    {
        if (s.LastNudgeUnix > 0 && nowUnix > s.LastNudgeUnix)
        {
            double hours = (nowUnix - s.LastNudgeUnix) / 3600.0;
            double instantRate = hours > 0 ? 1.0 / hours : NUDGES_PER_HOUR_MAX;
            instantRate = Clamp(instantRate, 0.0, NUDGES_PER_HOUR_MAX);
            s.NudgesEwma = (1.0 - EWMA_ALPHA) * s.NudgesEwma + EWMA_ALPHA * instantRate;
        }

        // Mild proportional governor: too many nudges/hour ⇒ raise threshold a
        // touch; too few ⇒ lower it. Gain kept tiny so 3a/3b dominate.
        double error = s.NudgesEwma - s.TargetNudgesPerHour;
        s.Threshold += GOVERNOR_GAIN * error;
    }

    private static double Clamp(double v, double lo, double hi)
        => v < lo ? lo : (v > hi ? hi : v);

    private static long ToUnix(DateTime now)
    {
        // Treat unspecified kinds as UTC; never throw on odd inputs.
        DateTime utc = now.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(now, DateTimeKind.Utc)
            : now.ToUniversalTime();
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }
}
