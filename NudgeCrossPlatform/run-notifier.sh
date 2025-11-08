#!/bin/bash
# Script to run the Nudge Notifier

echo "=== Starting Nudge Notifier ==="
echo ""

# Check for notify-send
if ! command -v notify-send &> /dev/null; then
    echo "WARNING: notify-send not found. Install with: sudo apt-get install libnotify-bin"
    echo ""
fi

cd NudgeNotifier

# Check if interval argument provided
if [ $# -eq 0 ]; then
    echo "Using default interval (5 minutes)"
    dotnet run
else
    echo "Using custom interval: $1"
    dotnet run -- "$1"
fi
