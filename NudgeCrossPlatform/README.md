# Nudge - Productivity Tracker

ML-powered productivity tracker that learns from your behavior.

> "Better control over the PCC can help you catch your mind in the act of wandering and nudge it gently back on task." - Sara Lazar, neuroscientist at Harvard Medical School

## What It Does

1. Tracks what app you're using every 5 minutes
2. Asks "Were you productive?" (via notification)
3. Trains ML model on your responses
4. (Eventually) Nudges you when it detects unproductive patterns

## Requirements

- **Wayland** compositor (Sway, GNOME, or KDE)
- **C# compiler** (dotnet or mono)
- **Python 3** with TensorFlow (for training)

## Build

```bash
./build.sh
```

This creates two executables:
- `nudge` - Main tracker (runs continuously)
- `nudge-notify` - Send YES/NO responses

## Run

Terminal 1 (tracker):
```bash
./nudge
```

Terminal 2 (when prompted):
```bash
./nudge-notify YES    # I was productive
./nudge-notify NO     # I was not productive
```

## Train Model

After collecting data (20+ examples):

```bash
python3 -m pip install -r requirements.txt
python3 train_model.py
```

## Files

- `nudge.cs` (350 lines) - Main tracker, no abstractions
- `nudge-notify.cs` (50 lines) - Notifier
- `train_model.py` (300 lines) - ML training
- `validate_data.py` (100 lines) - Data validation

Total: ~800 lines of actual code

## Data Format

CSV at `/tmp/HARVEST.CSV`:
```
foreground_app,idle_time,time_last_request,productive
-123456789,1500,30000,1
987654321,500,120000,0
```

## Architecture

**No architecture.** Just code that works.

- No interfaces (only 1 implementation)
- No factories (just call the function)
- No separate projects (everything's inline)
- No abstraction layers (direct system calls)

Read the code top-to-bottom. It does what it says.

## Philosophy

This is Jon Blow-style programming:
- **Specific over general** - Solves this problem, not hypothetical futures
- **Inline over abstract** - Read the actual code, not architectural diagrams
- **Working over perfect** - Does the job, doesn't pretend to be elegant

If you need X11 support, copy `nudge.cs` and change the Wayland calls.
If you need macOS support, copy `nudge.cs` and change the Wayland calls.

Don't build abstractions for problems you don't have.

## Previous Versions

See `BRUTAL_TRUTH.md` - the analysis that led to this rewrite.

This version deleted:
- 53,838 lines of legacy code
- 3 separate C# projects
- Over-engineered abstractions
- Duplicate training scripts

What remains: Direct, readable code that solves the actual problem.

## License

MIT - Do whatever you want with it

## Credits

- Original hackathon project (RU Hack 2017): Sammy Guergachi
- Ruthless simplification: Jon Blow's philosophy applied
