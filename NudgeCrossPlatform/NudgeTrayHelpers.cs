using System.Collections.Generic;

namespace NudgeTray;

/// <summary>
/// Deduplication logic for SUPPRESS events. When the ML daemon emits MLDATA
/// immediately followed by SUPPRESS (gate suppression), we mutate the existing
/// event rather than appending a Score=0 duplicate — the root cause of the
/// zig-zag in the prediction history graph (fixed in commit dd9903c).
/// </summary>
internal static class SuppressionDeduplication
{
    private const int RecentWindowSeconds = 5;

    /// <summary>
    /// If <paramref name="latest"/> is non-null and was recorded within
    /// <see cref="RecentWindowSeconds"/> of <paramref name="nowEpochSec"/>, stamps
    /// its <see cref="MLLiveEvent.SuppressReason"/> and clears
    /// <see cref="MLLiveEvent.Triggered"/> in-place and returns <c>true</c>.
    /// Returns <c>false</c> when the caller must create a new standalone event.
    /// </summary>
    internal static bool TryMutateLatest(MLLiveEvent? latest, string reason, long nowEpochSec)
    {
        if (latest == null || nowEpochSec - latest.T > RecentWindowSeconds) return false;
        latest.SuppressReason = reason;
        latest.Triggered = false;
        return true;
    }
}

/// <summary>
/// Filtering helpers for the AI prediction history chart.
/// Interval fallbacks (TriggerSource="int") and standalone suppression
/// placeholders (TriggerSource="sup") must be excluded so they don't
/// cause artificial zig-zags in the gradient chart (fixed in commit dd9903c).
/// </summary>
internal static class PredictionChartHelper
{
    internal static List<MLLiveEvent> FilterToAiOnly(IReadOnlyList<MLLiveEvent> events)
    {
        var result = new List<MLLiveEvent>(events.Count);
        foreach (var e in events)
        {
            if (e.TriggerSource == "ai")
                result.Add(e);
        }
        return result;
    }
}
