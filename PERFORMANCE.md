# Nudge Harvest Engine — Performance Log

Benchmark: `NudgeHarvestBenchmarks.Diagnostic_PrintBenchmarkNumbers`
Data: 300 pre-filled mixed samples + 10K alternating code/firefox captures

## V2.0 — Queue<ActivitySample> + LINQ
**Date:** 2026-05-30 (session start, pre-circular-buffer)
**Arch:** Queue, GetSamplesSince(), 5 LINQ chains (~1500 iters)

| Metric | Debug |
|--------|-------|
| Time/call | 130.86 µs |
| Alloc/call | 78,769 B |
| Gen0/10K | 133 |

## V2.1 — Circular buffer + separate passes
**Date:** 2026-05-30
**Arch:** ActivitySample[300] ring buffer, BufAt(), 5-6 passes per tick

| Metric | Debug | Release |
|--------|-------|---------|
| Time/call | 48.03 µs | 30.24 µs |
| Alloc/call | 700 B | 700 B |
| Gen0/10K | 1 | 1 |

Improvements vs V2.0: **63% faster** (Debug), **99% fewer allocs**

## V3.0 RC1 — Single-pass inline + cached dict
**Date:** 2026-05-30
**Arch:** One pass inline counting, cached `_freqDict`, `FastAddDistinct`, no dead methods, IsBrowser deduplication

| Metric | Debug | Release |
|--------|-------|---------|
| Time/call | 24.41 µs | 26.29 µs |
| Alloc/call | 92 B | 92 B |
| Gen0/10K | 0 | 0 |

Improvements vs V2.1: **13% faster** (Release), **7.6x fewer allocs**, **zero GC**

Stacked vs V2.0: **80% faster**, **99.9% fewer allocs**, **133→0 Gen0 collections**

## Lessons Learned
- Single centralized loop body (V3 attempt 1) regresses — too large for JIT to optimize well
- Split-loop with ref readonly also regresses — duplicated code hurts icache
- `ref readonly` for 40-byte structs is *slower* than stack copy on modern .NET JIT
- Keep loop body compact: inline switch counting but avoid merging disconnected concerns
- Modulo in hot loop (300x divs) is fine on modern CPUs; branch-based indexing adds more overhead than it saves
- Cached `Dictionary.Clear()` per tick is a clear win over `new Dictionary(16)` (saves 7.6x alloc)
