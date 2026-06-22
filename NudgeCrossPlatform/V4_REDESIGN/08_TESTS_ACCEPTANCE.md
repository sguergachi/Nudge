# 08 — Tests + Acceptance (WP8)

**Goal:** Pin the engine's behavior with xunit unit tests and — critically — a **regression
matrix that reproduces the user's actual complaints** and asserts they're fixed. This doc is
the acceptance gate for the whole rewrite.

**Framework:** xunit only (no moq), `net10.0`, project `NudgeCrossPlatform.Tests/`. Pure
logic is `internal static` and reached via `InternalsVisibleTo` (already configured in
`NudgeCore.TestableLogic.cs`). Run: `dotnet test NudgeCrossPlatform.Tests/...` or via
`./build.sh` (always runs tests). Can be authored in parallel with 01–05 against the 00 types.

**Existing patterns to mirror:** `DomainReputationStoreTests.cs`, `NudgeFeatureTrackerTests.cs`,
`DistractionPriorsTests.cs`, `NudgeCsvParsingTests.cs`.

---

## 1. Per-package unit tests (owned by each package, listed here for the full map)

- **01 FocusScoring** → `FocusScoringTests.cs` (see 01 §5).
- **02 ReputationAuthority** → `ReputationAuthorityTests.cs` (see 02 §5).
- **03 Scorer/Engine** → `DistractionScorerTests.cs` (see 03 §4).
- **04 Calibrator** → `CalibrationTests.cs` (see 04 §5), incl. the convergence simulation.
- **06 Persistence** → `ExpStatePersistenceTests.cs` (round-trip, corrupt→default).
- **07** → `NudgeJsonContext` round-trip for any added fields.

## 2. The regression matrix (the headline — `V4RegressionTests.cs`)

Each row builds a `DecisionInputs` (or drives `FocusScoring` + `ReputationAuthority` +
`RuleBasedScorer` end to end) representing a real scenario the user reported, and asserts the
**expected trigger**. Use a fixed default `CalibrationState` (threshold 0.60) unless the row
tests calibration.

| # | Scenario | Reputation | Behavior / sensors | Expect |
|---|----------|-----------|--------------------|--------|
| R1 | Slack during real collaboration (user has labeled Slack productive ×3) | app rate ≈0.9, count≥3 → ConfidentProductive | some switching, mic maybe | **no nudge** |
| R2 | Reading StackOverflow / docs in browser | domain prior ≈0.85 → ConfidentProductive | stable focus | **no nudge** |
| R3 | Music while coding | app=editor ConfidentProductive | audio+media on, fullscreen editor | **no nudge** |
| R4 | Doomscroll Reddit/Twitter in browser | domain prior ≈0.12 → ConfidentLowValue | drift elevated, low focus | **nudge** |
| R5 | YouTube fullscreen | domain low / LowEvidence | audio+media+fullscreen | **nudge** (passive media) |
| R6 | Background music video, working in editor foreground | editor ConfidentProductive | audio+media, editor focused | **no nudge** (rep wins) |
| R7 | Unknown new app, deep focus | LowEvidence (count 0, rate 0.5) | high app-share, long dwell, warm baseline, no drift | **no nudge** |
| R8 | Unknown new app, scattered | LowEvidence | many switches/apps, drift elevated | **nudge** |
| R9 | Quiet distraction: scrolling a low-prior feed that *behaves* like reading | domain ConfidentLowValue | stable focus, no media | **nudge** (reputation carries it) |
| R10 | Cold baseline (first 10 min of use), mild scatter | LowEvidence | not warm | **no nudge** (conservative during warmup) |

For each row: assert `DecisionEngine.Evaluate(...).Trigger` equals expected, and assert the
`Rationale` names the deciding branch. Keep thresholds/constants from 01/03/04 such that all
rows pass — this matrix is what tunes them.

## 3. Personalization-over-time tests

- `PersonalizationTest_SlackFlips`: start with Slack at neutral/low → engine nudges in a
  scattered Slack session; feed 3 YES labels via `DomainReputationStore.Update` → re-run same
  inputs → engine no longer nudges. Proves instant personalization (the core fix).
- `CalibrationTest_FalsePositivesRaiseBar`: simulate repeated "nudge → user productive" and
  assert subsequent borderline scores stop triggering.

## 4. Integration / no-regression

- V3 path untouched: existing tests still pass unchanged.
- `SnapshotGate` still suppresses meeting/AFK/poor before the engine (existing gate tests pass).
- CSV schema unchanged: `NudgeCsvParsingTests` for `HarvestHeadersV4` still pass.

## 5. Acceptance criteria (the bar for "done")

1. `./build.sh` green: both projects build, **zero warnings**, all tests pass.
2. The R1–R10 regression matrix passes.
3. The personalization + calibration time-series tests pass.
4. Manual run (see verify/run + 05 §9, 06 §6, 07 §5): experimental mode makes decisions with
   no Python process / no port 45003 listener; AI Brain tab shows rationale; answering YES/NO
   shifts reputation + calibration state files.

## 6. Done when

All of §5 holds and the regression matrix is committed as the living spec of "what counts as
distraction" for this engine.
