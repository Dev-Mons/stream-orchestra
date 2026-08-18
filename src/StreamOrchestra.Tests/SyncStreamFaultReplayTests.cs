using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncStreamFaultReplayTests
{
    [Fact]
    public void StaleMajorityReplayNeverPullsTheEstimatorTowardRepeatedTailValues()
    {
        var estimator = new SyncTimelineEstimator();
        SyncEstimatorShadowResult? stable = null;
        for (var tick = 1; tick <= 6; tick++)
        {
            stable = estimator.Observe(Observation(1000 + (tick % 2 == 0 ? 5 : -5), tick));
        }

        SyncEstimatorShadowResult? stale = null;
        for (var tick = 7; tick <= 30; tick++)
        {
            stale = estimator.Observe(Observation(
                9000,
                tick,
                SyncEstimatorObservationDisposition.Stale));
        }

        Assert.NotNull(stable);
        Assert.NotNull(stale);
        Assert.False(stale!.KalmanCandidate.ObservationAccepted);
        Assert.Equal("stale", stale.KalmanCandidate.RejectionReason);
        Assert.InRange(stale.KalmanCandidate.OffsetMilliseconds!.Value, 800, 1200);
        Assert.True(
            stale.KalmanCandidate.StandardDeviationMilliseconds >
            stable!.KalmanCandidate.StandardDeviationMilliseconds);
    }

    [Fact]
    public void SyntheticKnownMappingReportsPredictionIntervalCoverageWithoutAccuracyClaim()
    {
        const double knownCombinedOffset = 1250;
        var estimator = new SyncTimelineEstimator(new SyncTimelineEstimatorOptions
        {
            KalmanMeasurementNoiseMilliseconds = 50,
            KalmanInitialOffsetDeviationMilliseconds = 500
        });
        var noise = new[] { -30d, -10, 0, 15, 25 };
        var covered = 0;
        var evaluated = 0;
        for (var tick = 1; tick <= 50; tick++)
        {
            var result = estimator.Observe(Observation(
                knownCombinedOffset + noise[tick % noise.Length],
                tick));
            if (result.KalmanCandidate.PredictionLowerMilliseconds is { } lower &&
                result.KalmanCandidate.PredictionUpperMilliseconds is { } upper)
            {
                evaluated++;
                covered += knownCombinedOffset >= lower && knownCombinedOffset <= upper ? 1 : 0;
            }
        }

        Assert.Equal(50, evaluated);
        Assert.True(covered / (double)evaluated >= 0.8);
    }

    [Fact]
    public void IntervalPolicyFaultSequenceFallsBackAcrossSourceSwitchGapAndBuffering()
    {
        var policy = new SyncIntervalControlPolicy();
        var first = Member(1);
        var second = Member(2);

        var healthy = policy.Evaluate(Request(first, second));
        var sourceSwitch = policy.Evaluate(Request(
            first with { SourceStable = false },
            second));
        var gap = policy.Evaluate(Request(
            first with { PlayableRanges = [new MediaTimeRange(100, 105)] },
            second with { PlayableRanges = [new MediaTimeRange(110, 115)] }));
        var buffering = policy.Evaluate(Request(
            first with { PlayerHealthy = false },
            second));
        var recovered = policy.Evaluate(Request(first, second));

        Assert.Equal(SyncIntervalPolicyState.Shadow, healthy.State);
        Assert.Equal(SyncIntervalPolicyState.Suppressed, sourceSwitch.State);
        Assert.Equal("source-reset", sourceSwitch.Reason);
        Assert.Equal(SyncIntervalPolicyState.NoIntersection, gap.State);
        Assert.Equal(SyncIntervalPolicyState.Suppressed, buffering.State);
        Assert.All(
            sourceSwitch.Members.Concat(gap.Members).Concat(buffering.Members),
            decision => Assert.False(decision.HardSeekAllowed));
        Assert.Equal(SyncIntervalPolicyState.Shadow, recovered.State);
    }

    private static SyncEstimatorObservation Observation(
        double offset,
        int tick,
        SyncEstimatorObservationDisposition disposition =
            SyncEstimatorObservationDisposition.NewEvidence) => new()
    {
        SourceIdentity = "source",
        SourceEpoch = 1,
        ObservationIdentity = $"observation-{tick}",
        ProgressKeyIdentity = $"progress-{tick}",
        RawOffsetMilliseconds = offset,
        ActiveBaselineOffsetMilliseconds = offset,
        ObservedAtUtc = DateTimeOffset.Parse("2026-08-18T00:00:00Z").AddSeconds(tick),
        ObservedMonotonicTicks = tick * 1000,
        MonotonicFrequency = 1000,
        Disposition = disposition,
        IsEpochStable = true,
        IndependentEvidenceCount = tick,
        ControllabilityScore = 1
    };

    private static SyncIntervalPolicyRequest Request(params SyncIntervalMemberInput[] members) => new()
    {
        Members = members,
        SafetyDelayMilliseconds = 1000
    };

    private static SyncIntervalMemberInput Member(int slotId) => new()
    {
        SlotId = slotId,
        PlayableRanges = [new MediaTimeRange(100, 120)],
        CurrentMediaTimeSeconds = 118,
        TimelineFresh = true,
        EpochStable = true,
        SourceStable = true,
        PlayerHealthy = true,
        HasFullRangeObservation = true,
        TimelineUncertaintyMilliseconds = null,
        BiasUncertaintyMilliseconds = null,
        ControllabilityUncertaintyMilliseconds = null
    };
}
