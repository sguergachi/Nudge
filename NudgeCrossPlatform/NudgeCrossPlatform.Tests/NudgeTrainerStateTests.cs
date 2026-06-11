using System;
using Xunit;
using NudgeTray;

namespace NudgeCrossPlatform.Tests;

/// <summary>
/// ParseLine must match the exact line formats background_trainer.py prints —
/// these literals are copied from its print() calls. If a trainer print format
/// changes, the matching test here must change with it (and vice versa).
/// </summary>
public sealed class TrainerStateParseLineTests
{
    private static void Reset()
    {
        TrainerState.SampleCount = 0;
        TrainerState.MinSamples = 10;
        TrainerState.LastTrainedCount = 0;
        TrainerState.ModelVersion = 0;
        TrainerState.IsTraining = false;
        TrainerState.LastAccuracy = -1f;
        TrainerState.PreviousAccuracy = -1f;
        TrainerState.Architecture = "";
        TrainerState.LastError = "";
        TrainerState.LastChecked = DateTime.MinValue;
        TrainerState.LastTrained = DateTime.MinValue;
        TrainerState.TrainingProgress = -1f;
    }

    [Fact]
    public void Heartbeat_ParsesRealCountNotSeedInflatedTotal()
    {
        Reset();
        TrainerState.ParseLine("[trainer] Labeled samples: 519  real: 19  last-trained-at: 0  min: 10");

        Assert.Equal(19, TrainerState.SampleCount);
        Assert.Equal(0, TrainerState.LastTrainedCount);
        Assert.Equal(10, TrainerState.MinSamples);
        Assert.NotEqual(DateTime.MinValue, TrainerState.LastChecked);
        Assert.Equal(DateTime.MinValue, TrainerState.LastTrained);
    }

    [Fact]
    public void Heartbeat_PriorTrainingRecoversLastTrained()
    {
        Reset();
        TrainerState.ParseLine("[trainer] Labeled samples: 142  real: 142  last-trained-at: 120  min: 10");

        Assert.Equal(142, TrainerState.SampleCount);
        Assert.Equal(120, TrainerState.LastTrainedCount);
        Assert.NotEqual(DateTime.MinValue, TrainerState.LastTrained);
    }

    [Fact]
    public void Heartbeat_ClearsStaleError()
    {
        Reset();
        TrainerState.LastError = "Training failed: boom";
        TrainerState.ParseLine("[trainer] Labeled samples: 5  real: 5  last-trained-at: 0  min: 10");

        Assert.Equal("", TrainerState.LastError);
    }

    [Fact]
    public void TrainingStart_SetsArchitectureAndIsTraining()
    {
        Reset();
        TrainerState.ParseLine("[trainer] Training (standard) on 19 samples...");

        Assert.True(TrainerState.IsTraining);
        Assert.Equal("standard", TrainerState.Architecture);
        Assert.Equal(19, TrainerState.SampleCount);
    }

    [Fact]
    public void Done_RecordsAccuracyVersionAndTrainedState()
    {
        Reset();
        TrainerState.SampleCount = 25;
        TrainerState.IsTraining = true;
        TrainerState.ParseLine("[trainer] Done. accuracy=0.872 version=3");

        Assert.False(TrainerState.IsTraining);
        Assert.Equal(0.872f, TrainerState.LastAccuracy, 3);
        Assert.Equal(3, TrainerState.ModelVersion);
        Assert.Equal(25, TrainerState.LastTrainedCount);
        Assert.NotEqual(DateTime.MinValue, TrainerState.LastTrained);
    }

    [Fact]
    public void TrainingFailed_SetsErrorAndStopsTraining()
    {
        Reset();
        TrainerState.IsTraining = true;
        TrainerState.ParseLine("[trainer] Training failed: No module named 'sklearn'");

        Assert.False(TrainerState.IsTraining);
        Assert.Equal("No module named 'sklearn'", TrainerState.LastError);
    }

    [Fact]
    public void NothingToDo_StopsTraining()
    {
        Reset();
        TrainerState.IsTraining = true;
        TrainerState.ParseLine("[trainer] Nothing to do.");

        Assert.False(TrainerState.IsTraining);
    }
}
