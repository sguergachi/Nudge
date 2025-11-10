# ML Performance Tracking - Example Output

This document shows what you'll see when running `./nudge-tray --ml` with the new ML performance tracking features.

## Startup Sequence

```bash
$ ./nudge-tray --ml
╔═══════════════════════════════════════════════════════╗
║        Nudge Tray - Productivity Tracker          ║
║        Version 1.1.0                                   ║
║        🧠 ML MODE ENABLED                         ║
╚═══════════════════════════════════════════════════════╝

🧠 Starting ML services...
  Starting ML inference service...
[ML Inference] 🚀 Inference server listening on /tmp/nudge_ml.sock
[ML Inference] ✅ Model loaded from ./model/productivity_model.keras
[ML Inference] ✅ Scaler loaded from ./model/scaler.json
  ✓ ML inference service started (socket: /tmp/nudge_ml.sock)
  Starting background trainer...
[ML Trainer] 🚀 Background trainer started
[ML Trainer] 📂 Loaded state: 1 trainings, 200 samples seen
[ML Trainer] ⏳ Waiting for more data... need 30 more samples (20/50)
  ✓ Background trainer started
✓ ML services ready
✓ Nudge process started
  ML mode enabled - waiting for inference server connection...

[Nudge] ╔═══════════════════════════════════════════════╗
[Nudge] ║  Nudge - ML-Powered Productivity Tracker  ║
[Nudge] ║  Version 1.1.0                               ║
[Nudge] ╚═══════════════════════════════════════════════╝
[Nudge] ✓ Compositor: kde
[Nudge] ✓ Qt D-Bus available
[Nudge] ✓ Detected window: vscode
[Nudge] ✓ Idle time: 234ms
[Nudge] ✓ UDP listener started on port 45001
[Nudge] ✓ Nudge is running
[Nudge]   Taking snapshots every 5 minutes
[Nudge]   ML-powered adaptive notifications enabled
[Nudge]   Confidence threshold: 98%
[Nudge]   Respond with: nudge-notify YES or nudge-notify NO
```

## Real-Time ML Decisions

### Scenario 1: ML Detects Productive Work (Skips Alert)

```
[Nudge] 4 min until next snapshot [ML: active]  (vscode, idle: 124ms)

[Nudge]   ML SKIP: Productive (confidence: 99.1%, avg: 94.2%)
[Nudge]   Stats: 23 predictions, 3 triggered, 20 skipped

[Nudge] 3 min until next snapshot [ML: active]  (vscode, idle: 89ms)
```

**What happened**: Model is 99.1% confident you're being productive in VS Code, so it suppresses the alert to avoid interrupting your flow.

---

### Scenario 2: ML Detects Unproductive Activity (Triggers Alert Early)

```
[Nudge] 3 min until next snapshot [ML: active]  (youtube, idle: 5234ms)

[Nudge]   ML TRIGGER: NOT productive (confidence: 98.4%, avg: 94.5%)
[Nudge]   Stats: 47 predictions, 4 triggered, 43 skipped
[Nudge]   ✓ ML-TRIGGERED SNAPSHOT (detected unproductive)

[Nudge]
[Nudge] ━━━ SNAPSHOT #4 ━━━
[Nudge]   App:       youtube
[Nudge]   Hash:      74829
[Nudge]   Idle:      5.2s
[Nudge]   Attention: 8.3 min
[Nudge]
[Nudge]   ❯ Waiting for response...
[Nudge]   Run: nudge-notify YES or nudge-notify NO
[Nudge]
SNAPSHOT
📸 Snapshot taken! Respond using the notification buttons.
```

**What happened**: Model is 98.4% confident you're being unproductive on YouTube. Instead of waiting the full 5 minutes, it triggers an alert after only 2 minutes to nudge you back to work.

---

### Scenario 3: ML Uncertain (Falls Back to Interval)

```
[Nudge] 2 min until next snapshot [ML: active]  (firefox, idle: 2341ms)

[Nudge]   ML: Low confidence (67.3%, avg: 93.8%) - waiting for interval

[Nudge] 1 min until next snapshot [ML: active]  (firefox, idle: 1892ms)

[Nudge]   ⏰ INTERVAL SNAPSHOT (ML low confidence or productive)

[Nudge]
[Nudge] ━━━ SNAPSHOT #5 ━━━
[Nudge]   App:       firefox
[Nudge]   Hash:      12847
[Nudge]   Idle:      1.9s
[Nudge]   Attention: 12.4 min
```

**What happened**: Model only has 67.3% confidence (below the 98% threshold). It safely falls back to the regular 5-minute interval rather than making a potentially wrong decision.

---

## ML Performance Summary (Every 10 Snapshots)

```
[Nudge]
[Nudge] ━━━ ML PERFORMANCE SUMMARY ━━━
[Nudge]   Predictions Made:        342
[Nudge]   Average Confidence:     94.2%
[Nudge]
[Nudge]   ML Triggered Alerts:    8 (detected unproductive)
[Nudge]   ML Skipped Alerts:      54 (detected productive)
[Nudge]   Interval Fallbacks:     13 (low confidence)
[Nudge]
[Nudge]   Alerts Prevented:       87.1% (interruptions avoided)
[Nudge] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**What this means**:
- **342 predictions**: Model checked your activity 342 times
- **94.2% avg confidence**: Model is very confident in its decisions
- **8 triggered**: Caught unproductive behavior 8 times before the interval
- **54 skipped**: Prevented 54 unnecessary interruptions during productive work
- **13 fallbacks**: Wasn't sure 13 times, used safe 5-minute interval
- **87.1% prevented**: You received 87.1% fewer interruptions than traditional mode!

---

## Complete Session Example

```bash
$ ./nudge-tray --ml

╔═══════════════════════════════════════════════════════╗
║        Nudge Tray - Productivity Tracker          ║
║        Version 1.1.0                                   ║
║        🧠 ML MODE ENABLED                         ║
╚═══════════════════════════════════════════════════════╝

🧠 Starting ML services...
  ✓ ML inference service started (socket: /tmp/nudge_ml.sock)
  ✓ Background trainer started
✓ ML services ready
✓ Nudge process started
  ML mode enabled - waiting for inference server connection...

[Nudge] ✓ Nudge is running
[Nudge]   ML-powered adaptive notifications enabled
[Nudge]   Confidence threshold: 98%

# ===== Hour 1: Productive Coding Session =====

[Nudge] 4 min until next snapshot [ML: active]  (vscode, idle: 89ms)
[Nudge]   ML SKIP: Productive (confidence: 99.2%, avg: 95.1%)
[Nudge]   Stats: 12 predictions, 0 triggered, 12 skipped

[Nudge] 3 min until next snapshot [ML: active]  (vscode, idle: 145ms)
[Nudge]   ML SKIP: Productive (confidence: 98.8%, avg: 95.3%)
[Nudge]   Stats: 24 predictions, 0 triggered, 24 skipped

# ... ML continues to skip alerts for 45 minutes of focused work ...

[Nudge] 1 min until next snapshot [ML: active]  (vscode, idle: 234ms)
[Nudge]   ⏰ INTERVAL SNAPSHOT (ML low confidence or productive)

[Nudge] ━━━ SNAPSHOT #1 ━━━
User responded: YES

# ===== Hour 2: Distraction Detected =====

[Nudge] 4 min until next snapshot [ML: active]  (terminal, idle: 123ms)
[Nudge]   ML SKIP: Productive (confidence: 99.0%, avg: 95.2%)

# User switches to YouTube
[Nudge]   Switched: terminal → youtube

[Nudge] 3 min until next snapshot [ML: active]  (youtube, idle: 3421ms)
[Nudge]   ML TRIGGER: NOT productive (confidence: 99.4%, avg: 95.3%)
[Nudge]   Stats: 156 predictions, 1 triggered, 155 skipped
[Nudge]   ✓ ML-TRIGGERED SNAPSHOT (detected unproductive)

[Nudge] ━━━ SNAPSHOT #2 ━━━
User responded: NO

# ===== Hour 3: Mixed Activity =====

[Nudge] 2 min until next snapshot [ML: active]  (slack, idle: 2134ms)
[Nudge]   ML: Low confidence (72.4%, avg: 93.8%) - waiting for interval

[Nudge]   ⏰ INTERVAL SNAPSHOT (ML low confidence or productive)
[Nudge] ━━━ SNAPSHOT #3 ━━━
User responded: NO

# ===== Performance Summary (after 10 snapshots) =====

[Nudge] ━━━ ML PERFORMANCE SUMMARY ━━━
[Nudge]   Predictions Made:        589
[Nudge]   Average Confidence:     94.7%
[Nudge]
[Nudge]   ML Triggered Alerts:    3 (detected unproductive)
[Nudge]   ML Skipped Alerts:      112 (detected productive)
[Nudge]   Interval Fallbacks:     7 (low confidence)
[Nudge]
[Nudge]   Alerts Prevented:       97.4% (interruptions avoided)
[Nudge] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

# ===== Background Trainer Updates =====

[ML Trainer] ✅ Sufficient new data for retraining: 52 new samples
[ML Trainer]
[ML Trainer] ============================================================
[ML Trainer] 🧠 STARTING TRAINING #2
[ML Trainer] ============================================================
[ML Trainer] 📂 Loading: /tmp/HARVEST.CSV
[ML Trainer]    Loaded 252 rows
[ML Trainer] 📊 Dataset:
[ML Trainer]    Productive: 152 (60.3%)
[ML Trainer]    Unproductive: 100 (39.7%)
[ML Trainer] 🏗️  Building standard model...
[ML Trainer] 🚀 Starting training...
[ML Trainer] Epoch 50/150 - loss: 0.2134 - accuracy: 0.9231
[ML Trainer] ✅ Model validation passed
[ML Trainer]
[ML Trainer] ============================================================
[ML Trainer] ✅ TRAINING #2 COMPLETED
[ML Trainer]    Accuracy: 94.2%
[ML Trainer]    Samples trained on: 252
[ML Trainer] ============================================================

[ML Inference] ✅ Model reloaded from ./model/productivity_model.keras
[ML Inference] 📊 New model ready for predictions
```

## Key Benefits Demonstrated

### 1. **Reduced Interruptions**
- Traditional mode: 12 alerts per hour (every 5 minutes)
- ML mode: ~3 alerts per hour (when actually unproductive)
- **75% fewer interruptions!**

### 2. **Faster Detection**
- Traditional mode: Always waits full 5 minutes
- ML mode: Triggers within 1-2 minutes when unproductive detected
- **2-3x faster feedback loop**

### 3. **Transparency**
- Every decision shows confidence percentage
- Running average tracks model performance
- Clear attribution (ML vs interval)

### 4. **Continuous Improvement**
- Background trainer retrains automatically
- Model gets better over time
- No manual intervention needed

### 5. **Safe Fallback**
- Always falls back to 5-minute interval when uncertain
- Never makes risky decisions
- Reliability maintained

## Installation Requirements

To see this in action, you need:

```bash
# Install Python dependencies
pip install tensorflow pandas numpy scikit-learn

# Generate sample data (for testing)
python3 generate_sample_data.py --samples 200

# Train initial model
python3 train_model.py /tmp/HARVEST.CSV

# Start with ML enabled
./nudge-tray --ml
```

## What to Watch For

### Good Performance Signs
- ✅ Average confidence >90%
- ✅ Alerts prevented >60%
- ✅ ML triggers catching real distractions
- ✅ ML skips during productive work

### Model Needs More Data
- ⚠️ Average confidence <75%
- ⚠️ Frequent fallbacks
- ⚠️ Incorrect predictions

**Solution**: Collect more diverse training examples, then retrain.

---

## Try It Yourself

1. Install dependencies: `pip install tensorflow pandas numpy scikit-learn`
2. Generate sample data: `python3 generate_sample_data.py`
3. Train model: `python3 train_model.py /tmp/HARVEST.CSV`
4. Run ML mode: `./nudge-tray --ml`
5. Watch the ML performance tracking in real-time!
