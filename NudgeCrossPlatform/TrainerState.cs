using System;
using System.Collections.Generic;
using System.Globalization;
using NudgeCore;

namespace NudgeTray;

internal static class TrainerState
{
    private static readonly object _lock = new();
    private static readonly List<string> _log = new(capacity: 10);

    // Cache for trainer_meta.json to avoid re-reading on every refresh
    private static DateTime _lastMetaWrite;
    private static DateTime _cachedTrained;
    private static int _cachedTrainedCount;
    private static float _cachedAccuracy = -1f;

    public static int  SampleCount;
    public static int  MinSamples   = 20;
    public static int  LastTrainedCount;
    public static int  ModelVersion;
    public static bool IsTraining;
    public static float LastAccuracy = -1f;
    public static float PreviousAccuracy = -1f;
    public static string Architecture = "";
    public static string LastError    = "";
    public static DateTime LastChecked;
    public static DateTime LastTrained;
    public static float TrainingProgress = -1f;

    public static void ParseLine(string raw)
    {
        // raw is the line emitted by background_trainer.py (no prefix added yet)
        lock (_lock)
        {
            if (_log.Count >= 8) _log.RemoveAt(0);
            _log.Add(raw);
        }

        // [trainer] Labeled samples: 119  last-trained-at: 0  min: 100
        var m = System.Text.RegularExpressions.Regex.Match(raw,
            @"\[trainer\] Labeled samples:\s*(\d+)\s+last-trained-at:\s*(\d+)\s+min:\s*(\d+)");
        if (m.Success)
        {
            lock (_lock)
            {
                SampleCount      = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                LastTrainedCount = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                MinSamples       = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                LastChecked      = DateTime.Now;
                // Healthy heartbeat from a live trainer → clear any stale "Training failed" so a
                // past transient error doesn't stick as the section's state forever (a recurring
                // failure re-sets LastError on the next train attempt within the same cycle).
                LastError        = "";
                if (LastTrainedCount > 0 && LastTrained == DateTime.MinValue)
                    LastTrained = DateTime.Now;
            }
            return;
        }

        // [trainer] Training lightweight model on 119 samples...
        m = System.Text.RegularExpressions.Regex.Match(raw,
            @"\[trainer\] Training (\w+) model on (\d+) samples");
        if (m.Success)
        {
            lock (_lock)
            {
                Architecture     = m.Groups[1].Value;
                SampleCount      = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                IsTraining       = true;
                LastError        = "";
                TrainingProgress = -1f;
            }
            return;
        }

        // [trainer] Done. accuracy=0.872 version=3
        m = System.Text.RegularExpressions.Regex.Match(raw,
            @"\[trainer\] Done\. accuracy=([0-9.]+) version=(\d+)");
        if (m.Success)
        {
            lock (_lock)
            {
                IsTraining       = false;
                LastTrained      = DateTime.Now;
                LastTrainedCount = SampleCount;
                TrainingProgress = -1f;
                if (float.TryParse(m.Groups[1].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float acc))
                {
                    PreviousAccuracy = LastAccuracy;
                    LastAccuracy = acc;
                }
                if (int.TryParse(m.Groups[2].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int ver))
                    ModelVersion = ver;
            }
            return;
        }

        // [trainer] Training failed: ...
        m = System.Text.RegularExpressions.Regex.Match(raw,
            @"\[trainer\] Training failed: (.+)");
        if (m.Success)
        {
            lock (_lock)
            {
                IsTraining       = false;
                LastError        = m.Groups[1].Value;
                TrainingProgress = -1f;
            }
            return;
        }

        // [trainer] Nothing to do.
        if (raw.Contains("[trainer] Nothing to do", StringComparison.Ordinal))
        {
            lock (_lock) { IsTraining = false; TrainingProgress = -1f; }
        }
    }

    public static void RefreshFromCsv()
    {
        string csvPath = Program.ExperimentalMode ? PlatformConfig.CsvPathExp : PlatformConfig.CsvPath;
        if (!System.IO.File.Exists(csvPath)) return;
        try
        {
            var lines = System.IO.File.ReadAllLines(csvPath);
            if (lines.Length < 2) return;
            var header = lines[0].Split(',');
            int idx = System.Array.IndexOf(header, "productive");
            if (idx < 0) return;
            int count = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length > idx)
                {
                    var val = parts[idx].Trim();
                    if (val != "" && !string.Equals(val, "nan", StringComparison.OrdinalIgnoreCase))
                        count++;
                }
            }

            string modelDir = Program.ExperimentalMode ? "model_exp" : "model";
            string metaPath = System.IO.Path.Combine(PlatformConfig.DataDirectory, modelDir, "trainer_meta.json");
            DateTime trained = DateTime.MinValue;
            int trainedCount = 0;
            float accuracy = -1f;
            if (System.IO.File.Exists(metaPath))
            {
                var lastWrite = System.IO.File.GetLastWriteTimeUtc(metaPath);
                lock (_lock)
                {
                    if (lastWrite == _lastMetaWrite)
                    {
                        trained = _cachedTrained;
                        trainedCount = _cachedTrainedCount;
                        accuracy = _cachedAccuracy;
                    }
                }
                if (trained == DateTime.MinValue)
                {
                    try
                    {
                        var json = System.IO.File.ReadAllText(metaPath);
                        var meta = System.Text.Json.JsonSerializer.Deserialize(
                            json, NudgeJsonContext.Default.TrainerMeta);
                        if (meta != null)
                        {
                            if (meta.TrainedAt > 0)
                                trained = DateTimeOffset.FromUnixTimeMilliseconds(
                                    (long)(meta.TrainedAt * 1000)).LocalDateTime;
                            trainedCount = meta.SampleCount;
                            accuracy = (float)meta.Accuracy;
                            ModelVersion = meta.ModelVersion;
                        }
                        lock (_lock)
                        {
                            _lastMetaWrite = lastWrite;
                            _cachedTrained = trained;
                            _cachedTrainedCount = trainedCount;
                            _cachedAccuracy = accuracy;
                        }
                    }
                    catch { }
                }
            }

            lock (_lock)
            {
                SampleCount = count;
                LastChecked = DateTime.Now;
                if (trained != DateTime.MinValue)
                {
                    LastTrained = trained;
                    LastTrainedCount = trainedCount;
                    LastAccuracy = accuracy;
                }
            }
        }
        catch { }
    }

    public static IReadOnlyList<string> GetLog()
    {
        lock (_lock) { return _log.ToArray(); }
    }

    public static (int sample, int min, int lastTrained, bool training,
                    float acc, float prevAcc, string arch, string err,
                    DateTime lastChecked, DateTime lastTrained2,
                    int version, IReadOnlyList<string> log,
                    float trainingProgress) Snapshot()
    {
        lock (_lock)
        {
            return (SampleCount, MinSamples, LastTrainedCount, IsTraining,
                    LastAccuracy, PreviousAccuracy, Architecture, LastError,
                    LastChecked, LastTrained, ModelVersion, _log.ToArray(),
                    TrainingProgress);
        }
    }
}
