using System.Collections.Generic;
using System.Text.Json.Serialization;
using NudgeCore;

namespace NudgeTray;

internal sealed class MLPredictionRequest
{
    public int SchemaVersion { get; init; } = FeatureSchema.SchemaVersion;
    public string[] FeatureOrder { get; init; } = [];
    public IReadOnlyDictionary<string, double> Features { get; init; } = new Dictionary<string, double>();
    public string FocusSource { get; init; } = "unknown";
    public string SignalQuality { get; init; } = "poor";
}

internal sealed class MLPrediction
{
    public int? Prediction { get; set; }
    public double Confidence { get; set; }
    public double? Probability { get; set; }
    public bool ModelAvailable { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Broadcast over stdout as "MLDATA:{json}" by nudge.cs after every ML check.
/// Consumed by nudge-tray.cs → LiveAIState → AnalyticsWindow AI tab.
/// </summary>
internal sealed class MLLiveEvent
{
    /// <summary>Unix timestamp (seconds UTC)</summary>
    public long T { get; set; }
    /// <summary>Foreground app name at time of check</summary>
    public string App { get; set; } = "";
    /// <summary>Productivity score 0-1: 1=AI thinks productive, 0=not productive</summary>
    public double Score { get; set; }
    /// <summary>Raw model confidence (0-1)</summary>
    public double Confidence { get; set; }
    /// <summary>True if model predicted productive</summary>
    public bool Productive { get; set; }
    /// <summary>True if this prediction caused a nudge notification</summary>
    public bool Triggered { get; set; }
    /// <summary>User's response: true=YES productive, false=NO not productive, null=pending/skipped</summary>
    public bool? UserResponse { get; set; }
    /// <summary>Whether the AI prediction matched the user's response</summary>
    public bool? AiCorrect { get; set; }
}

/// <summary>
/// Broadcast over stdout as "MLRESPONSE:{json}" by nudge.cs after the user responds.
/// Consumed by nudge-tray.cs to update the corresponding MLLiveEvent.
/// </summary>
internal sealed class MLResponseEvent
{
    /// <summary>Unix timestamp (seconds UTC) of the original ML prediction</summary>
    public long T { get; set; }
    /// <summary>User response: true=YES productive, false=NO not productive</summary>
    public bool Response { get; set; }
}

/// <summary>
/// Broadcast over stdout as "HARVEST:{json}" by nudge.cs every 2 seconds.
/// Contains key sensor fusion signals for display in the AI Brain tab.
/// </summary>
internal sealed class HarvestSignal
{
    public string Quality { get; set; } = "poor";     // "trusted", "usable", "poor"
    public string FocusSrc { get; set; } = "unknown"; // FocusSource enum name (snake_case)
    public string Category { get; set; } = "";        // AppCategoryClassifier result
    public float CategoryConf { get; set; }           // confidence score 0.0–1.0
    public int IdleMs { get; set; }
    public int FocusedMs { get; set; }
    public string Domain { get; set; } = "";
    public int Work { get; set; }       // WorkDomainFlag
    public int Ent { get; set; }        // EntertainmentDomainFlag
    public int Comm { get; set; }       // CommunicationAppFlag
    public int Browser { get; set; }    // BrowserWindowFlag
    public int Afk { get; set; }        // AfkFlag
    public int Fullscreen { get; set; } // FullscreenFlag
    public int Sw300 { get; set; }      // SwitchCount300s
    public double Share { get; set; }   // CurrentAppShare300s
    public int Apps300 { get; set; }    // DistinctApps300s
}

internal sealed class NotificationPositionConfig
{
    public double X { get; set; }
    public double Y { get; set; }
    public bool HasSavedPosition { get; set; }
}

/// <summary>
/// Persisted user preferences written to ~/.nudge/tray-settings.json.
/// </summary>
internal sealed class TraySettings
{
    /// <summary>Whether ML mode was last enabled by the user.</summary>
    public bool MlEnabled { get; set; }
    /// <summary>Last-used snapshot interval in minutes.</summary>
    public int IntervalMinutes { get; set; } = 5;
}

/// <summary>
/// Written by background_trainer.py after each training run.
/// Read by TrainerState.RefreshFromCsv() to show training status.
/// </summary>
internal sealed class TrainerMeta
{
    /// <summary>Unix timestamp (seconds) of the last training completion.</summary>
    public double TrainedAt { get; init; }
    /// <summary>Number of samples used in the last training run.</summary>
    public int SampleCount { get; init; }
    /// <summary>Accuracy score of the trained model (0-1).</summary>
    public double Accuracy { get; init; }
    /// <summary>Monotonically increasing version number.</summary>
    public int ModelVersion { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MLPredictionRequest))]
[JsonSerializable(typeof(MLPrediction))]
[JsonSerializable(typeof(MLLiveEvent))]
[JsonSerializable(typeof(List<MLLiveEvent>))]
[JsonSerializable(typeof(HarvestSignal))]
[JsonSerializable(typeof(MLResponseEvent))]
[JsonSerializable(typeof(NotificationPositionConfig))]
[JsonSerializable(typeof(TraySettings))]
[JsonSerializable(typeof(TrainerMeta))]
internal sealed partial class NudgeJsonContext : JsonSerializerContext
{
}
