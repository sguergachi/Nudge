# 01 — Behavioral Focus Score + Personal Drift Baseline (WP1)

**Goal:** Produce a `FocusAssessment` each ML check that says how focused the user is *right
now* (focus score) and how far that deviates from *their own* normal (drift z-score). This is
the signal that catches distraction **inside** a high-reputation app — the class the GBM
cannot see.

**Locus:** pure C# in `NudgeCore.TestableLogic.cs`. No new sensors, no Python, no allocations
in the hot path beyond the struct itself. Fully unit-tested.

**Depends on:** `00_ARCHITECTURE.md` types (`FocusAssessment`, `BaselineState`,
`FeatureVectorV4`). Nothing else.

---

## 1. Inputs (all already present on `FeatureVectorV4`)

| Field | Meaning | Direction (higher ⇒) |
|---|---|---|
| `SwitchCount300s` | app/window switches in 5 min | scattered |
| `SwitchCount60s` | switches in 1 min (recency) | scattered |
| `DistinctApps300s` | unique apps in 5 min | scattered |
| `CurrentAppShare300s` | dominance of current app (0..1) | focused |
| `FocusedSinceMs` | continuous time in current window | focused |
| `TitleStabilityMs` | time title unchanged | focused |
| `ReturnedToAnchorApp300s` | bounced back to an anchor app | focused (recovery) |
| `WorkspaceSwitchCount300s` | virtual-desktop hops | scattered |

`AfkFlag` and `Context.SignalQuality == Poor` are handled by the caller/gate, but WP1 must
**not fold AFK or poor-signal ticks into the baseline** (they would pollute "normal").

## 2. Focus score (instantaneous, 0..1)

A bounded, monotonic, hand-auditable blend. Recommended starting form (tune in tests):

```
focus =  w_share   * CurrentAppShare300s
       + w_dwell   * squash(FocusedSinceMs / DWELL_REF_MS)        // e.g. DWELL_REF_MS = 180_000
       + w_title   * squash(TitleStabilityMs / TITLE_REF_MS)      // e.g. TITLE_REF_MS = 120_000
       - w_switch  * squash(SwitchCount300s / SWITCH_REF)         // e.g. SWITCH_REF = 18
       - w_apps    * squash(DistinctApps300s / APPS_REF)          // e.g. APPS_REF = 6
       - w_ws      * squash(WorkspaceSwitchCount300s / WS_REF)
clamp focus to [0,1]
```

- `squash(x) = min(1.0, x)` (or `tanh`/`x/(1+x)` — pick one, document it, test it). Keep it
  cheap and saturating so one extreme signal can't dominate.
- Weights are `const double` named constants near the method. They must sum such that a calm
  deep-work tick scores ≳0.8 and a channel-surfing tick ≲0.3 on the synthetic fixtures in 08.
- `ReturnedToAnchorApp300s` adds a small positive (recovery from a glance-away should not be
  punished as hard as sustained scatter).

> Rationale for hand-tuning over learning: these weights are the *same knowledge* the old
> synthetic generator encoded implicitly, made explicit and testable. A future online scorer
> (00 §8) can relearn them per-user; the rule is the v1.

## 3. Personal baseline (EWMA mean/variance) + drift z-score

The baseline is the user's own focus-score distribution, tracked online with an
exponentially-weighted Welford update so it adapts and needs no history buffer.

```
// update (only on non-AFK, non-poor ticks)
alpha = EWMA_ALPHA                       // e.g. 0.02 → ~50-sample memory; tune
delta = focus - baseline.Mean
baseline.Mean += alpha * delta
baseline.Var   = (1 - alpha) * (baseline.Var + alpha * delta * delta)
baseline.Count += 1
baseline.UpdatedUnix = now

// drift
sd   = sqrt(max(baseline.Var, VAR_FLOOR))   // VAR_FLOOR avoids div-by-zero early
z    = (focus - baseline.Mean) / sd          // negative ⇒ below personal norm ⇒ drifting
warm = baseline.Count >= WARMUP_MIN          // e.g. 30
elevated = warm && z <= DRIFT_Z_TRIGGER      // e.g. -1.0
```

Return `new FocusAssessment(focus, z, elevated, warm)`.

### Decisions to lock in this doc (and test)

- **Update vs assess ordering:** assess against the baseline *before* folding the current
  tick in, so a sudden drop registers as drift rather than being half-absorbed. (Compute `z`
  from the pre-update mean/sd, then update.)
- **Warmup:** while `!warm`, `DriftElevated` is always `false`; the engine (03) must rely on
  reputation/sensors until the baseline matures. Document this clearly — it's expected.
- **Staleness / session gaps:** if `now - UpdatedUnix` exceeds a gap (e.g. machine asleep
  overnight), do **not** treat the first post-gap tick as drift. Either skip the z-eval for
  one tick or widen `sd`. Specify the exact rule and test it.
- **Half-life option:** optionally decay `Count` influence so months-old habits don't ossify
  the baseline. Keep simple for v1 (constant alpha) unless tests show ossification.

## 4. Signature

```csharp
internal static class FocusScoring
{
    // Computes the instantaneous focus score, evaluates drift vs the CURRENT baseline,
    // then folds this tick into the baseline (unless AFK / poor signal, which the caller
    // filters). Mutates `baseline` in place.
    public static FocusAssessment Assess(in FeatureVectorV4 f, ref BaselineState baseline, DateTime now);

    // Exposed for unit tests / 08 fixtures.
    internal static double FocusScore(in FeatureVectorV4 f);
}
```

Caller contract (05): only call `Assess` on ticks where `AfkFlag == 0` and signal quality is
not Poor. If the caller cannot guarantee that, add an early `return` in `Assess` that yields a
neutral `FocusAssessment(FocusScore(f), 0, false, baseline.Count >= WARMUP_MIN)` **without**
updating the baseline. State which approach you took.

## 5. Tests (land with this package; see 08 for the suite)

- `FocusScore_DeepWork_HighScore` — high app-share, long dwell, low switches ⇒ ≥0.8.
- `FocusScore_ChannelSurfing_LowScore` — many switches/apps, low share ⇒ ≤0.3.
- `Baseline_Warmup_NoDriftBeforeWarmupMin`.
- `Drift_SuddenScatterAfterCalmHistory_Elevated` — feed N calm ticks, then a scattered tick
  ⇒ `DriftElevated == true`, `DriftZ` strongly negative.
- `Drift_ConsistentlyScatteredUser_NotAlwaysElevated` — a user whose *normal* is scattered
  should not be permanently flagged (baseline adapts). This is the personalization proof.
- `Baseline_AfkTicksExcluded` — AFK ticks don't move `Mean`.
- `Baseline_SessionGap_NoFalseDrift`.
- Determinism: same inputs ⇒ same outputs (no wall-clock except `now` parameter).

## 6. Done when

`FocusScoring.Assess` compiles, all WP1 tests pass, and on the 08 fixtures a deep-work
sequence yields warm baseline + non-elevated drift while a mid-session doomscroll yields
elevated drift. No changes outside `NudgeCore.TestableLogic.cs` + its test file.
