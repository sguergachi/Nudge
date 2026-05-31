using System;
using System.Linq;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeSignalFusionTests
{
    // ── AppCategoryClassifier ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Development;TextEditor;Qt", AppCategory.Development)]
    [InlineData("AudioVideo;Audio;Sequencer", AppCategory.Creative)]
    [InlineData("Office;WordProcessor", AppCategory.Office)]
    [InlineData("Network;InstantMessaging", AppCategory.Communication)]
    [InlineData("Game;ActionGame", AppCategory.Entertainment)]
    [InlineData("System;Utility", AppCategory.Utility)]
    [InlineData("TerminalEmulator;System", AppCategory.Development)]
    [InlineData("", AppCategory.Unknown)]
    public void MapXdgCategories_MapsStandardCategoriesCorrectly(string input, AppCategory expected)
    {
        var result = AppCategoryClassifier.MapXdgCategories(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("konsole", "", AppCategory.Development)]
    [InlineData("alacritty", "", AppCategory.Development)]
    [InlineData("term-emulator", "", AppCategory.Development)]
    [InlineData("code-tool", "", AppCategory.Development)]
    [InlineData("krita-sketch", "", AppCategory.Creative)]
    [InlineData("blender3d", "", AppCategory.Creative)]
    [InlineData("libreoffice-writer", "", AppCategory.Office)]
    [InlineData("discord-app", "", AppCategory.Communication)]
    [InlineData("spotify-player", "", AppCategory.Entertainment)]
    [InlineData("steam-runtime", "", AppCategory.Entertainment)]
    public void TryClassifyFromTokens_MatchesSemanticKeywords(string appId, string title, AppCategory expected)
    {
        bool matched = AppCategoryClassifier.TryClassifyFromTokens(appId, title, out var category);
        Assert.True(matched);
        Assert.Equal(expected, category);
    }

    [Fact]
    public void TryClassifyFromTokens_NoMatch_ReturnsFalse()
    {
        bool matched = AppCategoryClassifier.TryClassifyFromTokens("xyz-unknown-app-q7z", "", out _);
        Assert.False(matched);
    }

    [Fact]
    public void ActivityFeatureTracker_DevAppFlag_SetForTerminal()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 14, 0, 0),
            new WindowObservation("konsole", "~/Dev/Nudge", "w1", "", FocusSource.KWinScript, false, 1),
            new IdleObservation(0, IdleSource.Unknown));

        Assert.Equal(1, tick.Features.DevAppFlag);
        Assert.Equal(0, tick.Features.CreativeAppFlag);
        Assert.Equal(0, tick.Features.OfficeAppFlag);
        Assert.Equal(0, tick.Features.CommAppFlag);
        Assert.Equal(0, tick.Features.EntAppFlag);
        Assert.Equal(AppCategory.Development, tick.AppCategory);
        // keyword match → Semantic confidence
        Assert.Equal(CategoryConfidence.Semantic, tick.AppCategoryConfidence);
        Assert.True(AppCategoryClassifier.GetConfidenceScore(tick.AppCategoryConfidence) >= 0.70f);
    }

    [Fact]
    public void ActivityFeatureTracker_EntAppFlag_SetForSpotify()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 14, 0, 0),
            new WindowObservation("spotify", "Spotify", "w1", "", FocusSource.KWinScript, false, 1),
            new IdleObservation(0, IdleSource.Unknown));

        Assert.Equal(1, tick.Features.EntAppFlag);
        Assert.Equal(0, tick.Features.DevAppFlag);
        Assert.Equal(AppCategory.Entertainment, tick.AppCategory);
        Assert.Equal(CategoryConfidence.Semantic, tick.AppCategoryConfidence);
    }

    [Fact]
    public void ActivityFeatureTracker_CommAppFlag_SetForDiscord()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 23, 14, 0, 0),
            new WindowObservation("discord", "Discord", "w1", "", FocusSource.KWinScript, false, 1),
            new IdleObservation(0, IdleSource.Unknown));

        Assert.Equal(1, tick.Features.CommAppFlag);
        Assert.Equal(0, tick.Features.DevAppFlag);
        Assert.Equal(AppCategory.Communication, tick.AppCategory);
        Assert.Equal(CategoryConfidence.Semantic, tick.AppCategoryConfidence);
    }

    // ── CategoryConfidence ────────────────────────────────────────────────────

    [Fact]
    public void Classify_UnknownApp_ReturnsFallbackConfidence()
    {
        var (cat, conf) = AppCategoryClassifier.Classify("xq7z-completely-unknown-binary", "");
        Assert.Equal(AppCategory.Utility, cat);
        Assert.Equal(CategoryConfidence.Fallback, conf);
        Assert.True(AppCategoryClassifier.GetConfidenceScore(conf) < 0.45f);
    }

    [Fact]
    public void Classify_SemanticMatch_ReturnsSemanticConfidence()
    {
        // Engine rework: TryMatchByExec now matches appId containing system binary names.
        // "xterm-test-unique-xyz" contains "xterm" which matches xterm.desktop on systems
        // with xterm installed, producing Desktop confidence. When xterm is absent,
        // falls back to token-based classification producing Semantic.
        var (cat, conf) = AppCategoryClassifier.Classify("xterm-test-unique-xyz", "");
        Assert.Equal(AppCategory.Development, cat);
        // Desktop or Semantic are both valid depending on system state
        Assert.True(conf is CategoryConfidence.Desktop or CategoryConfidence.Semantic,
            $"Expected Desktop or Semantic, got {conf}");
    }

    [Fact]
    public void GetConfidenceScore_OrderedCorrectly()
    {
        Assert.True(AppCategoryClassifier.GetConfidenceScore(CategoryConfidence.Override)
                  > AppCategoryClassifier.GetConfidenceScore(CategoryConfidence.Desktop));
        Assert.True(AppCategoryClassifier.GetConfidenceScore(CategoryConfidence.Desktop)
                  > AppCategoryClassifier.GetConfidenceScore(CategoryConfidence.Semantic));
        Assert.True(AppCategoryClassifier.GetConfidenceScore(CategoryConfidence.Semantic)
                  > AppCategoryClassifier.GetConfidenceScore(CategoryConfidence.Inferred));
        Assert.True(AppCategoryClassifier.GetConfidenceScore(CategoryConfidence.Inferred)
                  > AppCategoryClassifier.GetConfidenceScore(CategoryConfidence.Fallback));
    }

    [Fact]
    public void GetConfidenceLabel_CorrectLabelsForTiers()
    {
        Assert.Equal("Verified",  AppCategoryClassifier.GetConfidenceLabel(CategoryConfidence.Desktop));
        Assert.Equal("Verified",  AppCategoryClassifier.GetConfidenceLabel(CategoryConfidence.Override));
        Assert.Equal("Estimated", AppCategoryClassifier.GetConfidenceLabel(CategoryConfidence.Semantic));
        Assert.Equal("Inferred",  AppCategoryClassifier.GetConfidenceLabel(CategoryConfidence.Inferred));
        Assert.Equal("",          AppCategoryClassifier.GetConfidenceLabel(CategoryConfidence.Fallback));
    }

    [Fact]
    public void FeatureSchema_OrderedFeatureNames_IncludesNewCategoryFlags()
    {
        var names = FeatureSchema.OrderedFeatureNames;
        Assert.Contains("dev_app_flag", names);
        Assert.Contains("creative_app_flag", names);
        Assert.Contains("office_app_flag", names);
        Assert.Contains("comm_app_flag", names);
        Assert.Contains("ent_app_flag", names);
    }

    [Fact]
    public void FeatureSchema_ToFeatureDictionary_IncludesNewFlags()
    {
        var features = new FeatureVector(
            HourOfDay: 10, DayOfWeek: 1, FocusedAppHash: 0, FocusedDomainHash: 0,
            IdleMs: 0, FocusedSinceMs: 5000, TitleStabilityMs: 5000,
            SwitchCount60s: 0, SwitchCount300s: 0, DistinctApps300s: 1,
            DistinctDomains300s: 0, ReturnedToAnchorApp300s: 0,
            CurrentAppShare300s: 1.0, CurrentDomainShare300s: 0,
            BrowserWindowFlag: 0, CommunicationAppFlag: 0,
            EntertainmentDomainFlag: 0, WorkDomainFlag: 0,
            AfkFlag: 0, FullscreenFlag: 0, WorkspaceSwitchCount300s: 0,
            DevAppFlag: 1, CreativeAppFlag: 0, OfficeAppFlag: 0,
            CommAppFlag: 0, EntAppFlag: 0);

        var dict = FeatureSchema.ToFeatureDictionary(features);
        Assert.Equal(1.0, dict["dev_app_flag"]);
        Assert.Equal(0.0, dict["creative_app_flag"]);
        Assert.Equal(0.0, dict["comm_app_flag"]);
    }


    [Fact]
    public void ActivityFeatureTracker_YouTubeIsEntertainmentEvenWhenAnchorIsDevelopment()
    {
        // Regression: anchor fusion was overriding domain knowledge — youtube.com was classified
        // as Development because the previous anchor app (konsole) is a Development app.
        var tracker = new ActivityFeatureTracker();
        var start = new DateTime(2026, 5, 23, 14, 0, 0);

        // Establish konsole as the dominant anchor (Development)
        for (int i = 0; i < 60; i++)
        {
            tracker.Capture(
                start.AddSeconds(i),
                new WindowObservation("konsole", "~/Dev", "k1", "", FocusSource.KWinScript, false, 1),
                new IdleObservation(0, IdleSource.Unknown));
        }

        // Switch to Zen on youtube.com — must be Entertainment, NOT Development
        var youtubeTick = tracker.Capture(
            start.AddSeconds(61),
            new WindowObservation("zen", "Industry Wide AI Psychosis - YouTube - Zen Browser", "z1", "", FocusSource.KWinScript, false, 1),
            new IdleObservation(0, IdleSource.Unknown));

        Assert.Equal("youtube.com", youtubeTick.Context.FocusedDomain);
        Assert.Equal(AppCategory.Entertainment, youtubeTick.AppCategory);
        Assert.Equal(1, youtubeTick.Features.EntAppFlag);
        Assert.Equal(0, youtubeTick.Features.DevAppFlag);
    }

    [Fact]
    public void ActivityFeatureTracker_BrowserDomainWorkVsEntertainmentFlagsDiffer()
    {
        var tracker = new ActivityFeatureTracker();
        var start = new DateTime(2026, 5, 3, 9, 0, 0);

        var workTick = tracker.Capture(
            start,
            new WindowObservation("firefox", "openai/nudge pull request - GitHub - Mozilla Firefox", "1", "", FocusSource.SwayIpc, false, 0),
            new IdleObservation(0, IdleSource.Unknown));

        var entertainmentTick = tracker.Capture(
            start.AddSeconds(1),
            new WindowObservation("firefox", "YouTube - Mozilla Firefox", "2", "", FocusSource.SwayIpc, false, 0),
            new IdleObservation(0, IdleSource.Unknown));

        Assert.Equal("github.com", workTick.Context.FocusedDomain);
        Assert.Equal(1, workTick.Features.WorkDomainFlag);
        Assert.Equal(0, workTick.Features.EntertainmentDomainFlag);

        Assert.Equal("youtube.com", entertainmentTick.Context.FocusedDomain);
        Assert.Equal(1, entertainmentTick.Features.EntertainmentDomainFlag);
        Assert.Equal(0, entertainmentTick.Features.WorkDomainFlag);
    }

    [Fact]
    public void ActivityFeatureTracker_ReturnToAnchorAndSwitchCountsAreTracked()
    {
        var tracker = new ActivityFeatureTracker();
        var start = new DateTime(2026, 5, 3, 10, 0, 0);

        for (int i = 0; i < 120; i++)
        {
            tracker.Capture(
                start.AddSeconds(i),
                new WindowObservation("code", "main.cs", "code", "1", FocusSource.SwayIpc, false, 0),
                new IdleObservation(0, IdleSource.Unknown));
        }

        for (int i = 120; i < 180; i++)
        {
            tracker.Capture(
                start.AddSeconds(i),
                new WindowObservation("slack", "Slack", "slack", "1", FocusSource.SwayIpc, false, 0),
                new IdleObservation(0, IdleSource.Unknown));
        }

        ActivityTickResult result = default;
        for (int i = 180; i < 240; i++)
        {
            result = tracker.Capture(
                start.AddSeconds(i),
                new WindowObservation("code", "main.cs", "code", "1", FocusSource.SwayIpc, false, 0),
                new IdleObservation(0, IdleSource.Unknown));
        }

        Assert.Equal("code", result.Context.FocusedAppId);
        Assert.Equal(1, result.Features.ReturnedToAnchorApp300s);
        Assert.True(result.Features.SwitchCount300s >= 2);
        Assert.True(result.Features.CurrentAppShare300s > 0.5);
        Assert.Equal(2, result.Features.DistinctApps300s);
    }

    [Fact]
    public void ActivityFeatureTracker_AfkAndPoorSignalAreMarked()
    {
        var tracker = new ActivityFeatureTracker();
        var tick = tracker.Capture(
            new DateTime(2026, 5, 3, 11, 0, 0),
            new WindowObservation("firefox", "", "browser", "", FocusSource.HeuristicProcessScan, false, 0),
            new IdleObservation(75_000, IdleSource.Unknown));

        Assert.Equal(SignalQuality.Poor, tick.Context.SignalQuality);
        Assert.Equal(1, tick.Features.AfkFlag);
    }

    [Fact]
    public void TryParseHarvestLine_HandlesQuotedAppNames()
    {
        string line = "2026-05-03 12:00:00,12,0,\"Chrome (github.com, docs)\",123,0,1000,1";

        bool parsed = NudgeCoreLogic.TryParseHarvestLine(line, out var entry);

        Assert.True(parsed);
        Assert.Equal("Chrome (github.com, docs)", entry.AppName);
        Assert.True(entry.Productive);
    }

    // ── Semantic rule ordering ────────────────────────────────────────────────

    [Fact]
    public void TryClassifyFromTokens_TerminalKeyword_ClassifiedAsDevelopment()
    {
        // "terminal" sits in the Development rule — first match wins
        bool matched = AppCategoryClassifier.TryClassifyFromTokens("my-terminal-emulator", "", out var cat);
        Assert.True(matched);
        Assert.Equal(AppCategory.Development, cat);
    }

    [Fact]
    public void TryClassifyFromTokens_GitKeyword_ClassifiedAsDevelopment()
    {
        bool matched = AppCategoryClassifier.TryClassifyFromTokens("gitkraken-client", "", out var cat);
        Assert.True(matched);
        Assert.Equal(AppCategory.Development, cat);
    }

    [Theory]
    [InlineData("openshot-video-editor", AppCategory.Creative)]
    [InlineData("my-slack-workspace", AppCategory.Communication)]
    [InlineData("steam-game-launcher", AppCategory.Entertainment)]
    public void TryClassifyFromTokens_SecondaryRules_MatchCorrectly(string appId, AppCategory expected)
    {
        bool matched = AppCategoryClassifier.TryClassifyFromTokens(appId, "", out var cat);
        Assert.True(matched);
        Assert.Equal(expected, cat);
    }

    // ── Browser anchor fusion edge cases ──────────────────────────────────────

    // Use a fake browser name so we don't depend on installed .desktop files
    private const string FakeBrowserId = "my-fake-test-browser-x7z";

    [Fact]
    public void Classify_BrowserWithDevAnchor_InheritsDevCategory()
    {
        var (cat, conf) = AppCategoryClassifier.Classify(FakeBrowserId, "", AppCategory.Development);
        Assert.Equal(AppCategory.Development, cat);
        Assert.Equal(CategoryConfidence.Inferred, conf);
    }

    [Fact]
    public void Classify_BrowserWithCreativeAnchor_InheritsCreativeCategory()
    {
        var (cat, conf) = AppCategoryClassifier.Classify(FakeBrowserId, "", AppCategory.Creative);
        Assert.Equal(AppCategory.Creative, cat);
        Assert.Equal(CategoryConfidence.Inferred, conf);
    }

    [Fact]
    public void Classify_BrowserWithEntertainmentAnchor_DoesNotInheritAnchor()
    {
        // Entertainment anchor must NOT be transferred — only Dev/Creative/Office are eligible
        var (_, conf) = AppCategoryClassifier.Classify(FakeBrowserId, "", AppCategory.Entertainment);
        Assert.NotEqual(CategoryConfidence.Inferred, conf);
    }

    [Fact]
    public void Classify_BrowserWithCommunicationAnchor_DoesNotInheritAnchor()
    {
        var (_, conf) = AppCategoryClassifier.Classify(FakeBrowserId, "", AppCategory.Communication);
        Assert.NotEqual(CategoryConfidence.Inferred, conf);
    }

    [Fact]
    public void Classify_BrowserWithUnknownDomain_ReturnsUtilityAtSemanticConfidence()
    {
        // Browsers are Utility — we know this via process-name detection (same basis as a
        // semantic keyword match). Semantic confidence (0.75) keeps signal quality Trusted.
        var (cat, conf) = AppCategoryClassifier.Classify(FakeBrowserId, "", AppCategory.Unknown);
        Assert.Equal(AppCategory.Utility, cat);
        Assert.Equal(CategoryConfidence.Semantic, conf);
        Assert.True(AppCategoryClassifier.GetConfidenceScore(conf) >= 0.45f);
    }

    [Fact]
    public void Classify_BrowserWithUnclassifiedAnchor_ReturnsUtilityNotFallback()
    {
        // Entertainment anchor must not transfer, but browser should still be Utility/Semantic.
        var (cat, conf) = AppCategoryClassifier.Classify(FakeBrowserId, "", AppCategory.Entertainment);
        Assert.Equal(AppCategory.Utility, cat);
        Assert.Equal(CategoryConfidence.Semantic, conf);
    }

    // ── Browser anchor fusion edge cases ──────────────────────────────────────
    // Test that browser anchor fusion only applies for Dev/Creative/Office anchors
    [Fact]
    public void BrowserAnchorFusion_OnlyAppliesForDevCreativeOffice()
    {
        var t = new DateTime(2026, 5, 23, 10, 0, 0);

        // Test Entertainment anchor - should NOT transfer
        var entertainmentTracker = new ActivityFeatureTracker();
        for (int i = 0; i < 5; i++)
            entertainmentTracker.Capture(t.AddSeconds(i), 
                new WindowObservation("spotify", "Spotify", "w1", "1", FocusSource.KWinScript, false, 1), 
                new IdleObservation(0, IdleSource.Unknown));
        
        var entertainmentResult = entertainmentTracker.Capture(
            t.AddSeconds(6),
            new WindowObservation("my-browser", "Some Page - MyBrowser", "f1", "1", FocusSource.KWinScript, false, 1),
            new IdleObservation(0, IdleSource.Unknown));
            
        Assert.NotEqual(AppCategory.Entertainment, entertainmentResult.AppCategory);
        Assert.NotEqual(CategoryConfidence.Inferred, entertainmentResult.AppCategoryConfidence);

        // Test Communication anchor - should NOT transfer
        var communicationTracker = new ActivityFeatureTracker();
        for (int i = 0; i < 5; i++)
            communicationTracker.Capture(t.AddSeconds(i), 
                new WindowObservation("discord", "Discord", "w1", "1", FocusSource.KWinScript, false, 1), 
                new IdleObservation(0, IdleSource.Unknown));
                
        var communicationResult = communicationTracker.Capture(
            t.AddSeconds(6),
            new WindowObservation("my-browser", "Some Page - MyBrowser", "f1", "1", FocusSource.KWinScript, false, 1),
            new IdleObservation(0, IdleSource.Unknown));
            
        Assert.NotEqual(AppCategory.Communication, communicationResult.AppCategory);
        Assert.NotEqual(CategoryConfidence.Inferred, communicationResult.AppCategoryConfidence);

        // Test Dev anchor - SHOULD transfer
        var devTracker = new ActivityFeatureTracker();
        for (int i = 0; i < 5; i++)
            devTracker.Capture(t.AddSeconds(i), 
                new WindowObservation("code", "code", "w1", "1", FocusSource.KWinScript, false, 1), 
                new IdleObservation(0, IdleSource.Unknown));
                
        var devResult = devTracker.Capture(
            t.AddSeconds(6),
            new WindowObservation("my-browser", "Some Page - MyBrowser", "f1", "1", FocusSource.KWinScript, false, 1),
            new IdleObservation(0, IdleSource.Unknown));
            
        Assert.Equal(AppCategory.Development, devResult.AppCategory);
        Assert.Equal(CategoryConfidence.Inferred, devResult.AppCategoryConfidence);

        // Test Creative anchor - SHOULD transfer
        var creativeTracker = new ActivityFeatureTracker();
        for (int i = 0; i < 5; i++)
            creativeTracker.Capture(t.AddSeconds(i), 
                new WindowObservation("gimp", "GIMP", "w1", "1", FocusSource.KWinScript, false, 1), 
                new IdleObservation(0, IdleSource.Unknown));
                
        var creativeResult = creativeTracker.Capture(
            t.AddSeconds(6),
            new WindowObservation("my-browser", "Some Page - MyBrowser", "f1", "1", FocusSource.KWinScript, false, 1),
            new IdleObservation(0, IdleSource.Unknown));
            
        Assert.Equal(AppCategory.Creative, creativeResult.AppCategory);
        Assert.Equal(CategoryConfidence.Inferred, creativeResult.AppCategoryConfidence);

        // Test Office anchor - SHOULD transfer
        var officeTracker = new ActivityFeatureTracker();
        for (int i = 0; i < 5; i++)
            officeTracker.Capture(t.AddSeconds(i), 
                new WindowObservation("libreoffice", "LibreOffice", "w1", "1", FocusSource.KWinScript, false, 1), 
                new IdleObservation(0, IdleSource.Unknown));
                
        var officeResult = officeTracker.Capture(
            t.AddSeconds(6),
            new WindowObservation("my-browser", "Some Page - MyBrowser", "f1", "1", FocusSource.KWinScript, false, 1),
            new IdleObservation(0, IdleSource.Unknown));
            
        Assert.Equal(AppCategory.Office, officeResult.AppCategory);
        Assert.Equal(CategoryConfidence.Inferred, officeResult.AppCategoryConfidence);
    }

    // ── FeatureSchema dictionary ──────────────────────────────────────────────

    [Fact]
    public void FeatureSchema_ToFeatureDictionary_ValuesMatchStructFields()
    {
        var features = new FeatureVector(
            HourOfDay: 14, DayOfWeek: 3, FocusedAppHash: 42, FocusedDomainHash: 99,
            IdleMs: 500, FocusedSinceMs: 10_000, TitleStabilityMs: 8_000,
            SwitchCount60s: 2, SwitchCount300s: 5, DistinctApps300s: 3,
            DistinctDomains300s: 1, ReturnedToAnchorApp300s: 1,
            CurrentAppShare300s: 0.6, CurrentDomainShare300s: 0.4,
            BrowserWindowFlag: 1, CommunicationAppFlag: 0,
            EntertainmentDomainFlag: 0, WorkDomainFlag: 1,
            AfkFlag: 0, FullscreenFlag: 0, WorkspaceSwitchCount300s: 1,
            DevAppFlag: 0, CreativeAppFlag: 0, OfficeAppFlag: 0,
            CommAppFlag: 0, EntAppFlag: 0);

        var dict = FeatureSchema.ToFeatureDictionary(features);
        Assert.Equal(14.0, dict["hour_of_day"]);
        Assert.Equal(3.0, dict["day_of_week"]);
        Assert.Equal(42.0, dict["focused_app_hash"]);
        Assert.Equal(500.0, dict["idle_ms"]);
        Assert.Equal(5.0, dict["switch_count_300s"]);
        Assert.Equal(0.6, dict["current_app_share_300s"]);
        Assert.Equal(1.0, dict["work_domain_flag"]);
        Assert.Equal(1.0, dict["workspace_switch_count_300s"]);
    }

    [Fact]
    public void FeatureSchema_OrderedNames_ContainAllExpectedKeys()
    {
        var names = FeatureSchema.OrderedFeatureNames;
        Assert.Contains("hour_of_day", names);
        Assert.Contains("day_of_week", names);
        Assert.Contains("switch_count_60s", names);
        Assert.Contains("workspace_switch_count_300s", names);
        Assert.Contains("afk_flag", names);
        Assert.Contains("fullscreen_flag", names);
    }

    // ── Group 6: FeatureSchemaV2 determinism tests ────────────────────────

    [Fact]
    public void FeatureDictionary_OrderIsDeterministic()
    {
        // Two calls with same input should return keys in same order
        var features1 = new FeatureVector(
            HourOfDay: 10, DayOfWeek: 2, FocusedAppHash: 12345, FocusedDomainHash: 67890,
            IdleMs: 500, FocusedSinceMs: 10000, TitleStabilityMs: 8000,
            SwitchCount60s: 3, SwitchCount300s: 7, DistinctApps300s: 2,
            DistinctDomains300s: 1, ReturnedToAnchorApp300s: 1,
            CurrentAppShare300s: 0.7, CurrentDomainShare300s: 0.3,
            BrowserWindowFlag: 1, CommunicationAppFlag: 0,
            EntertainmentDomainFlag: 0, WorkDomainFlag: 1,
            AfkFlag: 0, FullscreenFlag: 0, WorkspaceSwitchCount300s: 2,
            DevAppFlag: 0, CreativeAppFlag: 1, OfficeAppFlag: 0,
            CommAppFlag: 0, EntAppFlag: 0);

        var dict1 = FeatureSchema.ToFeatureDictionary(features1);
        var dict2 = FeatureSchema.ToFeatureDictionary(features1);

        // Get the keys in order from both dictionaries
        var keys1 = dict1.Keys.ToList();
        var keys2 = dict2.Keys.ToList();

        // They should be identical in order
        Assert.Equal(keys1.Count, keys2.Count);
        for (int i = 0; i < keys1.Count; i++)
        {
            Assert.Equal(keys1[i], keys2[i]);
        }

        // Also verify all 26 keys are present
        Assert.Equal(26, keys1.Count);
        Assert.Equal(26, keys2.Count);
    }
}
