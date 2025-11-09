using NudgeCommon.Models;
using NudgeCommon.Communication;
using NudgeCommon.Monitoring;
using NudgeCommon.Utilities;
using CsvHelper;
using System.Globalization;

namespace NudgeHarvester;

/// <summary>
/// Main harvester program that monitors user activity and collects training data
/// </summary>
class Program
{
    private const int CYCLE_MS = 1000; // Check every second
    private const int MAX_ATTENTION_SPAN_MS = 30 * 60 * 1000; // Cap at 30 minutes
    private const int WARNING_ATTENTION_SPAN_MS = 2 * 60 * 60 * 1000; // Warn if > 2 hours

    private static IActivityMonitor? _activityMonitor;
    private static UdpEngine? _udpEngine;
    private static HarvestData _currentHarvest = new();
    private static StreamWriter? _csvStream;
    private static CsvWriter? _csvWriter;
    private static bool _running = true;

    // Thread safety for CSV writes
    private static readonly object _csvLock = new object();

    // Track app hashes for collision detection
    private static readonly Dictionary<int, string> _seenAppHashes = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Nudge Harvester (Cross-Platform) ===");
        Console.WriteLine("Monitoring user activity for productivity tracking...\n");

        // Initialize activity monitor
        _activityMonitor = ActivityMonitorFactory.Create();

        // Setup CSV output
        var csvPath = Path.Combine(Path.GetTempPath(), "HARVEST.CSV");
        _csvStream = new StreamWriter(csvPath, append: true);
        _csvWriter = new CsvWriter(_csvStream, CultureInfo.InvariantCulture);

        // Write header if file is empty
        if (new FileInfo(csvPath).Length == 0)
        {
            _csvWriter.WriteHeader<HarvestData>();
            _csvWriter.NextRecord();
        }

        Console.WriteLine($"Saving harvest data to: {csvPath}");

        // Setup UDP communication (listens on 11111, sends to 22222)
        _udpEngine = new UdpEngine(22222, 11111, HandleUdpMessage);
        _ = Task.Run(async () => await _udpEngine.StartUdpServerAsync());

        Console.WriteLine("\nWaiting for commands...");
        Console.WriteLine("Commands: SNAP (take snapshot), YES (productive), NO (not productive), QUIT (exit)\n");

        // Handle Ctrl+C gracefully
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _running = false;
        };

        // Main loop
        while (_running)
        {
            _activityMonitor.Update(CYCLE_MS);
            await Task.Delay(CYCLE_MS);
        }

        // Cleanup
        Console.WriteLine("\nShutting down...");
        _csvWriter?.Dispose();
        _csvStream?.Close();
        _udpEngine?.Stop();
        Console.WriteLine("Goodbye!");
    }

    private static void HandleUdpMessage(string message)
    {
        switch (message.Trim().ToUpper())
        {
            case "SNAP":
                TakeSnapshot();
                break;

            case "YES":
                _currentHarvest.Productive = 1;
                SaveHarvest();
                break;

            case "NO":
                _currentHarvest.Productive = 0;
                SaveHarvest();
                break;

            case "QUIT":
                _running = false;
                break;

            default:
                Console.WriteLine($"Unknown command: {message}");
                break;
        }
    }

    private static void TakeSnapshot()
    {
        if (_activityMonitor == null) return;

        string appName = _activityMonitor.GetForegroundApp();

        // Use deterministic hash for cross-platform compatibility
        var (appHash, isCollision) = StableHash.GetHashWithCollisionCheck(appName, _seenAppHashes);

        int attentionSpan = _activityMonitor.GetAttentionSpanMs();

        // Warn about extreme values (but still record them)
        if (attentionSpan > WARNING_ATTENTION_SPAN_MS)
        {
            Console.WriteLine($"⚠️  WARNING: Attention span is {attentionSpan/1000/60} minutes - unusually long!");
        }

        _currentHarvest = new HarvestData
        {
            ForegroundApp = appName,
            ForegroundAppHash = appHash,
            KeyboardActivity = _activityMonitor.GetKeyboardInactivityMs(),
            MouseActivity = _activityMonitor.GetMouseInactivityMs(),
            AttentionSpan = attentionSpan
        };

        // Validate data quality
        if (!ValidateHarvestData(_currentHarvest))
        {
            Console.WriteLine("⚠️  WARNING: Invalid data detected in snapshot!");
        }

        Console.WriteLine("\n=== SNAPSHOT ===");
        Console.WriteLine($"Foreground App: {_currentHarvest.ForegroundApp}");
        Console.WriteLine($"Foreground App Hash: {_currentHarvest.ForegroundAppHash}" +
                         (isCollision ? " ⚠️ COLLISION!" : ""));
        Console.WriteLine($"Keyboard Inactive: {_currentHarvest.KeyboardActivity}ms");
        Console.WriteLine($"Mouse Inactive: {_currentHarvest.MouseActivity}ms");
        Console.WriteLine($"Attention Span: {_currentHarvest.AttentionSpan}ms");
        Console.WriteLine("Waiting for productivity response (YES/NO)...\n");
    }

    private static bool ValidateHarvestData(HarvestData data)
    {
        bool isValid = true;

        // Check for negative values
        if (data.KeyboardActivity < 0)
        {
            Console.WriteLine($"ERROR: Negative keyboard activity: {data.KeyboardActivity}");
            isValid = false;
        }

        if (data.MouseActivity < 0)
        {
            Console.WriteLine($"ERROR: Negative mouse activity: {data.MouseActivity}");
            isValid = false;
        }

        if (data.AttentionSpan < 0)
        {
            Console.WriteLine($"ERROR: Negative attention span: {data.AttentionSpan}");
            isValid = false;
        }

        // Check for unreasonably large values (likely bugs)
        const int maxReasonableValue = 24 * 60 * 60 * 1000; // 24 hours in ms

        if (data.KeyboardActivity > maxReasonableValue)
        {
            Console.WriteLine($"WARNING: Keyboard activity > 24 hours: {data.KeyboardActivity}ms");
        }

        if (data.MouseActivity > maxReasonableValue)
        {
            Console.WriteLine($"WARNING: Mouse activity > 24 hours: {data.MouseActivity}ms");
        }

        if (data.AttentionSpan > maxReasonableValue)
        {
            Console.WriteLine($"WARNING: Attention span > 24 hours: {data.AttentionSpan}ms");
        }

        return isValid;
    }

    private static void SaveHarvest()
    {
        if (_csvWriter == null) return;

        // Thread-safe CSV writing (HandleUdpMessage runs on UDP thread)
        lock (_csvLock)
        {
            try
            {
                _csvWriter.WriteRecord(_currentHarvest);
                _csvWriter.NextRecord();
                // Note: Removed Flush() - CsvHelper handles buffering efficiently
                // Explicit flush on every write is wasteful

                Console.WriteLine($"✓ Saved: Productive={_currentHarvest.Productive}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving harvest: {ex.Message}");
                Console.WriteLine("Data may be lost. Check file permissions and disk space.");
            }
        }
    }
}
