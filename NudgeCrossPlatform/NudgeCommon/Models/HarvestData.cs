using CsvHelper.Configuration.Attributes;

namespace NudgeCommon.Models;

/// <summary>
/// Represents harvested productivity data for ML training
/// Column names must match exactly what the TensorFlow model expects
/// </summary>
public class HarvestData
{
    /// <summary>
    /// Hash of the foreground application name
    /// Maps to model feature: foreground_app
    /// </summary>
    [Name("foreground_app")]
    public int ForegroundAppHash { get; set; }

    /// <summary>
    /// Milliseconds since last user input activity (keyboard or mouse)
    /// Maps to model feature: idle_time
    /// Note: On Linux, X11 idle time covers both keyboard and mouse,
    /// so we use a single unified measurement instead of separate fields
    /// </summary>
    [Name("idle_time")]
    public int IdleTime { get; set; }

    /// <summary>
    /// Milliseconds focused on current application (attention span)
    /// Maps to model feature: time_last_request
    /// NOTE: Column name "time_last_request" is legacy/confusing - it actually measures
    /// attention span (time in current app), not time of last request. Kept for
    /// backward compatibility with existing training data and models.
    /// </summary>
    [Name("time_last_request")]
    public int AttentionSpan { get; set; }

    /// <summary>
    /// Whether the user was being productive (1) or not (0)
    /// Maps to model target: productive
    /// </summary>
    [Name("productive")]
    public byte Productive { get; set; }

    /// <summary>
    /// The actual foreground application name (for debugging only)
    /// This field is NOT written to CSV to avoid confusing the ML model
    /// </summary>
    [Ignore]
    public string? ForegroundApp { get; set; }
}
