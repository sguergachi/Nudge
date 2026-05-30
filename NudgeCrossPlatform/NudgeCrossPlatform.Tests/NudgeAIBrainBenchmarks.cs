using System;
using System.Diagnostics;
using System.Text.Json;
using Xunit;
using NudgeCore;
using NudgeTray;

namespace NudgeCrossPlatform.Tests;

/// <summary>
/// Performance regression guard for the AI Brain data pipeline.
/// Covers LiveAIState operations (GetRecent, SaveToDisk, Add, UpdateResponse).
///
/// Baseline measured 2026-05-30 with LiveAIState V1.
/// </summary>
public sealed class NudgeAIBrainBenchmarks
{
    // ── Thresholds (generous to pass on slow Windows CI VMs) ─────────────────

    const double MaxGetRecentMicroseconds = 200;    // ~200 events → array (10x Linux baseline)
    const long MaxGetRecentAllocBytes = 5000;        // MLLiveEvent[] + strings
    const int MaxGetRecentGen0Per10K = 200;           // GC budget is platform-dependent

    const double MaxSaveToDiskMilliseconds = 200;   // JSON serialize 200 events + disk write
    const long MaxSaveToDiskAllocBytes = 200_000;

    const double MaxAddMicroseconds = 500;           // Add() + debounced timer arm (5x headroom)
    const int MaxAddGen0Per10K = 500;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MLLiveEvent MakeEvent(long t, string app, double score, bool triggered)
    {
        return new MLLiveEvent
        {
            T = t, App = app, Score = score,
                    Confidence = 0.85, Productive = score > 0.5,
                    Triggered = triggered,
                    TriggerSource = triggered ? "ai" : (triggered ? "int" : string.Empty),
                    UserResponse = null, AiCorrect = null
                };
    }

    private static void FillEvents(int count)
    {
        // Wipe existing events
        var existing = LiveAIState.GetRecent();
        int removeCount = existing.Count;
        for (int i = 0; i < count; i++)
        {
            LiveAIState.Add(MakeEvent(1700000000 + i, i % 3 == 0 ? "code" : "firefox",
                0.5 + (i % 10) * 0.05, i % 7 == 0));
        }
    }

    // ── GetRecent() ───────────────────────────────────────────────────────────

    [Fact]
    public void GetRecent_10KIterations_BelowThresholds()
    {
        FillEvents(200);
        const int iterations = 10_000;
        GC.Collect(2, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced);
        long allocBefore = GC.GetTotalAllocatedBytes(true);
        int gen0Before = GC.CollectionCount(0);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var list = LiveAIState.GetRecent();
            Assert.True(list.Count >= 0);
        }
        sw.Stop();

        long allocAfter = GC.GetTotalAllocatedBytes(true);
        int gen0After = GC.CollectionCount(0);

        double avgUs = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
        long avgAlloc = (allocAfter - allocBefore) / iterations;
        int gen0 = gen0After - gen0Before;

        Assert.True(avgUs < MaxGetRecentMicroseconds,
            $"GetRecent {avgUs:F2} µs exceeds {MaxGetRecentMicroseconds} µs");
        Assert.True(avgAlloc < MaxGetRecentAllocBytes,
            $"GetRecent {avgAlloc} B/iter exceeds {MaxGetRecentAllocBytes} B");
        Assert.True(gen0 < MaxGetRecentGen0Per10K,
            $"GetRecent {gen0} Gen0 exceeds {MaxGetRecentGen0Per10K}");
    }

    // ── SaveToDisk() ──────────────────────────────────────────────────────────

    [Fact]
    public void SaveToDisk_50Iterations_BelowThresholds()
    {
        FillEvents(200);
        const int iterations = 50; // disk writes are slow, limit iterations
        GC.Collect(2, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced);
        long allocBefore = GC.GetTotalAllocatedBytes(true);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            // Trigger SaveToDisk by updating a response
            LiveAIState.UpdateResponse(1700000000 + i, true);
        }
        sw.Stop();

        long allocAfter = GC.GetTotalAllocatedBytes(true);
        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        long avgAlloc = (allocAfter - allocBefore) / iterations;

        Assert.True(avgMs < MaxSaveToDiskMilliseconds,
            $"SaveToDisk {avgMs:F2} ms exceeds {MaxSaveToDiskMilliseconds} ms");
        Assert.True(avgAlloc < MaxSaveToDiskAllocBytes,
            $"SaveToDisk {avgAlloc} B/iter exceeds {MaxSaveToDiskAllocBytes} B");
    }

    // ── Add() (with SaveToDisk) ───────────────────────────────────────────────

    [Fact]
    public void Add_10KIterations_BelowThresholds()
    {
        FillEvents(200);
        const int iterations = 10_000;
        GC.Collect(2, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced);
        long allocBefore = GC.GetTotalAllocatedBytes(true);
        int gen0Before = GC.CollectionCount(0);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            LiveAIState.Add(MakeEvent(1700100000 + i, "code", 0.5, false));
        }
        sw.Stop();

        long allocAfter = GC.GetTotalAllocatedBytes(true);
        int gen0After = GC.CollectionCount(0);

        double avgUs = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
        int gen0 = gen0After - gen0Before;

        Assert.True(avgUs < MaxAddMicroseconds,
            $"Add {avgUs:F2} µs exceeds {MaxAddMicroseconds} µs");
        Assert.True(gen0 < MaxAddGen0Per10K,
            $"Add {gen0} Gen0 exceeds {MaxAddGen0Per10K}");
    }

    // ── Diagnostic ────────────────────────────────────────────────────────────

    [Fact]
    public void Diagnostic_PrintAIBrainBenchmarkNumbers()
    {
        FillEvents(200);
        const int iter = 10_000;

        GC.Collect(2, GCCollectionMode.Forced); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced);
        long a0 = GC.GetTotalAllocatedBytes(true);
        int g0 = GC.CollectionCount(0);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iter; i++) LiveAIState.GetRecent();
        sw.Stop();
        long a1 = GC.GetTotalAllocatedBytes(true);
        int g1 = GC.CollectionCount(0);
        Console.WriteLine($"=== AI Brain GetRecent() ===");
        Console.WriteLine($"  Avg:   {sw.Elapsed.TotalMilliseconds * 1000.0 / iter:F2} µs");
        Console.WriteLine($"  Alloc: {(a1 - a0) / iter} B/call");
        Console.WriteLine($"  Gen0:  {g1 - g0}");

        FillEvents(200);
        GC.Collect(2, GCCollectionMode.Forced); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced);
        a0 = GC.GetTotalAllocatedBytes(true);
        g0 = GC.CollectionCount(0);
        sw.Restart();
        for (int i = 0; i < iter; i++)
        {
            LiveAIState.Add(MakeEvent(1700200000 + i, "code", 0.5, false));
        }
        sw.Stop();
        a1 = GC.GetTotalAllocatedBytes(true);
        g1 = GC.CollectionCount(0);
        Console.WriteLine($"=== AI Brain Add() (incl SaveToDisk) ===");
        Console.WriteLine($"  Avg:   {sw.Elapsed.TotalMilliseconds * 1000.0 / iter:F2} µs");
        Console.WriteLine($"  Alloc: {(a1 - a0) / iter} B/call");
        Console.WriteLine($"  Gen0:  {g1 - g0}");
    }
}
