# V4 Rewrite — Implementation Progress

Living log. Update when each step completes.

## Conventions
- New engine source files: `V4Engine.*.cs` (namespace `NudgeCore`), registered in **both**
  `nudge.csproj` and `nudge-tray.csproj`. Test files auto-include (test csproj uses default
  compile items) — no csproj edits needed for tests.
- Sub-agents own one source file + one test file (disjoint) and do **not** run builds; the
  lead integrates and builds centrally to avoid `bin/obj` races.

## Status

| Step | Package | Owner | Status |
|------|---------|-------|--------|
| Foundation: shared types + skeletons + csproj wiring | 00 | lead | ✅ done — builds, 0 warnings |
| WP1 FocusScoring + tests | 01 | sub-agent | ✅ done — integrated, tests green |
| WP2 ReputationAuthority + tests | 02 | sub-agent | ✅ done — integrated, tests green |
| WP4 Calibrator + tests | 04 | sub-agent | ✅ done — integrated, tests green |
| WP3 Scorer + DecisionEngine + regression matrix | 03 | lead | ✅ done — R1–R10 + personalization green |
| WP5 Daemon integration (`nudge.cs`) | 05 | lead | ✅ done — V4 decides in-process, no Python; build green |
| WP6 State persistence (`V4State`) | 06 | lead | ✅ done — atomic JSON + tests |
| WP6 Tray process gating (don't launch model_inference in V4) | 06 | lead | ✅ done — no Python in experimental |
| WP7 UI/IPC | 07 | lead | ✅ done — rationale line + Personalization panel; build green |
| WP8 Acceptance build + full test run | 08 | partial | ◐ 683 tests green; live run pending |
| WP9 Seed priors curation | 09 | lead | ✅ done — ambiguous comms omitted, banners added |

**Milestone reached: the entire pure-logic decision engine is implemented and tested.**
653/653 tests pass; `nudge.csproj` + `nudge-tray.csproj` build with 0 warnings. What remains
is wiring (WP5 daemon, WP6 persistence/process, WP7 UI, WP9 priors) — no decision logic left.

## Log
- **Foundation (done):** Added `V4Engine.Types.cs` (frozen contract from 00 §4),
  `V4Engine.FocusScoring.cs`, `V4Engine.Reputation.cs`, `V4Engine.Scorer.cs`,
  `V4Engine.Calibrator.cs` (compiling skeletons). Registered all five in `nudge.csproj` and
  `nudge-tray.csproj`. `dotnet build nudge.csproj` → succeeded, 0 warnings. `Calibrator.Default()`
  implemented (first-run defaults); all other methods throw `NotImplementedException` pending
  their package.
- **WP1/2/4 (done):** Dispatched to three parallel sub-agents (each owned one source file +
  one test file, no builds). Integrated centrally. Notes flagged for WP5:
  - WP1: poor-signal gating is caller-only (`FocusScoring.Assess` can't see `SignalQuality`);
    WP5 must ensure poor-signal ticks never call `Assess`. AFK is guarded inside `Assess`.
  - WP2: "domain present" = `FocusedDomainHash != 0`; confirm the `nudge.cs` overlay leaves the
    hash non-zero whenever a real domain rate is present (it's set in `ComputeFeatures`).
  - WP4: WP5 must call `Calibrator.Observe(triggered = trigger_source=="ai", ...)` and ALSO
    pass interval (`"int"`) snapshots through, or recall feedback (3b) never fires. Timestamps
    must be UTC. `THRESHOLD_MIN/MAX` are `public const` on `Calibrator`.
- **WP3 (done, lead):** `RuleBasedScorer` fusion (03 §1) + `DecisionEngine.Evaluate`. Fusion
  constants tuned so the regression matrix passes at default threshold 0.60. Added
  `DistractionScorerTests.cs` (11 unit tests) and `V4RegressionTests.cs` (R1–R10 +
  `PersonalizationTest_SlackFlips`, which drives the real `DomainReputationStore` to prove a
  scattered Slack session stops nudging after 3 YES labels). Fixed a `ref readonly`-to-property
  compile error (plain copy; not a hot path). Full suite: **653 passed, 0 warnings**.
- **Upstream merge:** merged origin/master (16 commits, v2.1.2–2.1.5). One conflict in
  `AnalyticsWindow.Views.cs` (convergent #176 fix — kept our `GetDataPaths` helper). 676 tests green.
- **WP5 + WP6 persistence (done, lead):** Wired the engine into `nudge.cs`:
  - `ShouldTriggerSnapshot` now branches by mode after the poor/AFK gate; experimental calls the
    new `EvaluateExperimental(app, tick)` which runs `ReputationAuthority.From` +
    `FocusScoring.Assess(ref _baselineState)` + `DecisionEngine.Evaluate(ref _calibrationState)`,
    emits the same `MLLiveEvent` (mapped per 00 §7), and **never touches Python/TCP**. V3 path
    untouched (still `CheckMLAvailability`/`QueryMLModel`).
  - Startup loads `exp_baseline.json` + `exp_calibration.json`; sets `_mlAvailable = true` in V4
    (in-process engine, no server).
  - `SaveSnapshot` now calls `Calibrator.Observe` on each labeled outcome (routing ai vs int via
    `_snapshotWasAiTrigger`, captured at `TakeSnapshot`) and flushes calibration; baseline flushes
    each check. Reputation update path unchanged.
  - New `V4Engine.State.cs` (`V4State` + `BaselineDto`/`CalibrationDto`, registered in
    `NudgeJsonContext`): atomic temp-file+rename writes, defaults on miss/corrupt. Tests:
    `ExpStatePersistenceTests.cs` (6). Full suite: **682 passed**, 0 new warnings (only the
    pre-existing upstream CA1711 in `NudgeHarvestBenchmarks.cs`).
  - `nudge_build.cs` regenerated from `nudge.cs` by `build.sh` (verified in sync).
- **WP6 tray gating (done, lead):** `StartMLServices()` now short-circuits in experimental mode —
  deploys the shipped `distraction_priors.tsv` only, marks `_mlEnabled = true`, and returns
  **before** the Python dependency check / `model_inference.py` / `background_trainer.py` ever
  spawn. `DeployBundledModelExp()` trimmed to priors-only (V4 needs no joblib seed/scaler).
  `TriggerTrainingNow()` guarded to no-op in experimental (defense-in-depth; the Train button is
  also hidden in the UI). V3 launch path unchanged — its now-dead `_experimentalMode` ternaries
  simplified out. The interactive `--schema v4` trainer plumbing remains only on the V3-reachable
  `StartContinuousTrainer` (unreachable in experimental, harmless).
- **WP7 UI/IPC (done, lead):** Additive fields on `MLLiveEvent` — `Rationale`, `Distraction`,
  `Threshold` (registered via existing source-gen; old parsers ignore them). `EvaluateExperimental`
  populates them from `DecisionResult`. AI Brain tab: a **"Why this decision"** line under the score
  card shows the engine rationale + `distraction X vs. threshold Y` (experimental only — null/0 in
  V3 so it's hidden). The "Model Training" accordion is replaced by a **"Personalization"** panel in
  experimental mode (`CreatePersonalizationView`) reading `exp_calibration.json` + `exp_baseline.json`
  via the shared `V4State`: calibrated threshold, target nudges/hr, focus-baseline warm/Count. V3
  training view untouched. Build green, **682 tests pass**, 0 new warnings.
- **WP9 priors curation (done, lead):** Curated at the source (`tools/build_distraction_kb.py`)
  and regenerated `model_exp/distraction_priors.tsv`. Removed the entire **ambiguous communication
  cluster** — webmail (gmail/outlook/proton/…), chat (slack/discord/telegram/whatsapp/signal/…),
  and meetings (zoom/teams/webex/meet/whereby) as both domains and app ids, plus the email-client
  apps (thunderbird/outlook/evolution) and `webmail` from `CATEGORY_PRIORS`. They now fall to the
  neutral 0.5 prior so the user's labels decide — fixing "Slack always flagged" at the source.
  Strong distractors (games/gambling/adult/social/streaming) and producers (dev/office/cloud/edu)
  kept. New count 361 domains / 135 apps (still ≥300/≥100, coverage test unchanged). Added
  `DistractionPriorsTests.ShippedTsv_OmitsAmbiguousCommunicationKeys` to lock the decision in.
  Decommission banners added to `generate_sample_data.py`, `train_model.py`, `background_trainer.py`
  and a V4 note to `PRETRAINED_DISTRACTION_MODEL.md` (V4 runtime is GBM-free; scripts kept for
  V3/future offline scorer). Full suite: **683 tests pass**.
- **Remaining:** a live `--experimental` smoke run on a real session.
