#!/bin/bash
# Script to run the Nudge Harvester

echo "=== Starting Nudge Harvester ==="
echo ""
echo "Checking dependencies..."

# Check for xdotool
if ! command -v xdotool &> /dev/null; then
    echo "WARNING: xdotool not found. Install with: sudo apt-get install xdotool"
fi

# Check for xprintidle
if ! command -v xprintidle &> /dev/null; then
    echo "WARNING: xprintidle not found. Install with: sudo apt-get install xprintidle"
fi

echo ""
cd NudgeHarvester
dotnet run
