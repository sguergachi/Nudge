# ML-Powered Adaptive Productivity Nudging

This document describes the machine learning system that enables personalized, adaptive productivity notifications in Nudge.

## Overview

The ML system learns from your behavior to predict when you're productive or not. Instead of always asking every 5 minutes, it adapts based on:

- **High Confidence (>98%)**: Nudge detects you're NOT productive → Sends alert immediately
- **Low Confidence (<98%)**: Nudge is uncertain → Falls back to regular 5-minute intervals
- **Productive Detection**: Nudge detects you're productive → Suppresses unnecessary alerts

This creates a personalized feedback loop that improves over time as the model learns your patterns.

## System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Main Components                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌────────────┐      ┌──────────────┐      ┌────────────────┐  │
│  │   nudge    │◄────►│ ML Inference │      │   Background   │  │
│  │  (C# app)  │      │   Service    │      │    Trainer     │  │
│  │            │      │  (Python)    │      │   (Python)     │  │
│  └────────────┘      └──────────────┘      └────────────────┘  │
│       │                     │                      │            │
│       │                     │                      │            │
│       ▼                     ▼                      ▼            │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │              /tmp/HARVEST.CSV                             │  │
│  │          (Productivity Log Database)                      │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Components

1. **nudge** (Main Application)
   - Tracks foreground app, idle time, attention span
   - Queries ML inference service for predictions
   - Falls back to interval-based alerts when ML unavailable
   - Collects labeled training data from user responses

2. **ML Inference Service** (`model_inference.py`)
   - Loads trained TensorFlow model
   - Provides real-time predictions via Unix domain socket
   - Returns confidence scores for adaptive behavior
   - Low-latency (<10ms per prediction)

3. **Background Trainer** (`background_trainer.py`)
   - Monitors productivity log for new data
   - Automatically retrains model when enough data accumulated
   - Validates models before deployment
   - Rolls back if performance degrades

## Getting Started

### Phase 1: Initial Data Collection (Days 1-7)

Start with regular interval-based notifications to collect initial training data:

```bash
# Start nudge-tray without ML (regular 5-minute intervals)
./nudge-tray --interval 5
```

**Goal**: Collect at least 100 labeled examples (about 8 hours of usage)

**Tips**:
- Be honest with your responses
- Respond to every notification
- Use for a full work day to capture different patterns

### Phase 2: First Training (After 100+ samples)

Once you have sufficient data, train your first model:

```bash
# Train the model
python3 train_model.py /tmp/HARVEST.CSV --model-dir ./model

# Expected output:
# 📊 Dataset: 150 samples
#    Productive: 90 (60.0%)
#    Unproductive: 60 (40.0%)
# 🏗️  Building standard model...
# 🚀 Starting training...
# ✅ Model saved to: ./model/
```

### Phase 3: Enable ML-Powered Notifications

Start nudge-tray with ML enabled (it automatically manages all services):

```bash
# Easy way - use the launcher
./start_nudge_ml.sh

# Or directly
./nudge-tray --ml --interval 5
```

Nudge Tray automatically starts:
- ML inference server (real-time predictions)
- Background trainer (continuous learning)
- Nudge process (productivity tracking)

Now the system will:
- ✅ Send alerts when ML is >98% confident you're NOT productive
- ⏭️  Skip alerts when ML is >98% confident you ARE productive
- 🔄 Fall back to 5-minute intervals when confidence is low
- 📈 Continuously improve as you provide more feedback

## Configuration Options

### Main Application (nudge-tray)

```bash
./nudge-tray [options]

Options:
  --ml              Enable ML-powered adaptive notifications
  --interval N      Fallback interval in minutes (default: 5)

Examples:
  ./nudge-tray                    # Data collection mode
  ./nudge-tray --ml               # ML with 5-min fallback
  ./nudge-tray --ml --interval 2  # ML with 2-min fallback
  ./start_nudge_ml.sh             # Recommended launcher
```

**Note**: Nudge Tray automatically manages all services (ML inference, background trainer, nudge process).
No need to manually start Python services!

### Inference Service

```bash
python3 model_inference.py [options]

Options:
  --model-dir PATH   Model directory (default: ./model)
  --socket PATH      Unix socket path (default: /tmp/nudge_ml.sock)
  --test             Test mode: send sample prediction

Examples:
  python3 model_inference.py
  python3 model_inference.py --model-dir /path/to/model
  python3 model_inference.py --test  # Test if server is working
```

### Background Trainer

```bash
python3 background_trainer.py [options]

Options:
  --csv PATH              CSV file path (default: /tmp/HARVEST.CSV)
  --model-dir PATH        Model directory (default: ./model)
  --min-new-samples N     Min new samples before retrain (default: 50)
  --min-total-samples N   Min total for first training (default: 100)
  --check-interval N      Check interval in seconds (default: 300)
  --architecture TYPE     Model type: lightweight/standard/deep

Examples:
  python3 background_trainer.py
  python3 background_trainer.py --min-new-samples 100
  python3 background_trainer.py --architecture deep
```

## Model Performance

### Confidence Threshold

The system uses **98% confidence** as the threshold for ML-based decisions:

- **High confidence NOT productive (>98%)**: Trigger alert immediately
- **High confidence productive (>98%)**: Suppress alert
- **Low confidence (<98%)**: Fall back to interval-based

This conservative threshold ensures:
- ✅ Minimal false positives (bothering you when productive)
- ✅ High accuracy when triggering alerts
- ✅ Graceful fallback when uncertain

### Training Requirements

| Metric | Minimum | Recommended | Optimal |
|--------|---------|-------------|---------|
| Initial training | 100 samples | 200 samples | 500+ samples |
| Retraining trigger | 50 new | 100 new | 200+ new |
| Class balance | 30/70 split | 40/60 split | 45/55 split |
| Training time | 30 seconds | 2 minutes | 5 minutes |

### Model Architectures

Choose based on your needs:

- **Lightweight**: Fast, low memory, good for limited data (<200 samples)
- **Standard**: Balanced performance, recommended for most users
- **Deep**: Best accuracy, requires more data (>500 samples)

```bash
# Train with different architectures
python3 train_model.py /tmp/HARVEST.CSV --architecture lightweight
python3 train_model.py /tmp/HARVEST.CSV --architecture standard
python3 train_model.py /tmp/HARVEST.CSV --architecture deep
```

## Monitoring & Debugging

### Check Inference Server Status

```bash
# Test if inference server is running
python3 model_inference.py --test

# Expected output:
# 🧪 Testing inference server...
# 📤 Sending: {'foreground_app': 12345, 'idle_time': 1000, ...}
# 📥 Response: {...}
# ✅ Model is working!
```

### View Prediction Logs

The inference server logs all predictions to stderr:

```
📊 Request #1: NOT_PRODUCTIVE (confidence: 99.2%, 8.3ms)
📊 Request #2: PRODUCTIVE (confidence: 87.5%, 7.1ms)
📊 Request #3: NOT_PRODUCTIVE (confidence: 98.8%, 6.9ms)
```

### View Training Logs

The background trainer logs training events:

```
⏳ Waiting for more data... need 30 more samples (70/100)
✅ Sufficient data for initial training: 150 samples
🧠 STARTING TRAINING #1
🏗️  Building standard model...
✅ TRAINING #1 COMPLETED
   Accuracy: 94.5%
   Samples trained on: 150
```

### Nudge ML Status

When running with `--ml`, nudge shows ML status:

```bash
# Main loop messages
  ML: NOT productive (confidence: 99.1%) - triggering alert
  ML: Productive (confidence: 98.5%) - skipping alert
  ML: Low confidence (67.3%) - waiting for interval
  2 min until next snapshot [ML: active]  (firefox, idle: 1234ms)
```

## Data Privacy

All data stays **local on your machine**:

- ✅ CSV file stored locally (`/tmp/HARVEST.CSV`)
- ✅ Model trained locally
- ✅ Predictions computed locally
- ✅ No data sent to any server
- ✅ All communication via Unix domain sockets (local only)

Application hashes are FNV-1a hashes, making them:
- Deterministic (same app → same hash)
- Privacy-preserving (hash cannot be reversed)
- Collision-resistant (different apps → different hashes)

## Troubleshooting

### ML Not Available

**Symptoms**: `[ML: fallback]` status, no ML predictions

**Fixes**:
1. Check if inference server is running:
   ```bash
   python3 model_inference.py --test
   ```

2. Check if socket exists:
   ```bash
   ls -la /tmp/nudge_ml.sock
   ```

3. Check if model exists:
   ```bash
   ls -la ./model/productivity_model.keras
   ```

### Low Accuracy

**Symptoms**: Model makes poor predictions, accuracy <75%

**Fixes**:
1. Collect more data (aim for 200+ samples)
2. Ensure class balance (30% productive, 70% not productive)
3. Be consistent with labels
4. Try different architecture:
   ```bash
   python3 train_model.py /tmp/HARVEST.CSV --architecture deep
   ```

### Training Fails

**Symptoms**: Training errors, cannot save model

**Fixes**:
1. Validate data:
   ```bash
   python3 validate_data.py /tmp/HARVEST.CSV
   ```

2. Check TensorFlow installation:
   ```bash
   python3 -c "import tensorflow as tf; print(tf.__version__)"
   ```

3. Check disk space:
   ```bash
   df -h ./model
   ```

## Advanced Usage

### Multiple Models

Run A/B tests with different models:

```bash
# Train multiple architectures
python3 train_model.py /tmp/HARVEST.CSV --model-dir ./model_light --architecture lightweight
python3 train_model.py /tmp/HARVEST.CSV --model-dir ./model_deep --architecture deep

# Compare performance
python3 model_inference.py --model-dir ./model_light &
./nudge --ml  # Test for a day
killall python3

python3 model_inference.py --model-dir ./model_deep &
./nudge --ml  # Test for a day
```

### Custom Confidence Threshold

Edit `nudge.cs` line 51 to adjust threshold:

```csharp
const double ML_CONFIDENCE_THRESHOLD = 0.95;  // 95% instead of 98%
```

Lower threshold = More aggressive ML triggers
Higher threshold = More conservative, fewer false positives

### Export Model Metrics

```bash
# View TensorBoard logs
tensorboard --logdir ./model/logs

# Open browser to: http://localhost:6006
```

## Performance Benchmarks

Typical performance on modern hardware:

| Operation | Time | Notes |
|-----------|------|-------|
| Single prediction | <10ms | Unix socket + inference |
| Model load time | 200-500ms | At inference server startup |
| Training (100 samples) | 30-60s | CPU-only, standard model |
| Training (500 samples) | 2-5min | CPU-only, deep model |

## Roadmap

Future improvements planned:

- [ ] Time-of-day features (morning vs afternoon patterns)
- [ ] Day-of-week features (weekday vs weekend)
- [ ] Multi-user support
- [ ] Model export for sharing anonymized patterns
- [ ] Web dashboard for visualizing productivity patterns
- [ ] Integration with calendar/task management tools

## Support

For issues or questions:
1. Check this README
2. Validate your data: `python3 validate_data.py /tmp/HARVEST.CSV`
3. Test inference: `python3 model_inference.py --test`
4. Check logs in stderr

## License

Same as parent Nudge project.
