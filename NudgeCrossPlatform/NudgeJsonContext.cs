using System.Text.Json.Serialization;

internal readonly record struct MLPredictionRequest(
    int HourOfDay,
    int DayOfWeek,
    int ForegroundApp,
    int IdleTime,
    int TimeLastRequest);

internal sealed class MLPrediction
{
    public int? Prediction { get; set; }
    public double Confidence { get; set; }
    public double? Probability { get; set; }
    public bool ModelAvailable { get; set; }
    public string? Reason { get; set; }
}

namespace NudgeTray
{
    internal sealed class NotificationPositionConfig
    {
        public double X { get; set; }
        public double Y { get; set; }
        public bool HasSavedPosition { get; set; }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MLPredictionRequest))]
[JsonSerializable(typeof(MLPrediction))]
[JsonSerializable(typeof(NudgeTray.NotificationPositionConfig))]
internal partial class NudgeJsonContext : JsonSerializerContext
{
}
