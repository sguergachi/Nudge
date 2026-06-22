# 04 — Closed-Loop Calibration (WP4)

**Goal:** Replace the flat 75% threshold with a per-user trigger threshold that **self-tunes
from YES/NO outcomes** toward a target nudge rate. If the user keeps answering "I was
productive" to nudges, raise the bar; if real distraction is being missed, lower it. This is
the long-term defense against false positives.

**Locus:** pure C# in `NudgeCore.TestableLogic.cs`, state persisted (06). Fully unit-tested.

**Depends on:** `00_ARCHITECTURE.md` types (`CalibrationState`, `DistractionScore`). No
dependency on 01/02/03 internals.

---

## 1. State

```csharp
internal struct CalibrationState
{
    public double Threshold;            // current trigger threshold on distraction Value (0..1)
    public double TargetNudgesPerHour;  // desired cadence, e.g. 1.0
    public double NudgesEwma;           // EWMA of realized nudges/hour
    public int    FalsePositiveStreak;  // consecutive "nudged → user said productive"
    public long   LastNudgeUnix;
    public long   UpdatedUnix;
}
```

Defaults (first run): `Threshold = 0.60`, `TargetNudgesPerHour = 1.0`, `NudgesEwma = target`,
`FalsePositiveStreak = 0`. Document defaults in 06 too. Bounds: `THRESHOLD_MIN = 0.40`,
`THRESHOLD_MAX = 0.90` (never so low it spams, never so high it never fires).

## 2. Trigger decision

```csharp
public static bool ShouldTrigger(in DistractionScore score, in CalibrationState s)
    => score.Value >= s.Threshold;
```

Pure comparison. All adaptation lives in `Observe`.

## 3. Observation / control law

Called on every snapshot outcome (WP5 hooks it into `SaveSnapshot`). Two feedback paths:

```csharp
public static void Observe(ref CalibrationState s, bool triggered, bool userSaidProductive, DateTime now)
```

### 3a. Precision feedback (the main loop — attacks false positives)

Applies when the snapshot was an **engine-triggered nudge** (`triggered == true`, i.e.
`trigger_source == "ai"`):

```
if userSaidProductive:                       // FALSE POSITIVE: we nudged, user was productive
    FalsePositiveStreak += 1
    Threshold += FP_STEP * (1 + 0.5*(FalsePositiveStreak-1))   // additive increase, accelerating with streak
else:                                          // TRUE POSITIVE: we nudged, user agreed distracted
    FalsePositiveStreak = 0
    Threshold -= TP_RELAX * small              // gently allow more sensitivity; bounded
clamp Threshold to [THRESHOLD_MIN, THRESHOLD_MAX]
```

### 3b. Recall feedback (attacks false negatives — uses interval snapshots)

The interval floor still fires snapshots the engine *didn't* trigger (`trigger_source ==
"int"`). If the user answers **NO (not productive)** on an interval snapshot, the engine
missed a distraction → nudge bar is too high:

```
if !triggered and !userSaidProductive:        // MISS: floor caught distraction the engine didn't
    Threshold -= MISS_STEP
    clamp
```

`triggered && userSaidProductive`-on-interval and `productive`-on-interval cases are
informative but should move the threshold little or none (avoid double-counting with 3a).

### 3c. Rate governor (keeps cadence sane)

Maintain `NudgesEwma` (nudges per hour) updated on each engine trigger using elapsed time
since `LastNudgeUnix`. If `NudgesEwma` drifts far above `TargetNudgesPerHour` even without
explicit FPs, apply a mild upward threshold nudge; far below, mild downward. This bounds
runaway in either direction and encodes the "≤1/hour" intent. Keep the gain small so 3a/3b
dominate. Document the exact EWMA alpha and gain; test it.

> Keep all three additive and bounded — no multiplicative spirals. The law must be obviously
> stable from reading it. Tune constants against 08 simulations.

## 4. Reset / first-run

- Missing/corrupt state file ⇒ defaults (06 handles IO). `Calibrator` itself never throws.
- Provide `CalibrationState Default()` factory used by 06 and tests.
- A user "reset learning" action (alongside reputation `Clear()`) deletes the file ⇒ defaults.

## 5. Tests (land with this package)

- `ShouldTrigger_Boundary` (== threshold triggers; below does not).
- `Observe_RepeatedFalsePositives_RaisesThreshold_AndStreakGrows`.
- `Observe_TruePositive_ResetsStreak`.
- `Observe_IntervalMiss_LowersThreshold`.
- `Observe_Threshold_StaysWithinBounds` (drive 1000 random outcomes ⇒ always in [MIN,MAX]).
- `Observe_RateGovernor_PullsTowardTarget` (simulate high/low cadence).
- `Convergence_Sim` — simulate a user with a fixed "true distraction" labeling policy and a
  stream of scores; assert the threshold converges to a stable band and the realized FP rate
  drops over time. This is the headline test proving the loop works.
- Determinism given `now` inputs.

## 6. Done when

`Calibrator` compiles, tests pass, the convergence simulation shows falling false-positive
rate and bounded threshold. State shape matches 00/06. No code outside
`NudgeCore.TestableLogic.cs` + its test file (persistence is 06's job).
