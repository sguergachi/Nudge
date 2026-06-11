# Pre-trained Distraction Prior — Implementation Plan

> **Status:** **IMPLEMENTED.** Companion to `EXPERIMENTAL_SIGNAL_MODE.md`.
> The committed DKB (`model_exp/distraction_priors.tsv`) is the curated seed —
> ~390 domains + ~155 apps, LLM-labeled offline and hand-reviewed. Tranco/UT1
> enrichment to 20k domains (§3) is wired into `tools/build_distraction_kb.py`
> but needs a network-enabled dev machine; rerun it and commit the larger TSV
> when convenient. The V4 seed has been retrained per §5 and validated on
> Windows via `tools/v4_acceptance.py` (10 scenarios, in-process + live TCP):
> quiet x.com scrolling triggers at conf 0.93 ≥ 0.75 (~98% of sampled
> quiet-distraction rows); passive fullscreen video fires even on unknown
> domains; steam-like gaming fires on the app prior; deep work (± background
> music), quiet docs reading, quiet unknown sites and unknown fullscreen apps
> do not trigger; 12 user YES labels flip x.com productive (personalization
> overrides the prior). Seed repro: `generate_sample_data.py --schema v4
> --samples 3000 --seed 42` → `train_model.py --architecture deep`.
> Originally targeted v2.0.6 master (post-UIA #152/#153/#155, post
> interval-floor #154).

## Context & motivation

Signal mode (V4) deliberately dropped the hardcoded category flags and starts every
domain/app at a **neutral reputation prior (0.5)**. The result, confirmed in the field:
**no nudges on obvious distractions**. Quietly scrolling x.com is behaviorally identical
to reading documentation (stable focus, one domain, no media), the synthetic seed has no
opinion, and predictions never reach the **85% confidence** trigger (`nudge.cs:94`).

**#154 fixed half of this:** the interval is now a *guaranteed floor* — productive
predictions no longer reset the timer, so baseline nudges (and therefore labels) always
flow. What remains is the *intelligence* half: the model still has no opinion about
x.com, so the **ML trigger never fires on distraction moments** — nudges land on the
interval clock, not when you actually drift, and the AI adds nothing over a dumb timer
until weeks of labels accumulate. This plan fixes that half.

**Goal:** restore out-of-box distraction knowledge as a **pre-trained, shipped prior** —
built offline from public data + open-weight LLM labeling — that seeds the existing V4
reputation feature. User YES/NO labels still override it (YouTube can still become "work"
for a video editor). No hardcoded category flags return; no runtime network is added.

**Why now:** the Windows UIA omnibox reader (#152/#153, key-normalized in #155) means
domains actually resolve on Windows — a prior finally has something to attach to.

---

## 0. Guiding constraints

- **Zero runtime network.** All model/LLM work happens **at dev time** in a build script.
  The app ships a static data file. This is non-negotiable (no cloud, no telemetry).
- **Parametrize, don't fork.** The prior plugs into the *existing* reputation math as
  per-key pseudo-counts — no second code path, no schema change, no new features.
- **V3 untouched.** Priors live only in the experimental store; the V3 pipeline never
  sees them.
- **Generalize current behavior, don't replace it.** A key absent from the prior table
  must behave exactly as today (0.5). Shipping an empty table = current behavior.
- **Key normalization parity.** Prior-table keys must be normalized identically to
  `BrowserDetector.TryNormalizeDomain` output (lowercase, no `www.`, no localhost) —
  see PR #155, which made the UIA and title paths consistent. A mismatched key is a
  silent no-op.

---

## 1. Architecture

```
DEV TIME (offline, repo tooling — never runs on user machines)
  public category datasets (UT1/Toulouse, Tranco, Cloudflare Radar categories)
        + open-weight LLM batch labeling (gaps, apps)
        → tools/build_distraction_kb.py
        → NudgeCrossPlatform/model_exp/distraction_priors.tsv   (~top 10–20k domains + ~200 apps)

RUNTIME (experimental mode only)
  DeployBundledModelExp() copies distraction_priors.tsv → ~/.nudge/model_exp/
  daemon loads it once at startup → DomainReputationStore(path, priors)
  unknown key:   rate = prior              (today: 0.5)
  labeled key:   rate = (p + S·prior) / (p + n + S)     S = 4 (prior strength)
  → feeds the EXISTING domain_productive_rate / app_productive_rate features
  → retrained V4 seed model weights those features confidently
  → x.com scores 'not productive' above the trigger threshold → nudge → labels →
    personalization takes over (deadlock broken)
```

---

## 2. The prior-informed reputation math (core change, ~10 lines)

`DomainReputationStore` (`DomainReputationStore.cs`) currently smooths with a fixed
symmetric prior: `rate = (p + 2) / (p + n + 4)` (`:15-16, :106`), and returns hard
`0.5` for unknown keys (`:32, :46`).

**Change:** make the pseudo-counts per-key. With `prior(key)` from the table
(default 0.5 when absent) and total prior strength `S = 4` (unchanged mass):

```
rate(key) = (p + S·prior(key)) / (p + n + S)
```

Properties worth preserving in tests:
- key absent from table, no labels → 0.5 (**byte-identical to today**)
- key in table, no labels → exactly `prior(key)` (x.com ≈ 0.10 out of the box)
- labels blend smoothly: after 4 user labels, user evidence equals prior weight;
  it then dominates. No cliff when the first label arrives.
- `domain_label_count` stays 0 for prior-only keys — the model can already
  distinguish "shipped prior" from "user-confirmed" via the existing count feature.
  **No schema change.**

Constructor becomes `DomainReputationStore(string path, IReadOnlyDictionary<string,double>? domainPriors = null, IReadOnlyDictionary<string,double>? appPriors = null)`;
the load site is `nudge.cs:1796` (experimental-only block). Feature build sites
(`nudge.cs:2042-2045`) and label-update flow are untouched.

---

## 3. Building the Distraction Knowledge Base (dev-time)

New script `tools/build_distraction_kb.py` (repo tooling, not shipped to runtime):

1. **Coverage set:** top ~10–20k domains from the **Tranco** list (research-grade
   popularity ranking) — covers the overwhelming majority of real browsing.
2. **Category sources (free, redistributable):**
   - **UT1 / Université de Toulouse** categorized domain lists (social_networks,
     games, gambling, audio-video, shopping, press, …) — millions of domains.
   - Cloudflare Radar domain categories where available.
3. **Category → distraction score** mapping (the only hand-curated part, ~20 rows):
   social/games/gambling/streaming → 0.85–0.95; shopping/news/sports → 0.6–0.75;
   webmail/docs/dev/cloud → 0.1–0.2; unknown → omit (falls back to 0.5).
   Scores are stored as **productive-rate priors** (`prior = 1 − distraction`).
4. **LLM batch labeling for the gaps** (offline, dev machine): top-ranked domains not
   covered by the lists, plus the **app-id set** (~200 common desktop app ids:
   `steam`, `discord`, `spotify` → distracting; `code`, `terminal`, `blender` →
   productive). Any open-weight model run locally is fine — it labels a CSV once;
   nothing of it ships.
5. **Normalize keys** exactly like `TryNormalizeDomain` (lowercase, strip `www.`),
   dedupe, emit TSV: `key<TAB>kind(domain|app)<TAB>prior` — ~200–400 KB.
6. Commit the TSV to `NudgeCrossPlatform/model_exp/` with provenance notes in the
   script header (sources + date), so it can be regenerated/audited.

> **Deliberately deferred (open decision):** a tiny char-ngram classifier for
> *unseen* domains (`freemovies123.net` → distracting from token shape). Start with
> the lookup table — parsimony; add the classifier only if coverage proves
> insufficient in practice.

---

## 4. Shipping & loading

- **Packaging:** `distraction_priors.tsv` joins the existing `model_exp/` content
  (`nudge-tray.csproj` already globs `model_exp\**\*`); add it to
  `DeployBundledModelExp()`'s aux-file list (`nudge-tray.cs:~1508`) and to the
  **`installer.wxs` ModelExpComponent** (the #144 lesson: the MSI ships only what's
  declared — don't repeat it).
- **Loading:** daemon, experimental block only (`nudge.cs:1796`): parse the TSV into
  two `Dictionary<string,double>` (Ordinal comparer), pass to the store ctor. Malformed
  or missing file → empty priors → exactly today's behavior. Never throw; log one
  sensor-health-style line: `priors: 18,432 domains, 211 apps` (or `priors: none`).
- **Reset semantics:** "Delete Model"/"Delete Harvest Data" must **not** treat the
  priors file as user data — it redeploys anyway; user labels in
  `exp_reputation.json` remain the only thing reset.

---

## 5. Retrain the V4 seed to trust the prior

The shipped seed must learn "low domain rate + browsing + no work signals →
not productive, *confidently*", or the prior never moves predictions past the trigger.

- `generate_sample_data.py` already couples the rate to the label
  (`signal_adj += (domain_rate − 0.5) * 0.25`, `:438`; app `* 0.20`, `:440`).
  **Strengthen** the coupling (≈ double those weights) and draw `domain_rate` for
  distraction-category samples from the actual DKB distribution (≈0.05–0.2) instead
  of mid-range noise, with `domain_label_count = 0` so the model learns to act on
  prior-only evidence.
- Retrain per the existing runbook (`EXPERIMENTAL_SIGNAL_MODE.md` §8). The
  committed seed is reproducible:
  `python3 generate_sample_data.py --schema v4 --samples 3000 --seed 42 --output <csv>`
  then `python3 train_model.py <csv> --model-dir model_exp --architecture deep`.
  Validate `feature_order` parity, commit the new seed, bump `model_version`.
- **Acceptance check (scripted):** `python3 tools/v4_acceptance.py [--sweep N]
  [--server --port 45003]` — a "quiet x.com scrolling" row (stable focus,
  browser=1, `domain_productive_rate=0.1`, count=0, no media) must predict
  `not productive` with confidence **≥ the trigger threshold**; deep work
  (± background music), quiet docs reading, quiet unknown sites and unknown
  fullscreen apps must not trigger; passive fullscreen video and steam-like
  gaming must trigger; user labels must override the prior.

---

## 6. Trigger calibration (the last mile)

Even a good prior is wasted if gating eats it:
- Make the trigger threshold **mode-aware**: keep 0.85 for V3, start the
  experimental mode at **0.75** (`ML_CONFIDENCE_THRESHOLD`, `nudge.cs:94` — becomes
  a `_experimentalMode ?` expression). Tunable; revisit with real data.
- Label flow is already guaranteed by the **#154 interval floor** (productive
  predictions no longer reset the timer) — no further calibration mechanism is
  needed while the model warms up. The success metric for this plan is therefore
  the **`ML TRIGGER` : `INTERVAL SNAPSHOT` ratio** in `nudge.log`: today distraction
  nudges arrive on the clock; after the prior + retrain they should arrive from the
  model, at the moment of drift.

---

## 7. File-by-file checklist

| File | Change |
|---|---|
| `tools/build_distraction_kb.py` *(new, dev-only)* | DKB pipeline: Tranco ∩ UT1/Radar + LLM gap labeling → normalized TSV |
| `NudgeCrossPlatform/model_exp/distraction_priors.tsv` *(new, shipped)* | the prior table |
| `DomainReputationStore.cs` | ctor takes priors; per-key pseudo-count math (§2) |
| `nudge.cs` | load TSV at `:1796`; mode-aware threshold at `:94`; one health log line |
| `nudge-tray.cs` | `DeployBundledModelExp()` copies the TSV |
| `assets/windows/installer.wxs` | add TSV to ModelExpComponent |
| `generate_sample_data.py` | stronger rate→label coupling; DKB-shaped rate draws (§5) |
| `model_exp/*` seed artifacts | retrained per §5 |
| `NudgeCrossPlatform.Tests/DomainReputationStoreTests.cs` | §2 property tests (absent-key parity, exact-prior, smooth blend) |
| `NudgeCrossPlatform.Tests/` *(new)* | TSV loader test + key-normalization parity test (every shipped key passes `TryNormalizeDomain` round-trip) |

Python sidecars (`train_model.py`, `model_inference.py`, `background_trainer.py`):
**no code change** — the prior only changes feature *values*, not the schema.

---

## 8. Verification

- **xunit:** the §2 properties; loader robustness (missing/malformed file → empty);
  shipped-TSV key normalization parity.
- **Acceptance script (§5):** quiet-distraction row triggers, deep-work row doesn't.
- **Manual on Windows:** experimental mode on → open x.com → Sensor Signals shows
  `Web App: x.com` and `Domain reputation` ≈ 10% → within one ML check, a nudge
  fires. Answer YES a few times on a "work" site from the distraction list →
  watch its rate climb past the prior (personalization overrides).
- CI as usual; the seed retrain is validated by the existing schema-order test.

---

## 9. Open decisions

1. **KB size:** top 10k vs 20k vs 50k domains (this plan: 20k; ~300 KB).
2. **Unseen-domain classifier (Tier 2):** deferred — add only if "unknown site"
   remains common after the UIA reader + 20k table.
3. **Prior strength S = 4:** matches today's smoothing mass; raise to 6–8 if users
   report the prior caving too fast to a couple of mislabels.
4. **Experimental threshold 0.75:** tune against real trigger rates.
5. ~~Calibration check-ins to keep labels flowing~~ — **resolved by #154**: the
   interval is now a guaranteed floor, so labels flow regardless of model opinion.
