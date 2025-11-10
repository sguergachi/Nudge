#!/bin/bash
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# Nudge ML Launcher - Start all ML components
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
RESET='\033[0m'

# Configuration
CSV_FILE="${CSV_FILE:-/tmp/HARVEST.CSV}"
MODEL_DIR="${MODEL_DIR:-./model}"
INTERVAL="${INTERVAL:-5}"
ARCHITECTURE="${ARCHITECTURE:-standard}"

# Function to print colored output
info() {
    echo -e "${CYAN}ℹ${RESET}  $1"
}

success() {
    echo -e "${GREEN}✓${RESET}  $1"
}

error() {
    echo -e "${RED}✗${RESET}  $1"
}

warning() {
    echo -e "${YELLOW}⚠${RESET}  $1"
}

header() {
    echo ""
    echo -e "${BOLD}━━━ $1 ━━━${RESET}"
}

# Print banner
echo ""
echo -e "${BOLD}${CYAN}╔═══════════════════════════════════════════════════════════╗${RESET}"
echo -e "${BOLD}${CYAN}║          Nudge ML-Powered Productivity Tracker            ║${RESET}"
echo -e "${BOLD}${CYAN}╚═══════════════════════════════════════════════════════════╝${RESET}"
echo ""

# Check dependencies
header "Checking Dependencies"

check_command() {
    if command -v "$1" &> /dev/null; then
        success "$1 found"
        return 0
    else
        error "$1 not found"
        return 1
    fi
}

deps_ok=true
check_command python3 || deps_ok=false
check_command dotnet || deps_ok=false

if ! $deps_ok; then
    error "Missing dependencies. Please install:"
    echo "  - Python 3.8+"
    echo "  - .NET 8.0+"
    exit 1
fi

# Check Python packages
info "Checking Python packages..."
if python3 -c "import tensorflow, pandas, numpy, sklearn" 2>/dev/null; then
    success "Python packages OK"
else
    error "Missing Python packages"
    echo ""
    echo "Install with:"
    echo "  pip install tensorflow pandas numpy scikit-learn"
    exit 1
fi

# Check data
header "Checking Data"

if [ -f "$CSV_FILE" ]; then
    sample_count=$(tail -n +2 "$CSV_FILE" | wc -l)
    success "CSV file found: $CSV_FILE"
    info "Sample count: $sample_count"

    if [ "$sample_count" -lt 100 ]; then
        warning "Less than 100 samples - consider collecting more data first"
        info "Run without --ml flag to collect initial data"
    fi
else
    warning "CSV file not found: $CSV_FILE"
    info "Will be created on first run"
fi

# Check model
header "Checking Model"

if [ -f "$MODEL_DIR/productivity_model.keras" ]; then
    success "Model found: $MODEL_DIR/productivity_model.keras"
    model_exists=true
else
    warning "No trained model found"
    info "Train a model first with: python3 train_model.py $CSV_FILE"
    model_exists=false
fi

# Determine mode
echo ""
if $model_exists && [ "$sample_count" -ge 100 ]; then
    mode="ml"
    echo -e "${GREEN}${BOLD}Mode: ML-Powered (Adaptive)${RESET}"
    echo ""
    info "Nudge will use ML predictions to adapt notification timing"
    info "Confidence threshold: 98%"
else
    mode="interval"
    echo -e "${YELLOW}${BOLD}Mode: Interval-Based (Data Collection)${RESET}"
    echo ""
    if ! $model_exists; then
        info "No model available - collecting training data"
        info "Train model after collecting 100+ samples"
    else
        info "Insufficient data - collecting more samples"
    fi
fi

# Ask user confirmation
echo ""
read -p "$(echo -e ${CYAN}❯${RESET} Continue? [Y/n]: )" -n 1 -r
echo ""

if [[ ! $REPLY =~ ^[Yy]$ ]] && [[ ! -z $REPLY ]]; then
    info "Cancelled"
    exit 0
fi

# Clean up function
cleanup() {
    echo ""
    header "Shutting Down"

    if [ ! -z "$INFERENCE_PID" ]; then
        info "Stopping inference server (PID: $INFERENCE_PID)"
        kill $INFERENCE_PID 2>/dev/null || true
    fi

    if [ ! -z "$TRAINER_PID" ]; then
        info "Stopping background trainer (PID: $TRAINER_PID)"
        kill $TRAINER_PID 2>/dev/null || true
    fi

    success "Cleanup complete"
    exit 0
}

trap cleanup EXIT INT TERM

# Start components
header "Starting Components"

INFERENCE_PID=""
TRAINER_PID=""

if [ "$mode" = "ml" ]; then
    # Start inference server
    info "Starting ML inference server..."
    python3 model_inference.py --model-dir "$MODEL_DIR" --socket /tmp/nudge_ml.sock 2>&1 | sed 's/^/  [INFERENCE] /' &
    INFERENCE_PID=$!

    # Wait for socket to be created
    for i in {1..10}; do
        if [ -S /tmp/nudge_ml.sock ]; then
            success "Inference server ready (PID: $INFERENCE_PID)"
            break
        fi
        sleep 0.5
    done

    if [ ! -S /tmp/nudge_ml.sock ]; then
        error "Inference server failed to start"
        exit 1
    fi

    # Test inference server
    info "Testing inference server..."
    if python3 model_inference.py --test >/dev/null 2>&1; then
        success "Inference server test passed"
    else
        warning "Inference server test failed - but continuing anyway"
    fi

    # Start background trainer
    info "Starting background trainer..."
    python3 background_trainer.py \
        --csv "$CSV_FILE" \
        --model-dir "$MODEL_DIR" \
        --architecture "$ARCHITECTURE" \
        --min-new-samples 50 \
        --check-interval 300 \
        2>&1 | sed 's/^/  [TRAINER] /' &
    TRAINER_PID=$!
    success "Background trainer started (PID: $TRAINER_PID)"

    sleep 1
fi

# Start main nudge application
echo ""
header "Starting Nudge"
echo ""

if [ "$mode" = "ml" ]; then
    info "Starting with ML-powered adaptive notifications..."
    echo ""
    ./nudge --ml --interval "$INTERVAL" "$CSV_FILE"
else
    info "Starting in data collection mode (interval-based)..."
    echo ""
    ./nudge --interval "$INTERVAL" "$CSV_FILE"
fi
