namespace StreamOrchestra.App.Models;

public sealed record SyncPairwiseProxyLabel(
    double BaselineLeftDelayMilliseconds,
    double BaselineRightDelayMilliseconds,
    double CandidateLeftDelayMilliseconds,
    double CandidateRightDelayMilliseconds,
    double AcceptedLeftDelayMilliseconds,
    double AcceptedRightDelayMilliseconds);

public sealed record SyncConfidenceEvaluationSample(
    string Domain,
    double ConfidenceScore,
    bool OutcomeSucceeded,
    bool? PredictionIntervalCovered = null,
    double? PredictionIntervalWidthMilliseconds = null);

public sealed record SyncEvaluationStrata(
    string NetworkBucket = "unknown",
    string PcLoadBucket = "unknown",
    string ChannelBucket = "unknown",
    string QualityBucket = "unknown",
    string CdnBucket = "unknown",
    string PlaybackBucket = "normal",
    string SourceBucket = "unknown");

public sealed record SyncShadowSessionEvaluation
{
    public string IndependentSessionId { get; init; } = "";

    public IReadOnlyList<SyncPairwiseProxyLabel> PairLabels { get; init; } = [];

    public double? InitialConvergenceSeconds { get; init; }

    public double ActivePlaybackHours { get; init; }

    public int VerifiedHardSeekCount { get; init; }

    public int FailedHardSeekCount { get; init; }

    public int WrongCorrectionCount { get; init; }

    public int EvaluatedCorrectionCount { get; init; }

    public int ManualAdjustmentEventCount { get; init; }

    public double TotalAbsoluteManualAdjustmentMilliseconds { get; init; }

    public IReadOnlyList<SyncConfidenceEvaluationSample> ConfidenceSamples { get; init; } = [];

    public SyncEvaluationStrata Strata { get; init; } = new();
}

public sealed record SyncDistributionSummary(
    int SessionCount,
    double? Median,
    double? P90,
    double? P95);

public sealed record SyncConfidenceCalibrationBin(
    string Domain,
    int BinIndex,
    int Count,
    double MeanConfidence,
    double SuccessRate,
    double? IntervalCoverage,
    double? MeanIntervalWidthMilliseconds);

public sealed record SyncShadowEvaluationSummary
{
    public SyncDistributionSummary BaselinePairwiseAbsoluteProxyErrorMilliseconds { get; init; } =
        new(0, null, null, null);

    public SyncDistributionSummary CandidatePairwiseAbsoluteProxyErrorMilliseconds { get; init; } =
        new(0, null, null, null);

    public SyncDistributionSummary InitialConvergenceSeconds { get; init; } =
        new(0, null, null, null);

    public double? VerifiedHardSeeksPerPlaybackHour { get; init; }

    public int FailedHardSeekCount { get; init; }

    public double? WrongCorrectionRate { get; init; }

    public int ManualAdjustmentEventCount { get; init; }

    public double TotalAbsoluteManualAdjustmentMilliseconds { get; init; }

    public IReadOnlyList<SyncConfidenceCalibrationBin> ConfidenceCalibration { get; init; } = [];

    public IReadOnlyDictionary<string, SyncDistributionSummary> CandidateErrorByStratum { get; init; } =
        new Dictionary<string, SyncDistributionSummary>();
}
