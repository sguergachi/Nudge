# Nudge Alert Logic

How and when nudge decides to show you a productivity alert.

## Overview

Nudge has two modes for triggering alerts:

1. **Interval-based** (default, no ML) — fires every 5–10 minutes
2. **ML-powered** (`--ml`) — adapts to your behavior using a trained model

Both modes run inside the `nudge` subprocess (`nudge.cs`), which communicates with
`nudge-tray` via stdout. The tray app picks up the `SNAPSHOT` marker and shows the
notification window.

## State Machine

```
                   MONITORING
  ┌──────────────────────────────┐
  │  _waitingForResponse = false │
  │  elapsed timer counts up     │
  │                              │
  │  Every 1s: tick              │
  │  Every 60s: ML check (if on) │
  │  Every N min: interval check │
  └──────────┬───────────────────┘
             │ trigger condition met
             ▼
   SNAPSHOT / WAITING
  ┌──────────────────────────────┐
  │  TakeSnapshot() called       │
  │  _waitingForResponse = true  │
  │  "SNAPSHOT" → tray shows UI  │
  │  Timer: 60s core timeout     │
  │                              │
  │  Possible exits:             │
  │  ┌─ YES/NO via UDP → save    │
  │  ├─ UI auto-dismiss (30s)    │
  │  └─ Core timeout (60s)       │
  └──────────────────────────────┘
         │ response/timeout
         ▼
      MONITORING (reset elapsed)
```

## Interval-Based Mode

The simplest mode. A random timer runs continuously:

- **Interval**: random 5–10 minutes (or fixed with `--interval N`)
- **Every tick** (1s): `elapsed += CYCLE_MS`
- **When** `elapsed >= SNAPSHOT_INTERVAL_MS`: trigger snapshot, reset timer

```csharp
bool intervalReached = elapsed >= SNAPSHOT_INTERVAL_MS;

if (!_waitingForResponse)
{
    if (mlTriggered || (useIntervalFallback && intervalReached))
    {
        TakeSnapshot(app, title, idle, _attentionSpanMs, tick);
        elapsed = 0;
        SetRandomInterval();  // new random 5-10 min
    }
}
```

## ML-Powered Mode

When `--ml` is active, the ML model is queried every 60 seconds. The result
determines whether the nudge fires immediately, is suppressed, or falls back
to the interval timer.

### Every 60 Seconds

1. Check ML availability (TCP connect to `127.0.0.1:45002`)
2. Skip ML prediction if:
   - Signal quality is **Poor**
   - User is **AFK** (idle >= 60s)
3. Send 26 features to inference server, receive prediction

### Decision Matrix

| Prediction | Confidence | Action |
|---|---|---|
| Not productive (0) | Any | **TRIGGER nudge** immediately |
| Productive (1) | Any | **SKIP** nudge, reset interval timer |
| Model unavailable | — | Fall back to interval-based |
| Low confidence | — | Fall back to interval-based |
| Signal poor / AFK | — | Skip ML, let interval handle |

### Code Flow

```csharp
static bool ShouldTriggerSnapshot(string app, int idle, int attention, ActivityTickResult? tick)
{
    if (!_mlEnabled) return true;                           // defer to interval

    CheckMLAvailability();                                   // TCP connect
    if (!_mlAvailable) return false;                        // let interval handle

    if (tick?.Context.SignalQuality == SignalQuality.Poor)
        return false;                                       // poor signal, skip

    if (tick?.Features.AfkFlag == 1)
        return false;                                       // AFK, skip

    var prediction = QueryMLModel(app, idle, attention, tick);

    if (prediction == null || !prediction.ModelAvailable)
        return true;                                        // fallback to interval

    if (prediction.Prediction == 0)                         // NOT productive
        return true;                                        // TRIGGER nudge

    // Prediction == 1 → productive
    _productivityConfirmed = true;
    return false;                                           // skip, reset interval
}
```

## Snapshot Lifecycle

### TakeSnapshot

1. Saves current context (`_snapshotApp`, `_snapshotTitle`, `_snapshotIdle`, etc.)
2. Prints `SNAPSHOT` to stdout — the tray app detects this
3. Sets `_waitingForResponse = true`
4. Starts 60-second timeout timer

### Notification Display

The tray app (`nudge-tray.cs`) listens for the `SNAPSHOT` line:

```
stdout: SNAPSHOT
  → ShowSnapshotNotification()
    → CustomNotificationWindow
      - Auto-dismiss after 30 seconds
      - Keyboard: Y / Enter = YES, N = Escape = NO
      - UDP response to 127.0.0.1:45001
```

### Response Handling

User response arrives via UDP on port 45001:

| Message | Action |
|---|---|
| `YES` | Save as productive example |
| `NO` | Save as not-productive example |
| (timeout) | Discard, no snapshot saved |

`SaveSnapshot()` writes to `HARVEST.CSV` with a 3x weight multiplier for productive
responses (to address class imbalance in training data).

## Guard Conditions

A nudge will **not** fire when:

- `_waitingForResponse == true` — already showing a notification
- Signal quality is Poor — ML prediction is unreliable
- User is AFK (idle >= 60s)
- Nudge window is the foreground app — excluded from tracking
- No foreground app detected (`"unknown"`)

## Key Constants

| Constant | Value | Purpose |
|---|---|---|
| `CYCLE_MS` | 1000 ms | Main loop tick rate |
| `SNAPSHOT_INTERVAL_MS` | 5–10 min (random) | Interval-based trigger |
| `ML_CHECK_INTERVAL_MS` | 60,000 ms | ML prediction frequency |
| `RESPONSE_TIMEOUT_MS` | 60,000 ms | Max wait for user response |
| `AUTO_DISMISS_SECONDS` | 30 s | Notification auto-close |
| `ML_CONFIDENCE_THRESHOLD` | 0.98 | Reserved for future use |

## IPC Protocol

The nudge subprocess communicates with the tray app via stdout lines:

| Prefix | Direction | Purpose |
|---|---|---|
| `SNAPSHOT` | nudge → tray | Trigger notification |
| `MLDATA:{json}` | nudge → tray | ML prediction result |
| `MLNEXT:{unixts}` | nudge → tray | Next ML check timestamp |
| `APPFOCUS:{app}\t{title}` | nudge → tray | Foreground app changed |
| `HARVEST:{json}` | nudge → tray | Sensor fusion data (2s interval) |

Responses flow back via UDP: `YES` or `NO` to `127.0.0.1:45001`.

## Relevant Source Files

| File | Role |
|---|---|
| `nudge.cs` | Core loop, trigger logic, snapshot, UDP listener |
| `nudge-tray.cs` | Notification display, response UI |
| `CustomNotification.cs` | Notification window with auto-dismiss |
| `NudgeJsonContext.cs` | MLPrediction and other DTOs |
| `NudgeCore.TestableLogic.cs` | Signal quality, feature extraction |
