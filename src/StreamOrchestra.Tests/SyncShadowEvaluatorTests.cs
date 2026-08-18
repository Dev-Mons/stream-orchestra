using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncShadowEvaluatorTests
{
    [Fact]
    public void EvaluationUsesIndependentSessionWeightingAndPairwiseProxyFormula()
    {
        var first = Session(
            "session-1",
            [Pair(baselineDifference: 100, candidateDifference: 20)],
            network: "wifi");
        var manyPairs = Enumerable.Repeat(
            Pair(baselineDifference: 1000, candidateDifference: 500),
            100).ToArray();
        var second = Session("session-2", manyPairs, network: "ethernet");

        var summary = SyncShadowEvaluator.Evaluate([first, second]);

        Assert.Equal(2, summary.BaselinePairwiseAbsoluteProxyErrorMilliseconds.SessionCount);
        Assert.Equal(550, summary.BaselinePairwiseAbsoluteProxyErrorMilliseconds.Median);
        Assert.Equal(260, summary.CandidatePairwiseAbsoluteProxyErrorMilliseconds.Median);
        Assert.Equal(2, summary.CandidateErrorByStratum.Count);
    }

    [Fact]
    public void EvaluationProducesGuardrailAndSeparatedConfidenceMetrics()
    {
        var session = Session(
            "session",
            [Pair(500, 200)],
            network: "wifi") with
        {
            InitialConvergenceSeconds = 4,
            ActivePlaybackHours = 2,
            VerifiedHardSeekCount = 3,
            FailedHardSeekCount = 1,
            WrongCorrectionCount = 2,
            EvaluatedCorrectionCount = 10,
            ManualAdjustmentEventCount = 4,
            TotalAbsoluteManualAdjustmentMilliseconds = 1300,
            ConfidenceSamples =
            [
                new SyncConfidenceEvaluationSample("timeline", 0.8, true, true, 200),
                new SyncConfidenceEvaluationSample("timeline", 0.9, false, false, 300),
                new SyncConfidenceEvaluationSample("bias", 0.8, true),
                new SyncConfidenceEvaluationSample("controllability", 0.4, true)
            ]
        };

        var summary = SyncShadowEvaluator.Evaluate([session]);

        Assert.Equal(1.5, summary.VerifiedHardSeeksPerPlaybackHour);
        Assert.Equal(1, summary.FailedHardSeekCount);
        Assert.Equal(0.2, summary.WrongCorrectionRate);
        Assert.Equal(4, summary.ManualAdjustmentEventCount);
        Assert.Equal(1300, summary.TotalAbsoluteManualAdjustmentMilliseconds);
        Assert.Equal(3, summary.ConfidenceCalibration.Select(bin => bin.Domain).Distinct().Count());
        var timeline = summary.ConfidenceCalibration.Single(bin => bin.Domain == "timeline");
        Assert.Equal(0.5, timeline.SuccessRate);
        Assert.Equal(0.5, timeline.IntervalCoverage);
        Assert.Equal(250, timeline.MeanIntervalWidthMilliseconds);
    }

    private static SyncShadowSessionEvaluation Session(
        string sessionId,
        IReadOnlyList<SyncPairwiseProxyLabel> pairs,
        string network) => new()
    {
        IndependentSessionId = sessionId,
        PairLabels = pairs,
        Strata = new SyncEvaluationStrata(
            NetworkBucket: network,
            PcLoadBucket: "normal",
            ChannelBucket: "seen",
            QualityBucket: "1080p",
            CdnBucket: "cdn",
            PlaybackBucket: "normal",
            SourceBucket: "pdt")
    };

    private static SyncPairwiseProxyLabel Pair(
        double baselineDifference,
        double candidateDifference) => new(
        baselineDifference,
        0,
        candidateDifference,
        0,
        0,
        0);
}
