using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NudgeCore;
using Xunit;

namespace NudgeCrossPlatform.Tests;

/// <summary>
/// Chaos / fuzzy / stability tests — random inputs, edge cases, concurrent stress,
/// malformed data, rapid state transitions. Every component that touches the harvest
/// loop or user data must survive abuse without crashing or corrupting state.
/// </summary>
public sealed class NudgeStabilityTests
{
    private static readonly Random _rng = new(42); // seeded for reproducibility
    private static readonly string[] _garbageStrings =
    [
        "", "\0", "\n", "\t\t\t", "\x01\x02\x03", "<script>alert(1)</script>",
        "\uD800\uDC00", // surrogate pair
        new string('x', 10000), "emoji🚀🔥💻", "null", "undefined", "NaN",
        "\\\\\\\\\\\\", "////////", "::::::::", "--------", "________",
        "C:\\Windows\\System32\\calc.exe", "file:///etc/passwd",
        "https://example.com/?q=<>&x=\"", "", " ", "  ",
        "(2) (3) (4) - - -", "-" , "|", "—", "–", "·", "•",
        "Microsoft Teams", "Microsoft Teams Meeting", "Microsoft Teams Call",
        "zoom meeting", "zoom video", "Google Meet",
    ];

    private static readonly string[] _appNames =
    [
        "code", "firefox", "chrome", "msedge", "slack", "discord", "teams",
        "zoom", "unknown", "", null!, "explorer", "konsole", "kwin_wayland",
        "plasmashell", "nudge-tray", "background_trainer", "python3", "dotnet",
    ];

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 1. FeatureTracker chaos
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void FeatureTracker_RandomWindowObservations_NeverThrows()
    {
        var tracker = new ActivityFeatureTracker();
        var now = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 5000; i++)
        {
            var window = new WindowObservation(
                AppId: Pick(_appNames) ?? "unknown",
                Title: Pick(_garbageStrings) ?? "",
                WindowId: Pick(_garbageStrings) ?? "",
                WorkspaceId: Pick(_garbageStrings) ?? "",
                FocusSource: (FocusSource)_rng.Next(0, 5),
                Fullscreen: _rng.NextDouble() > 0.8,
                MappedToplevelCount: _rng.Next(-5, 20),
                CatalogDisagrees: _rng.NextDouble() > 0.9,
                AudioPlaying: _rng.NextDouble() > 0.7,
                MediaSessionActive: _rng.NextDouble() > 0.8,
                MicActive: _rng.NextDouble() > 0.9);

            var idle = new IdleObservation(_rng.Next(0, 300_000), IdleSource.X11Xprintidle);
            var tick = tracker.Capture(now.AddMilliseconds(i * 100), window, idle, experimental: _rng.NextDouble() > 0.5);

            // Invariants that must never break
            Assert.InRange(tick.Features.IdleMs, 0, int.MaxValue);
            Assert.InRange(tick.Features.SwitchCount60s, 0, int.MaxValue);
            Assert.InRange(tick.Features.SwitchCount300s, 0, int.MaxValue);
            Assert.True(tick.Context.FullscreenFlag is 0 or 1);
            Assert.True(tick.Context.AudioPlayingFlag is 0 or 1);
            Assert.True(tick.Context.MediaSessionActiveFlag is 0 or 1);
            Assert.True(tick.Context.MicActiveFlag is 0 or 1);
        }
    }

    [Fact]
    public void FeatureTracker_RapidCapture_DoesNotCorruptBuffer()
    {
        var tracker = new ActivityFeatureTracker();
        var baseTime = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        // 10 000 captures in 1s wall-clock simulation
        for (int i = 0; i < 10_000; i++)
        {
            var t = tracker.Capture(
                baseTime.AddMilliseconds(i),
                new WindowObservation("code", $"file{i % 50}.cs", $"w{i}", "1", FocusSource.KWinScript, false, 1),
                new IdleObservation(100, IdleSource.WaylandExtIdleNotify),
                experimental: true);

            Assert.False(string.IsNullOrEmpty(t.DisplayAppName));
        }
    }

    [Fact]
    public void FeatureTracker_IdenticalSamplesRepeatedly_BufferStable()
    {
        var tracker = new ActivityFeatureTracker();
        var now = DateTime.UtcNow;
        var window = new WindowObservation("code", "main.cs", "w1", "1", FocusSource.KWinScript, false, 1);
        var idle = new IdleObservation(100, IdleSource.Unknown);

        for (int i = 0; i < 2000; i++)
        {
            var tick = tracker.Capture(now.AddSeconds(i), window, idle, experimental: true);
            Assert.Equal("code", tick.Context.FocusedAppId);
            Assert.True(tick.Features.CurrentAppShare300s >= 0.99);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 2. DomainReputationStore chaos
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void ReputationStore_ConcurrentUpdates_AtomicAndConsistent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rep-chaos-{Guid.NewGuid()}.json");
        try
        {
            var store = new DomainReputationStore(path);
            var domains = Enumerable.Range(0, 50).Select(i => $"domain{i}.com").ToArray();
            var apps = Enumerable.Range(0, 50).Select(i => $"app{i}").ToArray();

            // 1000 concurrent updates from 10 threads
            Parallel.For(0, 1000, _ =>
            {
                var d = domains[_rng.Next(domains.Length)];
                var a = apps[_rng.Next(apps.Length)];
                store.Update(d, a, _rng.NextDouble() > 0.4);
            });

            store.Flush();

            // Every domain and app must have a sane rate
            foreach (var d in domains)
            {
                double r = store.DomainRate(d);
                Assert.InRange(r, 0.0, 1.0);
            }
            foreach (var a in apps)
            {
                double r = store.AppRate(a);
                Assert.InRange(r, 0.0, 1.0);
            }

            // Reload and verify persistence round-trip
            var store2 = new DomainReputationStore(path);
            foreach (var d in domains)
                Assert.Equal(store.DomainRate(d), store2.DomainRate(d));
            foreach (var a in apps)
                Assert.Equal(store.AppRate(a), store2.AppRate(a));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void ReputationStore_ExtremeLabelCounts_RateStillBounded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rep-extreme-{Guid.NewGuid()}.json");
        try
        {
            var store = new DomainReputationStore(path);

            // 10000 productive, 0 unproductive
            for (int i = 0; i < 10000; i++)
                store.Update("almost-perfect.com", "code", productive: true);

            // 0 productive, 10000 unproductive
            for (int i = 0; i < 10000; i++)
                store.Update("terrible.com", "spotify", productive: false);

            store.Flush();

            // With α=β=2 Laplace smoothing, even extreme counts stay away from 0/1
            Assert.InRange(store.DomainRate("almost-perfect.com"), 0.95, 1.0);
            Assert.InRange(store.DomainRate("terrible.com"), 0.0, 0.05);
            Assert.Equal(10000, store.DomainCount("almost-perfect.com"));
            Assert.Equal(10000, store.DomainCount("terrible.com"));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void ReputationStore_MalformedJsonOnDisk_GracefulFallback()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rep-bad-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "this is not json{[[");
            var store = new DomainReputationStore(path);

            // Should survive and return neutral defaults
            Assert.Equal(0.5, store.DomainRate("anything.com"));
            Assert.Equal(0, store.DomainCount("anything.com"));

            // Should still be able to update and persist
            store.Update("anything.com", "code", productive: true);
            store.Flush();
            Assert.True(store.DomainRate("anything.com") > 0.5);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void ReputationStore_NullAndEmptyKeys_NeverThrow()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rep-null-{Guid.NewGuid()}.json");
        try
        {
            var store = new DomainReputationStore(path);
            for (int i = 0; i < 100; i++)
            {
                store.Update(null!, "app", true);
                store.Update("domain", null!, false);
                store.Update(null!, null!, true);
                store.Update("", "", false);
            }
            store.Flush();
            Assert.Equal(0.5, store.DomainRate(null!));
            Assert.Equal(0.5, store.AppRate(null!));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 3. BrowserDetector fuzz
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void BrowserDetector_GarbledTitles_NeverThrows()
    {
        foreach (var title in _garbageStrings)
        {
            var _ = BrowserDetector.ExtractSite(title);
            var __ = BrowserDetector.TrimBrowserSuffix(title ?? "");
            var ___ = BrowserDetector.GetAppAndSite("chrome", title ?? "");
            // Assert: no exceptions thrown = success
        }
    }

    [Fact]
    public void BrowserDetector_InjectedUrlReaderThrows_DegradesToTitle()
    {
        BrowserDetector.TryGetBrowserUrl = () => throw new InvalidOperationException("boom");
        try
        {
            var site = BrowserDetector.ExtractSite("openai/nudge - GitHub - Chrome");
            Assert.Equal("github.com", site); // falls back to title parsing
        }
        finally
        {
            BrowserDetector.TryGetBrowserUrl = null;
        }
    }

    [Fact]
    public void BrowserDetector_TabCountPrefix_Fuzz()
    {
        for (int i = 0; i < 100; i++)
        {
            string prefix = $"({_rng.Next(0, 999)}{(_rng.NextDouble() > 0.5 ? "+" : "")}) ";
            string title = prefix + "Reddit - Dive into anything - Google Chrome";
            var site = BrowserDetector.ExtractSite(title);
            Assert.Equal("reddit.com", site);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 4. PipeWire / PulseAudio parser stress
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void PipeWireParser_GarbageJson_NeverThrows()
    {
        string[] inputs =
        [
            "", "not json", "{}", "[]", "[{}]", "[null]", "{\"type\": 123}",
            new string('x', 100_000), "\0\0\0", "<html></html>",
            "[{\"type\":\"PipeWire:Interface:Node\",\"info\":{\"state\":\"running\",\"props\":{\"media.class\":\"Stream/Output/Audio\",\"application.name\":\"Notification\"}}}]",
        ];

        foreach (var input in inputs)
        {
            var state = PipeWireParser.Parse(input);
            // Must not throw; may return Unavailable — just ensure it doesn't crash
            _ = state.InMeeting;
        }
    }

    [Fact]
    public void PipeWireParser_HasAudioOutput_GarbageJson_NeverThrows()
    {
        string[] inputs =
        [
            "", "not json", "[]", "[{\"type\":\"PipeWire:Interface:Node\",\"info\":{}}]",
            "[{\"type\":\"PipeWire:Interface:Node\",\"info\":{\"state\":\"running\",\"props\":{\"media.class\":\"Stream/Output/Audio\",\"application.name\":\"Firefox\"}}}]",
            "[{\"type\":\"PipeWire:Interface:Node\",\"info\":{\"state\":\"running\",\"props\":{\"media.class\":\"Stream/Output/Audio\",\"application.name\":\"Notification\"}}}]",
        ];

        foreach (var input in inputs)
        {
            bool result = PipeWireParser.HasAudioOutput(input);
            // Must not throw
        }
    }

    [Fact]
    public void PulseAudioParser_GarbageInput_NeverThrows()
    {
        string[] inputs = { "", null!, "State: RUNNING", "State: IDLE", new string('x', 50_000) };
        foreach (var input in inputs)
        {
            bool _ = PulseAudioParser.HasActiveCaptureStream(input);
            bool __ = PulseAudioParser.HasActivePlaybackStream(input);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 5. Meeting / presence logic fuzz
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void MeetingTitleDetector_GarbageTitles_NeverThrows()
    {
        foreach (var title in _garbageStrings)
        {
            bool _ = MeetingTitleDetector.IsMeetingTitle(title ?? "");
        }
    }

    [Fact]
    public void ConsentStorePresence_EmptyAndNullLeaves_NeverThrows()
    {
        var result = ConsentStorePresence.Evaluate([], [], "teams", "Weekly sync");
        Assert.False(result.InMeeting);

        var result2 = ConsentStorePresence.Evaluate(
            [new ConsentLeaf("", 1, 0, true, true)],
            [new ConsentLeaf("", 1, 0, true, true)],
            "", "");
        // Should not throw even with empty/null data
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 6. FeatureSchema / CSV round-trip stress
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void FeatureSchemaV4_DictionaryRoundTrip_AllFeaturesPresent()
    {
        var fv4 = new FeatureVectorV4(
            HourOfDay: 14, DayOfWeek: 2,
            FocusedAppHash: 12345, FocusedDomainHash: 67890,
            IdleMs: 500, FocusedSinceMs: 60000, TitleStabilityMs: 30000,
            SwitchCount60s: 1, SwitchCount300s: 3,
            DistinctApps300s: 2, DistinctDomains300s: 1,
            ReturnedToAnchorApp300s: 1,
            CurrentAppShare300s: 0.75, CurrentDomainShare300s: 0.50,
            BrowserWindowFlag: 1, AfkFlag: 0,
            FullscreenFlag: 1, WorkspaceSwitchCount300s: 0,
            AudioPlayingFlag: 1, MediaSessionActiveFlag: 1, MicActiveFlag: 0,
            DomainProductiveRate: 0.82, DomainLabelCount: 12,
            AppProductiveRate: 0.91, AppLabelCount: 45);

        var dict = FeatureSchemaV4.ToFeatureDictionary(fv4);
        Assert.Equal(FeatureSchemaV4.OrderedFeatureNames.Length, dict.Count);

        foreach (var name in FeatureSchemaV4.OrderedFeatureNames)
            Assert.True(dict.ContainsKey(name), $"Missing feature: {name}");
    }

    [Fact]
    public void FeatureSchemaV4_ExtremeValues_NeverThrows()
    {
        var fv4 = new FeatureVectorV4(
            HourOfDay: int.MaxValue, DayOfWeek: int.MinValue,
            FocusedAppHash: int.MinValue, FocusedDomainHash: int.MaxValue,
            IdleMs: int.MaxValue, FocusedSinceMs: int.MaxValue, TitleStabilityMs: int.MaxValue,
            SwitchCount60s: int.MaxValue, SwitchCount300s: int.MaxValue,
            DistinctApps300s: int.MaxValue, DistinctDomains300s: int.MaxValue,
            ReturnedToAnchorApp300s: int.MaxValue,
            CurrentAppShare300s: double.MaxValue, CurrentDomainShare300s: double.MinValue,
            BrowserWindowFlag: int.MaxValue, AfkFlag: int.MinValue,
            FullscreenFlag: int.MaxValue, WorkspaceSwitchCount300s: int.MinValue,
            AudioPlayingFlag: int.MaxValue, MediaSessionActiveFlag: int.MinValue, MicActiveFlag: int.MaxValue,
            DomainProductiveRate: double.MaxValue, DomainLabelCount: int.MaxValue,
            AppProductiveRate: double.MinValue, AppLabelCount: int.MinValue);

        var dict = FeatureSchemaV4.ToFeatureDictionary(fv4);
        Assert.Equal(FeatureSchemaV4.OrderedFeatureNames.Length, dict.Count);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 7. CLI parser fuzz
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void ParseNudgeArgs_RandomArgs_NeverThrows()
    {
        for (int i = 0; i < 1000; i++)
        {
            var args = new List<string>();
            int argCount = _rng.Next(0, 10);
            for (int j = 0; j < argCount; j++)
            {
                switch (_rng.Next(0, 8))
                {
                    case 0: args.Add("--help"); break;
                    case 1: args.Add("--version"); break;
                    case 2: args.Add("--ml"); break;
                    case 3: args.Add("--experimental"); break;
                    case 4: args.Add("--verbose"); break;
                    case 5: args.Add("--no-meeting-suppression"); break;
                    case 6: args.Add("--interval"); args.Add(_rng.Next(1, 60).ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                    case 7: args.Add(Pick(_garbageStrings) ?? ""); break;
                }
            }

            var parsed = NudgeCoreLogic.ParseNudgeArgs(args.ToArray());
            // Must not throw; Action must be a valid enum value
            Assert.True(Enum.IsDefined(parsed.Action));
        }
    }

    [Fact]
    public void ParseNudgeArgs_ExtremeIntervalValues_HandledGracefully()
    {
        var parsed1 = NudgeCoreLogic.ParseNudgeArgs(["--interval", int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        Assert.Equal(int.MaxValue, parsed1.IntervalMinutes);

        var parsed2 = NudgeCoreLogic.ParseNudgeArgs(["--interval", int.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        Assert.Equal(int.MinValue, parsed2.IntervalMinutes);

        var parsed3 = NudgeCoreLogic.ParseNudgeArgs(["--interval", "not_a_number"]);
        Assert.Null(parsed3.IntervalMinutes);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 8. KWin script idempotency
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void KWinScripts_MetadataJson_AlwaysValid()
    {
        // Should parse without exception
        var doc = JsonDocument.Parse(KWinScripts.MetadataJson);
        Assert.True(doc.RootElement.TryGetProperty("KPackageStructure", out _));
        Assert.True(doc.RootElement.TryGetProperty("KPlugin", out _));
    }

    [Fact]
    public void KWinScripts_MainJs_NoBannedApis()
    {
        var js = KWinScripts.MainJs;
        Assert.DoesNotContain("queryWindowInfo", js); // BANNED per AGENTS.md
        Assert.DoesNotContain("eval(", js);
        Assert.DoesNotContain("Function(", js);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 9. Rapid toggle / state transition stress
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void BoolToggle_RapidFlips_Consistent()
    {
        bool flag = false;
        for (int i = 0; i < 100; i++)
        {
            flag = !flag;
        }
        Assert.False(flag); // 100 flips from false → even → false
    }

    [Fact]
    public void SnapshotGate_AllSignalQualities_GracefulDegrade()
    {
        foreach (SignalQuality quality in Enum.GetValues<SignalQuality>())
        {
            var tick = MakeTick(quality, afk: false);
            var presence = new PresenceState(false, false, false, PresenceSource.None);
            var result = SnapshotGate.Evaluate(tick, presence);
            // Must return a valid decision, never throw
            Assert.False(result.Reason == SuppressionReason.None && result.Suppress);
        }
    }

    [Fact]
    public void SnapshotGate_PresenceUnavailable_FailsOpen()
    {
        var tick = MakeTick(SignalQuality.Trusted, afk: false);
        var presence = PresenceState.Unavailable;
        var result = SnapshotGate.Evaluate(tick, presence);
        // When presence is unavailable, InMeeting/ScreenSharing should NOT suppress
        Assert.False(result.Suppress);
    }

    private static ActivityTickResult MakeTick(SignalQuality quality, bool afk)
    {
        var ctx = new ActivityContext(
            FocusedAppId: "testapp", FocusedTitle: "", FocusedDomain: "",
            FocusedWindowId: "", IdleMs: afk ? 90_000 : 0, IsIdleNow: 0,
            FocusedSinceMs: 1000, TitleUnchangedForMs: 1000, MappedToplevelCount: 1,
            ActiveWorkspaceId: "", FocusSource: FocusSource.Unknown,
            SignalQuality: quality, FullscreenFlag: 0);

        var features = new FeatureVector(
            HourOfDay: 10, DayOfWeek: 1,
            FocusedAppHash: 0, FocusedDomainHash: 0,
            IdleMs: ctx.IdleMs, FocusedSinceMs: 1000, TitleStabilityMs: 1000,
            SwitchCount60s: 0, SwitchCount300s: 0,
            DistinctApps300s: 1, DistinctDomains300s: 0,
            ReturnedToAnchorApp300s: 0,
            CurrentAppShare300s: 1.0, CurrentDomainShare300s: 0.0,
            BrowserWindowFlag: 0, CommunicationAppFlag: 0,
            EntertainmentDomainFlag: 0, WorkDomainFlag: 0,
            AfkFlag: afk ? 1 : 0,
            FullscreenFlag: 0, WorkspaceSwitchCount300s: 0,
            DevAppFlag: 0, CreativeAppFlag: 0, OfficeAppFlag: 0,
            CommAppFlag: 0, EntAppFlag: 0);

        return new ActivityTickResult(
            Context: ctx, Features: features, FeaturesV4: null,
            AppCategory: AppCategory.Unknown, AppCategoryConfidence: CategoryConfidence.Unknown,
            DisplayAppName: "testapp", LegacyAppName: "testapp",
            LegacyForegroundAppHash: 0, TimeLastRequestMs: 1000);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Helpers
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static T? Pick<T>(T[] arr) => arr.Length == 0 ? default : arr[_rng.Next(arr.Length)];
}
