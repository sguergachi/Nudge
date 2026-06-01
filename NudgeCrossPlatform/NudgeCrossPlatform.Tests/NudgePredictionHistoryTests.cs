using System;
using System.Collections.Generic;
using NudgeCore;
using NudgeTray;
using Xunit;

namespace NudgeCrossPlatform.Tests;

/// <summary>
/// Regression tests for two bugs fixed in commits dd9903c and 3dec73d:
///
/// 1. Zig-zag in the prediction history graph caused by:
///    a. HandleSuppress creating a duplicate Score=0 event for each gate-suppressed
///       snapshot instead of stamping the reason on the existing MLDATA event.
///    b. Interval fallback events (TriggerSource="int") interleaving with real AI
///       predictions in the gradient chart.
///
/// 2. Windows meeting detection double-counting: IsMeetingTitle was also checking
///    the process name, which duplicated the signal already handled by
///    IsMeetingAppRunning(), causing Teams-foreground to contribute 0.20 on its
///    own instead of the intended 0.10.
/// </summary>
public sealed class NudgePredictionHistoryTests
{
    // ── SuppressionDeduplication.TryMutateLatest ─────────────────────────────

    [Fact]
    public void TryMutateLatest_RecentAiEvent_MutatesInPlaceAndReturnsTrue()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var evt = new MLLiveEvent
        {
            T             = now - 2,   // 2 s ago — within the 5 s window
            TriggerSource = "ai",
            Triggered     = true,
            Score         = 0.85,
        };

        bool mutated = SuppressionDeduplication.TryMutateLatest(evt, "InMeeting", now);

        Assert.True(mutated);
        Assert.Equal("InMeeting", evt.SuppressReason);
        Assert.False(evt.Triggered);    // cleared by the deduplication
        Assert.Equal(0.85, evt.Score);  // original score preserved
    }

    [Fact]
    public void TryMutateLatest_StaleEvent_ReturnsFalseAndLeavesEventUnchanged()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var evt = new MLLiveEvent
        {
            T             = now - 10,  // 10 s ago — outside the 5 s window
            TriggerSource = "ai",
            Triggered     = true,
        };

        bool mutated = SuppressionDeduplication.TryMutateLatest(evt, "InMeeting", now);

        Assert.False(mutated);
        Assert.Null(evt.SuppressReason);  // untouched
        Assert.True(evt.Triggered);       // untouched
    }

    [Fact]
    public void TryMutateLatest_NullEvent_ReturnsFalse()
    {
        bool mutated = SuppressionDeduplication.TryMutateLatest(
            null, "InMeeting", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.False(mutated);
    }

    [Fact]
    public void TryMutateLatest_ExactlyAtBoundary_Mutates()
    {
        // T == now - 5 is within the window (condition is > 5, exclusive)
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var evt = new MLLiveEvent { T = now - 5, TriggerSource = "ai" };

        bool mutated = SuppressionDeduplication.TryMutateLatest(evt, "PoorSignal", now);

        Assert.True(mutated);
        Assert.Equal("PoorSignal", evt.SuppressReason);
    }

    [Fact]
    public void TryMutateLatest_JustOutsideBoundary_DoesNotMutate()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var evt = new MLLiveEvent { T = now - 6, TriggerSource = "ai" };

        bool mutated = SuppressionDeduplication.TryMutateLatest(evt, "PoorSignal", now);

        Assert.False(mutated);
    }

    [Fact]
    public void TryMutateLatest_GateSuppression_DoesNotAddDuplicateEvent()
    {
        // MLDATA followed immediately by SUPPRESS must result in exactly one event
        // (the MLDATA event, now with SuppressReason set), NOT two events.
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var events = new List<MLLiveEvent>
        {
            new MLLiveEvent
            {
                T             = now - 1,
                TriggerSource = "ai",
                Score         = 0.3,
                Triggered     = false,
                App           = "teams",
            }
        };

        bool mutated = SuppressionDeduplication.TryMutateLatest(events[events.Count - 1], "InMeeting", now);
        if (!mutated)
        {
            events.Add(new MLLiveEvent
            {
                T              = now,
                SuppressReason = "InMeeting",
                TriggerSource  = "sup",
                Score          = 0,
            });
        }

        Assert.Single(events);                        // no duplicate was added
        Assert.Equal("InMeeting", events[0].SuppressReason);
    }

    [Fact]
    public void TryMutateLatest_StandaloneSuppression_NewEventHasSupTriggerSource()
    {
        // When there is no recent MLDATA event, SUPPRESS should create a new event
        // with TriggerSource="sup".
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var events = new List<MLLiveEvent>();

        bool mutated = SuppressionDeduplication.TryMutateLatest(
            events.Count > 0 ? events[events.Count - 1] : null,
            "InMeeting",
            now);

        if (!mutated)
        {
            events.Add(new MLLiveEvent
            {
                T              = now,
                SuppressReason = "InMeeting",
                TriggerSource  = "sup",
                Score          = 0,
                Confidence     = 0,
                Productive     = true,
                Triggered      = false,
            });
        }

        Assert.Single(events);
        Assert.Equal("sup", events[0].TriggerSource);
        Assert.Equal("InMeeting", events[0].SuppressReason);
        Assert.Equal(0, events[0].Score);
    }

    // ── PredictionChartHelper.FilterToAiOnly ─────────────────────────────────

    [Fact]
    public void FilterToAiOnly_MixedEvents_RetainsOnlyAiSource()
    {
        var events = new List<MLLiveEvent>
        {
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.9 },
            new MLLiveEvent { TriggerSource = "int", Score = 0.5 },
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.2 },
            new MLLiveEvent { TriggerSource = "sup", Score = 0.0 },
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.7 },
        };

        var filtered = PredictionChartHelper.FilterToAiOnly(events);

        Assert.Equal(3, filtered.Count);
        Assert.All(filtered, e => Assert.Equal("ai", e.TriggerSource));
    }

    [Fact]
    public void FilterToAiOnly_AllIntervalEvents_ReturnsEmpty()
    {
        var events = new List<MLLiveEvent>
        {
            new MLLiveEvent { TriggerSource = "int", Score = 0.5 },
            new MLLiveEvent { TriggerSource = "int", Score = 0.5 },
        };

        var filtered = PredictionChartHelper.FilterToAiOnly(events);

        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterToAiOnly_EmptyList_ReturnsEmpty()
    {
        var filtered = PredictionChartHelper.FilterToAiOnly(new List<MLLiveEvent>());
        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterToAiOnly_AllAiEvents_ReturnsAll()
    {
        var events = new List<MLLiveEvent>
        {
            new MLLiveEvent { TriggerSource = "ai", Score = 0.8 },
            new MLLiveEvent { TriggerSource = "ai", Score = 0.6 },
        };

        var filtered = PredictionChartHelper.FilterToAiOnly(events);

        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void FilterToAiOnly_DoesNotMutateOriginalList()
    {
        var events = new List<MLLiveEvent>
        {
            new MLLiveEvent { TriggerSource = "ai"  },
            new MLLiveEvent { TriggerSource = "int" },
        };

        PredictionChartHelper.FilterToAiOnly(events);

        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void FilterToAiOnly_InterleavedWithAi_ProducesMonotonousAiSequence()
    {
        // Before the fix, interval events (Score=0.5) interleaved with AI predictions
        // created artificial zig-zags. After filtering, only real model predictions remain.
        var events = new List<MLLiveEvent>
        {
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.8 },
            new MLLiveEvent { TriggerSource = "int", Score = 0.5 },
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.3 },
            new MLLiveEvent { TriggerSource = "int", Score = 0.5 },
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.9 },
        };

        var filtered = PredictionChartHelper.FilterToAiOnly(events);

        Assert.Equal(3, filtered.Count);
        Assert.Equal(0.8, filtered[0].Score);
        Assert.Equal(0.3, filtered[1].Score);
        Assert.Equal(0.9, filtered[2].Score);
    }

    [Fact]
    public void FilterToAiOnly_SuppressedAiEvents_Excluded()
    {
        // Suppressed events from HandleSuppress default TriggerSource="ai" with Score=0.
        // These must be excluded from the chart to prevent artificial zig-zags.
        var events = new List<MLLiveEvent>
        {
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.9 },
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.0, SuppressReason = "InMeeting" },
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.7 },
            new MLLiveEvent { TriggerSource = "ai",  Score = 0.0, SuppressReason = "PoorSignal" },
        };

        var filtered = PredictionChartHelper.FilterToAiOnly(events);

        Assert.Equal(2, filtered.Count);
        Assert.Equal(0.9, filtered[0].Score);
        Assert.Equal(0.7, filtered[1].Score);
    }
}

/// <summary>
/// Regression tests for the Windows meeting detection title-check fix (commit 3dec73d).
/// Before the fix, IsMeetingTitle() checked BOTH window title keywords AND process names,
/// double-counting the process-name signal that IsMeetingAppRunning() already emits.
/// After the fix, IsMeetingTitle() checks title keywords only.
/// </summary>
public sealed class NudgeMeetingTitleDetectorTests
{
    [Theory]
    [InlineData("teams")]
    [InlineData("zoom")]
    [InlineData("slack")]
    [InlineData("discord")]
    [InlineData("skype")]
    [InlineData("webex")]
    [InlineData("ms-teams")]
    [InlineData("ringcentral")]
    [InlineData("lark")]
    [InlineData("dingtalk")]
    [InlineData("wemeet")]
    [InlineData("voov meeting")]
    [InlineData("tencent meeting")]
    public void IsMeetingTitle_BareProcessName_DoesNotMatch(string processName)
    {
        Assert.False(MeetingTitleDetector.IsMeetingTitle(processName));
    }

    [Theory]
    [InlineData("Zoom Meeting — Project Kickoff")]
    [InlineData("zoom video webinar")]
    [InlineData("Google Meet - Weekly Sync")]
    [InlineData("Skype for Business - John Doe")]
    [InlineData("WebEx Meeting Room")]
    [InlineData("Slack Huddle — #engineering")]
    [InlineData("Slack Call with Alice")]
    [InlineData("Discord Voice - General")]
    [InlineData("GoToMeeting — Product Review")]
    [InlineData("BlueJeans Conference")]
    [InlineData("RingCentral Meeting")]
    [InlineData("Whereby — Standup")]
    [InlineData("Lark Meeting: Q3 OKRs")]
    [InlineData("DingTalk Meeting")]
    public void IsMeetingTitle_TitleKeyword_Matches(string title)
    {
        Assert.True(MeetingTitleDetector.IsMeetingTitle(title));
    }

    [Fact]
    public void IsMeetingTitle_EmptyTitle_ReturnsFalse()
    {
        Assert.False(MeetingTitleDetector.IsMeetingTitle(""));
    }

    [Fact]
    public void IsMeetingTitle_WhitespaceTitle_ReturnsFalse()
    {
        Assert.False(MeetingTitleDetector.IsMeetingTitle("   "));
    }

    [Fact]
    public void IsMeetingTitle_UnrelatedTitle_ReturnsFalse()
    {
        Assert.False(MeetingTitleDetector.IsMeetingTitle("Visual Studio Code — nudge-tray.cs"));
    }

    [Fact]
    public void IsMeetingTitle_IsCaseInsensitive()
    {
        Assert.True(MeetingTitleDetector.IsMeetingTitle("zoom meeting"));
        Assert.True(MeetingTitleDetector.IsMeetingTitle("ZOOM MEETING"));
    }

    [Theory]
    [InlineData("Microsoft Teams")]
    [InlineData("Chat | Singer, Malek | Microsoft Teams")]
    public void IsMeetingTitle_BareTeamsOrTeamsChat_DoesNotMatch(string title)
    {
        // Issue #128: a Teams chat window title contains "Microsoft Teams" but is not a call,
        // so the bare app name is deliberately not a meeting-title keyword. Real Teams calls
        // are caught by the Teams package owning the mic or by the camera, not the title.
        Assert.False(MeetingTitleDetector.IsMeetingTitle(title));
    }

    [Fact]
    public void ProcessNames_TeamsZoomSlack_AreNotStandaloneTitleKeywords()
    {
        string[] highRiskProcessNames = ["teams", "zoom", "slack", "discord", "skype", "webex"];
        foreach (string proc in highRiskProcessNames)
        {
            Assert.False(MeetingTitleDetector.IsMeetingTitle(proc),
                $"Process name '{proc}' must not match as a meeting title");
        }
    }
}
