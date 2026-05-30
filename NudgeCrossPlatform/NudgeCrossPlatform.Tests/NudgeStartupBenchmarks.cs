using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using Xunit;
using NudgeCore;
using NudgeTray;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeStartupBenchmarks
{
    private const int WarmupIterations = 3;
    private const int BenchmarkIterations = 20;
    private const int MaxAcceptableMs = 100; // per-benchmark threshold

    [Fact]
    public void Settings_LoadAndSave_Roundtrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nudge-bench-settings-{Guid.NewGuid()}.json");
        try
        {
            // Write baseline
            var settings = new { MlEnabled = true, IntervalMinutes = 5, MlCheckIntervalSeconds = 60 };
            File.WriteAllText(path, JsonSerializer.Serialize(settings));

            double loadUs = Benchmark<object?>(() =>
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<object>(json);
            }, "Settings load");

            double saveUs = Benchmark<object?>(() =>
            {
                File.WriteAllText(path, JsonSerializer.Serialize(settings));
                return null;
            }, "Settings save");

            Console.WriteLine($"=== Startup: Settings I/O ===");
            Console.WriteLine($"Load avg: {loadUs:F0} µs");
            Console.WriteLine($"Save avg: {saveUs:F0} µs");
            Assert.True(loadUs < MaxAcceptableMs * 1000,
                $"Settings load too slow: {loadUs / 1000:F1} ms");
            Assert.True(saveUs < MaxAcceptableMs * 1000,
                $"Settings save too slow: {saveUs / 1000:F1} ms");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AppCategory_FirstClassification_ColdCache()
    {
        // Force cold cache by calling Classify with a unique app name
        string uniqueApp = $"bench-cold-{Guid.NewGuid():N}";

        double coldUs = Benchmark<object?>(() =>
        {
            // This triggers desktop file scanning on first miss
            AppCategoryClassifier.Classify(uniqueApp, "");
            return null;
        }, "First classification (cold cache)", iterations: 1, warmup: 0);

        Console.WriteLine($"=== Startup: AppCategory First Classification ===");
        Console.WriteLine($"Cold avg: {coldUs:F0} µs ({coldUs / 1000:F1} ms)");
        // Cold classification can take 50-200ms depending on system desktop file count.
        // This is informational — no hard assertion.
    }

    [Fact]
    public void AppCategory_CachedClassification_WarmCache()
    {
        string knownApp = "code";

        // Ensure cache is warm by classifying once
        AppCategoryClassifier.Classify(knownApp, "");

        double warmUs = Benchmark<object?>(() =>
        {
            AppCategoryClassifier.Classify(knownApp, "");
            return null;
        }, "Cached classification");

        Console.WriteLine($"=== Startup: AppCategory Cached Classification ===");
        Console.WriteLine($"Warm avg: {warmUs:F0} µs");
        Assert.True(warmUs < 2000, // 2ms
            $"Cached classification too slow: {warmUs / 1000:F1} ms");
    }

    [Fact]
    public void ActivityFeatureTracker_SingleTick()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        var window = new WindowObservation("code", "main.cs", "w1", "1", FocusSource.KWinScript, false, 1);
        var idle = new IdleObservation(0, IdleSource.Unknown);

        // Warmup
        for (int i = 0; i < WarmupIterations; i++)
            tracker.Capture(t.AddSeconds(i), window, idle);

        double tickUs = Benchmark<ActivityTickResult>(() =>
        {
            var tick = tracker.Capture(t.AddSeconds(100), window, idle);
            return tick;
        }, "Single Capture() tick", iterations: 500);

        Console.WriteLine($"=== Startup: First Tick Performance ===");
        Console.WriteLine($"Capture avg: {tickUs:F0} µs");
        Assert.True(tickUs < 500,
            $"Single tick too slow: {tickUs:F0} µs");
    }

    [Fact]
    public void MutexCreation_SingleInstanceCheck()
    {
        double createUs = Benchmark<object?>(() =>
        {
            using var mutex = new Mutex(false, $"NudgeBench-{Guid.NewGuid()}");
            bool createdNew;
            try { createdNew = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { createdNew = true; }
            if (createdNew) mutex.ReleaseMutex();
            return null;
        }, "Mutex create+acquire", iterations: 20);

        Console.WriteLine($"=== Startup: Mutex ===");
        Console.WriteLine($"Create+acquire avg: {createUs:F0} µs");
        Assert.True(createUs < 5000, // 5ms
            $"Mutex creation too slow: {createUs / 1000:F1} ms");
    }

    [Fact]
    public void TimerAndCountdown_FormatOverhead()
    {
        // Measures string formatting overhead (tray status countdown)
        int remaining = 300;

        double formatUs = Benchmark<string>(() =>
        {
            int mins = remaining / 60;
            int secs = remaining % 60;
            return $"{mins}:{secs:D2}";
        }, "Countdown format", iterations: 1000);

        Console.WriteLine($"=== Startup: String Formatting ===");
        Console.WriteLine($"Countdown format avg: {formatUs:F0} µs");
        Assert.True(formatUs < 100,
            $"Countdown formatting too slow: {formatUs:F0} µs ({formatUs * 1000:F0} ns)");
    }

    [Fact]
    public void ProcessSpawn_PythonVersion_Check()
    {
        if (!PlatformConfig.IsLinux) return;

        double spawnUs = Benchmark<object?>(() =>
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                p?.WaitForExit(5000);
                return (object?)true;
            }
            catch { return (object?)false; }
        }, "python3 --version", iterations: 1, warmup: 0);

        Console.WriteLine($"=== Startup: Process Spawn ===");
        Console.WriteLine($"python3 --version: {spawnUs / 1000:F0} ms");
        // No assertion — this varies by system, just logs
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double Benchmark<T>(Func<T> action, string label, int iterations = BenchmarkIterations, int warmup = WarmupIterations)
    {
        // Warmup
        for (int i = 0; i < warmup; i++)
            action();

        // Force GC before measurement
        GC.Collect(2, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced);

        long totalTicks = 0;
        var sw = new Stopwatch();
        for (int i = 0; i < iterations; i++)
        {
            sw.Restart();
            action();
            sw.Stop();
            totalTicks += sw.ElapsedTicks;
        }

        double avgUs = totalTicks * 1_000_000.0 / iterations / Stopwatch.Frequency;
        return avgUs;
    }
}
