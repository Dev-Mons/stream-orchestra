namespace StreamOrchestra.App.Models;

public enum SyncIntervalControllerMode
{
    Disabled,
    Shadow,
    OptInPreview
}

public enum SyncIntervalPolicyState
{
    Disabled,
    Shadow,
    Suppressed,
    NoIntersection,
    Degraded
}

public sealed record SyncTimeIntervalMilliseconds(
    double StartMilliseconds,
    double EndMilliseconds)
{
    public bool IsValid =>
        double.IsFinite(StartMilliseconds) &&
        double.IsFinite(EndMilliseconds) &&
        EndMilliseconds > StartMilliseconds;
}

public sealed record SyncIntervalMemberInput
{
    public int SlotId { get; init; }

    public IReadOnlyList<MediaTimeRange> PlayableRanges { get; init; } = [];

    public double CurrentMediaTimeSeconds { get; init; }

    public double MediaToGroupOffsetMilliseconds { get; init; }

    public int ManualDelayMilliseconds { get; init; }

    public bool TimelineFresh { get; init; }

    public bool EpochStable { get; init; }

    public bool SourceStable { get; init; }

    public bool PlayerHealthy { get; init; }

    public bool HasFullRangeObservation { get; init; }

    public bool HardSeekEvidenceEligible { get; init; }

    public double? TimelineUncertaintyMilliseconds { get; init; }

    public double? BiasUncertaintyMilliseconds { get; init; }

    public double? ControllabilityUncertaintyMilliseconds { get; init; }
}

public sealed record SyncIntervalPolicyRequest
{
    public IReadOnlyList<SyncIntervalMemberInput> Members { get; init; } = [];

    public int SafetyDelayMilliseconds { get; init; }
}

public sealed record SyncIntervalMemberDecision
{
    public int SlotId { get; init; }

    public double? TargetMediaTimeSeconds { get; init; }

    public double? ErrorMilliseconds { get; init; }

    public string ProposedCommand { get; init; } = "none";

    public double? ProposedValue { get; init; }

    public bool HardSeekAllowed { get; init; }

    public string Reason { get; init; } = "low-confidence";

    public double? CombinedUncertaintyMilliseconds { get; init; }
}

public sealed record SyncIntervalPolicyResult
{
    public SyncIntervalControllerMode Mode { get; init; }

    public SyncIntervalPolicyState State { get; init; }

    public bool IsShadowOnly { get; init; } = true;

    public IReadOnlyList<SyncTimeIntervalMilliseconds> CommonPlayableIntervals { get; init; } = [];

    public SyncTimeIntervalMilliseconds? SelectedCommonInterval { get; init; }

    public double? TargetGroupTimeMilliseconds { get; init; }

    public IReadOnlyList<SyncIntervalMemberDecision> Members { get; init; } = [];

    public string Reason { get; init; } = "low-confidence";
}
