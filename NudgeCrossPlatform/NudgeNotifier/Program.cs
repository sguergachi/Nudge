using NudgeCommon.Communication;
using System.Diagnostics;

namespace NudgeNotifier;

/// <summary>
/// Notifier program that sends periodic nudges and collects productivity feedback
/// </summary>
class Program
{
    private static UdpEngine? _udpEngine;
    private static bool _running = true;
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5); // Nudge every 5 minutes

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Nudge Notifier (Cross-Platform) ===");
        Console.WriteLine("Sending productivity nudges...\n");

        // Parse interval from command line or use default
        var interval = args.Length > 0 && TimeSpan.TryParse(args[0], out var customInterval)
            ? customInterval
            : DefaultInterval;

        Console.WriteLine($"Nudge interval: {interval.TotalMinutes} minutes");

        // Setup UDP communication (listens on 22222, sends to 11111)
        _udpEngine = new UdpEngine(11111, 22222, HandleUdpMessage);
        _ = Task.Run(async () => await _udpEngine.StartUdpServerAsync());

        // Handle Ctrl+C gracefully
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _running = false;
        };

        Console.WriteLine("\nCommands:");
        Console.WriteLine("  n - Send nudge now");
        Console.WriteLine("  q - Quit");
        Console.WriteLine();

        // Start automatic nudge timer
        _ = Task.Run(async () => await AutoNudgeLoop(interval));

        // Interactive command loop
        while (_running)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                switch (key.KeyChar)
                {
                    case 'n':
                    case 'N':
                        await SendNudge();
                        break;

                    case 'q':
                    case 'Q':
                        _running = false;
                        break;
                }
            }

            await Task.Delay(100);
        }

        // Cleanup
        Console.WriteLine("\nShutting down...");
        _udpEngine?.Stop();
        Console.WriteLine("Goodbye!");
    }

    private static async Task AutoNudgeLoop(TimeSpan interval)
    {
        while (_running)
        {
            await Task.Delay(interval);
            if (_running)
            {
                await SendNudge();
            }
        }
    }

    private static async Task SendNudge()
    {
        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Sending nudge...");

        // Send SNAP command to harvester to capture current state
        if (_udpEngine != null)
        {
            await _udpEngine.SendToClientsAsync("SNAP");
        }

        // Show desktop notification (Linux-specific using notify-send)
        await ShowDesktopNotification();

        // Prompt user in console
        Console.WriteLine("\n=== PRODUCTIVITY CHECK ===");
        Console.WriteLine("Were you being productive just now?");
        Console.Write("Answer (Y/N): ");

        var response = await Task.Run(() => Console.ReadLine());

        if (response?.Trim().ToUpper() == "Y")
        {
            if (_udpEngine != null)
            {
                await _udpEngine.SendToClientsAsync("YES");
            }
            Console.WriteLine("✓ Recorded: Productive");
        }
        else if (response?.Trim().ToUpper() == "N")
        {
            if (_udpEngine != null)
            {
                await _udpEngine.SendToClientsAsync("NO");
            }
            Console.WriteLine("✓ Recorded: Not productive");
        }
        else
        {
            Console.WriteLine("Invalid response. Skipping...");
        }

        Console.WriteLine();
    }

    private static async Task ShowDesktopNotification()
    {
        try
        {
            // Use notify-send on Linux for desktop notifications
            var psi = new ProcessStartInfo
            {
                FileName = "notify-send",
                Arguments = "\"Nudge\" \"Are you being productive?\" -u critical -t 5000",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not show desktop notification: {ex.Message}");
            Console.WriteLine("Hint: Install notify-send with: sudo apt-get install libnotify-bin");
        }
    }

    private static void HandleUdpMessage(string message)
    {
        // Handle any incoming messages if needed
        Console.WriteLine($"Received: {message}");
    }
}
