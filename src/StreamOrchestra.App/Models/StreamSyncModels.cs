namespace StreamOrchestra.App.Models;

public sealed class SyncGroupPreset
{
    public int MinimumSafetyDelayMs { get; init; } = 3000;

    public IReadOnlyList<SyncMemberPreset> Members { get; init; } = [];
}

public sealed class SyncMemberPreset
{
    public int SlotId { get; init; }

    public int ManualDelayMs { get; init; }

    public string CalibratedStreamUrl { get; init; } = "";
}

public enum SyncRuntimeState
{
    Stopped,
    Preparing,
    Running,
    Recovering,
    Waiting,
    Degraded
}

public enum SyncTimelineSource
{
    None,
    ProgramDateTime,
    CdnDate,
    LiveEdgeEstimate
}

public enum SyncCommandType
{
    SetRate,
    Seek,
    Pause,
    Resume,
    ResetRate
}

public sealed record SyncMemberSnapshot
{
    public bool HasVideo { get; init; }

    public bool IsSoop { get; init; }

    public double CurrentTime { get; init; }

    public double PlaybackRate { get; init; } = 1;

    public bool Paused { get; init; }

    public int ReadyState { get; init; }

    public bool Buffering { get; init; }

    public double? BufferSec { get; init; }

    public double? SeekableStart { get; init; }

    public double? SeekableEnd { get; init; }

    public long LastBufferEventAt { get; init; }

    public double ViewportArea { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record TimelineObservation
{
    public SyncTimelineSource Source { get; init; }

    public DateTimeOffset EdgeUtc { get; init; }

    public double? MediaToUtcOffsetMs { get; init; }

    public double SegmentDurationSec { get; init; }

    public double Confidence { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record SyncCommand(SyncCommandType Type, double? Value = null);

public sealed record SyncBadgeState(
    bool IsVisible,
    SyncRuntimeState RuntimeState,
    SyncTimelineSource TimelineSource,
    double? ErrorMs,
    string Text);

public sealed record SyncMemberViewState(
    int SlotId,
    string StreamName,
    string StreamUrl,
    bool IsReady,
    bool IsTemporarilyExcluded,
    SyncTimelineSource TimelineSource,
    double? BufferSec,
    double? ErrorMs,
    int ManualDelayMs,
    string StatusText);

public sealed record SyncGroupViewState(
    SyncRuntimeState RuntimeState,
    bool IsEnabled,
    int MinimumSafetyDelayMs,
    int EffectiveSafetyDelayMs,
    int ReadyMemberCount,
    IReadOnlyList<SyncMemberViewState> Members,
    string Notice);
