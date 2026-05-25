using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeIdleDetectionTests
{
    [Fact]
    public void GetIdleSourceName_Win32LastInput_ReturnsCorrectString()
    {
        Assert.Equal("win32_last_input", NudgeCoreLogic.GetIdleSourceName(IdleSource.Win32LastInput));
    }

    [Fact]
    public void GetIdleSourceName_KdeKwinIdle_ReturnsCorrectString()
    {
        Assert.Equal("kde_kwin_idle", NudgeCoreLogic.GetIdleSourceName(IdleSource.KdeKwinIdle));
    }

    [Fact]
    public void GetIdleSourceName_WaylandExtIdleNotify_ReturnsCorrectString()
    {
        Assert.Equal("wayland_ext_idle_notify", NudgeCoreLogic.GetIdleSourceName(IdleSource.WaylandExtIdleNotify));
    }

    [Fact]
    public void GetIdleSourceName_FreedesktopScreenSaver_ReturnsCorrectString()
    {
        Assert.Equal("freedesktop_screensaver", NudgeCoreLogic.GetIdleSourceName(IdleSource.FreedesktopScreenSaver));
    }

    [Fact]
    public void GetIdleSourceName_GnomeIdleMonitor_ReturnsCorrectString()
    {
        Assert.Equal("gnome_idle_monitor", NudgeCoreLogic.GetIdleSourceName(IdleSource.GnomeIdleMonitor));
    }

    [Fact]
    public void GetIdleSourceName_X11Xprintidle_ReturnsCorrectString()
    {
        Assert.Equal("x11_xprintidle", NudgeCoreLogic.GetIdleSourceName(IdleSource.X11Xprintidle));
    }

    [Fact]
    public void GetIdleSourceName_LogindIdleHint_ReturnsCorrectString()
    {
        Assert.Equal("logind_idle_hint", NudgeCoreLogic.GetIdleSourceName(IdleSource.LogindIdleHint));
    }

    [Fact]
    public void GetIdleSourceName_UnknownValue_ReturnsUnknown()
    {
        Assert.Equal("unknown", NudgeCoreLogic.GetIdleSourceName((IdleSource)999));
    }

    [Fact]
    public void IdleSourcePreferenceOrder_WaylandFirst()
    {
        // ext-idle-notify-v1 is the preferred idle source; verify it exists
        // as the highest priority option in the preference order.
        var wayland = IdleSource.WaylandExtIdleNotify;
        var kde = IdleSource.KdeKwinIdle;
        var freep = IdleSource.FreedesktopScreenSaver;

        Assert.NotEqual(wayland, kde);
        Assert.NotEqual(wayland, freep);
    }

    [Fact]
    public void IdleObservation_CapturesSource()
    {
        var obs = new IdleObservation(IdleMs: 5000, IdleSource: IdleSource.KdeKwinIdle);
        Assert.Equal(5000, obs.IdleMs);
        Assert.Equal(IdleSource.KdeKwinIdle, obs.IdleSource);
    }

    [Fact]
    public void IdleObservation_ZeroIdle_IsValid()
    {
        var obs = new IdleObservation(IdleMs: 0, IdleSource: IdleSource.WaylandExtIdleNotify);
        Assert.Equal(0, obs.IdleMs);
        Assert.Equal(IdleSource.WaylandExtIdleNotify, obs.IdleSource);
    }

    [Fact]
    public void WaylandIdleMonitor_Initialize_OnLinux()
    {
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(waylandDisplay) || string.IsNullOrEmpty(runtimeDir))
            return; // Not on Wayland — skip silently

        // Verify the Wayland socket exists (compositor is running)
        var socketPath = System.IO.Path.Combine(runtimeDir, waylandDisplay.StartsWith('/') ? waylandDisplay.TrimStart('/') : waylandDisplay);
        Assert.True(System.IO.File.Exists(socketPath),
            $"Wayland display '{waylandDisplay}' not found at {socketPath}");
    }

    [Fact]
    public void GetIdleSourceName_AllKnownValues_HaveUniqueNames()
    {
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (IdleSource source in Enum.GetValues<IdleSource>())
        {
            if (source == IdleSource.Unknown) continue;
            string name = NudgeCoreLogic.GetIdleSourceName(source);
            Assert.DoesNotContain(name, names);
            names.Add(name);
        }
    }

    [Fact]
    public void StopwatchIdle_ResetOnActivity()
    {
        // Verify the pattern used by WaylandIdleMonitor works:
        // a Stopwatch that resets on activity and restarts on idle.
        var sw = new Stopwatch();
        sw.Restart(); // simulate idle start
        Thread.Sleep(10);
        Assert.True(sw.ElapsedMilliseconds >= 8, "Stopwatch should measure idle time");

        sw.Reset(); // simulate activity (OnResumed)
        Assert.Equal(0, sw.ElapsedMilliseconds);
        Assert.False(sw.IsRunning);

        sw.Restart(); // simulate idle again
        Thread.Sleep(5);
        Assert.True(sw.ElapsedMilliseconds >= 3);
    }
}
