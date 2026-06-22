# 00 — Architecture (the spine)

**Status:** authoritative. Every other doc (01–09) depends on the types and contracts
frozen here. Change this doc first if a contract must move; do not let packages drift.

---

## 1. Why we are doing this

Experimental Signal Mode (V4) nudges the user while they are working (false positives) and
misses real distraction (false negatives). Root causes, confirmed in code:

1. **The V4 GBM imitates a synthetic oracle.** `generate_sample_data.py` labels rows with a
   hand-written `effective_bias` formula; the model's ceiling *is* that formula. Each false
   positive is a case where the formula disagrees with the user's reality, untunable.
2. **User labels are nearly powerless.** The personalized signal (reputation rate) is 1 of 25
   features in a GBM dominated by ~500 synthetic seed rows, retrained only every 20 labels.
3. **Hash features are noise** (`focused_app_hash`, `focused_domain_hash` as floats).
4. **Uncalibrated probability + flat 75% threshold**, no feedback from YES/NO.
5. **No personal baseline** — cannot detect drift *inside* a high-reputation app.

## 2. The new design in one paragraph

In experimental mode, the daemon stops calling the Python model. Instead a **pure C#
`DecisionEngine`** fuses three per-user signals — **reputation** (`DomainReputationStore`,
kept and made authoritative), a **personal behavioral-drift baseline** (new EWMA z-score),
and **sensors** (audio/media/mic/fullscreen) — into a single **distraction score** in
`[0,1]`. A **closed-loop `Calibrator`** turns that score into a trigger decision against a
threshold that self-tunes to the user's tolerance using YES/NO outcomes. The whole thing is
deterministic, unit-testable, and microseconds in-process. The scorer sits behind an
interface (`IDistractionScorer`) so a learned/nonlinear scorer can replace the default rule
later with no other code change. `HARVEST_EXP.CSV` keeps logging the full labeled corpus.

## 3. End-to-end data flow

```
every harvest tick (~1s):
  ActivityFeatureTracker.Capture(...) ──► ActivityTickResult { Context, Features, FeaturesV4 }
        (FeaturesV4 = behavioral signals; reputation/sensor fields still default here)

  nudge.cs overlay (experimental, ~nudge.cs:2093):
        FeaturesV4 { DomainProductiveRate, DomainLabelCount, AppProductiveRate, AppLabelCount }
        ◄── _reputationStore.DomainRate/AppRate/Counts

  every ML check (~60s), if not _waitingForResponse and not meeting-suppressed:
        FocusScoring.Assess(FeaturesV4, ref BaselineState, now) ──► FocusAssessment
        ReputationVerdict.From(FeaturesV4 / store)              ──► ReputationVerdict
        DecisionInputs { Behavioral, Reputation, Focus, Now }
        DecisionEngine.Evaluate(inputs, ref CalibrationState)   ──► DecisionResult
                                                                     │
        emit MLDATA:{MLLiveEvent from DecisionResult}  ◄────────────┘
        if DecisionResult.Trigger: SnapshotGate.Evaluate(...) then snapshot / SUPPRESS
        else: defer to interval floor (unchanged)

  user answers YES/NO/SKIP (UDP 45001) ──► SaveSnapshot:
        write row to HARVEST_EXP.CSV (label column)
        _reputationStore.Update(domain, app, productive)        (exists)
        Calibrator.Observe(ref CalibrationState, triggered, userProductive, now)   (new)
        FocusScoring baseline already updated each tick (persisted on flush)
```

Meeting/AFK/poor-signal suppression (`SnapshotGate`) and the random 5–10 min interval floor
are unchanged and still wrap the decision. ML only ever triggers *earlier* than the floor.

## 4. Frozen shared types (declare in `NudgeCore.TestableLogic.cs`, namespace `NudgeCore`)

These are the decoupling contract. Field names/types are normative; packages implement
against them and may stub the producers.

```csharp
// ── WP1 output (01_FOCUS_DRIFT) ───────────────────────────────────────────────
// FocusScore: 0 = fully scattered, 1 = deep focus (higher = better).
// DriftZ: signed z-score of current focus score vs the user's own baseline.
//         Negative = below personal norm (drifting). DriftElevated = below threshold.
internal readonly record struct FocusAssessment(
    double FocusScore,
    double DriftZ,
    bool DriftElevated,
    bool BaselineWarm);   // false during warmup → drift not yet trustworthy

// Persisted personal baseline (EWMA mean/variance of FocusScore). See 06.
internal struct BaselineState   // mutable struct, updated by ref each tick
{
    public double Mean;        // EWMA mean of focus score
    public double Var;         // EWMA variance
    public long   Count;       // samples folded in (warmup gate)
    public long   UpdatedUnix; // last update, for staleness/half-life
}

// ── WP2 output (02_REPUTATION_AUTHORITY) ──────────────────────────────────────
internal enum ReputationStance { ConfidentProductive, ConfidentLowValue, LowEvidence }

internal readonly record struct ReputationVerdict(
    double DomainRate, int DomainCount,
    double AppRate,    int AppCount,
    ReputationStance Stance);

// ── WP3 (03_DECISION_FUSION_SCORER) ───────────────────────────────────────────
internal readonly record struct DecisionInputs(
    FeatureVectorV4 Behavioral,     // existing struct, reputation+sensor fields populated
    ReputationVerdict Reputation,
    FocusAssessment Focus,
    DateTime Now);

// Value: 0 = clearly productive, 1 = clearly distracted. Rationale is human-readable
// (shown in logs + AI Brain tab; also fed to MLLiveEvent).
internal readonly record struct DistractionScore(double Value, string Rationale);

internal interface IDistractionScorer
{
    DistractionScore Score(in DecisionInputs inputs);
}

// ── WP4 (04_CALIBRATION) ──────────────────────────────────────────────────────
internal struct CalibrationState   // persisted; see 06
{
    public double Threshold;          // current distraction-score trigger threshold (0..1)
    public double TargetNudgesPerHour;
    public double NudgesEwma;         // EWMA of realized nudge rate (per hour)
    public int    FalsePositiveStreak;// consecutive "nudged but user said productive"
    public long   LastNudgeUnix;
    public long   UpdatedUnix;
}

// ── Engine output (consumed by 05/07) ─────────────────────────────────────────
internal readonly record struct DecisionResult(
    bool Trigger,
    double DistractionValue,     // scorer Value, 0..1
    double ProductivityScore,    // 1 - DistractionValue, for the existing UI "score"
    double EffectiveThreshold,   // calibrated threshold at decision time
    string Rationale);
```

### Producer entry points (signatures other packages call)

```csharp
// 01
internal static class FocusScoring
{
    public static FocusAssessment Assess(in FeatureVectorV4 f, ref BaselineState baseline, DateTime now);
}

// 02 (read-side helper over the existing store; pure given the four overlaid values)
internal static class ReputationAuthority
{
    public static ReputationVerdict From(in FeatureVectorV4 f);   // uses *Rate/*Count already overlaid
}

// 03
internal sealed class RuleBasedScorer : IDistractionScorer { /* ... */ }

internal static class DecisionEngine
{
    public static DecisionResult Evaluate(in DecisionInputs inputs,
                                          IDistractionScorer scorer,
                                          ref CalibrationState calibration);
}

// 04
internal static class Calibrator
{
    public static bool ShouldTrigger(in DistractionScore score, in CalibrationState s);
    public static void Observe(ref CalibrationState s, bool triggered, bool userSaidProductive, DateTime now);
}
```

`DecisionEngine.Evaluate` is the single seam `nudge.cs` calls (WP5). It runs the scorer,
asks `Calibrator.ShouldTrigger`, and packages a `DecisionResult`.

## 5. `FeatureVectorV4` (existing — fields the engine reads)

Already defined in `NudgeCore.TestableLogic.cs`. The engine consumes these; **do not add
the hash fields to any scoring path** (they remain in the CSV only for corpus logging):

- Behavioral: `SwitchCount60s`, `SwitchCount300s`, `DistinctApps300s`, `DistinctDomains300s`,
  `CurrentAppShare300s`, `CurrentDomainShare300s`, `ReturnedToAnchorApp300s`,
  `FocusedSinceMs`, `TitleStabilityMs`, `WorkspaceSwitchCount300s`, `BrowserWindowFlag`,
  `AfkFlag`, `FullscreenFlag`.
- Sensors: `AudioPlayingFlag`, `MediaSessionActiveFlag`, `MicActiveFlag`.
- Reputation (overlaid in `nudge.cs` before the decision): `DomainProductiveRate`,
  `DomainLabelCount`, `AppProductiveRate`, `AppLabelCount`.
- Ignored by scoring: `FocusedAppHash`, `FocusedDomainHash`, `HourOfDay`, `DayOfWeek`
  (kept in CSV; not scored — time-of-day correlations were a source of bias).

## 6. Contracts that MUST be preserved (do not break)

- **IPC stdout prefixes** read by the tray/AI Brain tab: `MLDATA:` (`MLLiveEvent`),
  `MLNEXT:`, `MLRESPONSE:`, `HARVEST:`, `SUPPRESS:`. Keep emitting them in V4.
- **`MLLiveEvent` fields** (`NudgeJsonContext.cs`): `t, app, score, confidence, productive,
  triggered, user_response, ai_correct, trigger_source, suppress_reason`. Map the new engine
  onto them (see §7). New fields are additive only.
- **`SnapshotGate.Evaluate`** (`NudgeCore.TestableLogic.cs`) ordering PoorSignal → Afk →
  ScreenSharing → InMeeting runs *in front of* the decision; meeting suppression trumps the engine.
- **Interval floor** (random 5–10 min) remains a guaranteed floor; the engine only fires earlier.
- **`_lastMLTriggerT`** correlation for YES/NO ✓/✗ in Recent Checks stays as-is.
- **`nudge.cs` ↔ `nudge_build.cs` parity.** `nudge_build.cs` is generated from `nudge.cs` by
  the build script (shebang stripped). Edit `nudge.cs`; the build regenerates the mirror.
  Never hand-edit `nudge_build.cs`.

## 7. Mapping `DecisionResult` → `MLLiveEvent`

| MLLiveEvent field | V4 source |
|---|---|
| `score` | `DecisionResult.ProductivityScore` (`1 - DistractionValue`) |
| `confidence` | `abs(DistractionValue - 0.5) * 2` (keeps UI color/size semantics) |
| `productive` | `DistractionValue < 0.5` |
| `triggered` | `DecisionResult.Trigger` |
| `trigger_source` | `"ai"` when engine triggers, `"int"` for interval floor, `"sup"` if gated |
| `suppress_reason` | from `SnapshotGate` as today |
| (additive, optional) `rationale` | `DecisionResult.Rationale` for the tab (see 07) |

## 8. Extensibility (the pluggable scorer)

`IDistractionScorer` is the upgrade seam. Default is `RuleBasedScorer` (03). Future options,
none requiring changes outside the scorer:

1. **Online logistic / SGD in C#** over the same `DecisionInputs`, trained on the YES/NO
   stream — learns feature interactions per-user, online, still no Python.
2. **ONNX Runtime model in C#** loaded from a file trained offline on `HARVEST_EXP.CSV`.
3. **Re-enable the GBM** purely as a middle-band tie-breaker, now trained on a *real* corpus.

Because the corpus keeps being logged unchanged, any of these is reachable later. Today we
ship the rule.

## 9. New files / state (summary; details in 06)

- `~/.nudge/exp_baseline.json` — `BaselineState`.
- `~/.nudge/exp_calibration.json` — `CalibrationState`.
- `~/.nudge/exp_reputation.json` — exists, unchanged.
- `model_exp/distraction_priors.tsv` — exists; curated in 09. **The only V4 seed.**
- `HARVEST_EXP.CSV` — unchanged schema (`FeatureSchemaV4.HarvestHeadersV4`); still written.

## 10. Out of scope (do not touch)

V3 decision path; `model_inference.py`; `train_model.py`; `background_trainer.py`;
`generate_sample_data.py` (kept for V3 / future corpus model); the custom notification
window; the tray menu. Python is removed only from the V4 **runtime decision**, not the repo.
