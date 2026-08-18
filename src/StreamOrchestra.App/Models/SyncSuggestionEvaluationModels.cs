namespace StreamOrchestra.App.Models;

public sealed record SyncSuggestionPairMetric(
    string Segment,
    int EligiblePairCount,
    int SuggestedPairCount,
    double? SuggestionCoverage,
    double? MedianAbsoluteProxyErrorMilliseconds,
    double? P90AbsoluteProxyErrorMilliseconds);

public sealed record SyncSuggestionHierarchyMetric(
    string HierarchyLevel,
    int SuggestionCount,
    double? MeanIndependentSessionSupport,
    double? MeanTrainingAgeDays);

public sealed record SyncSuggestionEventSummary
{
    public int ShownCount { get; init; }

    public int AcceptedCount { get; init; }

    public int RejectedCount { get; init; }

    public int RevertedCount { get; init; }

    public int PostSuggestionResidualAdjustmentCount { get; init; }

    public double TotalAbsolutePostSuggestionResidualAdjustmentMilliseconds { get; init; }
}

public sealed record SyncSuggestionTemporalHoldoutReport
{
    public int ReportSchemaVersion { get; init; } = 1;

    public string Status { get; init; } = "insufficient-development";

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public int EligibleIndependentSessionCount { get; init; }

    public int DevelopmentSessionCount { get; init; }

    public int HoldoutSessionCount { get; init; }

    public int ExcludedUnlabeledSessionCount { get; init; }

    public int ExcludedIneligibleLabelCount { get; init; }

    public IReadOnlyList<string> DevelopmentSessionIds { get; init; } = [];

    public IReadOnlyList<string> HoldoutSessionIds { get; init; } = [];

    public IReadOnlyList<SyncSuggestionPairMetric> PairMetrics { get; init; } = [];

    public IReadOnlyList<SyncSuggestionHierarchyMetric> HierarchyMetrics { get; init; } = [];

    public SyncSuggestionEventSummary EventSummary { get; init; } = new();

    public bool UsesOnlyExplicitStableIndependentLabels { get; init; } = true;

    public bool TreatsUnadjustedSessionsAsZeroLabels { get; init; }

    public bool IsLocalOnly { get; init; } = true;
}
