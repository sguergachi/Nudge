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
    /// Milliseconds since last keyboard activity
    /// Maps to model feature: keyboard_activity
    /// </summary>
    [Name("keyboard_activity")]
    public int KeyboardActivity { get; set; }

    /// <summary>
    /// Milliseconds since last mouse activity
    /// Maps to model feature: mouse_activity
    /// </summary>
    [Name("mouse_activity")]
    public int MouseActivity { get; set; }

    /// <summary>
    /// Milliseconds focused on current application (attention span)
    /// Maps to model feature: time_last_request
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
