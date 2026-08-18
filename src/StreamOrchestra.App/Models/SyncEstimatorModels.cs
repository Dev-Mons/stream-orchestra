namespace StreamOrchestra.App.Models;

public enum SyncEstimatorObservationDisposition
{
    NewEvidence,
    Duplicate,
    Stale,
    Rollback,
    Invalid
}

public enum SyncEstimatorResetReason
{
    None,
    Initial,
    SourceChanged,
    EpochChanged,
    Explicit
}

public sealed record SyncEstimatorObservation
{
    public string LaneIdentity { get; init; } = "primary-video";

    public string SourceIdentity { get; init; } = "";

    public long SourceEpoch { get; init; }

    public string ObservationIdentity { get; init; } = "";

    public string ProgressKeyIdentity { get; init; } = "";

    public double RawOffsetMilliseconds { get; init; }

    public double? ActiveBaselineOffsetMilliseconds { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }

    public long ObservedMonotonicTicks { get; init; }

    public long MonotonicFrequency { get; init; }

    public SyncEstimatorObservationDisposition Disposition { get; init; }

    public bool IsEpochStable { get; init; }

    public int IndependentEvidenceCount { get; init; }

    public double ControllabilityScore { get; init; }
}

public sealed record SyncEstimatorEstimate
{
    public string EstimatorId { get; init; } = "unknown";

    public bool IsShadowOnly { get; init; }

    public bool ObservationAccepted { get; init; }

    public string RejectionReason { get; init; } = "none";

    public double? OffsetMilliseconds { get; init; }

    public double? DriftMillisecondsPerSecond { get; init; }

    public double? StandardDeviationMilliseconds { get; init; }

    public double? PredictionLowerMilliseconds { get; init; }

    public double? PredictionUpperMilliseconds { get; init; }

    public double? InnovationMilliseconds { get; init; }

    public double TimelineScore { get; init; }

    public double BiasScore { get; init; }

    public double ControllabilityScore { get; init; }
}

public sealed record SyncEstimatorShadowResult
{
    public string ObservationIdentity { get; init; } = "";

    public string ProgressKeyIdentity { get; init; } = "";

    public string SourceIdentity { get; init; } = "";

    public long SourceEpoch { get; init; }

    public SyncEstimatorResetReason ResetReason { get; init; }

    public bool ChangePointSuspected { get; init; }

    public required SyncEstimatorEstimate ActiveBaseline { get; init; }

    public required SyncEstimatorEstimate KalmanCandidate { get; init; }

    public required SyncEstimatorEstimate HuberCandidate { get; init; }
}
