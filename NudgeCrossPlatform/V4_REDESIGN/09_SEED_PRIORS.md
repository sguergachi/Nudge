# 09 — Seed Priors + Decommissioning the V4 GBM Seed (WP9)

**Goal:** Make the shipped distraction priors (`distraction_priors.tsv`) the **only** seed for
V4, since the synthetic-CSV GBM is no longer on the V4 runtime path. Curate the list so new
users get sensible day-one behavior that their own labels then override.

**Depends on:** nothing (content + small docs). Can be done any time. Coordinates with 06
(tray still deploys the TSV) and 02 (which consumes the priors via the store).

**Files:** `NudgeCrossPlatform/model_exp/distraction_priors.tsv` (curate), and a short note in
`ML_README.md` / `PRETRAINED_DISTRACTION_MODEL.md` that V4 no longer trains a GBM.

---

## 1. Format (already supported by `DomainReputationStore.LoadPriors`)

Tab-separated, one entry per line, `#` comments allowed:

```
<key>\t<kind>\t<prior>
```

- `key`: domain (e.g. `youtube.com`) or app id (e.g. `code`, `steam`). Domains must match what
  `BrowserDetector.ExtractSite` produces; apps must match the focused app id used in the store.
- `kind`: `domain` or `app`.
- `prior`: productive-rate in `[0,1]`. Low = distracting (e.g. `0.08`), high = productive
  (e.g. `0.90`). Clamped on load.

The prior enters the Beta smoothing with pseudo-count mass `PriorStrength = 4.0`, so it acts
like ~4 virtual labels and **washes out after a handful of real user labels** — exactly the
behavior we want: helpful default, not a permanent verdict.

## 2. Curation guidance

Keep it **small and high-confidence** (a long speculative list just adds noise that the user
must override). Tiers:

- **Strong distractors (≈0.05–0.12):** `youtube.com`, `netflix.com`, `reddit.com`,
  `twitter.com`/`x.com`, `instagram.com`, `tiktok.com`, `twitch.tv`; apps `steam`, `mpv`,
  `spotify`(borderline — maybe 0.2), games.
- **Strong producers (≈0.85–0.92):** `github.com`, `stackoverflow.com`, `docs.python.org` and
  major docs sites, `notion.so`; apps `code`, `vim`, `idea`, `gimp`, `blender`.
- **Deliberately omit ambiguous ones** (`gmail.com`, `slack.com`, `discord.com`, `zoom.us`):
  leave them at neutral so the *user's* labels — not our guess — decide. This directly avoids
  the "Slack always flagged" false positive at the source.

Audit the current `model_exp/distraction_priors.tsv` against this and trim/adjust. There is an
existing test `DistractionPriorsTests.ShippedTsv_LoadsWithSubstantialCoverage` — keep it green
(adjust the expected coverage count if you change the list, but don't pad the list to satisfy
a number).

## 3. Decommission notes (documentation only)

- V4 (`--experimental`) no longer loads `productivity_model.joblib` / `scaler.json` or queries
  `model_inference.py`. The only V4 seed is this TSV (priors) + the user's `exp_reputation.json`.
- `generate_sample_data.py --schema v4`, `train_model.py` on V4 columns, and the V4 branch of
  `background_trainer.py` are **dormant** for runtime but kept in the repo for a future
  corpus-trained scorer (00 §8). Add a one-line banner comment at the top of each noting "V4
  runtime no longer uses this; retained for future offline scorer / V3."
- Do **not** delete these scripts (V3 still uses the pipeline; the corpus path may return).

## 4. Tests

- `DistractionPriorsTests` stay green (coverage + round-trip via `TryNormalizeDomain`).
- Add `Priors_AmbiguousAppsOmitted` asserting `slack`/`zoom`/`discord`/`gmail` are NOT in the
  shipped TSV (encodes the curation decision so a future edit doesn't silently re-add them).

## 5. Done when

The shipped TSV is curated (strong signals only, ambiguous omitted), tests pass, and the
decommission banners make it unambiguous that V4 runtime is GBM-free while the offline scripts
remain for V3 / future use.
