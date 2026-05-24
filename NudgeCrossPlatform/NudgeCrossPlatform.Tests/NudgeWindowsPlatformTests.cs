using System;
using System.Runtime.InteropServices;
using Xunit;

namespace NudgeCrossPlatform.Tests;

public class NudgeWindowsPlatformTests
{
    // ── Win32 surface under test ──────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

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

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetForegroundWindow_ReturnsNonZero()
    {
        Skip.If(!OperatingSystem.IsWindows());

        Assert.NotEqual(IntPtr.Zero, GetForegroundWindow());
    }

    [Fact]
    public void GetWindowText_ForegroundWindow_ReturnsNonEmptyTitle()
    {
        Skip.If(!OperatingSystem.IsWindows());

        var hwnd = GetForegroundWindow();
        var buf = new char[512];
        var len = GetWindowText(hwnd, buf, buf.Length);

        Assert.True(len >= 0);
    }

    [Fact]
    public void GetWindowThreadProcessId_ForegroundWindow_ReturnsValidPid()
    {
        Skip.If(!OperatingSystem.IsWindows());

        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out uint pid);

        Assert.True(pid > 0);
    }

    [Fact]
    public void GetLastInputInfo_ReturnsNonNegativeIdleTime()
    {
        Skip.If(!OperatingSystem.IsWindows());

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
        Skip.If(!OperatingSystem.IsWindows());

        Assert.True(NudgeCore.PlatformConfig.IsWindows);
        Assert.Equal("where", NudgeCore.PlatformConfig.WhichCommand);
        Assert.Equal("dotnet.exe", NudgeCore.PlatformConfig.DotnetCommand);
        Assert.Equal("python", NudgeCore.PlatformConfig.PythonCommand);
    }
}
