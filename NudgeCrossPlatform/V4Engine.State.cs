using System;
using System.IO;
using System.Text.Json;
using NudgeTray;

namespace NudgeCore;

// WP6 — persistence for the V4 decision-engine state (exp_baseline.json, exp_calibration.json).
// SPEC: V4_REDESIGN/06_STATE_AND_PROCESS.md. Atomic writes, never throw on the harvest path,
// defaults on miss/corrupt. Serialized via NudgeJsonContext source-gen (AOT/trim-safe), through
// DTOs because the engine state structs use public fields (STJ serializes properties).

internal sealed class BaselineDto
{
    public double Mean { get; set; }
    public double Var { get; set; }
    public long Count { get; set; }
    public long UpdatedUnix { get; set; }
}

internal sealed class CalibrationDto
{
    public double Threshold { get; set; }
    public double TargetNudgesPerHour { get; set; }
    public double NudgesEwma { get; set; }
    public int FalsePositiveStreak { get; set; }
    public long LastNudgeUnix { get; set; }
    public long UpdatedUnix { get; set; }
}

internal static class V4State
{
    public static BaselineState LoadBaseline(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var dto = JsonSerializer.Deserialize(File.ReadAllText(path), NudgeJsonContext.Default.BaselineDto);
                if (dto is not null)
                    return new BaselineState { Mean = dto.Mean, Var = dto.Var, Count = dto.Count, UpdatedUnix = dto.UpdatedUnix };
            }
        }
        catch { /* missing/corrupt → default (cold baseline) */ }
        return default;
    }

    public static void FlushBaseline(in BaselineState s, string path)
    {
        var dto = new BaselineDto { Mean = s.Mean, Var = s.Var, Count = s.Count, UpdatedUnix = s.UpdatedUnix };
        AtomicWrite(path, JsonSerializer.Serialize(dto, NudgeJsonContext.Default.BaselineDto));
    }

    public static CalibrationState LoadCalibration(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var dto = JsonSerializer.Deserialize(File.ReadAllText(path), NudgeJsonContext.Default.CalibrationDto);
                if (dto is not null)
                    return new CalibrationState
                    {
                        Threshold = dto.Threshold,
                        // Guard a corrupt/partial file from setting a 0 target (governor would
                        // then push the threshold to its ceiling). Fall back to the default.
                        TargetNudgesPerHour = dto.TargetNudgesPerHour > 0 ? dto.TargetNudgesPerHour : 1.0,
                        NudgesEwma = dto.NudgesEwma,
                        FalsePositiveStreak = dto.FalsePositiveStreak,
                        LastNudgeUnix = dto.LastNudgeUnix,
                        UpdatedUnix = dto.UpdatedUnix,
                    };
            }
        }
        catch { /* missing/corrupt → defaults */ }
        return Calibrator.Default();
    }

    public static void FlushCalibration(in CalibrationState s, string path)
    {
        var dto = new CalibrationDto
        {
            Threshold = s.Threshold,
            TargetNudgesPerHour = s.TargetNudgesPerHour,
            NudgesEwma = s.NudgesEwma,
            FalsePositiveStreak = s.FalsePositiveStreak,
            LastNudgeUnix = s.LastNudgeUnix,
            UpdatedUnix = s.UpdatedUnix,
        };
        AtomicWrite(path, JsonSerializer.Serialize(dto, NudgeJsonContext.Default.CalibrationDto));
    }

    // Atomic + never-throw: write a temp file then rename over the target. A failed flush is
    // swallowed and retried on the next event (matches DomainReputationStore.Flush semantics).
    private static void AtomicWrite(string path, string json)
    {
        try
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* never throw on the harvest path */ }
    }
}
