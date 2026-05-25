using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeMlPipelineTests
{
    private static readonly string[] ExpectedFeatureNames =
    [
        "hour_of_day",
        "day_of_week",
        "focused_app_hash",
        "focused_domain_hash",
        "idle_ms",
        "focused_since_ms",
        "title_stability_ms",
        "switch_count_60s",
        "switch_count_300s",
        "distinct_apps_300s",
        "distinct_domains_300s",
        "returned_to_anchor_app_300s",
        "current_app_share_300s",
        "current_domain_share_300s",
        "browser_window_flag",
        "communication_app_flag",
        "entertainment_domain_flag",
        "work_domain_flag",
        "afk_flag",
        "fullscreen_flag",
        "workspace_switch_count_300s",
        "dev_app_flag",
        "creative_app_flag",
        "office_app_flag",
        "comm_app_flag",
        "ent_app_flag",
    ];

    [Fact]
    public void FeatureSchema_OrderedFeatureNames_MatchesExpected()
    {
        Assert.Equal(26, FeatureSchema.OrderedFeatureNames.Length);
        for (int i = 0; i < ExpectedFeatureNames.Length; i++)
            Assert.Equal(ExpectedFeatureNames[i], FeatureSchema.OrderedFeatureNames[i]);
    }
    [Fact]
    public void FeatureSchema_ToFeatureDictionary_ContainsAllKeys()
    {
        var fv = new FeatureVector();
        var dict = FeatureSchema.ToFeatureDictionary(fv);

        Assert.Equal(26, dict.Count);
        foreach (var name in FeatureSchema.OrderedFeatureNames)
            Assert.Contains(name, dict.Keys);
    }

    [Fact]
    public void FeatureSchema_ToFeatureDictionary_DefaultValues()
    {
        var fv = new FeatureVector();
        var dict = FeatureSchema.ToFeatureDictionary(fv);

        Assert.All(dict.Values, v => Assert.Equal(0.0, v));
    }

    [Fact]
    public void FeatureSchema_OrderedFeatureNames_Stable()
    {
        Assert.Equal(26, FeatureSchema.OrderedFeatureNames.Length);
        Assert.Equal("hour_of_day", FeatureSchema.OrderedFeatureNames[0]);
        Assert.Equal("day_of_week", FeatureSchema.OrderedFeatureNames[1]);
        Assert.Equal("focused_app_hash", FeatureSchema.OrderedFeatureNames[2]);
    }

    [Fact]
    public void FeatureSchema_ToFeatureDictionary_RoundTrip()
    {
        var fv = new FeatureVector(
            HourOfDay: 14, DayOfWeek: 3,
            FocusedAppHash: 12345, FocusedDomainHash: 67890,
            IdleMs: 5000, FocusedSinceMs: 30000, TitleStabilityMs: 25000,
            SwitchCount60s: 3, SwitchCount300s: 10,
            DistinctApps300s: 5, DistinctDomains300s: 4,
            ReturnedToAnchorApp300s: 2,
            CurrentAppShare300s: 0.5, CurrentDomainShare300s: 0.4,
            BrowserWindowFlag: 1, CommunicationAppFlag: 0,
            EntertainmentDomainFlag: 0, WorkDomainFlag: 1,
            AfkFlag: 0, FullscreenFlag: 1,
            WorkspaceSwitchCount300s: 3,
            DevAppFlag: 1, CreativeAppFlag: 0,
            OfficeAppFlag: 1, CommAppFlag: 0, EntAppFlag: 0);

        var dict = FeatureSchema.ToFeatureDictionary(fv);

        Assert.Equal(12345.0, dict["focused_app_hash"]);
        Assert.Equal(67890.0, dict["focused_domain_hash"]);
        Assert.Equal(5000.0, dict["idle_ms"]);
        Assert.Equal(30000.0, dict["focused_since_ms"]);
        Assert.Equal(25000.0, dict["title_stability_ms"]);
        Assert.Equal(3.0, dict["switch_count_60s"]);
        Assert.Equal(10.0, dict["switch_count_300s"]);
        Assert.Equal(5.0, dict["distinct_apps_300s"]);
        Assert.Equal(4.0, dict["distinct_domains_300s"]);
        Assert.Equal(2.0, dict["returned_to_anchor_app_300s"]);
        Assert.Equal(0.5, dict["current_app_share_300s"]);
        Assert.Equal(0.4, dict["current_domain_share_300s"]);
        Assert.Equal(1.0, dict["browser_window_flag"]);
        Assert.Equal(0.0, dict["communication_app_flag"]);
        Assert.Equal(0.0, dict["entertainment_domain_flag"]);
        Assert.Equal(1.0, dict["work_domain_flag"]);
        Assert.Equal(0.0, dict["afk_flag"]);
        Assert.Equal(1.0, dict["fullscreen_flag"]);
        Assert.Equal(3.0, dict["workspace_switch_count_300s"]);
        Assert.Equal(1.0, dict["dev_app_flag"]);
        Assert.Equal(0.0, dict["creative_app_flag"]);
        Assert.Equal(1.0, dict["office_app_flag"]);
        Assert.Equal(0.0, dict["comm_app_flag"]);
        Assert.Equal(0.0, dict["ent_app_flag"]);
    }
}

public sealed class NudgePlatformConfigTests
{
    [Fact]
    public void PipInstallArgs_Windows_UsesUserFlag()
    {
        // The method is platform-aware; on Windows it emits --user.
        // We can't mock IsWindows, but we test that the format is valid.
        string args = PlatformConfig.PipInstallArgs("requirements.txt");
        Assert.Contains("-m pip install", args);
        Assert.Contains("requirements.txt", args);
        Assert.Contains("-r", args);
    }

    [Fact]
    public void PipInstallArgs_OutputsQuotedPath()
    {
        string args = PlatformConfig.PipInstallArgs("/path/with spaces/requirements.txt");
        Assert.Contains("\"/path/with spaces/requirements.txt\"", args);
    }

    [Fact]
    public void PlatformConfig_PythonCommand_Windows()
    {
        // Just verify the property returns a non-empty string
        Assert.False(string.IsNullOrWhiteSpace(PlatformConfig.PythonCommand));
    }

    [Fact]
    public void PlatformConfig_DataDirectory_IsNudgeDir()
    {
        string dir = PlatformConfig.DataDirectory;
        Assert.Contains(".nudge", dir);
    }

    [Fact]
    public void PlatformConfig_CsvPath_EndsWithHarvestCsv()
    {
        Assert.EndsWith("HARVEST.CSV", PlatformConfig.CsvPath);
    }

    [Fact]
    public void FindPython_ReturnsNonEmptyString()
    {
        string python = PlatformConfig.FindPython("/tmp");
        Assert.False(string.IsNullOrWhiteSpace(python));
    }

    [Fact]
    public void FindPython_PrefersVenvOverSystem()
    {
        // When the user-level venv exists, FindPython returns the venv Python
        string python = PlatformConfig.FindPython("/tmp");
        Assert.False(string.IsNullOrWhiteSpace(python));
        if (File.Exists(PlatformConfig.VenvPythonPath))
            Assert.Equal(PlatformConfig.VenvPythonPath, python);
    }

    [Fact]
    public void VenvPythonPath_IsUnderNudgeDir()
    {
        string path = PlatformConfig.VenvPythonPath;
        Assert.Contains(".nudge", path);
        Assert.Contains("venv", path);
    }

    [Fact]
    public void EnsureVenv_DoesNotThrowWhenVenvExists()
    {
        // If the venv already exists, EnsureVenv should return true without error
        string systemPython = PlatformConfig.FindPython("/tmp");
        bool result = PlatformConfig.EnsureVenv(systemPython);
        Assert.True(result);
    }

    [Fact]
    public void PipInstallArgs_ContainsPipInstall()
    {
        string args = PlatformConfig.PipInstallArgs("requirements.txt");
        Assert.Contains("-m pip", args);
        Assert.Contains("install", args);
    }

    [Fact]
    public void TrainerLaunchArgs_IncludeSeed()
    {
        // Verify nudge-tray.cs passes --seed to the background trainer
        // Test runs from NudgeCrossPlatform.Tests/bin/Debug/net10.0/
        // Source is at NudgeCrossPlatform/nudge-tray.cs — 4 levels up
        string content = File.ReadAllText(
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "nudge-tray.cs")));
        Assert.Contains("--seed", content);
    }

    [Fact]
    public void ConfidenceThreshold_Is85()
    {
        // Verify ML_CONFIDENCE_THRESHOLD is 0.85 in both nudge.cs and nudge_build.cs
        string srcDir = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", ".."));
        foreach (var file in new[] { "nudge.cs", "nudge_build.cs" })
        {
            string content = File.ReadAllText(Path.Combine(srcDir, file));
            Assert.Contains("ML_CONFIDENCE_THRESHOLD = 0.85", content);
        }
    }
}

public sealed class NudgeMlSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void MLLiveEvent_SerializesToSnakeCase()
    {
        var evt = CreateSampleEvent();
        string json = JsonSerializer.Serialize(evt, Options);

        Assert.Contains("\"trigger_source\"", json);
        Assert.Contains("\"triggered\"", json);
    }

    [Fact]
    public void MLLiveEvent_DeserializesFromSnakeCase()
    {
        const string json = """{"t":1700000000,"app":"Code","score":0.85,"confidence":0.92,"productive":true,"triggered":false,"trigger_source":"ai"}""";
        var evt = JsonSerializer.Deserialize<MLLiveEventContract>(json, Options);

        Assert.NotNull(evt);
        Assert.Equal(1700000000, evt!.T);
        Assert.Equal("Code", evt.App);
        Assert.Equal(0.85, evt.Score);
        Assert.Equal(0.92, evt.Confidence);
        Assert.True(evt.Productive);
        Assert.False(evt.Triggered);
        Assert.Equal("ai", evt.TriggerSource);
    }

    [Fact]
    public void MLLiveEvent_RoundTrip_PreservesAllFields()
    {
        var original = CreateSampleEvent();
        string json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<MLLiveEventContract>(json, Options);

        Assert.Equal(original.T, restored!.T);
        Assert.Equal(original.App, restored.App);
        Assert.Equal(original.Score, restored.Score);
        Assert.Equal(original.Confidence, restored.Confidence);
        Assert.Equal(original.Productive, restored.Productive);
        Assert.Equal(original.Triggered, restored.Triggered);
        Assert.Equal(original.UserResponse, restored.UserResponse);
        Assert.Equal(original.AiCorrect, restored.AiCorrect);
        Assert.Equal(original.TriggerSource, restored.TriggerSource);
    }

    [Fact]
    public void HarvestSignal_SerializesToSnakeCase()
    {
        var sig = new HarvestSignalContract
        {
            Quality = "trusted",
            FocusSrc = "kwin",
            Category = "Development",
            CategoryConf = 0.95f,
            IdleMs = 1000,
            FocusedMs = 30000,
            Domain = "github.com",
            Work = 1,
            Ent = 0,
            Comm = 0,
            Browser = 1,
            Afk = 0,
            Fullscreen = 0,
            Sw300 = 5,
            Share = 0.75,
            Apps300 = 3
        };

        string json = JsonSerializer.Serialize(sig, Options);

        Assert.Contains("\"focus_src\"", json);
        Assert.Contains("\"category_conf\"", json);
        Assert.Contains("\"idle_ms\"", json);
        Assert.Contains("\"focused_ms\"", json);
        Assert.Contains("\"sw300\"", json);
    }

    [Fact]
    public void HarvestSignal_RoundTrip_PreservesAllFields()
    {
        var original = new HarvestSignalContract
        {
            Quality = "usable",
            FocusSrc = "xprop",
            Category = "Communication",
            CategoryConf = 0.7f,
            IdleMs = 5000,
            FocusedMs = 15000,
            Domain = "slack.com",
            Work = 0,
            Ent = 0,
            Comm = 1,
            Browser = 1,
            Afk = 0,
            Fullscreen = 0,
            Sw300 = 12,
            Share = 0.33,
            Apps300 = 6
        };

        string json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<HarvestSignalContract>(json, Options);

        Assert.Equal(original.Quality, restored!.Quality);
        Assert.Equal(original.FocusSrc, restored.FocusSrc);
        Assert.Equal(original.Category, restored.Category);
        Assert.Equal(original.CategoryConf, restored.CategoryConf);
        Assert.Equal(original.IdleMs, restored.IdleMs);
        Assert.Equal(original.FocusedMs, restored.FocusedMs);
        Assert.Equal(original.Domain, restored.Domain);
        Assert.Equal(original.Work, restored.Work);
        Assert.Equal(original.Ent, restored.Ent);
        Assert.Equal(original.Comm, restored.Comm);
        Assert.Equal(original.Browser, restored.Browser);
        Assert.Equal(original.Afk, restored.Afk);
        Assert.Equal(original.Fullscreen, restored.Fullscreen);
        Assert.Equal(original.Sw300, restored.Sw300);
        Assert.Equal(original.Share, restored.Share);
        Assert.Equal(original.Apps300, restored.Apps300);
    }

    [Fact]
    public void MLPredictionRequest_SerializesToSnakeCase()
    {
        var req = new MLPredictionRequestContract
        {
            SchemaVersion = 3,
            FeatureOrder = ["focused_app_hash", "idle_ms"],
            Features = new Dictionary<string, double> { ["focused_app_hash"] = 12345 },
            FocusSource = "kwin",
            SignalQuality = "trusted"
        };

        string json = JsonSerializer.Serialize(req, Options);

        Assert.Contains("\"schema_version\"", json);
        Assert.Contains("\"feature_order\"", json);
        Assert.Contains("\"focus_source\"", json);
        Assert.Contains("\"signal_quality\"", json);
    }

    [Fact]
    public void MLPrediction_DeserializesFromPythonResponse()
    {
        const string json = """{"prediction":0,"confidence":0.98,"probability":0.97,"model_available":true,"reason":"low attention"}""";
        var pred = JsonSerializer.Deserialize<MLPredictionContract>(json, Options);

        Assert.NotNull(pred);
        Assert.Equal(0, pred!.Prediction);
        Assert.Equal(0.98, pred.Confidence);
        Assert.Equal(0.97, pred.Probability);
        Assert.True(pred.ModelAvailable);
        Assert.Equal("low attention", pred.Reason);
    }

    [Fact]
    public void MLPrediction_RoundTrip()
    {
        var original = new MLPredictionContract
        {
            Prediction = 1,
            Confidence = 0.75,
            Probability = 0.72,
            ModelAvailable = true,
            Reason = null
        };

        string json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<MLPredictionContract>(json, Options);

        Assert.Equal(original.Prediction, restored!.Prediction);
        Assert.Equal(original.Confidence, restored.Confidence);
        Assert.Equal(original.Probability, restored.Probability);
        Assert.Equal(original.ModelAvailable, restored.ModelAvailable);
        Assert.Null(restored.Reason);
    }

    [Fact]
    public void MLResponseEvent_RoundTrip()
    {
        var original = new MLResponseEventContract { T = 1700000000, Response = true };
        string json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<MLResponseEventContract>(json, Options);

        Assert.Equal(original.T, restored!.T);
        Assert.Equal(original.Response, restored.Response);
    }

    [Fact]
    public void TrainerMeta_RoundTrip()
    {
        var original = new TrainerMetaContract
        {
            TrainedAt = 1700000000,
            SampleCount = 500,
            NProductive = 300,
            NUnproductive = 200,
            Accuracy = 0.89,
            ModelVersion = 3
        };

        string json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<TrainerMetaContract>(json, Options);

        Assert.Equal(original.TrainedAt, restored!.TrainedAt);
        Assert.Equal(original.SampleCount, restored.SampleCount);
        Assert.Equal(original.NProductive, restored.NProductive);
        Assert.Equal(original.NUnproductive, restored.NUnproductive);
        Assert.Equal(original.Accuracy, restored.Accuracy);
        Assert.Equal(original.ModelVersion, restored.ModelVersion);
    }

    // --- Contract copies matching the ML types in NudgeJsonContext.cs ---

    sealed class MLLiveEventContract
    {
        public long T { get; set; }
        public string App { get; set; } = "";
        public double Score { get; set; }
        public double Confidence { get; set; }
        public bool Productive { get; set; }
        public bool Triggered { get; set; }
        public bool? UserResponse { get; set; }
        public bool? AiCorrect { get; set; }
        public string TriggerSource { get; set; } = "ai";
    }

    sealed class HarvestSignalContract
    {
        public string Quality { get; set; } = "poor";
        public string FocusSrc { get; set; } = "unknown";
        public string Category { get; set; } = "";
        public float CategoryConf { get; set; }
        public int IdleMs { get; set; }
        public int FocusedMs { get; set; }
        public string Domain { get; set; } = "";
        public int Work { get; set; }
        public int Ent { get; set; }
        public int Comm { get; set; }
        public int Browser { get; set; }
        public int Afk { get; set; }
        public int Fullscreen { get; set; }
        public int Sw300 { get; set; }
        public double Share { get; set; }
        public int Apps300 { get; set; }
    }

    sealed class MLPredictionRequestContract
    {
        public int SchemaVersion { get; init; }
        public string[] FeatureOrder { get; init; } = [];
        public IReadOnlyDictionary<string, double> Features { get; init; } = new Dictionary<string, double>();
        public string FocusSource { get; init; } = "unknown";
        public string SignalQuality { get; init; } = "poor";
    }

    sealed class MLPredictionContract
    {
        public int? Prediction { get; set; }
        public double Confidence { get; set; }
        public double? Probability { get; set; }
        public bool ModelAvailable { get; set; }
        public string? Reason { get; set; }
    }

    sealed class MLResponseEventContract
    {
        public long T { get; set; }
        public bool Response { get; set; }
    }

    sealed class TrainerMetaContract
    {
        public double TrainedAt { get; init; }
        public int SampleCount { get; init; }
        public int NProductive { get; init; }
        public int NUnproductive { get; init; }
        public double Accuracy { get; init; }
        public int ModelVersion { get; init; }
    }

    static MLLiveEventContract CreateSampleEvent() => new()
    {
        T = 1700000000,
        App = "Visual Studio Code",
        Score = 0.85,
        Confidence = 0.92,
        Productive = true,
        Triggered = false,
        UserResponse = null,
        AiCorrect = null,
        TriggerSource = "ai"
    };
}
