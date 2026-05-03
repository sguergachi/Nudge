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

### CA1863: Use CompositeFormat
**Issue:** Using a format string repeatedly in `string.Format` or `Console.WriteLine` causes redundant parsing.
**Fix:** Pre-parse the format string into a `static readonly CompositeFormat`.
```csharp
private static readonly CompositeFormat MyFormat = CompositeFormat.Parse("Value: {0}");
// Usage
string.Format(null, MyFormat, value);
```

## 3. Workflow for Warnings

1. **Build Locally:** Run `./build.sh` or `dotnet build` frequently.
2. **Read the Rule:** Most warnings include a link to documentation (e.g., `CA1852`). Read it to understand the underlying rationale.
3. **Verify Fix:** Ensure the warning is gone and that you haven't broken any tests by running `dotnet test`.
4. **Regenerate Build Files:** If you modify `.cs` files that have corresponding `_build.cs` files (like `nudge.cs`), ensure the build files are updated.
