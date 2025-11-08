namespace NudgeCommon.Models;

/// <summary>
/// Represents harvested productivity data
/// </summary>
public class HarvestData
{
    /// <summary>
    /// Hash of the foreground application name
    /// </summary>
    public int ForegroundAppHash { get; set; }

    /// <summary>
    /// Milliseconds since last keyboard activity
    /// </summary>
    public int KeyboardActivity { get; set; }

    /// <summary>
    /// Milliseconds since last mouse activity
    /// </summary>
    public int MouseActivity { get; set; }

    /// <summary>
    /// Milliseconds focused on current application
    /// </summary>
    public int AttentionSpan { get; set; }

    /// <summary>
    /// Whether the user was being productive (1) or not (0)
    /// </summary>
    public byte Productive { get; set; }

    /// <summary>
    /// The actual foreground application name (for debugging)
    /// </summary>
    public string? ForegroundApp { get; set; }
}
