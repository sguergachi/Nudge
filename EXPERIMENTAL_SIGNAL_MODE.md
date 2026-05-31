# Experimental Signal Mode — Implementation Plan

> **Status:** Design / handoff doc. No code written yet. This document is the spec for the
> implementing agent. Author: planning pass on branch `claude/pretrained-model-categories-BkSDb`.

## Context & motivation

Nudge currently decides "are you productive?" partly from a brittle, hand-maintained **website
category index** (`EntertainmentDomains`, `WorkDomains`, etc. in `NudgeCore.TestableLogic.cs:470-486`)
plus app-name keyword rules. GitHub issue **#125** shows the failure mode directly: a browser tab with
an unrecognized domain (`"domain":""`) gets classified `"category":"Development","category_conf":0.75`
because the unknown-domain path falls through to running **app-name keyword rules against the web page
title** (`Classify()` → `TryClassifyFromTokens()`, `NudgeCore.TestableLogic.cs:343-349, 921-922`) and
to browser-anchor inheritance (`:925-926`). A 37-domain index cannot scale to the whole web.

**Goal:** add an opt-in **Experimental Signal Mode** (Settings toggle) that replaces the
category-index approach with a **pure signal-based** model. It fully leverages the harvest engine's
behavioral signals (Solution 1), adds new OS-level media/presence signals (Solution 4), and learns a
**personalized per-domain/app productivity reputation** from the user's own YES/NO answers
(Solution 5). No hardcoded category buckets.

**Hard requirement — two parallel, isolated pipelines.** When the mode is ON, Nudge uses a new V4
schema, a separate harvest log, and a separate model. When OFF, it reverts to the existing V3
schema / log / model with **zero data loss on either side**. The two never share files. Toggling
just switches which pipeline is live.

The harvest loop is tuned for speed (100 ms tick, 2 s emit, see `AGENTS.md` "performance through
simplicity"). We have headroom to compute the extra V4 signals, but new OS calls (audio/media polling)
must be **cached/throttled** off the 100 ms hot path — see §5.

---

## 1. High-level architecture: dual pipelines

```
                         ┌───────────────────────── Experimental OFF (default) ─────────────────────────┐
  Settings toggle ──────▶│ schema V3  │ ~/.nudge/HARVEST.CSV    │ ~/.nudge/model/     │ inference :45002 │
                         └──────────────────────────────────────────────────────────────────────────────┘
                         ┌───────────────────────── Experimental ON ───────────────────────────────────┐
                         │ schema V4  │ ~/.nudge/HARVEST_EXP.CSV│ ~/.nudge/model_exp/ │ inference :45003 │
                         │            │ + ~/.nudge/exp_reputation.json (per-domain/app learning store)   │
                         └──────────────────────────────────────────────────────────────────────────────┘
```

Only **one** pipeline is live at a time (the active mode's). We do **not** run both inference servers
/ trainers simultaneously — it wastes CPU and the inactive mode's CSV is intentionally frozen. On
toggle, the tray restarts the daemon and swaps the Python sidecars (§9, §10). Each side keeps its own
`HARVEST*.CSV`, `model*/`, and metadata, so flipping back resumes exactly where it left off.

**Why this is low-risk:** the V3 path is untouched at runtime when the flag is off. All V4 behavior is
gated behind one boolean that flows Settings → `tray-settings.json` → `--experimental` CLI flag →
`ParseNudgeArgs` → daemon. This mirrors the existing `--ml` / `--force-model` plumbing exactly.

---

## 2. The toggle: Settings → daemon (Solution wiring)

Follow the **existing** flag pattern (`--ml`, `--force-model`) end to end. No new mechanisms.

| Step | File | Change |
|---|---|---|
| Persisted setting | `NudgeJsonContext.cs` (`TraySettings`, ~`:102`) | Add `public bool ExperimentalSignalMode { get; set; }` |
| Tray state | `nudge-tray.cs` (static fields region) | Add `static bool _experimentalMode;` + load/save in `LoadSettings()`/`SaveSettings()` (~`:2314-2351`) |
| Launch arg | `nudge-tray.cs` `StartNudge()` (~`:1823`) | `if (_experimentalMode) args += " --experimental";` |
| Arg struct | `NudgeCore.TestableLogic.cs` `NudgeParsedArgs` (`:27`) | Add `public bool ExperimentalMode { get; init; }` |
| Arg parse | `NudgeCore.TestableLogic.cs` `ParseNudgeArgs` (`:1256`) | `if (arg == "--experimental") { experimentalMode = true; continue; }` |
| Daemon read | `nudge.cs` `Main` (~`:1454`) | `_experimentalMode = parsed.ExperimentalMode;` |
| UI control | `SettingsWindow.cs` | New `CheckBox`/toggle card "Experimental: signal-based detection (beta)". On change → `Program.UpdateSettings(experimental: value)` |
| Apply | `nudge-tray.cs` `UpdateSettings()` (~`:2367`) | Accept `bool? experimental`; on change, `SaveSettings()` + restart **and** swap Python sidecars (§9) |

**Settings UX:** the existing `SettingsWindow` is all sliders/buttons — there is no checkbox yet, so
add one small toggle card. Include a one-line warning that switching modes starts a **fresh learning
history** for that mode (different model) and that interval-fallback nudges apply until the
experimental model has enough samples.

**Add a `ParseNudgeArgs` unit test** for the new flag (xunit, `NudgeCrossPlatform.Tests/`) — pure logic,
mandated by `AGENTS.md`.

---

## 3. V4 feature schema (the "pure signal" set)

The V4 schema **drops the 7 hardcoded category flags** that cause #125 and **adds OS + personalization
signals**. Identity hashes are kept so the model can still learn per-app/domain patterns implicitly.

Define a parallel schema alongside the existing one — do **not** mutate `FeatureSchema`. Add a sibling,
e.g. `FeatureSchemaV4` in `NudgeCore.TestableLogic.cs`, with its own `SchemaVersion = 4`,
`OrderedFeatureNames`, and `ToFeatureDictionary(FeatureVectorV4)`. The daemon selects which schema to
use from `_experimentalMode`.

### V4 `OrderedFeatureNames`

**Reused behavioral/temporal/identity (already computed in `ComputeFeatures`):**
```
hour_of_day, day_of_week, focused_app_hash, focused_domain_hash,
idle_ms, focused_since_ms, title_stability_ms,
switch_count_60s, switch_count_300s, distinct_apps_300s, distinct_domains_300s,
returned_to_anchor_app_300s, current_app_share_300s, current_domain_share_300s,
browser_window_flag, afk_flag, fullscreen_flag, workspace_switch_count_300s
```

**New OS signals (Solution 4) — see §5:**
```
audio_playing_flag          # media render stream active (speaker output), throttled poll
media_session_active_flag    # SMTC (Win) / MPRIS (Linux) reports a player in "Playing" state
mic_active_flag              # capture stream active (reuses presence layer; a feature now, not just suppression)
```

**Personalization (Solution 5) — see §6:**
```
domain_productive_rate       # Bayesian-smoothed productive rate for current domain, point-in-time
domain_label_count           # how many of the user's labels back that rate (model's trust signal)
app_productive_rate          # same, keyed by focused app
app_label_count
```

**Dropped vs V3:** `entertainment_domain_flag`, `work_domain_flag`, `communication_app_flag`,
`dev_app_flag`, `creative_app_flag`, `office_app_flag`, `comm_app_flag`, `ent_app_flag`.

> **Tunable / open decision:** whether to keep `communication_app_flag` (it's a behavioral state, not
> strictly a category). Default: drop it for purity; `mic_active_flag` + `media_session_active_flag`
> + meeting suppression cover the same ground. Easy to add back if the seed model wants it.

### V4 `FeatureVectorV4`

Add a sibling `readonly record struct FeatureVectorV4` (next to `FeatureVector` at
`NudgeCore.TestableLogic.cs:157`). `ComputeFeatures` already produces all reused fields; extend the
return path to also emit the 7 new fields. Keep the V3 `FeatureVector` intact for the V3 path —
compute V4 only when `_experimentalMode` (avoid extra work in the default path).

---

## 4. CSV / model / port isolation

| Concern | V3 (off) | V4 (on) |
|---|---|---|
| Harvest log | `~/.nudge/HARVEST.CSV` | `~/.nudge/HARVEST_EXP.CSV` |
| Headers | `HarvestHeaders` (`NudgeCore.TestableLogic.cs:545`) | new `HarvestHeadersV4` (V4 columns + label) |
| Activity log | `ACTIVITY_LOG.CSV` | `ACTIVITY_LOG_EXP.CSV` (optional; reuse same context cols) |
| Model dir | `~/.nudge/model/` | `~/.nudge/model_exp/` |
| Inference port | `45002` | `45003` |
| Reputation store | — | `~/.nudge/exp_reputation.json` |
| Schema version in `scaler.json` | 2/3 | **4** |

- `PlatformConfig.CsvPath` (`NudgeCore.TestableLogic.cs:635`) currently hardcodes `HARVEST.CSV`. Add
  `CsvPathExp` / `ModelDirExp` (or parametrize by mode). The daemon picks the path from `_experimentalMode`.
- The label-write path (`SaveSnapshot` → `WriteCsvRow`, `nudge.cs:2061-2130`) must write the V4 column
  set to `HARVEST_EXP.CSV` when experimental. Keep V3 write path unchanged.
- `--csv` positional arg already exists (`ParseNudgeArgs`) — the tray can also just pass the explicit
  path. Prefer deriving from the mode flag for clarity.

---

## 5. OS signal acquisition (Solution 4) — new sensors

All three are computed in the harvest tick but **must not** add latency to the 100 ms loop. Poll on a
throttle (e.g. every 2 s, aligned with the existing `HARVEST:` emit cadence) and cache the last value;
the per-tick feature build just reads the cached flag.

### 5a. `fullscreen_flag` — wire up the existing dead field
Currently hardcoded `false` (`nudge.cs:1635`, `CaptureActivityTick`). Sway already parses
`fullscreen_mode` (`nudge.cs:1499-1502`) but it's not plumbed through. Implement per platform:
- **Linux X11:** check `_NET_WM_STATE_FULLSCREEN` on the active window (EWMH).
- **Linux Sway:** use the already-parsed `fullscreen_mode > 0` and pass it into `WindowObservation.Fullscreen`.
- **Linux KWin/Wayland:** extend the KWin tracker script (`KWinScripts.cs`) to report fullscreen.
- **Windows:** compare the foreground window rect to the monitor work area (or `SHQueryUserNotificationState` / fullscreen-detection via `GetWindowRect` vs monitor bounds).
- This fixes a latent V3 bug too, but **only wire it into the V4 feature** to avoid changing V3 model
  inputs mid-stream (V3 was trained with `fullscreen_flag` always 0). Document this clearly.

### 5b. `audio_playing_flag` — media render output active
- **Linux:** extend `PipeWireParser.Parse` (`NudgeCore.TestableLogic.cs:1837-1908`) to also detect
  `media.class: "Stream/Output/Audio"` with `state: "running"`, excluding notification/system streams
  (filter by `media.role`/`application.name`). Fallback: `pactl list sink-inputs` → `State: RUNNING`.
- **Windows:** `IAudioMeterInformation::GetPeakValue` on the default render endpoint (peak > threshold
  over the poll window) — reuses the Core Audio COM plumbing already present for capture detection.

### 5c. `media_session_active_flag` — a player reports "Playing"
- **Linux:** MPRIS over D-Bus (`org.mpris.MediaPlayer2.*`, `PlaybackStatus == "Playing"`). `Tmds.DBus.Protocol`
  is already a dependency. Avoid shelling to `playerctl` if D-Bus is clean; `playerctl status` is an
  acceptable fallback.
- **Windows:** `GlobalSystemMediaTransportControlsSessionManager` (WinRT `Windows.Media.Control`) —
  any session with `PlaybackStatus == Playing`. Gate behind a runtime check; degrade to
  `audio_playing_flag` if WinRT unavailable.

### 5d. `mic_active_flag`
Already detectable via the existing presence layer (`GetPresenceState`, PipeWire `Stream/Input/Audio` /
Windows ConsentStore). Surface the mic-active boolean as a **feature** (in addition to its existing
meeting-suppression role). No new sensor — just expose the value.

> **Heuristic the model learns (not hardcoded):** `fullscreen_flag=1` + `current_domain_share≈1` +
> long `focused_since_ms` + `audio_playing_flag=1` ≈ passive video. We do **not** hardcode this — we
> feed the raw signals and let the V4 model learn it. That's the point of Solution 1.

**Graceful degradation:** every new sensor returns `0` when its API is unavailable (no PipeWire, no
WinRT, headless, etc.), exactly like the presence layers. Never throw on the harvest path.

---

## 6. Per-domain / per-app reputation (Solution 5)

The personalization engine. New file: `DomainReputationStore.cs` (testable `internal` logic per `AGENTS.md`).

**Store:** `~/.nudge/exp_reputation.json`, two maps keyed by domain string and app id:
```json
{ "domains": { "youtube.com": {"p": 3, "n": 11}, ... },
  "apps":    { "code":        {"p": 42, "n": 2}, ... } }
```
`p` = productive label count, `n` = unproductive label count.

**Smoothed rate (Bayesian, neutral prior):**
```
rate(key)  = (p + α) / (p + n + α + β)      with α = β = 2  (prior mean 0.5, prior strength 4)
count(key) = p + n
```
Unknown domains/apps return `rate = 0.5`, `count = 0` → the model sees "no evidence, neutral", which is
exactly what #125 needs (unknown ≠ productive).

**Point-in-time semantics (no leakage):**
1. At snapshot/feature-build time, read `rate`/`count` from the store and write them into the
   `HARVEST_EXP.CSV` row. This is the value *before* the current label exists.
2. When the user answers YES/NO (`SaveSnapshot`, `nudge.cs:2061`), **after** writing the row, update the
   store (`p`/`n`++ for that row's domain and app) and persist (debounced write, like prediction history).
3. Training reads the rate columns straight from the CSV — already point-in-time correct, no Python-side
   recomputation needed.

**Live inference:** the daemon loads the store on startup and keeps it in memory; feature build reads
the current domain/app rate each tick (O(1) dictionary lookup — cheap, hot-path safe).

**Reset semantics:** "Delete Harvest Data" / "Delete Model" in Settings should also clear
`exp_reputation.json` and `HARVEST_EXP.CSV` / `model_exp/` when in experimental mode (wire into the
existing destructive-action handlers in `SettingsWindow.cs`).

Unit-test the smoothing math and update logic in xunit.

---

## 7. Behavioral-only model (Solution 1)

This is already expressed by the V4 feature set: no category flags, rich behavioral windows + the new
signals + personalization. Nothing extra to build beyond §3. The retrained seed (deferred, §8) should
**lean on behavior** — the implementing agent should ensure `train_model.py`'s V4 path does not expect
the dropped columns.

---

## 8. Seed model strategy + retraining runbook

The V4 seed is retrained **after** the §9 Python changes land (so `train_model.py` understands the V4
columns). Until a seed is bundled, the infra must cold-start gracefully.

### 8.0 Cold-start contract (must hold before any seed exists)
- If `~/.nudge/model_exp/productivity_model.joblib` is absent, `model_inference.py` returns
  `model_available: false` and the daemon falls back to interval nudges (existing
  `MIN_SAMPLES_THRESHOLD` / no-model logic in `nudge.cs`). **Verify** by launching the V4 inference
  server pointed at an empty `model_exp/` and confirming no crash and interval fallback.
- `DeployBundledModel()` (`nudge-tray.cs:1476`) stays V3-only. Add a sibling `DeployBundledModelExp()`
  that no-ops until `NudgeCrossPlatform/model_exp/` exists in the build output, so a future bundled
  seed "just works" by dropping files in.

### 8.1 Prerequisites
```bash
python3 -m pip install scikit-learn joblib numpy pandas   # same deps as the V3 pipeline
```
Confirm the §9 change to `train_model.py` is in place: it must define `FEATURE_COLUMNS_V4` and detect
the V4 column set → `schema_version = 4`. Without it, training silently falls back to V3/V1 detection
and produces a model with the wrong `feature_order`.

### 8.2 Get V4 training data — pick ONE source

**Option A — real labels (preferred once available).** Run Nudge with Experimental Mode ON and answer
YES/NO nudges. Rows accrue in `~/.nudge/HARVEST_EXP.CSV` with the V4 columns + `domain_productive_rate`
etc. already point-in-time correct (§6). Need ≥ ~100 labeled rows for a usable model (≥ ~150 for the
`standard` architecture; see `_pick_architecture` in `train_model.py`).

**Option B — synthetic seed (for shipping an out-of-box model).** Extend `generate_sample_data.py` to
emit V4 rows. Encode the *intended* priors via realistic signal distributions, e.g.:
- passive video → `fullscreen_flag=1`, `audio_playing_flag=1`, `media_session_active_flag=1`,
  `current_domain_share_300s≈1`, long `focused_since_ms`, low `switch_count_*` → label `unproductive`.
- doomscrolling → high `switch_count_300s`, high `distinct_domains_300s`, low `current_domain_share`,
  short `focused_since_ms` → `unproductive`.
- deep work → long stable focus, high `title_stability_ms`, low switching, `audio_playing_flag`
  either value → `productive`.
- unknown/neutral browsing → `domain_productive_rate≈0.5`, `domain_label_count=0` → mixed labels so the
  model treats "no evidence" as genuinely neutral (the #125 fix).
```bash
python3 generate_sample_data.py --schema v4 --out ~/.nudge/HARVEST_EXP.CSV --n 600
```
(Flag names are illustrative — match whatever `generate_sample_data.py` exposes after the §9 edit.)

### 8.3 Train
```bash
python3 train_model.py ~/.nudge/HARVEST_EXP.CSV \
  --model-dir ~/.nudge/model_exp \
  --architecture standard          # or 'auto' to size by sample count
```
Outputs into `~/.nudge/model_exp/`: `productivity_model.joblib`, `scaler.json`, `trainer_state.json`,
`trainer_meta.json`.

### 8.4 Validate the artifact before trusting it
- **Schema sanity:** `scaler.json` must show `"schema_version": 4` and a `feature_order` array that is
  **identical (same names, same order)** to `FeatureSchemaV4.OrderedFeatureNames` in C#. A mismatch
  silently feeds the model wrong columns — this is the #1 failure mode. Add/extend an xunit test that
  asserts the C# V4 order equals the committed seed's `feature_order`.
- **Accuracy:** check `accuracy` in `scaler.json`/`trainer_meta.json` (held-out test split). Sanity-check
  it predicts `unproductive` on a hand-built passive-video feature row and `productive` on a deep-work
  row (quick `python3 -c` against `model_inference.ProductivityPredictor`, or via the live server).
- **No leakage:** confirm `domain_productive_rate` values in the CSV are point-in-time (written before
  the row's own label was applied — §6).

### 8.5 Live deploy (no restart needed)
The running V4 inference server (`model_inference.py` on :45003) hot-reloads when
`productivity_model.joblib` mtime changes (10 s poll). For development, training straight into
`~/.nudge/model_exp/` is picked up automatically. `background_trainer.py` (launched with
`--csv ~/.nudge/HARVEST_EXP.CSV --model-dir ~/.nudge/model_exp`) will then keep retraining at the 20+
new-sample threshold.

### 8.6 Bundle a seed into releases (optional, for OOB experience)
1. Copy the validated `productivity_model.joblib` + `scaler.json` + `trainer_meta.json` into
   `NudgeCrossPlatform/model_exp/` and commit them (mirrors how `NudgeCrossPlatform/model/` ships the
   V3 seed; ensure `build.sh`/csproj copy the dir to the output).
2. Implement `DeployBundledModelExp()` (§8.0) to copy them to `~/.nudge/model_exp/` on first
   experimental startup, exactly like `DeployBundledModel()`.
3. Bump the seed's `model_version` in `trainer_meta.json` so the background trainer's versioning stays
   monotonic.

> **Re-seeding later:** repeat 8.2→8.5. Because V4 is fully isolated, retraining never touches the V3
> model or `HARVEST.CSV`. To start the V4 history over from scratch, delete `~/.nudge/model_exp/`,
> `~/.nudge/HARVEST_EXP.CSV`, and `~/.nudge/exp_reputation.json` (wire these into the Settings
> "Delete" actions per §6).

---

## 9. Python backend changes (reuse the existing sidecars)

The Python backend is already schema-driven via `scaler.json` `feature_order` + `schema_version`, and
already parametrized by `--model-dir` / `--port`. Minimal changes:

- **`train_model.py`:** add `FEATURE_COLUMNS_V4` (the §3 list) and detect it the same way V3 is detected
  (`load_and_prepare_data`, present-column check → `schema_version = 4`). The new columns are plain
  numeric features — the existing `StandardScaler` + `GradientBoostingClassifier` + sample-weighting
  pipeline works unchanged. No V1→V4 migration (different sensors; V4 starts fresh).
- **`model_inference.py`:** no code change required — it reads `feature_order`/`schema_version` from
  `scaler.json` and builds `X` by name. Just launch a second instance with
  `--model-dir ~/.nudge/model_exp --port 45003`.
- **`background_trainer.py`:** no code change — launch a second instance with
  `--csv ~/.nudge/HARVEST_EXP.CSV --model-dir ~/.nudge/model_exp`. Thresholds/hot-reload all carry over.

**Sidecar lifecycle (tray):** `nudge-tray.cs` currently starts the V3 inference server + trainer. Make
the launch mode-aware: in experimental mode, start the sidecars with the `_exp` dir/port and the V4 CSV;
in normal mode, the V3 ones. On toggle, stop the running sidecars and start the other set (the daemon
restart already happens via `RestartHarvestProcess()` — extend it to also restart the Python sidecars).

---

## 10. Daemon (`nudge.cs`) changes summary

1. Read `_experimentalMode` from parsed args (`Main`, ~`:1454`).
2. Select CSV path (`HARVEST.CSV` vs `HARVEST_EXP.CSV`) and headers when opening the harvest file
   (`:1925`).
3. When experimental: compute the V4 feature vector (call into the new sensors §5 + reputation §6),
   build the V4 feature dict, and send `schemaVersion: 4` + V4 `feature_order` to port **45003**
   (`QueryMLModel`, `:2291`). Otherwise V3 → 45002, unchanged.
4. On YES/NO label (`SaveSnapshot`, `:2061`): write the V4 row to `HARVEST_EXP.CSV`, then update the
   reputation store (§6).
5. Load `DomainReputationStore` on startup (experimental only).
6. Compute and cache the new OS signals on the 2 s throttle (§5).

Keep all V4 work behind `if (_experimentalMode)` so the V3 hot path is byte-for-byte unchanged.

---

## 11. IPC additions

Extend `HarvestSignal` (`NudgeJsonContext.cs:72`) and the `HARVEST:{json}` emit (`nudge.cs:1703`) with
the new flags so the Analytics "AI Brain" tab can show them in experimental mode:
`audio`, `media`, `mic`, `fullscreen` (already a field), `dom_rate`, `app_rate`. The `MLDATA` event and
graph already carry `Score`/`Confidence` — no change needed there for the model output itself.

---

## 12. Companion task (separately approved): prediction graph → productivity score

Independent of the mode, issue #125's second request: the Analytics graph should plot **productivity
score over time**, not confidence. In `AnalyticsWindow.cs:2064` (`BuildGradientChart`), the Y value uses
`aiEvents[i].Confidence`; change to `aiEvents[i].Score` (probability productive) so the curve shows
daily productivity fluctuation. The sparkline already uses `Score` (`:1321`). Update the Y-axis
label/legend and the hover tooltip (`:2162`) to say "productivity" not "confidence". Small, can ship in
the same PR or separately.

---

## 13. Testing & verification

- **xunit (pure logic, mandated):** `ParseNudgeArgs` `--experimental`; `DomainReputationStore` smoothing
  + update + persistence round-trip; V4 `FeatureSchemaV4.ToFeatureDictionary` ordering/coverage;
  PipeWire render-stream parsing (feed sample `pw-dump` JSON, assert `audio_playing_flag`).
- **Build:** `cd NudgeCrossPlatform && ./build.sh` (zero-warning policy; runs tests). Before first build,
  `git diff --stat` to confirm no foreign files (parallel-agent rule in `AGENTS.md`).
- **Integration (manual):**
  1. Toggle experimental ON in Settings → confirm daemon relaunches with `--experimental`, sidecars
     bind 45003, and rows land in `HARVEST_EXP.CSV` (not `HARVEST.CSV`).
  2. Play a YouTube video → confirm `audio_playing_flag`/`media_session_active_flag`/`fullscreen_flag`
     go to 1 in the `HARVEST:` stream.
  3. Answer a few YES/NO on a domain → confirm `exp_reputation.json` updates and the next row's
     `domain_productive_rate` reflects it.
  4. Toggle OFF → confirm it reverts to `HARVEST.CSV`/`model/`/45002 and the V3 data is intact.
  5. Toggle ON again with no V4 model → confirm graceful interval-fallback (no crashes).

---

## 14. File-by-file change checklist

| File | Change |
|---|---|
| `NudgeJsonContext.cs` | `TraySettings.ExperimentalSignalMode`; extend `HarvestSignal` with new flags |
| `SettingsWindow.cs` | Experimental toggle card + wire destructive actions to `_exp` artifacts |
| `nudge-tray.cs` | `_experimentalMode` state, load/save, `StartNudge` arg, `UpdateSettings(experimental)`, mode-aware sidecar launch, `DeployBundledModelExp()` stub |
| `NudgeCore.TestableLogic.cs` | `NudgeParsedArgs.ExperimentalMode` + parser; `FeatureSchemaV4` + `FeatureVectorV4`; `HarvestHeadersV4`; `CsvPathExp`/`ModelDirExp`; extend `ComputeFeatures` for V4 fields; PipeWire render-stream parse |
| `nudge.cs` | mode-aware CSV/headers/port/schema; new-sensor polling (audio/media/fullscreen) + cache; reputation load + update on label; `QueryMLModel` V4 routing |
| `KWinScripts.cs` | fullscreen reporting (KWin/Wayland) |
| `DomainReputationStore.cs` *(new)* | reputation store + smoothing |
| `train_model.py` | `FEATURE_COLUMNS_V4` + detection → `schema_version=4` |
| `generate_sample_data.py` *(optional)* | synthetic V4 seed rows |
| `AnalyticsWindow.cs` | graph Score (companion §12) |
| `NudgeCrossPlatform.Tests/` | new xunit coverage (§13) |
| `model_inference.py`, `background_trainer.py` | **no code change** — launched with `_exp` dir/port |

---

## 15. Open decisions for the implementer / user

1. Keep or drop `communication_app_flag` in V4 (default: drop).
2. Run both pipelines concurrently vs only the active one (this plan: only active, for CPU; revisit if
   users want both models warm).
3. Bundle a synthetic V4 seed (via `generate_sample_data.py`) vs ship cold and rely on interval fallback
   until the user accumulates labels (this plan: cold start is fine; seed is a fast-follow).
4. Reputation prior strength `α=β=2` — tune after seeing real label volume.
5. Whether to expose the new OS signals in V3 too (this plan: **no** — keep V3 frozen so its trained
   model's inputs don't shift; V4 is where the new signals live).
