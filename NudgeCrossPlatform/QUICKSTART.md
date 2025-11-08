# Quick Start Guide

Get Nudge running on Linux in 5 minutes!

## Prerequisites

```bash
# Install required Linux tools
sudo apt-get update
sudo apt-get install xdotool xprintidle libnotify-bin

# Install .NET 8 SDK (if not already installed)
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
export PATH="$HOME/.dotnet:$PATH"
```

## Build

```bash
cd NudgeCrossPlatform
dotnet build
```

## Run

### Terminal 1: Start the Harvester

```bash
./run-harvester.sh
```

You should see:
```
=== Nudge Harvester (Cross-Platform) ===
Monitoring user activity for productivity tracking...
Saving harvest data to: /tmp/HARVEST.CSV
Waiting for commands...
```

### Terminal 2: Start the Notifier

```bash
./run-notifier.sh
```

You should see:
```
=== Nudge Notifier (Cross-Platform) ===
Sending productivity nudges...
Nudge interval: 5 minutes
```

## Test It Out

In the Notifier terminal, press `n` to send a test nudge:
- You'll see a desktop notification
- Answer Y or N when prompted
- Check `/tmp/HARVEST.CSV` to see your data!

## What's Happening?

1. **Harvester** monitors your activity every second
2. **Notifier** sends a nudge every 5 minutes
3. When nudged, you label if you were productive
4. Data is saved to CSV for machine learning training

## Customization

Change nudge interval to 2 minutes:
```bash
./run-notifier.sh "00:02:00"
```

## Next Steps

- Collect data for a few days
- Train the ML model (see main README.md)
- Let the AI learn your productivity patterns!

## Troubleshooting

**Can't detect active window?**
- Make sure you're on X11, not Wayland: `echo $XDG_SESSION_TYPE`

**Desktop notifications not working?**
- Install: `sudo apt-get install libnotify-bin`

For more help, see [README.md](README.md)
