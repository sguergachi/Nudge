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
| WP5 Daemon integration (`nudge.cs`) | 05 | lead | ⬜ next |
| WP6 State persistence + tray process gating | 06 | — | ⬜ |
| WP7 UI/IPC | 07 | — | ⬜ |
| WP8 Acceptance build + full test run | 08 | partial | ◐ pure-logic green (653 tests); manual run pending WP5 |
| WP9 Seed priors curation | 09 | — | ⬜ |

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
