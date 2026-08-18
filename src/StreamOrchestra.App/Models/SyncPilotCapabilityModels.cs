namespace StreamOrchestra.App.Models;

public sealed record SyncPilotAvailabilityMetric(
    string MetricId,
    int EligibleUnitCount,
    int ObservedUnitCount,
    double? AvailabilityRate);

public sealed record SyncPilotBucketCount(
    string Dimension,
    string Bucket,
    int UnitCount);

public sealed record CdpRuntimeCorrelationMetric
{
    public string RuntimeBucket { get; init; } = "unknown";

    public int AttemptCount { get; init; }

    public int CorrelatedCount { get; init; }

    public int AmbiguousCount { get; init; }

    public int InvalidCount { get; init; }

    public int RuntimeMismatchCount { get; init; }

    public int UnavailableCount { get; init; }

    public double? CoverageRate { get; init; }

    public double? AmbiguousRate { get; init; }

    public double? InvalidRate { get; init; }

    public bool GatePassed { get; init; }
}

public sealed record CdpCorrelationCoverageReport
{
    public bool HardSeekCoverageGatePassed { get; init; }

    public int MinimumAttemptsPerRuntime { get; init; }

    public double MinimumCoverageRate { get; init; }

    public double MaximumAmbiguousRate { get; init; }

    public double MaximumInvalidRate { get; init; }

    public IReadOnlyList<CdpRuntimeCorrelationMetric> Runtimes { get; init; } = [];

    public IReadOnlyList<string> Reasons { get; init; } = [];
}

public sealed record SyncPilotCapabilityReport
{
    public int ReportSchemaVersion { get; init; } = 1;

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public int InputSnapshotCount { get; init; }

    public int IndependentUnitCount { get; init; }

    public int ValidUnitCount { get; init; }

    public int SensitivityOnlyUnitCount { get; init; }

    public int BroadcastDayClusterCount { get; init; }

    public int DistinctChannelCount { get; init; }

    public int TargetUnitCount { get; init; } = 60;

    public int TargetBroadcastDayClusterCount { get; init; } = 20;

    public int TargetDistinctChannelCount { get; init; } = 12;

    public int DevelopmentUnitCount { get; init; }

    public int TemporalHoldoutUnitCount { get; init; }

    public long DroppedEventCount { get; init; }

    public string CollectionStatus { get; init; } = "not-started";

    public IReadOnlyList<string> InvalidUnitReasons { get; init; } = [];

    public IReadOnlyList<string> PrivacyViolations { get; init; } = [];

    public IReadOnlyList<SyncPilotAvailabilityMetric> Availability { get; init; } = [];

    public IReadOnlyList<SyncPilotBucketCount> BucketCounts { get; init; } = [];

    public CdpCorrelationCoverageReport CdpCorrelation { get; init; } = new();
}
