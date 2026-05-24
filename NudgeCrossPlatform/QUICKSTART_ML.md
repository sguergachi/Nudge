# Quick Start: ML-Powered Productivity Nudging

Get up and running with intelligent, adaptive productivity notifications in 3 steps.

## Prerequisites

```bash
# Install Python dependencies
pip install scikit-learn joblib pandas numpy

# Verify
python3 -c "import sklearn, joblib; print('OK')"
```

## Step 1: Collect Initial Data (1–2 days)

Start the tray in data-collection mode. Answer every prompt honestly.

```bash
./nudge-tray --interval 5
```

**Goal**: 100+ labeled examples (~8 hours of usage). The AI Brain tab shows a running count under **Model Training**.

## Step 2: Train Your Model (~5–30 seconds)

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

## Step 3: Enable ML Mode

```bash
./nudge-tray --ml
```

The tray automatically starts:
- ML inference server on `127.0.0.1:45002`
- Background trainer (retrains as you collect more data)
- Harvest subprocess (V2 engine + feature extraction)

Check the **AI Brain tab** — the badge should show **AI: Active**.

## How It Works

```
Interval-only (no --ml)
  Ask every 5–10 minutes regardless of what you're doing.

ML mode (--ml)
  Every 60s → run inference on 26 V2 Harvest Engine features
  ├─ High confidence NOT productive → nudge now (skip the wait)
  ├─ High confidence productive     → skip nudge entirely
  └─ Low confidence                 → fall back to 5–10 min interval
```

## Watching It in Action

The **AI Brain tab** in the tray window shows everything in real time:

- **In Focus Now** — current app, browser tab, sensor fusion quality (Green=Trusted / Amber=Usable / Red=Poor), latest ML prediction score
  - Expand **▸ Sensor Signals** to see all fused signals: focus source, idle, category, domain, switches, app share
- **Next AI Check** — countdown to the next ML inference (60s cycle)
- **Prediction History** — chart of recent productivity scores
- **Recent Checks** — event log with confidence labels (`low conf` = ML not certain, fell back to interval)

## Troubleshooting

### "No model found"
```bash
python3 train_model.py ~/.nudge/HARVEST.CSV
```

### "Insufficient data"
```bash
# Count current samples
tail -n +2 ~/.nudge/HARVEST.CSV | wc -l
# Need at least 100; recommend 200+
```

### "ML inference server unreachable"
```bash
# Check if port 45002 is listening
ss -tlnp | grep 45002

# Start manually to see errors
python3 model_inference.py --model-dir ~/.nudge/model
```

### Countdown stuck at "Checking now..."
The 60s countdown resets even when ML is offline (fallback mode keeps the timer moving). If it appears permanently stuck, restart nudge-tray.

### Low accuracy (<75%)
- Collect more data (200+ samples)
- Be consistent in how you define "productive"
- Try: `python3 train_model.py ~/.nudge/HARVEST.CSV --architecture standard`

## Configuration

### Change fallback interval
```bash
./nudge-tray --ml --interval 3   # 3-minute fallback
```

### Custom confidence threshold
Edit `nudge.cs` / `nudge_build.cs`:
```csharp
const double ML_CONFIDENCE_THRESHOLD = 0.95;  // default is higher
```

Lower = more aggressive ML triggers. Higher = more conservative.

## Tips for Best Results

- Answer **every** notification honestly — that's your training data
- Use Nudge for full work days, not just an hour
- Include varied activities: deep coding, meetings, browsing, communication
- Aim for a mix of productive and unproductive examples
- Let the model run for a few days before judging its accuracy

For full documentation see [ML_README.md](ML_README.md).
