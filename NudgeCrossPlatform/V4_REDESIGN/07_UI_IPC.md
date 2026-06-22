# 07 — UI / IPC Surface (WP7)

**Goal:** Make the AI Brain tab reflect the new engine — show *why* a decision was made
(reputation, focus/drift, distraction score, calibrated threshold) — without breaking the
existing live-update plumbing. Keep all IPC contracts; new fields are additive only.

**Depends on:** `00_ARCHITECTURE.md` §6–§7 (`MLLiveEvent` mapping, preserved IPC). Can start
as soon as 00 is frozen; final values come from 05's emission.

**Files:** `AnalyticsWindow.cs`, `AnalyticsWindow.Views.cs`, `LiveAIState.cs`,
`NudgeJsonContext.cs`. Read AGENTS.md: this is polished, animated UI — make **minimal**
changes, don't restructure the tab.

---

## 1. What stays exactly as-is

- IPC stream + parsing: `MLDATA:` → `MLLiveEvent`, `MLNEXT:` → countdown, `MLRESPONSE:` →
  ✓/✗ correlation, `HARVEST:` → live sensor card. `LiveAIState` ring buffer (last 30 events),
  `NextCheckAt`, `CurrentApp`, `LastHarvest`.
- The gradient prediction-history chart (`BuildGradientChart`), the "In Focus Now" card, the
  countdown card. They read `MLLiveEvent.score/confidence/productive/triggered` and
  `HarvestSignal` fields, all of which 05 keeps populating.

## 2. Additive fields (optional but recommended)

To surface the rationale and sub-scores, add to `MLLiveEvent` (and register in
`NudgeJsonContext.cs`):

```csharp
public string? Rationale { get; set; }          // DecisionResult.Rationale
public double  Distraction { get; set; }        // DecisionResult.DistractionValue (0..1)
public double  Threshold { get; set; }          // EffectiveThreshold at decision time
```

And to `HarvestSignal` (already carries `dom_rate`, `app_rate`, `audio/media/mic`) optionally
add the live focus/drift so the sensor card can show them between checks:

```csharp
public double FocusScore { get; set; }          // 0..1
public double DriftZ { get; set; }              // signed
```

All additive — old parsers ignore unknown fields; do not rename or remove existing ones.

## 3. AI Brain tab changes (minimal)

- **Reframe "ML confidence" language** to the engine. The score card and chart keep working
  off `score`/`confidence`; just relabel copy where it says "AI model"/"ML prediction" to the
  new engine name (keep it light; don't churn the layout).
- **"Why this decision" line:** under the score card, show `MLLiveEvent.Rationale` (one line,
  e.g. "low-value domain + focus drift" or "reputation: confidently productive"). This is the
  single highest-value UX change — it makes the engine legible and builds trust after the
  false-positive frustration.
- **Sensor Signals panel** (`PopulateSignalPanel`): it already shows domain/app reputation,
  audio/media/mic in V4. Add Focus score + Drift (from `HarvestSignal` if you added them, else
  from the latest `MLLiveEvent`). Show the **calibrated threshold** next to the distraction
  value so the user sees the bar moving over time.
- **Model Training accordion** (`CreateTrainingView`): in experimental mode there is no sklearn
  model/retrain. Replace its contents (experimental-only) with a small "Personalization"
  panel: reputation label counts, calibration threshold + target rate, baseline warm/Count.
  Keep the V3 training view unchanged when not experimental. Gate on the experimental flag the
  tab already knows (`Program.ExperimentalMode`).

## 4. Don't break

- `OnClosed` must still stop `_aiLiveRefreshTimer` (existing fix). If you add timers, stop them too.
- PulseDot animation phase continuity across rebuilds (existing behavior) — don't disturb.
- The Recent Checks ✓/✗ depends on `MLRESPONSE` + `_lastMLTriggerT` (05 keeps it) — verify a
  triggered V4 nudge still gets ✓/✗ after YES/NO.

## 5. Tests / verification

- UI is verified by running the app (08 / verify skill), not unit tests. Checklist:
  - AI Brain tab renders in experimental mode with the rationale line populated each check.
  - Score card + gradient chart animate as before.
  - Sensor panel shows focus/drift + reputation + calibrated threshold.
  - Recent Checks shows ✓/✗ after answering a V4-triggered nudge.
  - No exceptions on window close; no stuck timers.
- If any `MLLiveEvent`/`HarvestSignal` field was added, add a `NudgeJsonContext` round-trip
  unit test (mirrors `NudgeCsvParsingTests` style) so serialization stays AOT-safe.

## 6. Done when

The tab clearly shows why the engine nudged (or didn't), all existing live visuals keep
working, new fields are additive + registered for source-gen, and nothing in the V3 UI path
changed.
