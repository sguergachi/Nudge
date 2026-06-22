# 06 — State Persistence + Process Lifecycle (WP6)

**Goal:** Persist the two new pieces of per-user state (`BaselineState`, `CalibrationState`)
robustly, and stop launching the Python ML processes in experimental mode while still
deploying the priors. Pure plumbing; no decision logic.

**Depends on:** `00_ARCHITECTURE.md` (state shapes), 01 (`BaselineState`), 04
(`CalibrationState`, `Calibrator.Default()`).

---

## 1. New state files (`~/.nudge/`, i.e. `PlatformConfig.DataDirectory`)

| File | Type | Written when |
|---|---|---|
| `exp_baseline.json` | `BaselineState` | after each ML check (or throttled) + on shutdown |
| `exp_calibration.json` | `CalibrationState` | after each YES/NO observation |
| `exp_reputation.json` | (exists) | unchanged |

Mirror the robustness of `DomainReputationStore.Flush()`:

- **Atomic write:** serialize to a temp file in the same dir, then `File.Move(tmp, path,
  overwrite: true)` (atomic rename). Never partially overwrite the live file.
- **Never throw on the harvest path:** wrap IO in try/catch, swallow + (optionally) log to
  stderr; a failed flush retries on the next event. (Matches the existing store's contract.)
- **Missing/corrupt on load ⇒ defaults**, no crash:
  - `BaselineState`: zeroed (`Mean=0, Var=0, Count=0`) → 01 treats as cold/warmup.
  - `CalibrationState`: `Calibrator.Default()` (04 §1 defaults).

## 2. JSON source-gen registration (`NudgeJsonContext.cs`)

The project uses `System.Text.Json` source generation (`NudgeJsonContext`). Add:

```csharp
[JsonSerializable(typeof(BaselineState))]
[JsonSerializable(typeof(CalibrationState))]
```

to the context, and a small DTO if the structs aren't directly serializable (they are plain
fields, so direct should work; if `internal` visibility or struct-vs-class causes issues,
introduce `BaselineDto`/`CalibrationDto` records like `ReputationDto`). Keep field names
stable and snake_case via `[JsonPropertyName]` for consistency with existing files. Add a
schema/version field if you want forward-migration room (optional for v1).

## 3. Load/flush helpers

Place next to the reputation store usage in `nudge.cs`, or as tiny static helpers (mirror
`DomainReputationStore`'s pattern). Suggested:

```csharp
static BaselineState LoadBaseline(string path);        // defaults on miss/corrupt
static void          FlushBaseline(in BaselineState s, string path);   // atomic, no-throw
static CalibrationState LoadCalibration(string path);  // Calibrator.Default() on miss/corrupt
static void          FlushCalibration(in CalibrationState s, string path);
```

Throttle baseline flushing if per-second writes are too chatty (it updates every check ~60s,
so per-check flush is fine; a dirty-flag like the reputation store is optional).

## 4. Reset path

Extend whatever "reset learning" affordance exists (the reputation store has `Clear()` which
deletes `exp_reputation.json`). On reset: also delete `exp_baseline.json` and
`exp_calibration.json` so the user gets a clean personal baseline + default threshold. If a
Settings reset button exists (`SettingsWindow.cs`), wire all three together; if not, document
that reset = delete the three `exp_*.json` files.

## 5. Tray process lifecycle (`nudge-tray.cs`)

Today, experimental mode deploys `model_exp/` (incl. `distraction_priors.tsv`) and the daemon
TCP-queries `model_inference.py` on port 45003; `background_trainer.py` retrains.

Changes for V4:

- **Do not launch `model_inference.py` or `background_trainer.py` when `_experimentalMode`.**
  Find the launch sites (around `DeployBundledModel` / inference-start, `nudge-tray.cs:~1508`,
  and wherever the trainer/inference processes are spawned) and gate them on `!_experimentalMode`.
  In V3 mode they launch exactly as before.
- **Still deploy `distraction_priors.tsv`** to `~/.nudge/model_exp/` (the daemon's
  `DomainReputationStore.LoadPriors` reads it, `nudge.cs:~1846`). Keep that copy step.
- The seed model files (`productivity_model.joblib`, `scaler.json`) are no longer needed in
  V4. You may keep deploying them (harmless) or skip them in experimental mode. Prefer
  skipping to avoid confusion, but do not delete the bundling logic used by V3.
- Confirm no UI element assumes the inference process is alive in V4 (the AI Brain "model
  training" view — 07 handles its relabel).

## 6. Tests

- `Baseline_RoundTrip` (serialize → file → load equals original within tolerance).
- `Baseline_CorruptFile_ReturnsDefault`.
- `Calibration_RoundTrip` and `Calibration_MissingFile_ReturnsDefault`.
- `AtomicWrite_NoPartialFileOnSimulatedFailure` (if feasible — at least assert temp+rename used).
- Process-lifecycle is integration-tested by running the app (08 / verify): assert no
  `model_inference` process and no port 45003 listener in experimental mode.

## 7. Done when

The two state files persist atomically and survive restart; corrupt/missing files yield
defaults without crashing; experimental mode launches **no Python ML process** yet still loads
priors; V3 mode is unchanged. Reset clears all three `exp_*.json`.
