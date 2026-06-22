# 03 — Decision Fusion + Pluggable Scorer (WP3)

**Goal:** Fuse reputation verdict + focus/drift + sensors into a single `DistractionScore`,
then wrap it in the `DecisionEngine` that applies the calibrated threshold and returns a
`DecisionResult`. This is where every false-positive / false-negative case is reasoned
through explicitly.

**Locus:** pure C# in `NudgeCore.TestableLogic.cs`. The default scorer is a transparent rule;
the `IDistractionScorer` interface is the upgrade seam (00 §8).

**Depends on:** `00_ARCHITECTURE.md` types. Builds against WP1/WP2 *types* — can be written
and tested with hand-constructed `FocusAssessment` / `ReputationVerdict`, before 01/02 land.

---

## 1. `IDistractionScorer` and the default rule

```csharp
internal interface IDistractionScorer
{
    DistractionScore Score(in DecisionInputs inputs);   // Value 0..1 (1 = distracted)
}

internal sealed class RuleBasedScorer : IDistractionScorer { /* below */ }
```

### Fusion logic (the heart)

Order matters: reputation can short-circuit, then sensors, then behavior/drift. Every branch
sets a `Rationale` string for logs + UI.

```
inputs: Reputation (Stance, rates), Focus (FocusScore, DriftZ, DriftElevated, BaselineWarm),
        Behavioral (AudioPlayingFlag, MediaSessionActiveFlag, MicActiveFlag, FullscreenFlag,
                    BrowserWindowFlag, ...)

1. ConfidentProductive reputation
     ⇒ Value = LOW (e.g. 0.10), Rationale "reputation: confidently productive (rate=..)"
     // Slack-during-work, docs/SO, music-while-coding in a trusted app all land here.
     // Hard floor: do not let drift override a confidently-productive app in v1.

2. Passive-media pattern (regardless of reputation unless ConfidentProductive above):
     audio && media && fullscreen
     ⇒ Value = HIGH (e.g. 0.85), Rationale "passive media (audio+media+fullscreen)"
     // YouTube/Netflix fullscreen. Mic excludes (calls handled by meeting gate upstream).
     // Guard: if FocusScore is high AND ConfidentProductive → already returned in (1);
     // a video editor's own labels move youtube to ConfidentProductive over time.

3. ConfidentLowValue reputation
     base = MED_HIGH (e.g. 0.65)
     + behavior:  if DriftElevated → +0.20 ; if FocusScore very low → +0.10
     ⇒ Value = clamp(base + adj), Rationale "low-value app/domain + drift"
     // doomscroll on a known-low domain; gaming app prior.

4. LowEvidence reputation (reputation abstains) → behavior + sensors decide:
     start = 0.40 (neutral-ish)
     - if BaselineWarm && !DriftElevated && FocusScore high → toward LOW (productive-looking)
     - if BaselineWarm && DriftElevated                     → toward HIGH (drifting)
     - if audio && media (not fullscreen)                   → +moderate (likely background video)
     - if !BaselineWarm                                     → stay conservative near neutral,
                                                              lean on sensors only
     ⇒ Value = clamp(...), Rationale describes which signals fired.
```

Constants (`LOW`, `HIGH`, `MED_HIGH`, deltas) are named `const double` near the class and are
the knobs the 08 regression fixtures pin down. Keep the function linear-to-read,
top-to-bottom, no hidden state.

### Why this fixes the reported bugs

| Reported failure | Branch that fixes it |
|---|---|
| Slack/Teams nudged during work | (1) once user labels it productive a few times → ConfidentProductive |
| Reading docs/StackOverflow nudged | (1) shipped high prior + user labels → ConfidentProductive |
| Music while coding nudged | (1) the *app* is productive; audio alone never triggers under ConfidentProductive |
| Doomscroll missed (in browser) | (3)/(4) low/abstain reputation + elevated drift → HIGH |
| YouTube-in-Chrome while "working" missed | (2) passive-media pattern, or (3) if domain low-value |

## 2. `DecisionEngine.Evaluate`

```csharp
internal static class DecisionEngine
{
    public static DecisionResult Evaluate(in DecisionInputs inputs,
                                          IDistractionScorer scorer,
                                          ref CalibrationState calibration)
    {
        DistractionScore s = scorer.Score(inputs);
        bool trigger = Calibrator.ShouldTrigger(s, calibration);      // WP4
        return new DecisionResult(
            Trigger: trigger,
            DistractionValue: s.Value,
            ProductivityScore: 1.0 - s.Value,
            EffectiveThreshold: calibration.Threshold,
            Rationale: s.Rationale);
    }
}
```

- The engine does **not** mutate calibration here (no observation yet — that happens on YES/NO
  in WP5 via `Calibrator.Observe`). It only reads `calibration.Threshold`. `ref` is used for
  consistency / future use; document that `Evaluate` is side-effect-free on the state.
- The engine does **not** know about meeting/AFK gating — that is upstream in `nudge.cs` +
  `SnapshotGate` (00 §6). Assume inputs are already gate-eligible.

## 3. Wiring the default scorer

Provide a single shared instance (stateless): `RuleBasedScorer` has no fields, so a
`static readonly IDistractionScorer Default = new RuleBasedScorer();` is fine, or construct
once in `nudge.cs`. Document the choice; keep it allocation-free per decision.

## 4. Tests (land with this package)

Unit-test the scorer directly with hand-built `DecisionInputs` (no need for 01/02):

- `Score_ConfidentProductive_LowRegardlessOfDrift` (drift elevated but rep productive ⇒ Value low).
- `Score_PassiveMedia_High`.
- `Score_ConfidentLowValue_PlusDrift_High`.
- `Score_LowEvidence_DeepFocus_Low`.
- `Score_LowEvidence_Drift_High`.
- `Score_Warmup_Conservative` (baseline cold ⇒ no behavior-driven HIGH without sensors/reputation).
- `Evaluate_RespectsCalibratedThreshold` (same score, two thresholds ⇒ different Trigger).
- Rationale strings are non-empty and name the deciding signal (asserted loosely).
- The **regression matrix** in 08 is the acceptance gate — every row maps to a branch above.

## 5. Done when

`RuleBasedScorer` + `DecisionEngine` compile, unit tests pass, and the 08 regression matrix
passes end-to-end (with real 01/02 producers). No code outside `NudgeCore.TestableLogic.cs`
and its tests.
