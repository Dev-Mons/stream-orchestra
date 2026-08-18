using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Models;

public sealed record SyncEstimatorCalibrationObservation
{
    public string ObservationId { get; init; } = "";

    public string SourceIdentity { get; init; } = "";

    public long SourceEpoch { get; init; }

    public double RawOffsetMilliseconds { get; init; }

    public double LegacyOffsetMilliseconds { get; init; }

    public double ReferenceOffsetMilliseconds { get; init; }

    public bool IsIndependentReference { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }

    public long ObservedMonotonicTicks { get; init; }

    public long MonotonicFrequency { get; init; }

    public SyncEstimatorObservationDisposition Disposition { get; init; } =
        SyncEstimatorObservationDisposition.NewEvidence;

    public bool IsEpochStable { get; init; } = true;

    public int IndependentEvidenceCount { get; init; }

    public double TimelineConfidence { get; init; }

    public double ManualBiasConfidence { get; init; }

    public double ControllabilityConfidence { get; init; }

    public bool? TimelineOutcomeSucceeded { get; init; }

    public bool? ManualBiasOutcomeSucceeded { get; init; }

    public bool? ControllabilityOutcomeSucceeded { get; init; }

    public string FaultKind { get; init; } = "normal";

    public SyncEvaluationStrata Strata { get; init; } = new();
}

public sealed record SyncEstimatorCalibrationSession
{
    public string IndependentSessionId { get; init; } = "";

    public DateTimeOffset StartedAtUtc { get; init; }

    public bool IsIndependentSession { get; init; }

    public IReadOnlyList<SyncEstimatorCalibrationObservation> Observations { get; init; } = [];
}

public sealed record SyncCalibrationTuningStep(
    string Parameter,
    IReadOnlyList<double> CandidateValues,
    double SelectedValue,
    double DevelopmentMeanAbsoluteErrorMilliseconds);

public sealed record SyncEstimatorErrorMetric(
    string EstimatorId,
    int MatchedObservationCount,
    double? MeanAbsoluteErrorMilliseconds,
    double? MedianAbsoluteErrorMilliseconds,
    double? P90AbsoluteErrorMilliseconds,
    double? P95AbsoluteErrorMilliseconds);

public sealed record SyncIntervalCoverageMetric(
    string EstimatorId,
    string Stratum,
    int ObservationCount,
    double? Coverage,
    double? MeanWidthMilliseconds);

public sealed record SyncCalibrationFaultCoverage(
    string FaultKind,
    int DevelopmentSessionCount,
    int HoldoutSessionCount);

public sealed record SyncEstimatorCalibrationReport
{
    public int ReportSchemaVersion { get; init; } = 1;

    public string CalibrationVersion { get; init; } = "stream-sync-calibration-v1";

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public string EvidenceSha256 { get; init; } = "";

    public string Status { get; init; } = "insufficient-development";

    public int IndependentSessionCount { get; init; }

    public int DevelopmentSessionCount { get; init; }

    public int HoldoutSessionCount { get; init; }

    public IReadOnlyList<string> DevelopmentSessionIds { get; init; } = [];

    public IReadOnlyList<string> HoldoutSessionIds { get; init; } = [];

    public IReadOnlyList<string> ExclusionReasons { get; init; } = [];

    public SyncTimelineEstimatorOptions SelectedOptions { get; init; } = new();

    public SyncTimelineEstimatorOptions RollbackOptions { get; init; } = new();

    public IReadOnlyList<SyncCalibrationTuningStep> TuningTrace { get; init; } = [];

    public IReadOnlyList<SyncEstimatorErrorMetric> DevelopmentMetrics { get; init; } = [];

    public IReadOnlyList<SyncEstimatorErrorMetric> HoldoutMetrics { get; init; } = [];

    public IReadOnlyList<SyncConfidenceCalibrationBin> HoldoutConfidenceCalibration { get; init; } = [];

    public IReadOnlyList<SyncIntervalCoverageMetric> HoldoutIntervalCoverageByStratum { get; init; } = [];

    public IReadOnlyList<SyncCalibrationFaultCoverage> FaultCoverage { get; init; } = [];
}
