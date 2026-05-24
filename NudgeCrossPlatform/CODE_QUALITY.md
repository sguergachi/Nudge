# Code Quality & Warning Prevention Guide

This document outlines the engineering standards and best practices for the Nudge project to maintain a clean, warning-free codebase.

## 1. Core Principles

- **Zero Warning Policy:** All builds must complete with zero warnings. Warnings are often precursors to bugs or performance issues.
- **Modern C# Standards:** Leverage C# 13 and .NET 10 features for safety and performance (e.g., `FrozenSet`, `Span<T>`, `CompositeFormat`).
- **Surgical Changes:** When fixing warnings, aim for the minimal change required to solve the issue without introducing unrelated refactors.

## 2. Common Warnings & Fixes

### CA1852: Seal Internal Types
**Issue:** A class is not inherited from and is only visible within the assembly.
**Fix:** Mark the class as `sealed`. This allows the compiler to perform optimizations like devirtualization.
```csharp
// Before
class MyService { }
// After
sealed class MyService { }
```

### CA1805: Do Not Initialize Explicitly to Default Values
**Issue:** Explicitly assigning a field to its default value (e.g., `0`, `false`, `null`) is redundant.
**Fix:** Remove the explicit initialization.
```csharp
// Before
private int _count = 0;
// After
private int _count;
```

### CA1822: Mark Members as Static
**Issue:** A method does not access instance data.
**Fix:** Mark the method as `static`. This clarifies intent and can slightly improve performance.
```csharp
// Before
private string GetName() => "Nudge";
// After
private static string GetName() => "Nudge";
```

### CA1838: Avoid StringBuilder in P/Invokes
**Issue:** Using `StringBuilder` in P/Invokes causes a hidden copy.
**Fix:** Use a `char[]` buffer and pass it with `[Out]`.
```csharp
// Before
[DllImport("user32.dll")]
static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
// After
[DllImport("user32.dll")]
static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int count);
```

### CA1305/CA1304/CA1310: Culture Sensitivity
**Issue:** String operations like `ToString()`, `ToUpper()`, or `StartsWith()` depend on the current culture by default.
**Fix:** Always specify `CultureInfo.InvariantCulture` or `StringComparison.Ordinal` for machine-to-machine strings or stable identifiers.
```csharp
timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
line.StartsWith("WM_CLASS", StringComparison.Ordinal);
```

## 3. ML Pipeline Testing & Cross-Platform Rules

### 3.1 ML Serialization Contracts

ML data contracts (`MLPredictionRequest`, `MLPrediction`, `MLLiveEvent`, `HarvestSignal`, etc.) use `snake_case` JSON via `System.Text.Json` source generation. When adding or modifying these:

- **Must add/update round-trip tests** in `NudgeMlPipelineTests.cs` — serialize, deserialize, assert all fields survive.
- **Use `JsonNamingPolicy.SnakeCaseLower`** — all test assertions on serialized JSON must use snake_case keys.
- **Test both null and non-null optionals** — `bool?` and `string?` fields must survive round-trip when null.

### 3.2 Cross-Platform Helpers

`PlatformConfig` methods in `NudgeCore.TestableLogic.cs` are the single source of truth for platform detection:

- **`PythonCommand`** — returns `"python"` on Windows, `"python3"` on Linux. Never hardcode a Python path.
- **`PipInstallArgs(path)`** — returns `--user` on Windows, `--break-system-packages` on Linux. Never hardcode pip flags.
- **`FindPython(baseDir)`** — checks venv paths for both OS conventions (`Scripts/python.exe` on Windows, `bin/python` on Linux). Falls back to `"py"` on Windows (avoids Microsoft Store stub) and `PythonCommand` on Linux.

**Never hardcode paths** like `/home/user/...` or `/venv/bin/python` — use these helpers. Every Python subprocess must use `FindPython()` not a literal path.

### 3.3 Adding Tests for ML Behavior

1. **Feature vector schema tests** in `NudgeMlPipelineTests.cs` — `FeatureSchema.ToFeatureDictionary()` must produce exactly 26 keys matching `OrderedFeatureNames`. Default values must be 0.0.
2. **Round-trip serialization tests** for all ML contract types — verify all fields survive JSON serialize/deserialize.
3. **PlatformConfig tests** — verify helper methods produce correct format strings.

### 3.4 Zero Hardcoded Path Policy

Every file path, command name, or pip flag must go through a `PlatformConfig` helper or `FindPython()`/`FindScript()`. No literal `/home/...`, `/venv/...`, or OS-specific path strings outside of `PlatformConfig`.

### CA1863: Use CompositeFormat
**Issue:** Using a format string repeatedly in `string.Format` or `Console.WriteLine` causes redundant parsing.
**Fix:** Pre-parse the format string into a `static readonly CompositeFormat`.
```csharp
private static readonly CompositeFormat MyFormat = CompositeFormat.Parse("Value: {0}");
// Usage
string.Format(null, MyFormat, value);
```

## 4. UX Feedback for Long-Running Operations

**Issue:** Synchronous operations triggered by UI actions (e.g., clicking "Enable AI") block the UI thread with no progress indication — the app appears frozen.

**Fix:** Always provide immediate feedback when a user action starts a multi-second operation.

```csharp
// Before — freezes UI with no indication
enableBtn.Click += (s, e) => Program.RestartWithML();

// After — immediate feedback, async execution, error recovery
enableBtn.Click += async (s, e) =>
{
    enableBtn.IsEnabled = false;
    enableBtn.Content = "Starting…";
    descText.Text = "Installing dependencies…";
    bool success = await Task.Run(() => Program.RestartWithML());
    if (!success)
    {
        enableBtn.IsEnabled = true;
        enableBtn.Content = "Enable AI";
        descText.Text = "Failed. Check logs for details.";
    }
};
```

**Rules:**
- Disable the triggering control immediately on click to prevent double-submit.
- Show what's happening (text change, spinner, progress bar).
- Run blocking work on a background thread (`Task.Run`).
- On failure, restore the control and communicate the error to the user.
- The return type of a previously `void` blocking method should become `bool` (success/failure) or be wrapped in a try-catch with a callback.

## 5. Workflow for Warnings

1. **Build Locally:** Run `./build.sh` or `dotnet build` frequently.
2. **Read the Rule:** Most warnings include a link to documentation (e.g., `CA1852`). Read it to understand the underlying rationale.
3. **Verify Fix:** Ensure the warning is gone and that you haven't broken any tests by running `dotnet test`.
4. **Regenerate Build Files:** If you modify `.cs` files that have corresponding `_build.cs` files (like `nudge.cs`), ensure the build files are updated.
