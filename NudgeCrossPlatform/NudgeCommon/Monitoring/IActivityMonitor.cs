namespace NudgeCommon.Monitoring;

/// <summary>
/// Interface for monitoring user activity across different platforms
/// </summary>
public interface IActivityMonitor
{
    /// <summary>
    /// Get the name of the foreground application
    /// </summary>
    string GetForegroundApp();

    /// <summary>
    /// Get milliseconds since last keyboard activity
    /// </summary>
    int GetKeyboardInactivityMs();

    /// <summary>
    /// Get milliseconds since last mouse activity
    /// </summary>
    int GetMouseInactivityMs();

    /// <summary>
    /// Get current attention span (time in current app) in milliseconds
    /// </summary>
    int GetAttentionSpanMs();

    /// <summary>
    /// Reset attention span counter
    /// </summary>
    void ResetAttentionSpan();

    /// <summary>
    /// Update the monitor state (call periodically)
    /// </summary>
    void Update(int cycleMs);
}
