namespace StreamOrchestra.App.Models;

public static class SyncTelemetrySchema
{
    public const int SchemaVersion = 1;
    public const string ModelVersion = "stream-sync-shadow-v1";
}

public sealed record SyncTelemetryClockSample(
    DateTimeOffset Utc,
    long MonotonicTicks);

public sealed record SyncUrlIdentity(
    string SchemeBucket,
    string HostBucket,
    string PathBucket,
    string PersistenceHash);

public sealed record SyncSessionTelemetry(
    string SessionId,
    SyncTelemetryClockSample StartedAt,
    string AppVersion = "",
    string RuntimeBucket = "",
    long MonotonicFrequency = 0,
    int SchemaVersion = SyncTelemetrySchema.SchemaVersion,
    string ModelVersion = SyncTelemetrySchema.ModelVersion);

public sealed record SyncNetworkTelemetry(
    string SessionId,
    int SlotId,
    string RequestId,
    SyncUrlIdentity Resource,
    string ResourceKind,
    SyncTelemetryClockSample RequestStartedAt,
    SyncTelemetryClockSample? HeadersReceivedAt,
    SyncTelemetryClockSample? BodyCompletedAt,
    int? StatusCode = null,
    string ContentTypeBucket = "",
    string CacheBucket = "unknown",
    long? EncodedBodyLengthBucket = null,
    bool IsReducedConfidenceSource = false,
    string OutcomeCode = "",
    int NavigationGeneration = 0,
    long SourceEpoch = 0,
    long Sequence = 0,
    int SchemaVersion = SyncTelemetrySchema.SchemaVersion,
    string ModelVersion = SyncTelemetrySchema.ModelVersion);

public sealed record SyncPlaylistTelemetry(
    string SessionId,
    int SlotId,
    string PlaylistId,
    SyncUrlIdentity Playlist,
    string PlaylistKind,
    string RenditionKind,
    string ProgressKey,
    long Epoch,
    SyncTelemetryClockSample ObservedAt,
    bool IsDuplicate = false,
    bool IsStale = false,
    bool IsRollback = false,
    bool HasProgramDateTime = false,
    bool HasPrivateTimestamp = false,
    string PrivateExtensionBucket = "",
    IReadOnlyList<string>? WarningCodes = null,
    int NavigationGeneration = 0,
    long Sequence = 0,
    int SchemaVersion = SyncTelemetrySchema.SchemaVersion,
    string ModelVersion = SyncTelemetrySchema.ModelVersion);

public sealed record SyncPlayerTelemetry(
    string SessionId,
    int SlotId,
    long Epoch,
    SyncTelemetryClockSample HostReceivedAt,
    double CurrentTimeSeconds,
    IReadOnlyList<MediaTimeRange> BufferedRanges,
    IReadOnlyList<MediaTimeRange> SeekableRanges,
    string PlayerEvent,
    bool UsedVideoFrameCallback,
    double? PresentedMediaTimeSeconds = null,
    double? ExpectedDisplayMonotonicMilliseconds = null,
    long? DroppedVideoFrames = null,
    long? TotalVideoFrames = null,
    double? PageSampleMonotonicMilliseconds = null,
    int NavigationGeneration = 0,
    long Sequence = 0,
    int SchemaVersion = SyncTelemetrySchema.SchemaVersion,
    string ModelVersion = SyncTelemetrySchema.ModelVersion);

public sealed record SyncEstimateTelemetry(
    string SessionId,
    int SlotId,
    string EstimatorId,
    string SourceId,
    long Epoch,
    SyncTelemetryClockSample EstimatedAt,
    double? RawOffsetMilliseconds,
    double? FilteredOffsetMilliseconds,
    double? DriftMillisecondsPerSecond,
    double? StandardDeviationMilliseconds,
    bool ObservationAccepted,
    string RejectionReason = "",
    double? TimelineConfidence = null,
    double? BiasConfidence = null,
    double? ControllabilityConfidence = null,
    string ObservationId = "",
    string ProgressKey = "",
    string EstimatorRole = "shadow-candidate",
    double? PredictionLowerMilliseconds = null,
    double? PredictionUpperMilliseconds = null,
    double? InnovationMilliseconds = null,
    int NavigationGeneration = 0,
    long Sequence = 0,
    int SchemaVersion = SyncTelemetrySchema.SchemaVersion,
    string ModelVersion = SyncTelemetrySchema.ModelVersion,
    bool ChangePointSuspected = false);

public sealed record SyncPolicyDecision(
    string PolicyId,
    string State,
    double? TargetMediaTimeSeconds,
    string ProposedCommand,
    double? ProposedValue,
    bool HardSeekAllowed,
    string Reason,
    double? CommonIntervalStartMilliseconds = null,
    double? CommonIntervalEndMilliseconds = null,
    double? CombinedUncertaintyMilliseconds = null);

public sealed record SyncDecisionTelemetry(
    string SessionId,
    int SlotId,
    string DecisionId,
    long Epoch,
    SyncTelemetryClockSample DecidedAt,
    SyncPolicyDecision ExistingController,
    SyncPolicyDecision CandidateController,
    bool CandidateIsShadowOnly = true,
    string TickId = "",
    int NavigationGeneration = 0,
    long Sequence = 0,
    int SchemaVersion = SyncTelemetrySchema.SchemaVersion,
    string ModelVersion = SyncTelemetrySchema.ModelVersion);

public sealed record SyncActionTelemetry(
    string SessionId,
    int SlotId,
    string CommandId,
    long Epoch,
    string CommandType,
    double? RequestedValue,
    string Stage,
    SyncTelemetryClockSample OccurredAt,
    string DecisionId = "",
    double? ObservedMediaTimeSeconds = null,
    double? ObservedPlaybackRate = null,
    double? PostActionErrorMilliseconds = null,
    string OutcomeCode = "",
    int NavigationGeneration = 0,
    long Sequence = 0,
    int SchemaVersion = SyncTelemetrySchema.SchemaVersion,
    string ModelVersion = SyncTelemetrySchema.ModelVersion);

public sealed record SyncManualEventTelemetry(
    string SessionId,
    int SlotId,
    string EventId,
    string StableChannelHash,
    string BroadcastSessionHash,
    string EventType,
    SyncTelemetryClockSample OccurredAt,
    double? AlgorithmPriorMilliseconds,
    double? PreviousUserResidualMilliseconds,
    double? NewUserResidualMilliseconds,
    double? EffectiveDelayMilliseconds,
    string SuggestionId = "",
    bool IsStableFinalAcceptance = false,
    bool IsIndependentSession = false,
    string ContextBucket = "",
    int NavigationGeneration = 0,
    long SourceEpoch = 0,
    long Sequence = 0,
    int SchemaVersion = SyncTelemetrySchema.SchemaVersion,
    string ModelVersion = SyncTelemetrySchema.ModelVersion);

public sealed record SyncTelemetrySnapshot(
    int SchemaVersion,
    string ModelVersion,
    bool IsEnabled,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<SyncSessionTelemetry> Sessions,
    IReadOnlyList<SyncNetworkTelemetry> Network,
    IReadOnlyList<SyncPlaylistTelemetry> Playlists,
    IReadOnlyList<SyncPlayerTelemetry> Players,
    IReadOnlyList<SyncEstimateTelemetry> Estimates,
    IReadOnlyList<SyncDecisionTelemetry> Decisions,
    IReadOnlyList<SyncActionTelemetry> Actions,
    IReadOnlyList<SyncManualEventTelemetry> ManualEvents,
    long DroppedEventCount);

public sealed record SyncTelemetrySummary(
    int SchemaVersion,
    string ModelVersion,
    bool IsEnabled,
    int SessionCount,
    int NetworkEventCount,
    int PlaylistEventCount,
    int PlayerEventCount,
    int EstimateEventCount,
    int DecisionEventCount,
    int ActionEventCount,
    int ManualEventCount,
    long DroppedEventCount)
{
    public static SyncTelemetrySummary Disabled { get; } = new(
        SyncTelemetrySchema.SchemaVersion,
        SyncTelemetrySchema.ModelVersion,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);
}
