using System.Text.Json;
using NudgeCore;
using Xunit;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeMeetingGateTests
{
    // ── SnapshotGate.Evaluate ─────────────────────────────────────────────────

    [Fact]
    public void Evaluate_NullTick_AllowsSnapshot()
    {
        var decision = SnapshotGate.Evaluate(null, PresenceState.Unavailable);
        Assert.False(decision.Suppress);
        Assert.Equal(SuppressionReason.None, decision.Reason);
    }

    [Fact]
    public void Evaluate_GoodSignalNotAfk_AllowsSnapshot()
    {
        var tick = MakeTick(SignalQuality.Trusted, afk: false);
        var decision = SnapshotGate.Evaluate(tick, PresenceState.Unavailable);
        Assert.False(decision.Suppress);
    }

    [Fact]
    public void Evaluate_PoorSignal_SuppressesWithPoorSignalReason()
    {
        var tick = MakeTick(SignalQuality.Poor, afk: false);
        var decision = SnapshotGate.Evaluate(tick, PresenceState.Unavailable);
        Assert.True(decision.Suppress);
        Assert.Equal(SuppressionReason.PoorSignal, decision.Reason);
    }

    [Fact]
    public void Evaluate_Afk_SuppressesWithAfkReason()
    {
        var tick = MakeTick(SignalQuality.Trusted, afk: true);
        var decision = SnapshotGate.Evaluate(tick, PresenceState.Unavailable);
        Assert.True(decision.Suppress);
        Assert.Equal(SuppressionReason.Afk, decision.Reason);
    }

    [Fact]
    public void Evaluate_PoorSignalAndAfk_PrioritisesPoorSignal()
    {
        // PoorSignal is evaluated first.
        var tick = MakeTick(SignalQuality.Poor, afk: true);
        var decision = SnapshotGate.Evaluate(tick, PresenceState.Unavailable);
        Assert.Equal(SuppressionReason.PoorSignal, decision.Reason);
    }

    [Fact]
    public void Evaluate_PresenceSourceNone_NeverSuppressesOnPresence()
    {
        // Fail-open: when detection is unavailable, meeting flags are ignored.
        var presence = new PresenceState(IsMicActive: true, IsCameraActive: true, IsScreenSharing: true, Source: PresenceSource.None);
        var tick = MakeTick(SignalQuality.Trusted, afk: false);
        var decision = SnapshotGate.Evaluate(tick, presence);
        Assert.False(decision.Suppress);
    }

    [Fact]
    public void Evaluate_ScreenSharing_SuppressesWithScreenSharingReason()
    {
        var presence = new PresenceState(false, false, IsScreenSharing: true, Source: PresenceSource.PipeWire);
        var tick = MakeTick(SignalQuality.Trusted, afk: false);
        var decision = SnapshotGate.Evaluate(tick, presence);
        Assert.True(decision.Suppress);
        Assert.Equal(SuppressionReason.ScreenSharing, decision.Reason);
    }

    [Fact]
    public void Evaluate_MicActive_SuppressesWithInMeetingReason()
    {
        var presence = new PresenceState(IsMicActive: true, false, false, Source: PresenceSource.PipeWire);
        var tick = MakeTick(SignalQuality.Trusted, afk: false);
        var decision = SnapshotGate.Evaluate(tick, presence);
        Assert.True(decision.Suppress);
        Assert.Equal(SuppressionReason.InMeeting, decision.Reason);
    }

    [Fact]
    public void Evaluate_CameraActive_SuppressesWithInMeetingReason()
    {
        var presence = new PresenceState(false, IsCameraActive: true, false, Source: PresenceSource.PulseAudio);
        var tick = MakeTick(SignalQuality.Trusted, afk: false);
        var decision = SnapshotGate.Evaluate(tick, presence);
        Assert.True(decision.Suppress);
        Assert.Equal(SuppressionReason.InMeeting, decision.Reason);
    }

    [Fact]
    public void Evaluate_ScreenSharingBeforeInMeeting()
    {
        // When both flags are set, ScreenSharing is evaluated before InMeeting.
        var presence = new PresenceState(IsMicActive: true, false, IsScreenSharing: true, Source: PresenceSource.PipeWire);
        var tick = MakeTick(SignalQuality.Trusted, afk: false);
        var decision = SnapshotGate.Evaluate(tick, presence);
        Assert.Equal(SuppressionReason.ScreenSharing, decision.Reason);
    }

    [Fact]
    public void Evaluate_AfkTakesPrecedenceOverMeeting()
    {
        // AFK/poor-signal are checked before presence, so they dominate.
        var presence = new PresenceState(IsMicActive: true, false, false, Source: PresenceSource.PipeWire);
        var tick = MakeTick(SignalQuality.Trusted, afk: true);
        var decision = SnapshotGate.Evaluate(tick, presence);
        Assert.Equal(SuppressionReason.Afk, decision.Reason);
    }

    [Fact]
    public void Evaluate_WindowsRegistrySource_SuppressesOnMic()
    {
        var presence = new PresenceState(IsMicActive: true, false, false, Source: PresenceSource.WindowsRegistry);
        var tick = MakeTick(SignalQuality.Trusted, afk: false);
        var decision = SnapshotGate.Evaluate(tick, presence);
        Assert.True(decision.Suppress);
        Assert.Equal(SuppressionReason.InMeeting, decision.Reason);
    }

    // ── PulseAudioParser ──────────────────────────────────────────────────────

    [Fact]
    public void HasActiveCaptureStream_RunningStream_ReturnsTrue()
    {
        string output = SamplePactlOutput(running: true);
        Assert.True(PulseAudioParser.HasActiveCaptureStream(output));
    }

    [Fact]
    public void HasActiveCaptureStream_CorkedStream_ReturnsFalse()
    {
        // CORKED means suspended, not actively capturing.
        const string output = """
            Source Output #0
                State: CORKED
                Properties:
                    application.name = "PulseEffects"
            """;
        Assert.False(PulseAudioParser.HasActiveCaptureStream(output));
    }

    [Fact]
    public void HasActiveCaptureStream_EmptyOutput_ReturnsFalse() =>
        Assert.False(PulseAudioParser.HasActiveCaptureStream(""));

    [Fact]
    public void HasActiveCaptureStream_NullOutput_ReturnsFalse() =>
        Assert.False(PulseAudioParser.HasActiveCaptureStream(null!));

    [Fact]
    public void HasActiveCaptureStream_CaseInsensitive_ReturnsTrue()
    {
        // Guard against pactl variants that capitalise differently.
        Assert.True(PulseAudioParser.HasActiveCaptureStream("state: running"));
    }

    [Fact]
    public void HasActiveCaptureStream_NoSourceOutputs_ReturnsFalse()
    {
        const string output = "0 source output(s).";
        Assert.False(PulseAudioParser.HasActiveCaptureStream(output));
    }

    // ── PipeWireParser ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ActiveMicNode_ReturnsMicActive()
    {
        string json = BuildPwDump(mediaClass: "Stream/Input/Audio", state: "running");
        var state = PipeWireParser.Parse(json);
        Assert.True(state.IsMicActive);
        Assert.False(state.IsCameraActive);
        Assert.False(state.IsScreenSharing);
        Assert.Equal(PresenceSource.PipeWire, state.Source);
    }

    [Fact]
    public void Parse_ActiveCameraNode_ReturnsCameraActive()
    {
        string json = BuildPwDump(mediaClass: "Stream/Input/Video", state: "running");
        var state = PipeWireParser.Parse(json);
        Assert.False(state.IsMicActive);
        Assert.True(state.IsCameraActive);
        Assert.False(state.IsScreenSharing);
    }

    [Fact]
    public void Parse_PortalScreenCastNode_ReturnsScreenSharing()
    {
        string json = BuildPwDump(
            mediaClass: "Stream/Output/Video",
            state: "running",
            nodeName: "xdp.pipewire.source");
        var state = PipeWireParser.Parse(json);
        Assert.True(state.IsScreenSharing);
        Assert.False(state.IsMicActive);
    }

    [Fact]
    public void Parse_NonPortalVideoOutputNode_DoesNotDetectScreenSharing()
    {
        // A regular video player outputting to a virtual sink must not trigger screen-share.
        string json = BuildPwDump(
            mediaClass: "Stream/Output/Video",
            state: "running",
            nodeName: "vlc-video-output");
        var state = PipeWireParser.Parse(json);
        Assert.False(state.IsScreenSharing);
    }

    [Fact]
    public void Parse_SuspendedMicNode_ReturnsFalse()
    {
        string json = BuildPwDump(mediaClass: "Stream/Input/Audio", state: "suspended");
        var state = PipeWireParser.Parse(json);
        Assert.False(state.IsMicActive);
    }

    [Fact]
    public void Parse_EmptyJson_ReturnsUnavailable()
    {
        var state = PipeWireParser.Parse("");
        Assert.Equal(PresenceState.Unavailable, state);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsUnavailable()
    {
        var state = PipeWireParser.Parse("not json {{{");
        Assert.Equal(PresenceState.Unavailable, state);
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsAllFalse()
    {
        var state = PipeWireParser.Parse("[]");
        Assert.False(state.IsMicActive);
        Assert.False(state.IsCameraActive);
        Assert.False(state.IsScreenSharing);
        Assert.Equal(PresenceSource.PipeWire, state.Source);
    }

    [Fact]
    public void Parse_MicAndScreenShare_BothDetected()
    {
        // Multiple nodes in the same dump.
        string json = $"""
            [
              {BuildPwNode("Stream/Input/Audio", "running", "zoom-mic")},
              {BuildPwNode("Stream/Output/Video", "running", "xdp.pipewire.source")}
            ]
            """;
        var state = PipeWireParser.Parse(json);
        Assert.True(state.IsMicActive);
        Assert.True(state.IsScreenSharing);
    }

    // ── IsScreenCastPortalNode ────────────────────────────────────────────────

    [Theory]
    [InlineData("node.name", "xdp.pipewire.source", true)]
    [InlineData("node.name", "org.freedesktop.portal.ScreenCast", true)]
    [InlineData("application.name", "xdg-desktop-portal-gnome", true)]
    [InlineData("node.name", "vlc-video-render", false)]
    [InlineData("node.name", "obs-virtual-camera", false)]
    public void IsScreenCastPortalNode_RecognisesPortalNodes(string propKey, string propVal, bool expected)
    {
        var json = $"{{\"{propKey}\":\"{propVal}\"}}";
        using var doc = JsonDocument.Parse(json);
        bool result = PipeWireParser.IsScreenCastPortalNode(doc.RootElement);
        Assert.Equal(expected, result);
    }

    // ── ParseNudgeArgs meeting suppression ────────────────────────────────────

    [Fact]
    public void ParseNudgeArgs_Default_MeetingSuppressionEnabled()
    {
        var args = NudgeCoreLogic.ParseNudgeArgs([]);
        Assert.True(args.MeetingSuppression);
    }

    [Fact]
    public void ParseNudgeArgs_NoMeetingSuppression_DisablesMeetingSuppression()
    {
        var args = NudgeCoreLogic.ParseNudgeArgs(["--no-meeting-suppression"]);
        Assert.False(args.MeetingSuppression);
    }

    [Fact]
    public void ParseNudgeArgs_OtherFlags_DoNotAffectMeetingSuppression()
    {
        var args = NudgeCoreLogic.ParseNudgeArgs(["--ml", "--force-model"]);
        Assert.True(args.MeetingSuppression);
    }

    [Fact]
    public void Evaluate_NullTick_ActivePresence_Suppresses()
    {
        // When tick is null but presence shows InMeeting, should still suppress.
        var presence = new PresenceState(IsMicActive: true, false, false, Source: PresenceSource.PipeWire);
        var decision = SnapshotGate.Evaluate(null, presence);
        Assert.True(decision.Suppress);
        Assert.Equal(SuppressionReason.InMeeting, decision.Reason);
    }

    [Fact]
    public void Evaluate_NullTick_PresenceScreenSharing_Suppresses()
    {
        var presence = new PresenceState(false, false, IsScreenSharing: true, Source: PresenceSource.PipeWire);
        var decision = SnapshotGate.Evaluate(null, presence);
        Assert.True(decision.Suppress);
        Assert.Equal(SuppressionReason.ScreenSharing, decision.Reason);
    }

    [Fact]
    public void Evaluate_NullTick_NoPresence_Allows()
    {
        var decision = SnapshotGate.Evaluate(null, PresenceState.Unavailable);
        Assert.False(decision.Suppress);
    }

    [Fact]
    public void Evaluate_UsableSignal_DoesNotSuppress()
    {
        var tick = MakeTick(SignalQuality.Usable, afk: false);
        var decision = SnapshotGate.Evaluate(tick, PresenceState.Unavailable);
        Assert.False(decision.Suppress);
    }

    // ── ConsentStorePresence (Windows ConsentStore) ───────────────────────────

    private static ConsentLeaf Leaf(string appId, bool active, bool packaged = false, bool running = true)
        => new(appId, StartFileTime: active ? 1 : 0, StopFileTime: 0, IsPackaged: packaged, ProcessRunning: running);

    [Fact]
    public void ConsentLeaf_StartedNotStopped_Packaged_IsActive() =>
        Assert.True(new ConsentLeaf("MSTeams_8wekyb3d8bbwe", 100, 0, IsPackaged: true, ProcessRunning: false).IsActive);

    [Fact]
    public void ConsentLeaf_Stopped_IsInactive() =>
        Assert.False(new ConsentLeaf("app", 100, 200, false, true).IsActive);

    [Fact]
    public void ConsentLeaf_NeverStarted_IsInactive() =>
        Assert.False(new ConsentLeaf("app", 0, 0, false, true).IsActive);

    [Fact]
    public void ConsentLeaf_NonPackagedProcessNotRunning_IsInactive() =>
        Assert.False(new ConsentLeaf("C:#x#Zoom.exe", 100, 0, IsPackaged: false, ProcessRunning: false).IsActive);

    [Fact]
    public void ConsentLeaf_NonPackagedProcessRunning_IsActive() =>
        Assert.True(new ConsentLeaf("C:#x#Zoom.exe", 100, 0, IsPackaged: false, ProcessRunning: true).IsActive);

    [Fact]
    public void ConsentLeaf_PackagedStopMissing_IsInactive()
    {
        // Missing LastUsedTimeStop (sentinel -1) must not be treated as "still in use".
        Assert.False(new ConsentLeaf("MSTeams_8wekyb3d8bbwe", 100, -1, IsPackaged: true, ProcessRunning: false).IsActive);
    }

    [Fact]
    public void ConsentLeaf_NonPackagedStopMissing_IsInactive()
    {
        Assert.False(new ConsentLeaf("C:#x#Teams.exe", 100, -1, IsPackaged: false, ProcessRunning: true).IsActive);
    }

    [Theory]
    [InlineData(@"C:#Program Files#Zoom#bin#Zoom.exe", "Zoom")]
    [InlineData("MSTeams_8wekyb3d8bbwe", "MSTeams")]
    [InlineData("SomeApp", "SomeApp")]
    [InlineData("", "")]
    public void ExtractAppHint_ReducesToToken(string appId, string expected) =>
        Assert.Equal(expected, ConsentStorePresence.ExtractAppHint(appId));

    [Theory]
    [InlineData("MSTeams", true)]
    [InlineData("Zoom", true)]
    [InlineData("chrome", false)]
    [InlineData("", false)]
    public void IsMeetingApp_MatchesKnownCommsApps(string hint, bool expected) =>
        Assert.Equal(expected, ConsentStorePresence.IsMeetingApp(hint));

    [Fact]
    public void Evaluate_MicActiveMeetingAppOwner_IsInMeeting()
    {
        var state = ConsentStorePresence.Evaluate(
            micLeaves: [Leaf(@"C:#x#Zoom.exe", active: true)],
            camLeaves: [],
            foregroundProcess: "notepad", foregroundTitle: "Untitled");
        Assert.True(state.IsMicActive);
        Assert.True(state.InMeeting);
        Assert.Equal(PresenceSource.WindowsRegistry, state.Source);
    }

    [Fact]
    public void Evaluate_MicActivePackagedTeams_IsInMeeting()
    {
        var state = ConsentStorePresence.Evaluate(
            [Leaf("MSTeams_8wekyb3d8bbwe", active: true, packaged: true)], [],
            "explorer", "");
        Assert.True(state.InMeeting);
    }

    [Fact]
    public void Evaluate_MicActiveForegroundMeetingTitle_IsInMeeting()
    {
        // Browser meeting: mic is owned by chrome, but the foreground title gives it away.
        var state = ConsentStorePresence.Evaluate(
            [Leaf(@"C:#x#chrome.exe", active: true)], [],
            "chrome", "Weekly sync - Google Meet");
        Assert.True(state.InMeeting);
    }

    [Fact]
    public void Evaluate_MicActiveNoMeetingContext_NotInMeeting()
    {
        // Dictation / voice typing in a normal app must NOT suppress nudges.
        var state = ConsentStorePresence.Evaluate(
            [Leaf(@"C:#x#notepad.exe", active: true)], [],
            "notepad", "Untitled - Notepad");
        Assert.False(state.IsMicActive);
        Assert.False(state.InMeeting);
    }

    [Fact]
    public void Evaluate_CameraActive_IsInMeetingRegardlessOfApp()
    {
        var state = ConsentStorePresence.Evaluate(
            [], [Leaf(@"C:#x#obs64.exe", active: true)],
            "obs64", "OBS Studio");
        Assert.True(state.IsCameraActive);
        Assert.True(state.InMeeting);
    }

    [Fact]
    public void Evaluate_StaleMicEntry_NotInMeeting()
    {
        // Active timestamps but the owning process isn't running (force-killed app):
        // the device leaf is ignored, so a stale meeting window doesn't suppress.
        var state = ConsentStorePresence.Evaluate(
            [Leaf(@"C:#x#zoom.exe", active: true, running: false)], [],
            "zoom", "Zoom Meeting");
        Assert.False(state.InMeeting);
    }

    [Fact]
    public void Evaluate_NothingActive_NotInMeeting_ButSourceKnown()
    {
        var state = ConsentStorePresence.Evaluate([], [], "code", "main.cs");
        Assert.False(state.InMeeting);
        Assert.Equal(PresenceSource.WindowsRegistry, state.Source);
        // Source != None but nothing active → gate must allow.
        Assert.False(SnapshotGate.Evaluate(null, state).Suppress);
    }

    [Fact]
    public void Evaluate_ScreenSharingAlwaysFalseOnWindows()
    {
        var state = ConsentStorePresence.Evaluate(
            [Leaf("MSTeams_8wekyb3d8bbwe", active: true, packaged: true)], [],
            "ms-teams", "");
        Assert.False(state.IsScreenSharing);
    }

    [Fact]
    public void Evaluate_PackagedTeamsStopMissing_NotInMeeting()
    {
        // Packaged Teams has a mic history but LastUsedTimeStop is missing (-1 sentinel).
        // Must NOT suppress nudges — Teams being open is not a meeting.
        var leaf = new ConsentLeaf("MSTeams_8wekyb3d8bbwe",
            StartFileTime: 100, StopFileTime: -1,
            IsPackaged: true, ProcessRunning: false);
        var state = ConsentStorePresence.Evaluate(
            [leaf], [],
            "ms-teams", "Microsoft Teams");
        Assert.False(state.InMeeting);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ActivityTickResult MakeTick(SignalQuality quality, bool afk)
    {
        var ctx = new ActivityContext(
            FocusedAppId: "testapp",
            FocusedTitle: "",
            FocusedDomain: "",
            FocusedWindowId: "",
            IdleMs: afk ? 90_000 : 0,
            IsIdleNow: 0,
            FocusedSinceMs: 1000,
            TitleUnchangedForMs: 1000,
            MappedToplevelCount: 1,
            ActiveWorkspaceId: "",
            FocusSource: FocusSource.Unknown,
            SignalQuality: quality,
            FullscreenFlag: 0);

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
            Context: ctx,
            Features: features,
            AppCategory: AppCategory.Unknown,
            AppCategoryConfidence: CategoryConfidence.Unknown,
            DisplayAppName: "testapp",
            LegacyAppName: "testapp",
            LegacyForegroundAppHash: 0,
            TimeLastRequestMs: 1000);
    }

    private static string SamplePactlOutput(bool running) => $"""
        Source Output #0
            Driver: protocol-native.c
            Source: 1
            State: {(running ? "RUNNING" : "CORKED")}
            Properties:
                application.name = "Zoom"
        """;

    // Builds a minimal pw-dump JSON array with one node.
    private static string BuildPwDump(string mediaClass, string state, string nodeName = "test-node") =>
        $"[{BuildPwNode(mediaClass, state, nodeName)}]";

    private static string BuildPwNode(string mediaClass, string state, string nodeName) => $$"""
        {
          "type": "PipeWire:Interface:Node",
          "info": {
            "state": "{{state}}",
            "props": {
              "media.class": "{{mediaClass}}",
              "node.name": "{{nodeName}}"
            }
          }
        }
        """;
}
