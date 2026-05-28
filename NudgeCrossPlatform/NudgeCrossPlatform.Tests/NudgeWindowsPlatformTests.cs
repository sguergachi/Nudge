using System;
using System.Runtime.InteropServices;
using Xunit;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeWindowsPlatformTests
{
    // ── Win32 surface under test ──────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, char[] text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private struct LASTINPUTINFO
    {
        public int cbSize;
        public uint dwTime;
    }

    // ── Tests (no-op on non-Windows) ─────────────────────────────────────────

    [Fact]
    public void GetForegroundWindow_ReturnsNonZero()
    {
        if (!OperatingSystem.IsWindows()) return;
        // Skip when no window has focus (CI/headless environments)
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        Assert.NotEqual(IntPtr.Zero, hwnd);
    }

    [Fact]
    public void GetWindowText_ForegroundWindow_ReturnsNonNegativeLength()
    {
        if (!OperatingSystem.IsWindows()) return;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        var buf = new char[512];
        var len = GetWindowText(hwnd, buf, buf.Length);

        Assert.True(len >= 0);
    }

    [Fact]
    public void GetWindowThreadProcessId_ForegroundWindow_ReturnsValidPid()
    {
        if (!OperatingSystem.IsWindows()) return;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        uint threadId = GetWindowThreadProcessId(hwnd, out uint pid);

        Assert.True(pid > 0);
        Assert.True(threadId > 0);
    }

    [Fact]
    public void IsWindow_ForegroundWindow_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        Assert.True(IsWindow(hwnd));
    }

    [Fact]
    public void IsWindow_ZeroHandle_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.False(IsWindow(IntPtr.Zero));
    }

    [Fact]
    public void GetLastInputInfo_ReturnsNonNegativeIdleTime()
    {
        if (!OperatingSystem.IsWindows()) return;

        LASTINPUTINFO info = new();
        info.cbSize = Marshal.SizeOf<LASTINPUTINFO>();
        var ok = GetLastInputInfo(ref info);

        Assert.True(ok);
        var idleMs = (int)(Environment.TickCount64 - info.dwTime);
        Assert.True(idleMs >= 0);
    }

    [Fact]
    public void PlatformConfig_OnWindows_ReturnsWindowsSettings()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.True(NudgeCore.PlatformConfig.IsWindows);
        Assert.Equal("where", NudgeCore.PlatformConfig.WhichCommand);
        Assert.Equal("dotnet.exe", NudgeCore.PlatformConfig.DotnetCommand);
        Assert.Equal("python", NudgeCore.PlatformConfig.PythonCommand);
    }
}
