# Complete Nudge Workflow

This guide walks through the entire process from data collection to automated productivity predictions.

## Phase 1: Data Collection (Manual Labeling)

### Goal
Collect labeled training data by having you manually indicate when you're productive or not.

### Steps

1. **Start the Harvester**
   ```bash
   ./run-harvester.sh
   ```
   This monitors your activity continuously.

2. **Start the Notifier**
   ```bash
   ./run-notifier.sh
   ```
   This will nudge you every 5 minutes (customizable).

3. **Use Your Computer Normally**
   - Work, browse, code, whatever you normally do
   - The harvester silently tracks your activity

4. **Respond to Nudges**
   - Every 5 minutes, you'll get a desktop notification
   - Answer honestly: Were you productive?
   - Press Y for productive, N for not productive

5. **Collect Data for a Few Days**
   - Aim for at least 100 labeled examples
   - Try to label both productive AND unproductive moments
   - More data = better predictions

### What's Being Tracked?

For each snapshot, we record:
- Which application is in the foreground (as a hash)
- How long since your last keystroke
- How long since your last mouse movement
- How long you've been in the current app

### Data Location

All data is saved to `/tmp/HARVEST.CSV`

## Phase 2: Validate & Train the Model

### Step 1: Check Your Data

```bash
python validate_data.py /tmp/HARVEST.CSV
```

**Look for:**
- ✓ At least 20+ examples
- ✓ Both productive (1) and unproductive (0) labels
- ✓ Reasonable class balance (not 95% one class)

**Example output:**
```
✓ Column names match
✓ All 156 rows have valid data types
✓ All productivity labels are binary (0 or 1)
✓ Found 156 data points

📊 Data Statistics:
   Productive:     78 (50.0%)
   Not Productive: 78 (50.0%)
✓ Data has reasonable class balance

✅ CSV format is correct and ready for model training!
```

### Step 2: Train the Model

```bash
python train_model.py /tmp/HARVEST.CSV
```

**What happens:**
1. Loads your CSV data
2. Splits into training (80%) and test (20%) sets
3. Normalizes the features
4. Trains a neural network for 100 epochs (with early stopping)
5. Evaluates accuracy on test set
6. Saves model to `./model/`

**Example output:**
```
Loading data from /tmp/HARVEST.CSV...
Loaded 156 rows
After removing NaN: 156 rows

Class distribution:
  Productive: 78 (50.0%)
  Not productive: 78 (50.0%)

Training set: 124 samples
Test set: 32 samples

Building model...
Model: "sequential"
_________________________________________________________________
Layer (type)                Output Shape              Param #
=================================================================
dense (Dense)               (None, 10)                50
dense_1 (Dense)             (None, 10)                110
dense_2 (Dense)             (None, 1)                 11
=================================================================
Total params: 171
Trainable params: 171

Training model...
Epoch 1/100
...

Evaluating on test set...
Test Accuracy: 0.8750

✅ Model saved to ./model/
```

### Interpreting Results

- **Accuracy > 80%**: Great! The model learned your patterns
- **Accuracy 60-80%**: Okay, but could be better with more data
- **Accuracy < 60%**: Need more/better data or your behavior is very random
- **Accuracy > 95%**: Might be overfitting or very consistent patterns

## Phase 3: Make Predictions

### Option A: Test Individual Predictions

```bash
python predict.py
```

Then enter sample values:
```
Foreground app hash: -1867971589
Keyboard inactive (ms): 45000
Mouse inactive (ms): 38000
Attention span (ms): 120000
```

**Output:**
```
==================================================
PRODUCTIVITY PREDICTION
==================================================
Foreground App Hash: -1867971589
Keyboard Inactive:   45000ms (45.0s)
Mouse Inactive:      38000ms (38.0s)
Attention Span:      120000ms (120.0s)
--------------------------------------------------
Prediction:          NOT PRODUCTIVE ✗
Probability:         23.5%
Confidence:          76.5%
==================================================

💡 Possible reasons:
   - Both keyboard and mouse have been inactive for >30s
```

### Option B: Command Line Prediction

```bash
python predict.py 123456789 5000 2000 45000
```

## Phase 4: Automated Nudges (Future Enhancement)

Once you have a trained model, you could integrate it to:

1. **Real-time Monitoring**: Have the Harvester continuously predict productivity
2. **Smart Nudges**: Only nudge when model detects you're unproductive
3. **Auto-logging**: Automatically log data instead of asking every time
4. **Analytics Dashboard**: Visualize your productivity patterns

### Integration Ideas

**Modify NudgeHarvester to:**
- Call `predict.py` every minute
- If prediction shows unproductive for 5+ minutes → send alert
- Track productive hours per day

**Create a smart notifier:**
```python
# Pseudo-code
while True:
    prediction = model.predict(current_activity)

    if prediction == "unproductive":
        unproductive_time += 1

        if unproductive_time > 5:  # 5 minutes unproductive
            send_notification("Hey! Looks like you're distracted. Get back to work!")
            unproductive_time = 0
    else:
        unproductive_time = 0

    sleep(60)  # Check every minute
```

## Common Workflows

### Daily Use (Data Collection Phase)

```bash
# Morning - start monitoring
tmux new-session -d -s nudge-harvester './run-harvester.sh'
tmux new-session -d -s nudge-notifier './run-notifier.sh'

# Work normally, respond to nudges throughout the day

# Evening - check progress
python validate_data.py /tmp/HARVEST.CSV
```

### Weekly: Retrain Model

```bash
# Backup old model
mv model model.backup_$(date +%Y%m%d)

# Train with new data
python train_model.py /tmp/HARVEST.CSV

# Compare accuracy with previous week
```

### Testing Predictions

```bash
# Test on real-time data
tail -f /tmp/HARVEST.CSV | while read line; do
    # Parse CSV and predict
    python predict.py $(echo $line | cut -d',' -f1-4 | tr ',' ' ')
done
```

## Data Privacy Notes

All data stays local:
- ✓ No data sent to cloud/servers
- ✓ CSV stored in /tmp (cleared on reboot)
- ✓ App names hashed (not readable)
- ✓ You control all the data

To backup or share data:
```bash
# Backup (CSV only - model stays private)
cp /tmp/HARVEST.CSV ~/nudge_backup_$(date +%Y%m%d).csv

# Clear all data
rm /tmp/HARVEST.CSV
rm -rf ./model/
```

## Tips for Better Results

1. **Be Honest**: Label truthfully, not aspirationally
2. **Be Consistent**: Define "productive" the same way each time
3. **Variety**: Label during different activities and times
4. **Context**: Morning vs evening patterns might differ
5. **Threshold**: Adjust what you consider "productive" for your needs

## Troubleshooting

### "Not enough data to train!"
- Collect at least 20 labeled examples before training
- Run the notifier for a few hours with frequent nudges

### Low accuracy (<60%)
- Need more training data
- Your productivity patterns might be very irregular
- Try being more consistent with labeling

### High accuracy (>95%)
- Might be overfitting (model memorized data)
- Collect more diverse examples
- Could also mean very consistent behavior (good!)

### Model predicts same thing every time
- Check class balance: `python validate_data.py`
- Need examples of BOTH productive and unproductive
- Don't label everything the same way

## Next Steps

Once comfortable with the workflow:
- [ ] Increase nudge frequency during training
- [ ] Experiment with different intervals
- [ ] Build a dashboard to visualize patterns
- [ ] Add more features (time of day, day of week)
- [ ] Create automatic nudge based on predictions
