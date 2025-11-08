using NudgeCommon.Models;
using NudgeCommon.Communication;
using NudgeCommon.Monitoring;
using CsvHelper;
using System.Globalization;

namespace NudgeHarvester;

/// <summary>
/// Main harvester program that monitors user activity and collects training data
/// </summary>
class Program
{
    private const int CYCLE_MS = 1000; // Check every second
    private static IActivityMonitor? _activityMonitor;
    private static UdpEngine? _udpEngine;
    private static HarvestData _currentHarvest = new();
    private static StreamWriter? _csvStream;
    private static CsvWriter? _csvWriter;
    private static bool _running = true;

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

        _currentHarvest = new HarvestData
        {
            ForegroundApp = _activityMonitor.GetForegroundApp(),
            ForegroundAppHash = _activityMonitor.GetForegroundApp().GetHashCode(),
            KeyboardActivity = _activityMonitor.GetKeyboardInactivityMs(),
            MouseActivity = _activityMonitor.GetMouseInactivityMs(),
            AttentionSpan = _activityMonitor.GetAttentionSpanMs()
        };

        Console.WriteLine("\n=== SNAPSHOT ===");
        Console.WriteLine($"Foreground App: {_currentHarvest.ForegroundApp}");
        Console.WriteLine($"Foreground App Hash: {_currentHarvest.ForegroundAppHash}");
        Console.WriteLine($"Keyboard Inactive: {_currentHarvest.KeyboardActivity}ms");
        Console.WriteLine($"Mouse Inactive: {_currentHarvest.MouseActivity}ms");
        Console.WriteLine($"Attention Span: {_currentHarvest.AttentionSpan}ms");
        Console.WriteLine("Waiting for productivity response (YES/NO)...\n");
    }

    private static void SaveHarvest()
    {
        if (_csvWriter == null) return;

        try
        {
            _csvWriter.WriteRecord(_currentHarvest);
            _csvWriter.NextRecord();
            _csvWriter.Flush();

            Console.WriteLine($"✓ Saved: Productive={_currentHarvest.Productive}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving harvest: {ex.Message}");
        }
    }
}
