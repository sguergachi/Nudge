# V4 Decision Engine Rewrite — Documentation Set

This folder specifies the rewrite of Nudge's **Experimental Signal Mode (V4)** decision
path from a synthetic-trained sklearn GBM into a transparent, pure-C#, reputation-
authoritative, drift-aware decision engine with closed-loop calibration.

Everything here is gated behind the existing `--experimental` / `ExperimentalMode` flag.
The V3 (non-experimental) path and the Python pipeline (`model_inference.py`,
`train_model.py`, `background_trainer.py`) are **out of scope and must not change**.

## Read order

| Doc | Package | Owner can start | Depends on |
|-----|---------|-----------------|------------|
| `00_ARCHITECTURE.md` | Spine: diagnosis, data flow, **frozen types/interfaces**, preserved contracts | First, before all others | — |
| `01_FOCUS_DRIFT.md` | Behavioral focus score + personal EWMA drift baseline (pure C#) | After 00 | 00 types |
| `02_REPUTATION_AUTHORITY.md` | `ReputationVerdict` read-side over `DomainReputationStore` (pure C#) | After 00 | 00 types |
| `03_DECISION_FUSION_SCORER.md` | `IDistractionScorer` + `RuleBasedScorer` + `DecisionEngine` (pure C#) | After 00 (stub 01/02) | 00 types, 01/02 interfaces |
| `04_CALIBRATION.md` | Closed-loop `Calibrator` + `CalibrationState` (pure C#) | After 00 | 00 types |
| `05_DAEMON_INTEGRATION.md` | Wire engine into `nudge.cs`/`nudge_build.cs`; drop Python in V4 | After 01–04 land | 01–04 |
| `06_STATE_AND_PROCESS.md` | Persisted state files + tray process lifecycle | After 00 | 00, 04, 01 state shapes |
| `07_UI_IPC.md` | AI Brain tab + IPC surface updates | After 00 | 00 `MLLiveEvent` contract |
| `08_TESTS_ACCEPTANCE.md` | xunit plan + regression scenarios that reproduce the bug | Parallel with 01–05 | 00 types |
| `09_SEED_PRIORS.md` | Curate `distraction_priors.tsv`; decommission V4 GBM seed | Any time | — |

## Parallelization

`00` freezes the contract. After that, **01, 02, 04, 06, 08, 09 are fully independent**
and can be built in parallel by separate agents. `03` builds against the 01/02 *types*
(stub their outputs). `05` integrates and lands after 01–04. `07` needs only the
`MLLiveEvent` contract from `00`.

No two packages edit the same private internals; all new pure-logic types live as separate
declarations in `NudgeCore.TestableLogic.cs`.
