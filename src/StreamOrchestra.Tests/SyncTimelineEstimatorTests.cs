using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncTimelineEstimatorTests
{
    [Fact]
    public void MadGateRejectsSingleLargeOutlierWithoutMovingShadowCandidates()
    {
        var estimator = new SyncTimelineEstimator();
        SyncEstimatorShadowResult? accepted = null;
        var offsets = new[] { 1000d, 1010, 990, 1005, 995, 1002 };
        for (var index = 0; index < offsets.Length; index++)
        {
            accepted = estimator.Observe(Observation(offsets[index], index + 1));
            Assert.True(accepted.KalmanCandidate.ObservationAccepted);
        }

        var outlier = estimator.Observe(Observation(5000, 7));

        Assert.False(outlier.KalmanCandidate.ObservationAccepted);
        Assert.Equal("outlier", outlier.KalmanCandidate.RejectionReason);
        Assert.InRange(outlier.KalmanCandidate.OffsetMilliseconds!.Value, 900, 1100);
        Assert.InRange(outlier.HuberCandidate.OffsetMilliseconds!.Value, 900, 1100);
        Assert.Equal("observation-7", outlier.ObservationIdentity);
    }

    [Fact]
    public void KalmanAndHuberTrackSyntheticOffsetDriftInShadowOnlyMode()
    {
        var estimator = new SyncTimelineEstimator(new SyncTimelineEstimatorOptions
        {
            KalmanMeasurementNoiseMilliseconds = 30,
            KalmanOffsetProcessNoise = 1,
            KalmanDriftProcessNoise = 0.02
        });
        SyncEstimatorShadowResult? result = null;
        var noise = new[] { -8d, 4, 7, -3, 0 };
        for (var second = 0; second <= 60; second++)
        {
            result = estimator.Observe(Observation(
                1000 + 2 * second + noise[second % noise.Length],
                second + 1,
                evidenceCount: second + 1));
        }

        Assert.NotNull(result);
        Assert.True(result!.KalmanCandidate.IsShadowOnly);
        Assert.True(result.HuberCandidate.IsShadowOnly);
        Assert.InRange(result.KalmanCandidate.DriftMillisecondsPerSecond!.Value, 1.2, 2.8);
        Assert.InRange(result.HuberCandidate.DriftMillisecondsPerSecond!.Value, 1.8, 2.2);
        Assert.InRange(result.KalmanCandidate.OffsetMilliseconds!.Value, 1090, 1150);
        Assert.InRange(result.HuberCandidate.OffsetMilliseconds!.Value, 1100, 1140);
        Assert.Equal(0, result.KalmanCandidate.BiasScore);
        Assert.True(result.KalmanCandidate.TimelineScore > 0);
    }

    [Theory]
    [InlineData(SyncEstimatorObservationDisposition.Duplicate, "duplicate")]
    [InlineData(SyncEstimatorObservationDisposition.Stale, "stale")]
    [InlineData(SyncEstimatorObservationDisposition.Rollback, "rollback")]
    public void DuplicateStaleAndRollbackSkipUpdateButGrowPredictionUncertainty(
        SyncEstimatorObservationDisposition disposition,
        string expectedReason)
    {
        var estimator = new SyncTimelineEstimator();
        estimator.Observe(Observation(1000, 1));
        var before = estimator.Observe(Observation(1005, 2));

        var skipped = estimator.Observe(Observation(9000, 12, disposition));

        Assert.False(skipped.KalmanCandidate.ObservationAccepted);
        Assert.Equal(expectedReason, skipped.KalmanCandidate.RejectionReason);
        Assert.True(
            skipped.KalmanCandidate.StandardDeviationMilliseconds >
            before.KalmanCandidate.StandardDeviationMilliseconds);
        Assert.True(
            skipped.HuberCandidate.StandardDeviationMilliseconds >
            before.HuberCandidate.StandardDeviationMilliseconds);
        Assert.InRange(skipped.KalmanCandidate.OffsetMilliseconds!.Value, 900, 1200);
    }

    [Fact]
    public void SourceAndEpochChangesResetInsteadOfMixingPreviousOffsets()
    {
        var estimator = new SyncTimelineEstimator();
        estimator.Observe(Observation(1000, 1));
        estimator.Observe(Observation(1010, 2));

        var sourceReset = estimator.Observe(Observation(5000, 3, source: "source-b"));
        var epochReset = estimator.Observe(Observation(9000, 4, source: "source-b", epoch: 2));

        Assert.Equal(SyncEstimatorResetReason.SourceChanged, sourceReset.ResetReason);
        Assert.Equal(5000, sourceReset.KalmanCandidate.OffsetMilliseconds);
        Assert.Equal(SyncEstimatorResetReason.EpochChanged, epochReset.ResetReason);
        Assert.Equal(9000, epochReset.KalmanCandidate.OffsetMilliseconds);
    }

    [Fact]
    public void UtcStepDoesNotResetMonotonicEstimatorContext()
    {
        var estimator = new SyncTimelineEstimator();
        var first = Observation(1000, 1) with
        {
            ObservedAtUtc = DateTimeOffset.Parse("2026-08-18T00:00:00Z")
        };
        var second = Observation(1002, 2) with
        {
            ObservedAtUtc = DateTimeOffset.Parse("2026-08-17T23:00:00Z")
        };

        estimator.Observe(first);
        var result = estimator.Observe(second);

        Assert.Equal(SyncEstimatorResetReason.None, result.ResetReason);
        Assert.True(result.KalmanCandidate.ObservationAccepted);
    }

    [Fact]
    public void OutOfOrderMonotonicObservationIsRejectedWithoutThrowing()
    {
        var estimator = new SyncTimelineEstimator();
        estimator.Observe(Observation(1000, 2));

        var invalid = estimator.Observe(Observation(1100, 1));

        Assert.False(invalid.KalmanCandidate.ObservationAccepted);
        Assert.Equal("invalid", invalid.KalmanCandidate.RejectionReason);
    }

    [Fact]
    public void OneObservationProducesJoinedBaselineAndShadowRows()
    {
        var estimator = new SyncTimelineEstimator();

        var result = estimator.Observe(Observation(1234, 1));

        Assert.Equal("observation-1", result.ObservationIdentity);
        Assert.Equal("progress-1", result.ProgressKeyIdentity);
        Assert.Equal("legacy", result.ActiveBaseline.EstimatorId);
        Assert.False(result.ActiveBaseline.IsShadowOnly);
        Assert.Equal("kalman-shadow", result.KalmanCandidate.EstimatorId);
        Assert.Equal("huber-shadow", result.HuberCandidate.EstimatorId);
        Assert.True(result.KalmanCandidate.IsShadowOnly);
        Assert.True(result.HuberCandidate.IsShadowOnly);
    }

    [Fact]
    public void CusumFlagsSustainedAcceptedShiftButDoesNotResetTheEpoch()
    {
        var estimator = new SyncTimelineEstimator(new SyncTimelineEstimatorOptions
        {
            CusumAllowanceMilliseconds = 10,
            CusumDiagnosticThresholdMilliseconds = 200,
            MinimumOutlierGateMilliseconds = 1000
        });
        for (var second = 1; second <= 6; second++)
        {
            estimator.Observe(Observation(1000, second));
        }

        var results = Enumerable.Range(7, 6)
            .Select(second => estimator.Observe(Observation(1150, second)))
            .ToArray();

        Assert.Contains(results, result => result.ChangePointSuspected);
        Assert.All(results, result => Assert.Equal(SyncEstimatorResetReason.None, result.ResetReason));
    }

    private static SyncEstimatorObservation Observation(
        double offsetMilliseconds,
        int second,
        SyncEstimatorObservationDisposition disposition =
            SyncEstimatorObservationDisposition.NewEvidence,
        string source = "source-a",
        long epoch = 1,
        int evidenceCount = 3) => new()
    {
        LaneIdentity = "primary-video",
        SourceIdentity = source,
        SourceEpoch = epoch,
        ObservationIdentity = $"observation-{second}",
        ProgressKeyIdentity = $"progress-{second}",
        RawOffsetMilliseconds = offsetMilliseconds,
        ActiveBaselineOffsetMilliseconds = offsetMilliseconds,
        ObservedAtUtc = DateTimeOffset.Parse("2026-08-18T00:00:00Z").AddSeconds(second),
        ObservedMonotonicTicks = second * 1000L,
        MonotonicFrequency = 1000,
        Disposition = disposition,
        IsEpochStable = true,
        IndependentEvidenceCount = evidenceCount,
        ControllabilityScore = 1
    };
}
