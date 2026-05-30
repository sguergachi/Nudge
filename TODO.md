# Nudge — Future Work

## Done

### ✓ 1. Eliminate `nudge.cs` / `nudge_build.cs` duplication
175 lines removed across 11 files. Compiles real files directly. No more sync bugs.

### ✓ 2. Extract IPC handler bodies into named methods
112-line if/else → 18-line dispatch + 7 focused `Handle*` methods.

### ✓ 3. Code quality quick wins
- Deleted `ShowFallbackNotification()` (24 lines) and `FormatFocusSource()` (9 lines) — dead code
- Inlined 8 thin single-caller wrappers (removed 36 lines of method boilerplate)
- `DateTime.Now` → `DateTime.UtcNow` in `RunMainLoop`, `LogActivity`, `SaveSnapshot`
- `HarvestSignal` struct conversion skipped — `volatile HarvestSignal?` on `LiveAIState` makes atomic struct reads impossible at current size

---

## Pending

### Hot-path allocations (every 1s tick)

| Issue | File:Line | Fix |
|-------|-----------|-----|
| `GetSamplesSince()` allocs `List + ToArray()` twice per tick | `NudgeCore.TestableLogic.cs:335` | Use pooled arrays or span slices |
| LINQ chains in `BuildFeatureVector()` — `Count`, `Select`, `GroupBy`, `Distinct` | `NudgeCore.TestableLogic.cs:268-313` | Replace with manual loops, pre-allocated counts |
| `focusKey = $"{appId}\n{title}"` | `NudgeCore.TestableLogic.cs:211` | Value-tuple key or pre-allocated buffer |
| `NormalizeRawValue` `.Replace().Replace()` — 3 calls per tick | `NudgeCore.TestableLogic.cs:377` | Cache normalized value when input unchanged |

### Hot-path allocations (every 60s ML check)

| Issue | File:Line | Fix |
|-------|-----------|-----|
| `ToFeatureDictionary()` `new Dictionary(26)` | `NudgeCore.TestableLogic.cs:630` | FrozenDictionary or flat `(string,double)[]` |
| `QueryMLModel` `new byte[4096]` buffer | `nudge.cs:2467` | Static 4KB buffer (single-threaded operation) |
| `MLPredictionRequest` class allocation | `NudgeJsonContext.cs:7` | Pool or struct |

### Hot-path allocations (per snapshot/log write)

| Issue | File:Line | Fix |
|-------|-----------|-----|
| `WriteCsvRow params object[]` boxes every int/bool/double | `nudge.cs:2301` | Typed overloads or `Write(ReadOnlySpan<object>)` |

### Thread safety

| Issue | File:Line | Severity | Fix |
|-------|-----------|----------|-----|
| `_waitingForResponse` written by 3 threads (main loop, ThreadPool timer, UDP listener) — zero synchronization | `nudge.cs:1543` | **Critical** | `Volatile.Read/Write` or `lock` |
| Snapshot capture 5 fields read/written across threads without atomicity | `nudge.cs:1548-1552` | **High** | Immutable `CapturedSnapshot` struct + `Volatile.Read` |
| `_waitingForResponse` duplicate state in tray (diverges from daemon) | `nudge-tray.cs:2013` | **High** | Gate on active window, not separate boolean |
| `_loggedApps` `HashSet<string>` mutated with zero synchronization | `nudge.cs:1473` | Medium | `ConcurrentDictionary` or lock |

### State grouping

| Issue | File:Line | Fix |
|-------|-----------|-----|
| 13 ML stats fields spread across class | `nudge.cs:1556-1567` | Extract `MlEngineStats` struct |
| 6 audit flags dead after startup, persist as static fields | `nudge-tray.cs:87-92` | Move to `Main()` locals, thread through startup chain |
| `_sensorSignalsOpen` / `_trainingDetailsOpen` should be instance fields | `AnalyticsWindow.cs` | Remove `static`, make per-window |
| `_analyticsScrollVerificationAttempts` captured in closure | `nudge-tray.cs:89` | Move to local in `VerifyAnalyticsScroll()` |

### Duplicate code

| Issue | File:Line | Fix |
|-------|-----------|-----|
| `FormatSec` with inconsistent formats between `AnalyticsWindow.cs:308` and `SettingsWindow.cs:44` | Both | Extract shared utility method |

---

## Rejected

### ✗ Extract `RunMainLoop()` into discrete phases
3-phase Tick/Evaluate/Act split rejected: leaky boundaries, linear code easier to trace, testability needs DI.
