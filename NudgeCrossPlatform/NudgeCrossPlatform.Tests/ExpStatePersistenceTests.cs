using System;
using System.IO;
using NudgeCore;
using Xunit;

namespace NudgeCrossPlatform.Tests;

// WP6 persistence — V4_REDESIGN/06_STATE_AND_PROCESS.md §6. Round-trip + corrupt/missing → default.
public class ExpStatePersistenceTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "nudge_state_test_" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Baseline_RoundTrip()
    {
        string path = TempPath();
        try
        {
            var s = new BaselineState { Mean = 0.73, Var = 0.041, Count = 128, UpdatedUnix = 1_700_000_000 };
            V4State.FlushBaseline(s, path);
            var r = V4State.LoadBaseline(path);
            Assert.Equal(s.Mean, r.Mean, 9);
            Assert.Equal(s.Var, r.Var, 9);
            Assert.Equal(s.Count, r.Count);
            Assert.Equal(s.UpdatedUnix, r.UpdatedUnix);
        }
        finally { Try(() => File.Delete(path)); }
    }

    [Fact]
    public void Baseline_MissingFile_ReturnsDefault()
    {
        var r = V4State.LoadBaseline(TempPath()); // never created
        Assert.Equal(0, r.Count);
        Assert.Equal(0.0, r.Mean);
    }

    [Fact]
    public void Baseline_CorruptFile_ReturnsDefault()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, "{ this is not valid json ]");
            var r = V4State.LoadBaseline(path);
            Assert.Equal(0, r.Count);
        }
        finally { Try(() => File.Delete(path)); }
    }

    [Fact]
    public void Calibration_RoundTrip()
    {
        string path = TempPath();
        try
        {
            var s = new CalibrationState
            {
                Threshold = 0.71, TargetNudgesPerHour = 1.0, NudgesEwma = 1.4,
                FalsePositiveStreak = 3, LastNudgeUnix = 1_700_000_123, UpdatedUnix = 1_700_000_456
            };
            V4State.FlushCalibration(s, path);
            var r = V4State.LoadCalibration(path);
            Assert.Equal(s.Threshold, r.Threshold, 9);
            Assert.Equal(s.NudgesEwma, r.NudgesEwma, 9);
            Assert.Equal(s.FalsePositiveStreak, r.FalsePositiveStreak);
            Assert.Equal(s.LastNudgeUnix, r.LastNudgeUnix);
        }
        finally { Try(() => File.Delete(path)); }
    }

    [Fact]
    public void Calibration_MissingFile_ReturnsDefault()
    {
        var r = V4State.LoadCalibration(TempPath());
        var d = Calibrator.Default();
        Assert.Equal(d.Threshold, r.Threshold, 9);
        Assert.Equal(d.TargetNudgesPerHour, r.TargetNudgesPerHour, 9);
    }

    [Fact]
    public void Calibration_ZeroTargetInFile_FallsBackToDefaultTarget()
    {
        string path = TempPath();
        try
        {
            // A corrupt/partial file with target 0 must not let the governor push the threshold up.
            var bad = new CalibrationState { Threshold = 0.6, TargetNudgesPerHour = 0.0, NudgesEwma = 1.0 };
            V4State.FlushCalibration(bad, path);
            var r = V4State.LoadCalibration(path);
            Assert.True(r.TargetNudgesPerHour > 0);
        }
        finally { Try(() => File.Delete(path)); }
    }

    private static void Try(Action a) { try { a(); } catch { /* best effort */ } }
}
