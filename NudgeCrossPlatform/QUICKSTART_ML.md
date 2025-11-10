# Quick Start: ML-Powered Productivity Nudging

Get up and running with intelligent, adaptive productivity notifications in 3 steps.

## Prerequisites

```bash
# Install dependencies
pip install tensorflow pandas numpy scikit-learn

# Verify installation
python3 -c "import tensorflow; print('TensorFlow version:', tensorflow.__version__)"
```

## Quick Start

### Step 1: Collect Initial Data (1-2 days)

Start Nudge Tray in data collection mode. Answer honestly for best results.

```bash
# Start collecting data (5-minute intervals)
./nudge-tray --interval 5

# Or use the launcher
./start_nudge_ml.sh
```

**Goal**: Collect at least 100 labeled examples (~8 hours of usage)

### Step 2: Train Your Model (30 seconds)

Once you have 100+ samples, train your personalized model:

```bash
python3 train_model.py /tmp/HARVEST.CSV --model-dir ./model
```

Expected output:
```
📊 Dataset: 150 samples
   Productive: 90 (60.0%)
   Unproductive: 60 (40.0%)
🏗️  Building standard model...
🚀 Starting training...
✅ Model saved to: ./model/
📊 Final Evaluation
   accuracy: 0.9333
   precision: 0.9500
   recall: 0.9130
```

### Step 3: Enable ML Mode (Adaptive Notifications)

Start Nudge Tray with ML enabled:

```bash
./start_nudge_ml.sh
```

Nudge Tray automatically manages:
- ✅ ML inference server (real-time predictions)
- ✅ Background trainer (continuous learning)
- ✅ Nudge process (adaptive notifications)
- ✅ System tray GUI (easy responses)

## How It Works

```
Traditional Mode (Interval-Based)
┌─────────────────────────────────────────┐
│  Alert every 5 minutes, no matter what │
└─────────────────────────────────────────┘

ML Mode (Adaptive)
┌─────────────────────────────────────────────────────────────┐
│  High confidence NOT productive (>98%) → Alert immediately  │
│  High confidence productive (>98%)     → Skip alert         │
│  Low confidence (<98%)                 → Fall back to 5min  │
└─────────────────────────────────────────────────────────────┘
```

## Usage Examples

### Basic Usage

```bash
# Data collection mode (no ML)
./nudge-tray --interval 5

# ML-powered mode (recommended)
./nudge-tray --ml --interval 5

# ML mode with 2-minute fallback
./nudge-tray --ml --interval 2

# Or use the convenient launcher
./start_nudge_ml.sh
```

### What Nudge Tray Provides

- 🖥️ System tray icon (always accessible)
- 📢 Desktop notifications with buttons
- 🔄 Automatic process management (no manual service starting)
- 🧠 ML service lifecycle management
- 🛑 Clean shutdown of all components

### Test Your Setup

```bash
# Test inference server
python3 model_inference.py --test

# Validate your data
python3 validate_data.py /tmp/HARVEST.CSV

# Check sample count
tail -n +2 /tmp/HARVEST.CSV | wc -l
```

## Understanding ML Predictions

When running with `--ml`, you'll see messages like:

```
✓ ML inference server connected
ML: NOT productive (confidence: 99.1%) - triggering alert
  ━━━ SNAPSHOT #42 ━━━
  App: slack
  ...

ML: Productive (confidence: 98.5%) - skipping alert
  2 min until next snapshot [ML: active]

ML: Low confidence (67.3%) - waiting for interval
  1 min until next snapshot [ML: active]
```

## Configuration

### Environment Variables

```bash
# Customize paths
CSV_FILE=/data/productivity.csv ./start_nudge_ml.sh
MODEL_DIR=/data/model ./start_nudge_ml.sh

# Customize training
ARCHITECTURE=deep ./start_nudge_ml.sh
INTERVAL=2 ./start_nudge_ml.sh
```

### Edit Constants

To change confidence threshold, edit `nudge.cs` line 51:

```csharp
const double ML_CONFIDENCE_THRESHOLD = 0.98;  // 98% confidence
```

## Troubleshooting

### "No model found"

**Solution**: Train a model first
```bash
python3 train_model.py /tmp/HARVEST.CSV
```

### "Insufficient data"

**Solution**: Collect more samples
```bash
# Current samples
tail -n +2 /tmp/HARVEST.CSV | wc -l

# Need at least 100, recommend 200+
./nudge --interval 5  # Keep collecting
```

### "ML inference server unreachable"

**Solution**: Start the inference server
```bash
# Check if running
ps aux | grep model_inference

# Start it
python3 model_inference.py --model-dir ./model
```

### Low Accuracy (<75%)

**Solutions**:
1. Collect more data (aim for 200+ samples)
2. Be consistent with your responses
3. Try different architecture:
   ```bash
   python3 train_model.py /tmp/HARVEST.CSV --architecture deep
   ```

## Performance Tuning

### More Aggressive ML (Lower Threshold)

Edit confidence threshold to 95% instead of 98%:
```csharp
// nudge.cs line 51
const double ML_CONFIDENCE_THRESHOLD = 0.95;
```

Result: More ML-based triggers, may have more false positives

### More Conservative (Higher Threshold)

Edit confidence threshold to 99%:
```csharp
const double ML_CONFIDENCE_THRESHOLD = 0.99;
```

Result: Fewer ML-based triggers, very high accuracy when triggered

### Faster Retraining

```bash
python3 background_trainer.py --min-new-samples 20 --check-interval 60
```

Result: Model updates every 20 new samples, checks every minute

## Data & Privacy

All data stays on your machine:
- ✅ CSV stored locally
- ✅ Model trained locally
- ✅ Predictions computed locally
- ✅ No network traffic
- ✅ App names hashed (cannot be reversed)

## Next Steps

1. **Collect 100+ samples** - Run for a full work day
2. **Train your model** - `python3 train_model.py /tmp/HARVEST.CSV`
3. **Enable ML mode** - `./start_nudge_ml.sh`
4. **Observe behavior** - Watch how it learns your patterns
5. **Improve over time** - Model gets better with more data

## Tips for Best Results

### During Data Collection
- ✅ Answer every notification honestly
- ✅ Use for full work days (not just an hour)
- ✅ Include different activities (coding, meetings, browsing)
- ✅ Aim for 200+ samples before enabling ML

### During ML Mode
- ✅ Still answer interval-based notifications
- ✅ Let model run for a few days to stabilize
- ✅ Check accuracy periodically
- ✅ Retrain if patterns change significantly

### For Best Accuracy
- ✅ Be consistent with what "productive" means to you
- ✅ Collect diverse examples (different apps, times of day)
- ✅ Balance productive/unproductive examples (aim for 40/60 split)
- ✅ Update model regularly with new data

## Files Overview

```
NudgeCrossPlatform/
├── nudge.cs                  # Main application (with ML integration)
├── train_model.py            # Model training script
├── model_inference.py        # Real-time inference server
├── background_trainer.py     # Continuous learning service
├── validate_data.py          # Data validation tool
├── start_nudge_ml.sh         # Convenient launcher
├── ML_README.md              # Full documentation
└── QUICKSTART_ML.md          # This file

/tmp/
├── HARVEST.CSV               # Your productivity log
└── nudge_ml.sock             # Inference server socket

./model/
├── productivity_model.keras  # Trained model
├── scaler.json               # Feature normalization
├── trainer_state.json        # Training history
├── checkpoints/              # Model checkpoints
└── logs/                     # TensorBoard logs
```

## Support

For detailed documentation, see [ML_README.md](ML_README.md)

Common issues:
- Model not found → Train first with `train_model.py`
- Low accuracy → Collect more diverse data
- Server unreachable → Start `model_inference.py`
- TensorFlow errors → Check Python environment

## Example Session

```bash
# Day 1: Collect data
$ ./nudge-tray --interval 5
[... system tray icon appears, use normally for 8 hours ...]

# After work: Check progress
$ tail -n +2 /tmp/HARVEST.CSV | wc -l
127

# Day 2: Train model
$ python3 train_model.py /tmp/HARVEST.CSV
📊 Dataset: 127 samples
✅ Model saved to: ./model/

# Enable ML mode
$ ./start_nudge_ml.sh

Mode: ML-Powered (Adaptive)
🧠 Starting ML services...
  ✓ ML inference service started
  ✓ Background trainer started
✓ ML services ready
[Nudge Tray launches with system tray icon]

# Check tray menu shows: "🧠 ML: Active"

[... system learns and adapts over time ...]
```

## What's Next?

After using ML mode for a week:
- Check model accuracy in logs
- Review TensorBoard visualizations: `tensorboard --logdir ./model/logs`
- Experiment with different architectures
- Share your results (anonymously) with the community

Happy productive work! 🚀
