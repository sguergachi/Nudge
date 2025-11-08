# Nudge (Cross-Platform Edition)

A cross-platform productivity monitoring application that uses machine learning to detect when you're losing focus and nudges you back to work.

> "Better control over the PCC can help you catch your mind in the act of wandering and nudge it gently back on task." - Sara Lazar, neuroscientist at Harvard Medical School

## Overview

This is a cross-platform port of the original Nudge project, redesigned to run on **Linux** (and potentially other Unix-like systems). The original Windows version used WinForms/WPF and Windows-specific APIs, while this version uses cross-platform .NET 8 and Linux-compatible tools.

## Architecture

The system consists of three main components:

### 1. NudgeCommon (Shared Library)
- `HarvestData`: Data model for captured activity
- `UdpEngine`: Cross-platform UDP communication
- `IActivityMonitor`: Platform-agnostic activity monitoring interface
- `LinuxActivityMonitor`: Linux implementation using X11 tools

### 2. NudgeHarvester (Data Collection)
- Monitors user activity in real-time
- Tracks:
  - Foreground application
  - Keyboard inactivity time
  - Mouse inactivity time
  - Attention span (time in current app)
- Saves labeled data to CSV for ML training
- Listens on UDP port 11111

### 3. NudgeNotifier (Productivity Nudges)
- Sends periodic notifications
- Prompts user to label their productivity
- Communicates with Harvester via UDP
- Listens on UDP port 22222

## Requirements

### System Requirements
- **OS**: Linux with X11 (tested on Ubuntu/Debian)
- **.NET**: .NET 8.0 SDK or later
- **Display Server**: X11 (Wayland not currently supported)

### Linux Package Dependencies

```bash
# Required for activity monitoring
sudo apt-get install xdotool xprintidle

# Optional for desktop notifications
sudo apt-get install libnotify-bin
```

#### What these tools do:
- **xdotool**: Get active window and process information
- **xprintidle**: Track keyboard/mouse idle time
- **notify-send**: Display desktop notifications

## Installation

### 1. Clone the Repository

```bash
git clone <repository-url>
cd Nudge/NudgeCrossPlatform
```

### 2. Install .NET 8 SDK

If you don't have .NET 8 installed:

```bash
# Ubuntu/Debian
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
```

Add to your PATH:
```bash
export PATH="$HOME/.dotnet:$PATH"
```

### 3. Build the Projects

```bash
dotnet build
```

## Usage

### Running the Harvester

Open a terminal and run:

```bash
cd NudgeHarvester
dotnet run
```

The harvester will:
- Monitor your activity continuously
- Listen for commands on UDP port 11111
- Save data to `/tmp/HARVEST.CSV`

### Running the Notifier

Open a **second terminal** and run:

```bash
cd NudgeNotifier
dotnet run
```

By default, it sends a nudge every 5 minutes. To customize the interval:

```bash
dotnet run -- "00:02:00"  # Nudge every 2 minutes
```

### Interactive Commands

**Notifier Commands:**
- `n` - Send a nudge now
- `q` - Quit

**Harvester Commands (via UDP):**
- `SNAP` - Take activity snapshot
- `YES` - Label as productive
- `NO` - Label as not productive
- `QUIT` - Exit harvester

## Data Collection Workflow

1. **Notifier** sends "SNAP" command to **Harvester**
2. **Harvester** captures current activity metrics
3. **Notifier** prompts user: "Were you productive?"
4. User responds Y/N
5. **Notifier** sends "YES" or "NO" to **Harvester**
6. **Harvester** saves labeled data to CSV

## Training the ML Model

The backend Python code (in `../NudgeBackEnd`) can be used to train the model:

```bash
cd ../NudgeBackEnd
pip install tensorflow pandas numpy
python -m trainer.model
```

The model reads from `HARVEST.CSV` and learns to predict productivity based on:
- Foreground application hash
- Keyboard inactivity
- Mouse inactivity
- Attention span

## Project Structure

```
NudgeCrossPlatform/
├── NudgeCommon/              # Shared library
│   ├── Models/
│   │   └── HarvestData.cs
│   ├── Communication/
│   │   └── UdpEngine.cs
│   └── Monitoring/
│       ├── IActivityMonitor.cs
│       ├── LinuxActivityMonitor.cs
│       └── ActivityMonitorFactory.cs
├── NudgeHarvester/           # Data collector
│   └── Program.cs
├── NudgeNotifier/            # Nudge sender
│   └── Program.cs
└── NudgeCrossPlatform.sln    # Solution file
```

## Differences from Windows Version

| Feature | Windows Version | Linux Version |
|---------|----------------|---------------|
| UI Framework | WinForms + WPF | Console-based |
| Window Detection | Win32 API | xdotool + /proc |
| Input Monitoring | MouseKeyHook library | xprintidle |
| .NET Version | .NET Framework 4.x | .NET 8.0 |
| Desktop Notifications | WPF Toast | notify-send |

## Troubleshooting

### "xdotool not found" or "xprintidle not found"

Install the required tools:
```bash
sudo apt-get update
sudo apt-get install xdotool xprintidle
```

### "Could not show desktop notification"

Install libnotify:
```bash
sudo apt-get install libnotify-bin
```

### Harvester can't detect active window

Make sure you're running X11 (not Wayland):
```bash
echo $XDG_SESSION_TYPE
# Should output: x11
```

To switch to X11, log out and select "Ubuntu on Xorg" at the login screen.

### UDP communication not working

- Ensure both Harvester and Notifier are running
- Check firewall settings for localhost UDP ports 11111 and 22222
- Try: `sudo ufw allow 11111/udp` and `sudo ufw allow 22222/udp`

## Future Enhancements

- [ ] Wayland support
- [ ] macOS support
- [ ] GUI using Avalonia UI
- [ ] Systemd service for auto-start
- [ ] Real-time ML predictions
- [ ] Web dashboard for analytics

## License

This is a hackathon project (RU Hack 2017). See original repository for license details.

## Contributing

Contributions are welcome! Areas for improvement:
- Wayland compatibility
- macOS support
- Better activity detection algorithms
- Integration with ML backend

## Credits

- Original Windows version: Sammy Guergachi
- Cross-platform port: [Your name]
- Inspired by research from Sara Lazar, Harvard Medical School
