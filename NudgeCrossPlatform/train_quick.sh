#!/bin/bash
# Quick training script for fast local iteration
# Perfect for testing and development

echo "🚀 NUDGE QUICK TRAINER"
echo "====================="
echo ""
echo "This will train a model quickly using:"
echo "  ✓ CPU-only mode (no GPU needed)"
echo "  ✓ Lightweight model (less memory)"
echo "  ✓ Quick training (30 epochs max)"
echo ""

# Default CSV path
CSV_FILE="${1:-/tmp/HARVEST.CSV}"

if [ ! -f "$CSV_FILE" ]; then
    echo "❌ Error: CSV file not found: $CSV_FILE"
    echo ""
    echo "Usage: ./train_quick.sh [path_to_csv]"
    echo "Example: ./train_quick.sh /tmp/HARVEST.CSV"
    exit 1
fi

echo "📁 Training data: $CSV_FILE"
echo ""

# Validate data first
echo "Step 1: Validating data..."
python validate_data.py "$CSV_FILE" || exit 1

echo ""
echo "Step 2: Training model (this will take 1-2 minutes)..."
python train_model.py "$CSV_FILE" --quick --lightweight --cpu-only

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Training complete!"
    echo ""
    echo "Test your model with:"
    echo "  python predict.py"
else
    echo ""
    echo "❌ Training failed. Check the errors above."
    exit 1
fi
