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

    public int DelayModelVersion { get; init; }

    public int? AlgorithmPriorMs { get; init; }

    public int? UserResidualMs { get; init; }

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

public enum SyncPlayerEventKind
{
    Sample,
    Frame,
    Waiting,
    Stalled,
    Error,
    Seeking,
    Seeked,
    RateChange
}

public enum SyncNetworkObservationCapability
{
    Unknown,
    WebResourceResponseReduced,
    CdpCorrelated
}

public enum SyncPlayerClockSource
{
    CurrentTimeFallback,
    RequestVideoFrameCallback
}

public enum SyncCommandStage
{
    Issued,
    Applied,
    Verified,
    Failed,
    TimedOut
}

public sealed record MediaTimeRange(double StartSeconds, double EndSeconds)
{
    public bool IsValid =>
        double.IsFinite(StartSeconds) &&
        double.IsFinite(EndSeconds) &&
        EndSeconds > StartSeconds;

    public bool Contains(double mediaTimeSeconds, double marginSeconds = 0) =>
        IsValid &&
        double.IsFinite(mediaTimeSeconds) &&
        mediaTimeSeconds >= StartSeconds + Math.Max(0, marginSeconds) &&
        mediaTimeSeconds <= EndSeconds - Math.Max(0, marginSeconds);
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

    public IReadOnlyList<MediaTimeRange> BufferedRanges { get; init; } = [];

    public IReadOnlyList<MediaTimeRange> SeekableRanges { get; init; } = [];

    public MediaTimeRange? CurrentBufferedRange { get; init; }

    public MediaTimeRange? CurrentSeekableRange { get; init; }

    public bool Seeking { get; init; }

    public int NetworkState { get; init; }

    public string NetworkBucket { get; init; } = "unknown";

    public SyncPlayerEventKind EventKind { get; init; }

    public double? PageSampleMonotonicMilliseconds { get; init; }

    public double? PageEventMonotonicMilliseconds { get; init; }

    public long HostReceivedMonotonicTicks { get; init; }

    public long HostMonotonicFrequency { get; init; }

    public bool UsedVideoFrameCallback { get; init; }

    public double? PresentedMediaTimeSeconds { get; init; }

    public double? FrameAgeMilliseconds { get; init; }

    public double? ExpectedDisplayMonotonicMilliseconds { get; init; }

    public double? FrameProcessingDurationSeconds { get; init; }

    public long? PresentedFrames { get; init; }

    public long? DroppedVideoFrames { get; init; }

    public long? TotalVideoFrames { get; init; }

    public bool PlayerProgressHealthy { get; init; } = true;

    public double EffectiveMediaTimeSeconds { get; init; }

    public SyncPlayerClockSource MediaClockSource { get; init; }

    public long PlayerEventSequence { get; init; }

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

    public long ObservedMonotonicTicks { get; init; }

    public long MonotonicFrequency { get; init; }

    public double StaleAfterSeconds { get; init; } = 15;

    public string PlaylistIdentityHash { get; init; } = "";

    public HlsRenditionKind RenditionKind { get; init; }

    public long SourceEpoch { get; init; }

    public string ProgressKeyHash { get; init; } = "";

    public string CdnHostBucket { get; init; } = "unknown";

    public bool IsEpochStable { get; init; }

    public int IndependentEvidenceCount { get; init; }

    public SyncNetworkObservationCapability NetworkCapability { get; init; }

    public bool CdpHardSeekGatePassed { get; init; }

    public long? ResponseHeadersMonotonicTicks { get; init; }

    public long? BodyCompletedMonotonicTicks { get; init; }
}

public sealed record SyncCommand(SyncCommandType Type, double? Value = null)
{
    public string CommandId { get; init; } = Guid.NewGuid().ToString("N");
}

public sealed record SyncCommandResult
{
    public required string CommandId { get; init; }

    public SyncCommandStage Stage { get; init; }

    public bool WasApplied { get; init; }

    public bool WasVerified { get; init; }

    public double? ObservedMediaTimeSeconds { get; init; }

    public double? ObservedPlaybackRate { get; init; }

    public bool? ObservedPaused { get; init; }

    public string OutcomeCode { get; init; } = "unknown";

    public DateTimeOffset? IssuedAtUtc { get; init; }

    public DateTimeOffset? AppliedAtUtc { get; init; }

    public DateTimeOffset? VerifiedAtUtc { get; init; }

    public long? IssuedMonotonicTicks { get; init; }

    public long? AppliedMonotonicTicks { get; init; }

    public long? VerifiedMonotonicTicks { get; init; }

    public static SyncCommandResult Failed(string commandId, string outcomeCode) => new()
    {
        CommandId = commandId,
        Stage = SyncCommandStage.Failed,
        OutcomeCode = outcomeCode
    };
}

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
    string StatusText,
    int AlgorithmPriorMs = 0,
    int UserResidualMs = 0,
    int? SuggestedDelayMs = null,
    string SuggestionId = "",
    int SuggestionSupport = 0,
    SyncBiasHierarchyLevel SuggestionHierarchy = SyncBiasHierarchyLevel.None,
    bool CanRevertSuggestion = false);

public sealed record SyncGroupViewState(
    SyncRuntimeState RuntimeState,
    bool IsEnabled,
    int MinimumSafetyDelayMs,
    int EffectiveSafetyDelayMs,
    int ReadyMemberCount,
    IReadOnlyList<SyncMemberViewState> Members,
    string Notice);
