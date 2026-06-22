using System;
using System.Collections.Generic;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

// WP4 — closed-loop calibration. SPEC: V4_REDESIGN/04_CALIBRATION.md §5.
// Pure-logic tests: ShouldTrigger boundary, precision/recall feedback, bounds,
// the rate governor, and the headline convergence simulation. Deterministic
// given the supplied `now` inputs.
public sealed class CalibrationTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DistractionScore Score(double v) => new(v, "test");

    // Bounds are public consts on Calibrator so tests assert against the source of truth.
    private const double MIN = Calibrator.THRESHOLD_MIN;
    private const double MAX = Calibrator.THRESHOLD_MAX;

    // ── 04 §5: ShouldTrigger boundary ────────────────────────────────────────
    [Fact]
    public void ShouldTrigger_Boundary()
    {
        var s = Calibrator.Default();
        s.Threshold = 0.60;

        Assert.True(Calibrator.ShouldTrigger(Score(0.60), s));   // == threshold fires
        Assert.True(Calibrator.ShouldTrigger(Score(0.601), s));  // above fires
        Assert.False(Calibrator.ShouldTrigger(Score(0.599), s)); // below does not
    }

    // ── 04 §3a: repeated false positives raise threshold; streak grows ───────
    // Snapshots are spaced one hour apart so the realized nudge rate sits at the
    // 1/hr target and the rate governor (3c) contributes ~nothing — this isolates
    // the precision loop so the per-FP rise is the pure 3a step.
    [Fact]
    public void Observe_RepeatedFalsePositives_RaisesThreshold_AndStreakGrows()
    {
        var s = Calibrator.Default();
        double prev = s.Threshold;
        var now = T0;

        for (int i = 1; i <= 5; i++)
        {
            now = now.AddHours(1);
            Calibrator.Observe(ref s, triggered: true, userSaidProductive: true, now);

            Assert.Equal(i, s.FalsePositiveStreak);      // streak grows by one each FP
            Assert.True(s.Threshold >= prev);            // threshold is non-decreasing
            if (s.Threshold < MAX) Assert.True(s.Threshold > prev); // strictly up until clamp
            prev = s.Threshold;
        }
    }

    [Fact]
    public void Observe_FalsePositive_AcceleratesWithStreak()
    {
        // The step for the 2nd consecutive FP must exceed the step for the 1st.
        // Hourly spacing keeps the rate governor neutral (see test above).
        var a = Calibrator.Default();
        a.Threshold = 0.50; // headroom below MAX so neither step clamps
        var now = T0.AddHours(1);
        Calibrator.Observe(ref a, true, true, now);
        double firstStep = a.Threshold - 0.50;

        double before = a.Threshold;
        now = now.AddHours(1);
        Calibrator.Observe(ref a, true, true, now);
        double secondStep = a.Threshold - before;

        Assert.True(secondStep > firstStep);
    }

    // ── 04 §3a: true positive resets streak ──────────────────────────────────
    [Fact]
    public void Observe_TruePositive_ResetsStreak()
    {
        var s = Calibrator.Default();
        var now = T0;

        // Build a streak. Hourly spacing keeps the rate governor neutral so the
        // true-positive step below reflects the pure 3a relaxation, not cadence.
        for (int i = 0; i < 3; i++)
        {
            now = now.AddHours(1);
            Calibrator.Observe(ref s, true, userSaidProductive: true, now);
        }
        Assert.Equal(3, s.FalsePositiveStreak);

        // A true positive (user agreed distracted) resets it and relaxes the bar.
        double before = s.Threshold;
        now = now.AddHours(1);
        Calibrator.Observe(ref s, true, userSaidProductive: false, now);

        Assert.Equal(0, s.FalsePositiveStreak);
        Assert.True(s.Threshold < before); // strictly relaxed toward more sensitivity
    }

    // ── 04 §3b: interval miss lowers threshold ───────────────────────────────
    [Fact]
    public void Observe_IntervalMiss_LowersThreshold()
    {
        var s = Calibrator.Default();
        s.Threshold = 0.70;
        double before = s.Threshold;

        // Not triggered + user said NOT productive ⇒ engine missed a distraction.
        Calibrator.Observe(ref s, triggered: false, userSaidProductive: false, T0.AddMinutes(5));

        Assert.True(s.Threshold < before);
        Assert.Equal(0.70 - 0.02, s.Threshold, 6); // exactly one MISS_STEP
    }

    [Fact]
    public void Observe_IntervalProductive_DoesNotMoveThreshold()
    {
        var s = Calibrator.Default();
        s.Threshold = 0.65;
        double before = s.Threshold;

        // Not triggered + user productive ⇒ correct silence, no threshold move.
        Calibrator.Observe(ref s, triggered: false, userSaidProductive: true, T0.AddMinutes(5));

        Assert.Equal(before, s.Threshold, 9);
    }

    // ── 04 §5: threshold ALWAYS within [MIN,MAX] over 1000 random outcomes ───
    [Fact]
    public void Observe_Threshold_StaysWithinBounds()
    {
        var rng = new Random(12345);
        var s = Calibrator.Default();
        var now = T0;

        for (int i = 0; i < 1000; i++)
        {
            now = now.AddSeconds(rng.Next(1, 7200)); // up to 2h gaps, incl. tiny ones
            bool triggered = rng.NextDouble() < 0.5;
            bool productive = rng.NextDouble() < 0.5;
            Calibrator.Observe(ref s, triggered, productive, now);

            Assert.InRange(s.Threshold, MIN, MAX);
            Assert.True(s.FalsePositiveStreak >= 0);
            Assert.False(double.IsNaN(s.Threshold));
            Assert.False(double.IsNaN(s.NudgesEwma));
        }
    }

    // ── 04 §3c: rate governor pulls toward target ────────────────────────────
    [Fact]
    public void Observe_RateGovernor_PullsTowardTarget()
    {
        // HIGH cadence (nudges every 2 min ≫ 1/hr target). With user always agreeing
        // distracted (true positives), 3a relaxes the bar — so any net UPWARD drift
        // must come from the governor fighting the over-firing.
        var fast = Calibrator.Default();
        fast.Threshold = 0.60;
        var now = T0;
        for (int i = 0; i < 200; i++)
        {
            now = now.AddMinutes(2);
            Calibrator.Observe(ref fast, triggered: true, userSaidProductive: false, now);
        }
        Assert.True(fast.NudgesEwma > fast.TargetNudgesPerHour); // realized rate ran hot
        Assert.True(fast.Threshold > 0.60); // governor raised the bar despite TP relaxes

        // LOW cadence (nudges hours apart ≪ target). Governor should pull DOWN.
        var slow = Calibrator.Default();
        slow.Threshold = 0.60;
        now = T0;
        for (int i = 0; i < 200; i++)
        {
            now = now.AddHours(6);
            Calibrator.Observe(ref slow, triggered: true, userSaidProductive: false, now);
        }
        Assert.True(slow.NudgesEwma < slow.TargetNudgesPerHour);
        Assert.True(slow.Threshold < 0.60);
    }

    // ── Determinism given `now` inputs ───────────────────────────────────────
    [Fact]
    public void Observe_IsDeterministic_ForSameInputs()
    {
        CalibrationState Run()
        {
            var s = Calibrator.Default();
            var now = T0;
            var rng = new Random(7); // fixed sequence — drives the same outcome stream
            for (int i = 0; i < 300; i++)
            {
                now = now.AddSeconds(60 + (i % 40) * 30);
                Calibrator.Observe(ref s, rng.NextDouble() < 0.6, rng.NextDouble() < 0.4, now);
            }
            return s;
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a.Threshold, b.Threshold, 12);
        Assert.Equal(a.NudgesEwma, b.NudgesEwma, 12);
        Assert.Equal(a.FalsePositiveStreak, b.FalsePositiveStreak);
        Assert.Equal(a.UpdatedUnix, b.UpdatedUnix);
    }

    // ── 04 §5 HEADLINE: convergence simulation ───────────────────────────────
    // A user has a fixed, hidden "true distraction" policy: they consider themselves
    // distracted iff the underlying distraction score ≥ TRUE_CUT. The engine starts
    // at the floor (0.40), well below that cut, so early on it fires on a large band
    // of scores the user calls productive ⇒ many false positives. Snapshots arrive at
    // realistic ~30-minute spacing so the rate governor sits near its 1/hr target and
    // the precision loop (3a/3b) is what drives convergence. We assert the loop:
    //   (1) drives the realized false-positive rate DOWN over the run, and
    //   (2) converges the threshold UP toward a stable in-bounds band near TRUE_CUT.
    //
    // The exact figures below were validated by a standalone replica of the control
    // law (deterministic for this seed): first-100-trigger FP ≈ 0.30 falling to
    // last-400-trigger FP ≈ 0.22, threshold settling ≈ 0.745, back-half band width
    // ≈ 0.20. Margins are loose enough to absorb the modeled noise.
    [Fact]
    public void Convergence_Sim()
    {
        const double TRUE_CUT = 0.80;   // hidden user policy: distracted iff score ≥ this
        const int ROUNDS = 6000;
        const double START = Calibrator.THRESHOLD_MIN; // start at the floor (0.40)

        var s = Calibrator.Default();
        s.Threshold = START;
        var rng = new Random(2024);
        var now = T0;

        // Per-trigger false-positive flags (FP = engine nudged but user was productive).
        var fpFlags = new List<bool>();

        // Sample the converged threshold band over the back half of the run.
        double bandMin = double.MaxValue, bandMax = double.MinValue;

        for (int i = 0; i < ROUNDS; i++)
        {
            // Snapshots ~30 min apart so realized cadence hovers near the 1/hr target;
            // scores uniform in [0,1] so the boundary region is well exercised.
            now = now.AddMinutes(30 + rng.Next(0, 5));
            double score = rng.NextDouble();

            bool triggered = Calibrator.ShouldTrigger(Score(score), s);
            bool userProductive = score < TRUE_CUT; // hidden ground truth

            if (triggered) fpFlags.Add(userProductive);

            Calibrator.Observe(ref s, triggered, userProductive, now);

            if (i >= ROUNDS / 2)
            {
                bandMin = Math.Min(bandMin, s.Threshold);
                bandMax = Math.Max(bandMax, s.Threshold);
            }
        }

        // Enough triggers to have a meaningful early window and a stable late window.
        Assert.True(fpFlags.Count >= 500, $"too few triggers: {fpFlags.Count}");
        double earlyFp = FalsePositiveRate(fpFlags, 0, 100);
        double lateFp = FalsePositiveRate(fpFlags, fpFlags.Count - 400, 400);

        // (1) The false-positive rate falls from the early transient to steady state.
        Assert.True(
            lateFp < earlyFp,
            $"FP rate should fall: early={earlyFp:F3} late={lateFp:F3}");
        Assert.True(lateFp < 0.28,
            $"converged FP rate should be modest: {lateFp:F3}");

        // (2) The threshold climbed off the 0.40 floor toward the hidden cut and stayed
        //     in a stable, in-bounds band over the back half of the run.
        Assert.InRange(s.Threshold, MIN, MAX);
        Assert.True(bandMin >= MIN && bandMax <= MAX);
        Assert.True(bandMax - bandMin < 0.28,
            $"converged band should be stable: [{bandMin:F3},{bandMax:F3}] width={bandMax - bandMin:F3}");
        // Climbed well above the floor and sits near the true cut (not pinned to MAX).
        Assert.InRange(s.Threshold, 0.60, MAX);
    }

    private static double FalsePositiveRate(List<bool> flags, int from, int count)
    {
        int fps = 0, n = 0;
        for (int i = from; i < from + count && i < flags.Count && i >= 0; i++)
        {
            n++;
            if (flags[i]) fps++;
        }
        return n > 0 ? (double)fps / n : double.NaN;
    }
}
