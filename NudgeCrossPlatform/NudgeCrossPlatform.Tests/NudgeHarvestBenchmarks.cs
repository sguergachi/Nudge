using System;
using System.Diagnostics;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

/// <summary>
/// Performance regression guard for the Nudge harvest engine.
/// Every test here must pass to ship — any regression in time or allocations
/// is caught immediately.
///
/// Baseline measured 2026-05-30 with V2.1 circular buffer engine.
/// </summary>
public sealed class NudgeHarvestBenchmarks
{
    // ── Thresholds (V3 baseline: ~24 µs Debug, ~26 µs Release, ~92 bytes/alloc) ──

    const double MaxMicrosecondsPerTick = 50;
    const long MaxAllocationBytesPerTick = 500;
    const long MaxTotalAllocation10K = 5 * 1024 * 1024;

    // ── Shared test helpers ───────────────────────────────────────────────────

    private static WindowObservation Win(string appId, string title = "",
        string windowId = "w1", string workspaceId = "1") =>
        new(appId, title, windowId, workspaceId, FocusSource.KWinScript, false, 1);

    private static IdleObservation Active() => new(0, IdleSource.Unknown);

    /// <summary>Fills a tracker with 300 realistic mixed-activity samples.</summary>
    private static ActivityFeatureTracker CreatePopulatedTracker()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        string[] apps  = ["code", "firefox", "terminal", "slack", "code", "chrome", "firefox"];
        string[] tits  = ["main.cs", "GitHub - Mozilla Firefox", "~/projects",
                          "general", "tests.cs", "YouTube - Chrome", "reddit.com - Mozilla Firefox"];
        string[] winIds = ["w1", "w2", "w3"];
        for (int i = 0; i < 300; i++)
        {
            int idx = i % apps.Length;
            tracker.Capture(t.AddSeconds(i),
                new WindowObservation(apps[idx], tits[idx], winIds[idx % 3], $"{idx % 4}",
                    FocusSource.KWinScript, false, 1),
                new IdleObservation(i % 10 == 0 ? 70_000 : 200, IdleSource.Unknown));
        }
        // Warm-up (JIT)
        for (int i = 0; i < 50; i++)
            tracker.Capture(t.AddSeconds(i + 301), Win("code", "warmup.cs"), Active());
        return tracker;
    }

    // ── Throughput ────────────────────────────────────────────────────────────

    [Fact]
    public void TickThroughput_10KIiterations_Under100Microseconds()
    {
        var tracker = CreatePopulatedTracker();
        var t = new DateTime(2026, 5, 23, 10, 10, 0);

        const int iterations = 10_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            string app = i % 2 == 0 ? "code" : "firefox";
            tracker.Capture(t.AddSeconds(i + 401),
                Win(app, app == "code" ? "Program.cs" : "GitHub"),
                Active());
        }
        sw.Stop();

        double avgUs = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
        Assert.True(avgUs < MaxMicrosecondsPerTick,
            $"Average {avgUs:F2} µs per tick exceeds {MaxMicrosecondsPerTick} µs threshold");
    }

    // ── Allocations ───────────────────────────────────────────────────────────

    [Fact]
    public void TickAllocations_10KIterations_Under5KBPerCall()
    {
        var tracker = CreatePopulatedTracker();
        var t = new DateTime(2026, 5, 23, 10, 10, 0);

        const int iterations = 10_000;
        GC.Collect(2, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced);
        long allocBefore = GC.GetTotalAllocatedBytes(precise: true);

        for (int i = 0; i < iterations; i++)
        {
            string app = i % 2 == 0 ? "code" : "firefox";
            tracker.Capture(t.AddSeconds(i + 401),
                Win(app, app == "code" ? "Program.cs" : "GitHub"),
                Active());
        }

        long allocAfter = GC.GetTotalAllocatedBytes(precise: true);
        long totalAlloc = allocAfter - allocBefore;
        double avgAlloc = totalAlloc / (double)iterations;

        Assert.True(totalAlloc < MaxTotalAllocation10K,
            $"Total alloc {totalAlloc / 1024.0 / 1024.0:F2} MB for {iterations} ticks exceeds {MaxTotalAllocation10K / 1024.0 / 1024.0:F0} MB threshold");
        Assert.True(avgAlloc < MaxAllocationBytesPerTick,
            $"Average {avgAlloc:F1} bytes per tick exceeds {MaxAllocationBytesPerTick} byte threshold");
    }

    // ── GC pressure ───────────────────────────────────────────────────────────

    [Fact]
    public void TickAllocations_10KIterations_FewerThan20Gen0Collections()
    {
        var tracker = CreatePopulatedTracker();
        var t = new DateTime(2026, 5, 23, 10, 10, 0);

        const int iterations = 10_000;
        GC.Collect(2, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced);
        int gen0Before = GC.CollectionCount(0);

        for (int i = 0; i < iterations; i++)
        {
            string app = i % 2 == 0 ? "code" : "firefox";
            tracker.Capture(t.AddSeconds(i + 401),
                Win(app, app == "code" ? "Program.cs" : "GitHub"),
                Active());
        }

        int gen0Collections = GC.CollectionCount(0) - gen0Before;
        Assert.True(gen0Collections < 20,
            $"Gen0 collections ({gen0Collections}) exceeds threshold of 20 for {iterations} ticks");
    }

    // ── Correctness smoke test ────────────────────────────────────────────────

    [Fact]
    public void ComputeFeatures_WithMixedData_ProducesExpectedFeatures()
    {
        var tracker = new ActivityFeatureTracker();
        var t = new DateTime(2026, 5, 23, 10, 0, 0);
        string[] apps  = ["code", "slack", "terminal"];
        string[] winIds = ["w0", "w1", "w2"];

        // 120 code → 60 slack → 60 code → enough to fill the buffer
        for (int i = 0; i < 120; i++)
            tracker.Capture(t.AddSeconds(i), Win("code", "a.cs", winIds[i % 3]), Active());
        for (int i = 0; i < 60; i++)
            tracker.Capture(t.AddSeconds(i + 120), Win("slack", "msg", winIds[i % 3]), Active());
        for (int i = 0; i < 60; i++)
            tracker.Capture(t.AddSeconds(i + 180), Win("code", "b.cs", winIds[i % 3]), Active());
        // Add a distinct third app
        for (int i = 0; i < 10; i++)
            tracker.Capture(t.AddSeconds(i + 240), Win("terminal", "$", winIds[i % 3]), Active());

        var tick = tracker.Capture(t.AddSeconds(251), Win("terminal", "$"), Active());

        Assert.True(tick.Features.DistinctApps300s >= 2,
            $"Expected at least 2 distinct apps, got {tick.Features.DistinctApps300s}");
        Assert.True(tick.Features.SwitchCount300s > 0);
        Assert.Equal(SignalQuality.Trusted, tick.Context.SignalQuality);
    }

    // ── Diagnostic: print full benchmark numbers ───────────────────────────────

    [Fact]
    public void Diagnostic_PrintBenchmarkNumbers()
    {
        var tracker = CreatePopulatedTracker();
        var t = new DateTime(2026, 5, 23, 10, 10, 0);

        const int iterations = 10_000;
        GC.Collect(2, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced);
        long a0 = GC.GetTotalAllocatedBytes(true);
        int g0 = GC.CollectionCount(0);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            string app = i % 2 == 0 ? "code" : "firefox";
            tracker.Capture(t.AddSeconds(i + 401),
                Win(app, app == "code" ? "Program.cs" : "GitHub"),
                Active());
        }
        sw.Stop();

        long a1 = GC.GetTotalAllocatedBytes(true);
        int g1 = GC.CollectionCount(0);
        long totalAlloc = a1 - a0;
        double avgUs = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;

        Console.WriteLine($"=== Nudge Harvest Engine V3 Benchmarks ===");
        Console.WriteLine($"Iterations:       {iterations}");
        Console.WriteLine($"Total time:       {sw.Elapsed.TotalMilliseconds:F2} ms");
        Console.WriteLine($"Avg per call:     {avgUs:F2} µs");
        Console.WriteLine($"Total alloc:      {totalAlloc} bytes ({totalAlloc / 1024.0 / 1024.0:F2} MB)");
        Console.WriteLine($"Avg alloc/call:   {totalAlloc / (double)iterations:F1} bytes");
        Console.WriteLine($"GC count Gen0:    {g1 - g0}");
    }
}
