# ML-Powered Adaptive Productivity Nudging

This document describes the machine learning system that enables personalized, adaptive productivity notifications in Nudge.

## Overview

The ML system learns from your behavior to predict when you're productive or not. Instead of always asking every 5–10 minutes, it adapts based on:

- **High Confidence, Not Productive**: Nudge detects you're NOT productive → alert fires immediately
- **Low Confidence**: Nudge is uncertain → falls back to the random 5–10 min interval
- **High Confidence, Productive**: Nudge detects you're productive → alert suppressed

This creates a personalized feedback loop that improves over time as the model learns your patterns.

## System Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                         nudge-tray (GUI)                         │
│  AI Brain tab ← MLDATA/HARVEST/APPFOCUS/MLNEXT (stdout)         │
│  Settings, Analytics, Model Training accordion                   │
└─────────────────────────────┬────────────────────────────────────┘
                              │ spawns
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                    nudge.dll (harvest subprocess)                │
│                                                                  │
│  V2 Harvest Engine                                               │
│  ├─ ActivityContext (focus source, signal quality, domain, …)    │
│  └─ FeatureVectorV2 (26 features, 300s rolling windows)          │
│                                                                  │
│  Every 60s ──────────────────────────────────────────────────►  │
│                                          ML Inference Service    │
│                                          127.0.0.1:45002 (TCP)  │
│                                          scikit-learn model      │
└──────────────────────────────────────────────────────────────────┘
                              │
                              │ trains
                              ▼
                    Background Trainer
                    (monitors HARVEST.CSV, retrains automatically)
```

### Components

1. **nudge-tray** — Avalonia GUI, manages all subprocesses, shows AI Brain tab
2. **nudge.dll** — harvest subprocess; tracks focus, builds feature vectors, queries ML every 60s
3. **ML Inference Service** (`model_inference.py`) — scikit-learn model served over TCP 127.0.0.1:45002
4. **Background Trainer** (`background_trainer.py`) — watches HARVEST.CSV, retrains when enough new data

## Getting Started

### Phase 1: Initial Data Collection (Days 1–7)

Start the tray without ML to collect labeled training data:

```bash
./nudge-tray --interval 5
```

**Goal**: Collect at least 100 labeled examples (~8 hours of usage). Answer the nudge prompts honestly.

### Phase 2: First Training (After 100+ samples)

```bash
python3 train_model.py ~/.nudge/HARVEST.CSV --model-dir ~/.nudge/model
```

Expected output:
```
📊 Dataset: 150 samples
   Productive: 90 (60.0%)
   Unproductive: 60 (40.0%)
🏗️  Building lightweight model...
✅ Model saved to: ~/.nudge/model/productivity_model.joblib
```

### Phase 3: Enable ML Mode

```bash
./nudge-tray --ml
```

Nudge Tray automatically starts:
- ML inference server (TCP 127.0.0.1:45002)
- Background trainer (continuous learning)
- Harvest subprocess (feature extraction + nudge logic)

## AI Brain Tab

The **AI Brain tab** in the tray window shows the ML system in real time:

- **In Focus Now** — current foreground app, browser tab name, sensor fusion quality (Green=Trusted / Amber=Usable / Red=Poor), live ML prediction score
- **Sensor Signals** (expandable) — signal quality, focus source, idle time, category (Work/Entertainment/Communication), activity (switches, app share), domain
- **Next AI Check** — countdown progress bar (60s ML check interval)
- **Prediction History** — gradient area chart of recent productivity scores
- **Recent Checks** — timestamped event log with confidence labels
- **Model Training** — training status, accuracy, sample count; "▸ Details" for full log

## Configuration

### nudge-tray options

```
--ml              Enable ML-powered adaptive notifications
--interval N      Fallback interval in minutes (default: 5)
--harvest-engine  v1 or v2 (default: v2)
```

### Confidence Threshold

Defined in `nudge.cs` as `ML_CONFIDENCE_THRESHOLD`. The default threshold means:

- Nudge fires immediately only when the model is confident you're NOT productive
- Productive sessions with high confidence suppress the nudge
- Low-confidence checks fall back to the interval timer

### Inference Service

```bash
python3 model_inference.py [options]

Options:
  --model-dir PATH   Model directory (default: ./model)
  --host HOST        Bind host (default: 127.0.0.1)
  --port PORT        TCP port (default: 45002)
```

### Background Trainer

```bash
python3 background_trainer.py [options]

Options:
  --csv PATH              CSV file path (default: ~/.nudge/HARVEST.CSV)
  --model-dir PATH        Model directory (default: ~/.nudge/model)
  --min-new-samples N     Min new samples before retrain (default: 50)
  --min-total-samples N   Min total for first training (default: 100)
   --check-interval N      Check interval in seconds (default: 15)
  --architecture TYPE     Model type: lightweight/standard
```

## Model Performance

### Training Requirements

| Metric | Minimum | Recommended | Optimal |
|--------|---------|-------------|---------|
| Initial training | 100 samples | 200 samples | 500+ samples |
| Retraining trigger | 50 new | 100 new | 200+ new |
| Class balance | 30/70 split | 40/60 split | 45/55 split |
| Training time | ~5 seconds | ~15 seconds | ~60 seconds |

### Model Architectures

- **Lightweight**: Fast, low memory, good for limited data (<200 samples). Default.
- **Standard**: Balanced performance, recommended for 200+ samples.

```bash
python3 train_model.py ~/.nudge/HARVEST.CSV --architecture lightweight
python3 train_model.py ~/.nudge/HARVEST.CSV --architecture standard
```

## Monitoring & Debugging

### Test Inference Server

```bash
python3 model_inference.py --test
```

### Check if Server is Running

```bash
# Check port 45002
ss -tlnp | grep 45002
# or
lsof -i :45002
```

### nudge-tray Logs

When running with `--ml`, the tray console shows:
```
✓ ML inference server connected
[Nudge] ML SKIP: Productive (confidence: 98.8%, avg: 95.3%)
[Nudge] ML TRIGGER: NOT productive (confidence: 99.1%, avg: 94.2%)
[Nudge] ML: Low confidence (67.3%) - waiting for interval
[Nudge] ⏰ INTERVAL SNAPSHOT (ML low confidence or productive)
```

## Data Privacy

All data stays **local on your machine**:

- CSV files stored in `~/.nudge/`
- Model trained locally with scikit-learn
- Predictions computed locally over loopback TCP
- No data sent to any external server
- App names stored as FNV-1a hashes (non-reversible) in ML features

## Troubleshooting

### ML Not Available / Fallback Mode

**Symptoms**: `[ML: fallback]` in tray logs, AI Brain tab shows "Checking now..."

1. Check if inference server started: `ss -tlnp | grep 45002`
2. Check model exists: `ls ~/.nudge/model/productivity_model.joblib`
3. Check Python deps: `python3 -c "import joblib, sklearn; print('OK')"`

### Stuck "Checking now..." in AI Brain Tab

The countdown resets every 60s even when no ML server is running (fallback mode). If it appears stuck past 60s, restart nudge-tray.

### Low Accuracy

1. Collect more data (aim for 200+ samples)
2. Be consistent — define what "productive" means to you and apply it uniformly
3. Ensure class balance: both productive and unproductive examples
4. Try `--architecture standard` once you have 200+ samples

### Training Fails

```bash
# Validate data
python3 validate_data.py ~/.nudge/HARVEST.CSV

# Check Python dependencies
python3 -c "import sklearn, joblib, pandas, numpy; print('All OK')"
```

## Files Overview

```
NudgeCrossPlatform/
├── nudge.cs / nudge_build.cs     # Main harvest subprocess (with ML integration)
├── nudge-tray.cs                 # Tray UI + subprocess management
├── AnalyticsWindow.cs            # Analytics + AI Brain tab
├── NudgeJsonContext.cs           # IPC DTOs (MLLiveEvent, HarvestSignal, …)
├── NudgeCore.TestableLogic.cs    # V2 Harvest Engine, BrowserDetector, feature extraction
├── model_inference.py            # scikit-learn inference server (TCP 45002)
├── background_trainer.py         # Continuous learning service
├── train_model.py                # Model training script
├── validate_data.py              # Data validation tool
├── ML_README.md                  # This file
└── QUICKSTART_ML.md              # Quick start guide

~/.nudge/
├── HARVEST.CSV                   # Labeled productivity snapshots
├── ACTIVITY_LOG.CSV              # Minute-by-minute activity log
├── tray-settings.json            # Persisted tray preferences
└── model/
    ├── productivity_model.joblib # Trained scikit-learn model
    ├── scaler.json               # Feature normalization params
    └── trainer_state.json        # Training history
```
