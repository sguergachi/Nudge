# 05 — Daemon Integration (WP5)

**Goal:** Wire the pure-C# engine into the daemon's decision path **for experimental mode
only**, bypassing the Python TCP model, while preserving every IPC/UI/gate contract. The V3
path stays exactly as-is.

**Locus:** `nudge.cs` (then regenerate `nudge_build.cs` via the build script — never
hand-edit the mirror; AGENTS.md). Touches the experimental branches only.

**Depends on:** 01–04 landed (`FocusScoring`, `ReputationAuthority`, `DecisionEngine`,
`RuleBasedScorer`, `Calibrator`) and 06 state plumbing (`BaselineState` / `CalibrationState`
load + flush).

---

## 1. State the daemon holds (experimental only)

Alongside the existing `_reputationStore` (`nudge.cs:~109`, init `~1842`), add:

```csharp
static BaselineState     _baselineState;       // loaded from exp_baseline.json (06)
static CalibrationState  _calibrationState;     // loaded from exp_calibration.json (06)
static readonly IDistractionScorer _scorer = new RuleBasedScorer();
```

Load both at startup next to the reputation store init; flush on the same cadence the
reputation store flushes (after each label, and/or periodically). 06 owns the file IO.

## 2. The decision seam — `ShouldTriggerSnapshot` (`nudge.cs:~2898`)

Today this method: checks ML availability → queries the Python model via TCP
(`QueryMLModel`, `nudge.cs:~2774`) → thresholds. **Split it by mode.**

```
ShouldTriggerSnapshot(app, idle, attention, tick):
    if !_mlEnabled: return true                      // unchanged (interval-driven)

    // keep AFK / poor-signal short-circuit (nudge.cs:~2915-2928) for BOTH modes
    if tick poor-signal or AFK: return false

    if _experimentalMode:
        return EvaluateExperimental(tick)            // NEW — no TCP, no Python
    else:
        // EXISTING V3 path: CheckMLAvailability + QueryMLModel + threshold tree. UNCHANGED.
        ...
```

### `EvaluateExperimental(ActivityTickResult tick)` (new)

```
if tick is not ActivityTickResult t or t.FeaturesV4 is not FeatureVectorV4 fv4:
    return false                                     // no V4 vector ⇒ defer to interval

// reputation values are ALREADY overlaid onto fv4 in the main loop (nudge.cs:~2093)
var rep   = ReputationAuthority.From(fv4);
var focus = FocusScoring.Assess(fv4, ref _baselineState, DateTime.UtcNow);
var inputs = new DecisionInputs(fv4, rep, focus, DateTime.UtcNow);
var result = DecisionEngine.Evaluate(inputs, _scorer, ref _calibrationState);

EmitMlData(result, app: t.Context.FocusedAppId);     // §3 — replaces the GBM MLDATA emit
if (result.Trigger) _lastMLTriggerT = <the emitted MLLiveEvent.T>;

LogDecision(result);                                  // §4
return result.Trigger;
```

Notes:
- `FocusScoring.Assess` updates `_baselineState` by ref every check. Because the AFK/poor
  short-circuit above already returned, every call here is a clean tick (satisfies 01's caller
  contract). Persist `_baselineState` opportunistically (06).
- Do **not** call `QueryMLModel`, `CheckMLAvailability`, or open the TCP socket in experimental
  mode. `_mlAvailable` is irrelevant in V4.

## 3. `MLLiveEvent` emission (preserve the contract — 00 §7)

Build the same `MLLiveEvent` the UI already parses, from `DecisionResult`:

```
score          = result.ProductivityScore          // 1 - DistractionValue
confidence     = abs(result.DistractionValue - 0.5) * 2
productive     = result.DistractionValue < 0.5
triggered      = result.Trigger
trigger_source = result.Trigger ? "ai" : "int"     // "int" when deferring to floor; "sup" set by gate
suppress_reason= null here (gate sets it later)
rationale      = result.Rationale                   // additive field (07)
```

Emit `MLDATA:{json}` exactly as today (`nudge.cs:~2956`). Keep the rolling
`_mlConfidenceScores` average if the UI/menu uses it (map from `confidence`).

## 4. Logs (reworded, same families)

Keep recognizable `ML ...` log lines so existing log-reading/tests still parse:
- Trigger: `ML TRIGGER: distraction={Value:F2} ≥ thr={EffectiveThreshold:F2} — {Rationale}`
- Skip: `ML SKIP: distraction={Value:F2} < thr — {Rationale}`
- (No separate DEFER concept needed; below-threshold is SKIP and the interval floor still
  fires independently as `INTERVAL SNAPSHOT`.)

## 5. YES/NO outcome hooks — `SaveSnapshot` (`nudge.cs:~2476`)

After writing the CSV row (unchanged) and updating reputation (`_reputationStore.Update` +
`Flush`, `nudge.cs:~2597`), add the calibration observation **in experimental mode**:

```
if _experimentalMode and productive.HasValue:
    bool wasAiTrigger = (trigger_source of this snapshot == "ai");   // track on the pending snapshot
    Calibrator.Observe(ref _calibrationState, triggered: wasAiTrigger,
                       userSaidProductive: productive.Value, now: DateTime.UtcNow);
    PersistCalibration(_calibrationState);    // 06
    PersistBaseline(_baselineState);          // 06 (cheap; or flush periodically)
```

To know `wasAiTrigger`, carry the trigger source on the pending-snapshot state (the daemon
already stashes `_snapshotTick` etc. at `nudge.cs:~1745`; add a `_snapshotTriggerSource` or
reuse existing trigger bookkeeping). The interval-vs-ai distinction is exactly what
`Calibrator.Observe` needs for its precision (3a) vs recall (3b) paths.

## 6. Gate + interval (unchanged, verify)

- `SnapshotGate.Evaluate` still runs after the decision before firing (`nudge.cs:~2242`).
  Meeting/AFK/poor/screenshare suppression unchanged; sets `suppress_reason`.
- Interval floor (`nudge.cs:~2162`, random 5–10 min) unchanged; still fires `INTERVAL
  SNAPSHOT` and resets on any fire/suppress. The engine only triggers earlier.

## 7. Things to remove/avoid in V4 (not delete from repo)

- Don't spawn / require `model_inference.py` in experimental mode (tray side: 06).
- Don't write any path that depends on `scaler.json` / `productivity_model.joblib` for V4.
- Keep `HARVEST_EXP.CSV` writing intact (corpus for future scorer).

## 8. Parity + build

- Edit `nudge.cs` only; run `./build.sh` so `nudge_build.cs` is regenerated. Confirm both
  compile, zero warnings.
- If the build script does not auto-regenerate in your environment, regenerate per AGENTS.md
  (strip shebang) — but prefer the script.

## 9. Done when

In experimental mode the daemon makes decisions with **no TCP/Python**, emits unchanged
`MLDATA/MLNEXT/MLRESPONSE/HARVEST`, honors the gate + interval, updates reputation +
calibration + baseline on YES/NO, and `./build.sh` is green (both projects, zero warnings,
tests pass). V3 mode behaves identically to before (diff the V3 branch — it should be
untouched).
