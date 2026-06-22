# 02 — Reputation Authority (WP2)

**Goal:** Turn the existing Bayesian per-app/per-domain reputation into an **authoritative
verdict** the decision engine can act on directly, so that "I told you Slack is fine" sticks
*immediately* (one to a few labels) instead of waiting for a 20-label GBM retrain.

**Locus:** a small read-side helper. The heavy lifting (`DomainReputationStore`) already
exists and is correct — **do not change its smoothing, persistence, priors loader, or the
`Update()` write path.** This package only adds a classifier over its outputs.

**Depends on:** `00_ARCHITECTURE.md` types (`ReputationVerdict`, `ReputationStance`,
`FeatureVectorV4`). The four reputation values are already overlaid onto `FeatureVectorV4`
in `nudge.cs` (`DomainProductiveRate/DomainLabelCount/AppProductiveRate/AppLabelCount`).

---

## 1. What already exists (reuse, do not rebuild)

`DomainReputationStore.cs`:

- `DomainRate(d)` / `AppRate(a)` → Beta-smoothed productive rate in `[0,1]`:
  `(productive + PriorStrength * prior) / (total + PriorStrength)`, `PriorStrength = 4.0`,
  neutral prior `0.5`, shipped priors from `distraction_priors.tsv`.
- `DomainCount(d)` / `AppCount(a)` → total labels backing the rate.
- `Update(domain, app, productive)` + `Flush()` — the YES/NO write path (`nudge.cs:~2597`).
- `LoadPriors(tsv)` — shipped distraction priors.

Key property: a key with `count = 0` returns exactly its prior (or `0.5` if unknown); after
~`PriorStrength` real labels the user's own rate dominates the prior. This is the
personalization engine — WP2 just reads it.

## 2. The verdict

`ReputationAuthority.From(in FeatureVectorV4 f)` builds a `ReputationVerdict` from the four
overlaid fields. Two questions: (a) is there enough evidence to be *confident*, and (b) which
way.

```
domainEvidence = DomainLabelCount      // 0 ⇒ prior-only
appEvidence     = AppLabelCount

// Confidence: prior-only (count 0) is still usable when the prior is strong (shipped DKB
// entries like youtube/netflix ship near 0.05–0.1, github/docs near 0.9). Treat a rate far
// from neutral as confident even at count 0; treat near-neutral as low evidence.
isConfident(rate, count) =
      count >= CONFIDENT_LABELS                       // e.g. 3 real labels ⇒ confident
   || abs(rate - 0.5) >= STRONG_PRIOR_MARGIN          // e.g. 0.35 ⇒ a 0.1 or 0.9 prior is confident
```

### Combining domain vs app

Browsers carry no useful app reputation (the *domain* is the evidence); native apps carry no
domain. Rule:

```
if BrowserWindowFlag == 1 and domain present:
    primaryRate, primaryCount = DomainRate, DomainCount   // domain wins for browsers
else:
    primaryRate, primaryCount = AppRate, AppCount         // app wins for native apps
```

If both are present and confident and **disagree** (e.g. a meeting app hosting a productive
domain), prefer the more-evidenced one (higher count); tie ⇒ prefer the lower (more
conservative) rate only when behavioral drift is also elevated — but that fusion belongs to
03, so WP2 just exposes both rates/counts and the chosen `primary`. Keep WP2 a pure
classifier; let 03 own cross-signal conflict.

### Stance

```
if !isConfident(primaryRate, primaryCount):     Stance = LowEvidence
else if primaryRate >= PRODUCTIVE_RATE_HI:       Stance = ConfidentProductive   // e.g. >= 0.65
else if primaryRate <= LOWVALUE_RATE_LO:         Stance = ConfidentLowValue      // e.g. <= 0.35
else:                                            Stance = LowEvidence            // confident-but-middling ⇒ let behavior decide
```

Return `new ReputationVerdict(DomainProductiveRate, DomainLabelCount, AppProductiveRate,
AppLabelCount, Stance)`.

## 3. Signature

```csharp
internal static class ReputationAuthority
{
    // Pure given the four already-overlaid reputation fields on the vector.
    public static ReputationVerdict From(in FeatureVectorV4 f);
}
```

Pure and store-free by design (the store read happened during the overlay in `nudge.cs`), so
WP2 is trivially unit-testable by constructing `FeatureVectorV4` values directly.

## 4. How 03 uses the stance (informative, not implemented here)

- `ConfidentProductive` ⇒ engine should **not** nudge regardless of drift (kills the Slack /
  docs / music-while-coding false positives).
- `ConfidentLowValue` ⇒ a low-value app/domain; nudge if behavior also drifts (or, for
  passive-media patterns, on sensors alone — see 03).
- `LowEvidence` ⇒ reputation abstains; behavior + drift + sensors decide.

## 5. Tests (land with this package)

- `From_UnknownApp_LowEvidence` (count 0, rate 0.5).
- `From_StrongPriorYoutube_ConfidentLowValue` at count 0 (prior ≈ 0.08).
- `From_StrongPriorGithub_ConfidentProductive` at count 0 (prior ≈ 0.9).
- `From_UserLabeledSlackProductive_FlipsToConfidentProductive` after `CONFIDENT_LABELS`
  YES updates (drive via the real store + `From`, or via crafted rate/count).
- `From_Browser_PrefersDomainOverApp`.
- `From_MiddlingConfidentRate_LowEvidence` (e.g. rate 0.5 with high count ⇒ abstain).
- Boundary tests on `PRODUCTIVE_RATE_HI` / `LOWVALUE_RATE_LO` / `STRONG_PRIOR_MARGIN`.

## 6. Done when

`ReputationAuthority.From` compiles, tests pass, and the "labeled Slack productive ⇒
ConfidentProductive" case demonstrates instant personalization. `DomainReputationStore.cs`
itself is unchanged except possibly adding the (small) helper next to it — no behavior change
to existing methods.
