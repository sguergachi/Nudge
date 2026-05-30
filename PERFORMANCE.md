# Nudge — Performance Log

---

## Harvest Engine

Benchmark: `NudgeHarvestBenchmarks.Diagnostic_PrintBenchmarkNumbers`
Data: 300 pre-filled mixed samples + 10K alternating code/firefox captures

### V2.0 — Queue<ActivitySample> + LINQ
**Arch:** Queue, GetSamplesSince(), 5 LINQ chains

| Metric | Debug |
|--------|-------|
| Time/call | 130.86 µs |
| Alloc/call | 78,769 B |
| Gen0/10K | 133 |

### V2.1 — Circular buffer + separate passes
**Arch:** ActivitySample[300] ring buffer, BufAt(), 5-6 passes per tick

| Metric | Debug | Release |
|--------|-------|---------|
| Time/call | 48.03 µs | 30.24 µs |
| Alloc/call | 700 B | 700 B |
| Gen0/10K | 1 | 1 |

vs V2.0: **63% faster**, **99% fewer allocs**

### V3.0 — Single-pass inline + cached dict
**Arch:** One pass inline counting, cached `_freqDict`, `FastAddDistinct`, IsBrowser dedup

| Metric | Debug | Release |
|--------|-------|---------|
| Time/call | 24.41 µs | 24.26 µs |
| Alloc/call | 92 B | 92 B |
| Gen0/10K | 0 | 0 |

vs V2.1: **20% faster**, **87% fewer allocs**, **zero GC**
vs V2.0: **81% faster**, **99.9% fewer allocs**

---

## AI Brain Pipeline

Benchmark: `NudgeAIBrainBenchmarks.Diagnostic_PrintAIBrainBenchmarkNumbers`
Data: 200 pre-filled MLLiveEvents in LiveAIState

### V1.0 — Per-event SaveToDisk
| Operation | Time | Alloc/call | Gen0/10K |
|-----------|------|------------|----------|
| GetRecent() | 1.74 µs | 1,624 B | 2 |
| Add() + save | 183.63 µs | 48,761 B | 77 |

### V2.0 — Debounced SaveToDisk + GetCount()
**Changes:** 30s periodic save timer, `UpdateVersion` change tracking, AI tab skips no-op rebuilds

| Operation | Time | Alloc/call | Gen0/10K |
|-----------|------|------------|----------|
| GetRecent() | 1.74 µs | 1,624 B | 2 |
| GetCount() | ~0 µs | 0 B | 0 |
| Add() | 0.08 µs | 72 B | 0 |

vs V1.0 `Add()`: **99.96% faster**, **99.85% fewer allocs**, **zero GC**

---

## Lessons Learned
- Single centralized loop body regresses — too large for JIT
- `ref readonly` for 40-byte structs is slower than stack copy on modern .NET
- Cached `Dictionary.Clear()` wins over `new Dictionary(16)` (7.6x less alloc)
- Debounced disk writes eliminate 99.96% of save cost
- Change tracking (`UpdateVersion`) prevents wasted full UI rebuilds
- Expose `GetCount()` to avoid `ToArray()` allocation for simple count checks
