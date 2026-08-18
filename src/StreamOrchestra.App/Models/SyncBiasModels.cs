namespace StreamOrchestra.App.Models;

public static class SyncManualDelaySchema
{
    public const int CurrentVersion = 1;
}

public sealed record SyncManualDelayComponents(
    int AlgorithmPriorMilliseconds,
    int UserResidualMilliseconds,
    int FinalDelayMilliseconds);

public enum SyncBiasManualEventKind
{
    AlignmentConfirmed,
    SuggestionShown,
    SuggestionAccepted,
    SuggestionRejected,
    SuggestionReverted,
    UserAdjusted
}

public enum SyncBiasHierarchyLevel
{
    None,
    Channel,
    ChannelQuality,
    ChannelQualityCdn
}

public sealed record SyncBiasContext(
    string StableChannelHash,
    string QualityBucket,
    string CdnBucket);

public sealed record SyncBiasPairObservation
{
    public string ObservationId { get; init; } = "";

    public string IndependentSessionHash { get; init; } = "";

    public required SyncBiasContext Left { get; init; }

    public required SyncBiasContext Right { get; init; }

    public double DelayDifferenceMilliseconds { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public bool IsIndependentSession { get; init; }

    public bool IsStableFinal { get; init; }

    public SyncBiasManualEventKind EventKind { get; init; }
}

public sealed record SyncBiasManualEvent
{
    public string EventId { get; init; } = "";

    public string SuggestionId { get; init; } = "";

    public string IndependentSessionHash { get; init; } = "";

    public required SyncBiasContext Context { get; init; }

    public SyncBiasManualEventKind EventKind { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public int? AlgorithmPriorMilliseconds { get; init; }

    public int? UserResidualMilliseconds { get; init; }

    public int? FinalDelayMilliseconds { get; init; }
}

public sealed record SyncBiasSuggestion
{
    public string SuggestionId { get; init; } = "";

    public int SuggestedDelayMilliseconds { get; init; }

    public SyncBiasHierarchyLevel HierarchyLevel { get; init; }

    public string ComponentId { get; init; } = "";

    public int IndependentSessionSupport { get; init; }

    public double DiagnosticConfidenceScore { get; init; }

    public double ResidualScaleMilliseconds { get; init; }

    public bool IsSuggestionOnly { get; init; } = true;
}

public sealed record SyncBiasPriorDocument
{
    public int SchemaVersion { get; init; } = 1;

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public IReadOnlyList<SyncBiasPairObservation> PairObservations { get; init; } = [];

    public IReadOnlyList<SyncBiasManualEvent> ManualEvents { get; init; } = [];
}
