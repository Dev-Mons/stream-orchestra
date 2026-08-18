using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public interface ISyncTelemetryClock
{
    SyncTelemetryClockSample Capture();
}

public interface ISyncTelemetryRecorder
{
    bool IsEnabled { get; }

    string SessionId { get; }

    SyncUrlIdentity CreateUrlIdentity(string? rawUrl);

    void RecordSession(SyncSessionTelemetry value);

    void RecordNetwork(SyncNetworkTelemetry value);

    void RecordPlaylist(SyncPlaylistTelemetry value);

    void RecordPlayer(SyncPlayerTelemetry value);

    void RecordEstimate(SyncEstimateTelemetry value);

    void RecordDecision(SyncDecisionTelemetry value);

    void RecordAction(SyncActionTelemetry value);

    void RecordManualEvent(SyncManualEventTelemetry value);

    SyncTelemetrySnapshot CreateSnapshot();

    SyncTelemetrySummary CreateSummary();
}

public sealed record SyncTelemetryRecorderOptions
{
    public int MaxEventsPerCategory { get; init; } = 1024;

    public string SessionId { get; init; } = "";

    public string AppVersion { get; init; } = "";

    public string RuntimeBucket { get; init; } = "";

    public byte[]? IdentityKey { get; init; }
}

public sealed class SystemSyncTelemetryClock : ISyncTelemetryClock
{
    public static SystemSyncTelemetryClock Instance { get; } = new();

    private SystemSyncTelemetryClock()
    {
    }

    public SyncTelemetryClockSample Capture() =>
        new(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
}

public sealed class SyncTelemetryRecorder : ISyncTelemetryRecorder
{
    private static readonly IReadOnlySet<string> ResourceKinds = CodeSet(
        "playlist", "segment", "part", "map", "key", "media", "other", "unknown");
    private static readonly IReadOnlySet<string> NetworkOutcomes = CodeSet(
        "ok", "http-error", "read-failed", "parse-failed", "cancelled", "unsupported", "unknown");
    private static readonly IReadOnlySet<string> CacheBuckets = CodeSet(
        "hit", "miss", "revalidated", "bypass", "unknown");
    private static readonly IReadOnlySet<string> PlaylistKinds = CodeSet("master", "media", "unknown");
    private static readonly IReadOnlySet<string> RenditionKinds = CodeSet(
        "video", "audio", "subtitles", "variant", "unknown");
    private static readonly IReadOnlySet<string> PlayerEvents = CodeSet(
        "sample", "frame", "waiting", "stalled", "error", "seeking", "seeked", "ratechange", "progress", "timeupdate", "unknown");
    private static readonly IReadOnlySet<string> EstimatorIds = CodeSet(
        "legacy", "legacy-ema", "kalman-shadow", "huber-shadow", "unknown");
    private static readonly IReadOnlySet<string> EstimatorRoles = CodeSet(
        "active-baseline", "shadow-candidate", "unknown");
    private static readonly IReadOnlySet<string> RejectionReasons = CodeSet(
        "none", "duplicate", "stale", "rollback", "outlier", "epoch-reset", "source-reset",
        "invalid", "low-confidence", "unknown");
    private static readonly IReadOnlySet<string> PolicyIds = CodeSet("legacy", "interval-v1", "unknown");
    private static readonly IReadOnlySet<string> PolicyStates = CodeSet(
        "running", "shadow", "suppressed", "degraded", "waiting", "unknown");
    private static readonly IReadOnlySet<string> CommandCodes = CodeSet(
        "none", "seek", "rate", "pause", "resume", "reset-rate", "unknown");
    private static readonly IReadOnlySet<string> DecisionReasons = CodeSet(
        "legacy-decision", "low-confidence", "invalid-range", "no-intersection", "epoch-unstable",
        "stale", "source-reset", "deadband", "bounded-rate", "hard-seek", "unknown");
    private static readonly IReadOnlySet<string> ActionOutcomes = CodeSet(
        "ok", "position-confirmed", "rate-confirmed", "delivery-failed", "timed-out", "rejected", "unknown");
    private static readonly IReadOnlySet<string> PrivateExtensionBuckets = CodeSet(
        "ext-x-first-segment-timestamp", "other", "none", "unknown");
    private static readonly IReadOnlySet<string> WarningCodes = CodeSet(
        "malformed-playlist", "malformed-duration", "malformed-vendor-timestamp", "pdt-timezone-missing",
        "pdt-discontinuous", "invalid-byte-range", "invalid-uri", "unsupported-tag", "unknown");
    private static readonly IReadOnlySet<string> ContextBuckets = CodeSet(
        "channel-quality-cdn", "channel-quality", "channel", "global", "unknown");
    private readonly object _gate = new();
    private readonly ISyncTelemetryClock _clock;
    private readonly SyncTelemetryPrivacy _privacy;
    private readonly int _capacity;
    private readonly Queue<SyncSessionTelemetry> _sessions = [];
    private readonly Queue<SyncNetworkTelemetry> _network = [];
    private readonly Queue<SyncPlaylistTelemetry> _playlists = [];
    private readonly Queue<SyncPlayerTelemetry> _players = [];
    private readonly Queue<SyncEstimateTelemetry> _estimates = [];
    private readonly Queue<SyncDecisionTelemetry> _decisions = [];
    private readonly Queue<SyncActionTelemetry> _actions = [];
    private readonly Queue<SyncManualEventTelemetry> _manualEvents = [];
    private long _droppedEventCount;
    private long _nextSequence;

    private SyncTelemetryRecorder(
        SyncTelemetryRecorderOptions? options = null,
        ISyncTelemetryClock? clock = null)
    {
        options ??= new SyncTelemetryRecorderOptions();
        _clock = clock ?? SystemSyncTelemetryClock.Instance;
        _capacity = Math.Clamp(options.MaxEventsPerCategory, 1, 8192);
        _privacy = new SyncTelemetryPrivacy(options.IdentityKey);
        SessionId = _privacy.CreateOpaqueIdentity(
            "session",
            string.IsNullOrWhiteSpace(options.SessionId)
                ? Guid.NewGuid().ToString("N")
                : options.SessionId);

        RecordSession(new SyncSessionTelemetry(
            SessionId,
            _clock.Capture(),
            NormalizeBucket(options.AppVersion),
            NormalizeBucket(options.RuntimeBucket),
            Stopwatch.Frequency));
    }

    public bool IsEnabled => true;

    public string SessionId { get; }

    public static ISyncTelemetryRecorder Disabled { get; } = DisabledSyncTelemetryRecorder.Instance;

    public static SyncTelemetryRecorder CreateEnabled(
        SyncTelemetryRecorderOptions? options = null,
        ISyncTelemetryClock? clock = null) => new(options, clock);

    public SyncUrlIdentity CreateUrlIdentity(string? rawUrl) => _privacy.CreateUrlIdentity(rawUrl);

    public void RecordSession(SyncSessionTelemetry value)
    {
        if (!TryNormalizeStamp(value.StartedAt, out var startedAt))
        {
            DropEvent();
            return;
        }

        var safe = value with
        {
            SessionId = SessionId,
            StartedAt = startedAt,
            AppVersion = NormalizeBucket(value.AppVersion),
            RuntimeBucket = NormalizeBucket(value.RuntimeBucket),
            MonotonicFrequency = value.MonotonicFrequency > 0
                ? value.MonotonicFrequency
                : Stopwatch.Frequency,
            SchemaVersion = SyncTelemetrySchema.SchemaVersion,
            ModelVersion = SyncTelemetrySchema.ModelVersion
        };
        Add(_sessions, safe);
    }

    public void RecordNetwork(SyncNetworkTelemetry value)
    {
        if (!IsValidSlot(value.SlotId) ||
            !TryNormalizeStamp(value.RequestStartedAt, out var requestStartedAt) ||
            !TryNormalizeOptionalStamp(value.HeadersReceivedAt, out var headersReceivedAt) ||
            !TryNormalizeOptionalStamp(value.BodyCompletedAt, out var bodyCompletedAt) ||
            !AreOrdered(requestStartedAt, headersReceivedAt, bodyCompletedAt))
        {
            DropEvent();
            return;
        }

        var safe = value with
        {
            SessionId = SessionId,
            RequestId = NormalizeOpaqueId("request", value.RequestId),
            Resource = SanitizeIdentity(value.Resource),
            ResourceKind = NormalizeAllowed(value.ResourceKind, ResourceKinds),
            RequestStartedAt = requestStartedAt,
            HeadersReceivedAt = headersReceivedAt,
            BodyCompletedAt = bodyCompletedAt,
            ContentTypeBucket = NormalizeContentType(value.ContentTypeBucket),
            CacheBucket = NormalizeAllowed(value.CacheBucket, CacheBuckets),
            EncodedBodyLengthBucket = value.EncodedBodyLengthBucket is >= 0
                ? value.EncodedBodyLengthBucket
                : null,
            OutcomeCode = NormalizeAllowed(value.OutcomeCode, NetworkOutcomes),
            SourceEpoch = Math.Max(0, value.SourceEpoch),
            Sequence = NextSequence(),
            SchemaVersion = SyncTelemetrySchema.SchemaVersion,
            ModelVersion = SyncTelemetrySchema.ModelVersion
        };
        Add(_network, safe);
    }

    public void RecordPlaylist(SyncPlaylistTelemetry value)
    {
        if (!IsValidSlot(value.SlotId) || !TryNormalizeStamp(value.ObservedAt, out var observedAt))
        {
            DropEvent();
            return;
        }

        var safe = value with
        {
            SessionId = SessionId,
            PlaylistId = NormalizeOpaqueId("playlist", value.PlaylistId),
            Playlist = SanitizeIdentity(value.Playlist),
            PlaylistKind = NormalizeAllowed(value.PlaylistKind, PlaylistKinds),
            RenditionKind = NormalizeAllowed(value.RenditionKind, RenditionKinds),
            ProgressKey = NormalizeOpaqueId("progress", value.ProgressKey),
            Epoch = Math.Max(0, value.Epoch),
            ObservedAt = observedAt,
            PrivateExtensionBucket = NormalizeAllowed(value.PrivateExtensionBucket, PrivateExtensionBuckets, "none"),
            WarningCodes = NormalizeCodes(value.WarningCodes),
            Sequence = NextSequence(),
            SchemaVersion = SyncTelemetrySchema.SchemaVersion,
            ModelVersion = SyncTelemetrySchema.ModelVersion
        };
        Add(_playlists, safe);
    }

    public void RecordPlayer(SyncPlayerTelemetry value)
    {
        if (!IsValidSlot(value.SlotId) ||
            !double.IsFinite(value.CurrentTimeSeconds) ||
            !TryNormalizeStamp(value.HostReceivedAt, out var hostReceivedAt))
        {
            DropEvent();
            return;
        }

        var safe = value with
        {
            SessionId = SessionId,
            Epoch = Math.Max(0, value.Epoch),
            HostReceivedAt = hostReceivedAt,
            BufferedRanges = NormalizeRanges(value.BufferedRanges),
            SeekableRanges = NormalizeRanges(value.SeekableRanges),
            PlayerEvent = NormalizeAllowed(value.PlayerEvent, PlayerEvents),
            PresentedMediaTimeSeconds = FiniteOrNull(value.PresentedMediaTimeSeconds),
            ExpectedDisplayMonotonicMilliseconds = FiniteOrNull(
                value.ExpectedDisplayMonotonicMilliseconds),
            PageSampleMonotonicMilliseconds = FiniteOrNull(value.PageSampleMonotonicMilliseconds),
            DroppedVideoFrames = value.DroppedVideoFrames is >= 0 ? value.DroppedVideoFrames : null,
            TotalVideoFrames = value.TotalVideoFrames is >= 0 ? value.TotalVideoFrames : null,
            Sequence = NextSequence(),
            SchemaVersion = SyncTelemetrySchema.SchemaVersion,
            ModelVersion = SyncTelemetrySchema.ModelVersion
        };
        Add(_players, safe);
    }

    public void RecordEstimate(SyncEstimateTelemetry value)
    {
        if (!IsValidSlot(value.SlotId) || !TryNormalizeStamp(value.EstimatedAt, out var estimatedAt))
        {
            DropEvent();
            return;
        }

        var predictionLower = FiniteOrNull(value.PredictionLowerMilliseconds);
        var predictionUpper = FiniteOrNull(value.PredictionUpperMilliseconds);
        if (predictionLower is not null && predictionUpper is not null && predictionLower > predictionUpper)
        {
            DropEvent();
            return;
        }

        var safe = value with
        {
            SessionId = SessionId,
            EstimatorId = NormalizeAllowed(value.EstimatorId, EstimatorIds),
            SourceId = NormalizeOpaqueId("source", value.SourceId),
            Epoch = Math.Max(0, value.Epoch),
            EstimatedAt = estimatedAt,
            RawOffsetMilliseconds = FiniteOrNull(value.RawOffsetMilliseconds),
            FilteredOffsetMilliseconds = FiniteOrNull(value.FilteredOffsetMilliseconds),
            DriftMillisecondsPerSecond = FiniteOrNull(value.DriftMillisecondsPerSecond),
            StandardDeviationMilliseconds = NonNegativeFiniteOrNull(value.StandardDeviationMilliseconds),
            RejectionReason = NormalizeAllowed(value.RejectionReason, RejectionReasons, "none"),
            TimelineConfidence = ConfidenceOrNull(value.TimelineConfidence),
            BiasConfidence = ConfidenceOrNull(value.BiasConfidence),
            ControllabilityConfidence = ConfidenceOrNull(value.ControllabilityConfidence),
            ObservationId = NormalizeOpaqueId("observation", value.ObservationId),
            ProgressKey = NormalizeOpaqueId("progress", value.ProgressKey),
            EstimatorRole = NormalizeAllowed(value.EstimatorRole, EstimatorRoles),
            PredictionLowerMilliseconds = predictionLower,
            PredictionUpperMilliseconds = predictionUpper,
            InnovationMilliseconds = FiniteOrNull(value.InnovationMilliseconds),
            Sequence = NextSequence(),
            SchemaVersion = SyncTelemetrySchema.SchemaVersion,
            ModelVersion = SyncTelemetrySchema.ModelVersion
        };
        Add(_estimates, safe);
    }

    public void RecordDecision(SyncDecisionTelemetry value)
    {
        if (!IsValidSlot(value.SlotId) || !TryNormalizeStamp(value.DecidedAt, out var decidedAt))
        {
            DropEvent();
            return;
        }

        var safe = value with
        {
            SessionId = SessionId,
            DecisionId = NormalizeOpaqueId("decision", value.DecisionId),
            Epoch = Math.Max(0, value.Epoch),
            DecidedAt = decidedAt,
            ExistingController = SanitizeDecision(value.ExistingController),
            CandidateController = SanitizeDecision(value.CandidateController),
            CandidateIsShadowOnly = true,
            TickId = NormalizeOpaqueId("tick", value.TickId),
            Sequence = NextSequence(),
            SchemaVersion = SyncTelemetrySchema.SchemaVersion,
            ModelVersion = SyncTelemetrySchema.ModelVersion
        };
        Add(_decisions, safe);
    }

    public void RecordAction(SyncActionTelemetry value)
    {
        if (!IsValidSlot(value.SlotId) ||
            !TryNormalizeStamp(value.OccurredAt, out var occurredAt) ||
            !IsKnownActionStage(value.Stage))
        {
            DropEvent();
            return;
        }

        var safe = value with
        {
            SessionId = SessionId,
            CommandId = NormalizeOpaqueId("command", value.CommandId),
            Epoch = Math.Max(0, value.Epoch),
            CommandType = NormalizeAllowed(value.CommandType, CommandCodes),
            RequestedValue = FiniteOrNull(value.RequestedValue),
            Stage = NormalizeBucket(value.Stage),
            OccurredAt = occurredAt,
            DecisionId = NormalizeOpaqueId("decision", value.DecisionId),
            ObservedMediaTimeSeconds = FiniteOrNull(value.ObservedMediaTimeSeconds),
            ObservedPlaybackRate = FiniteOrNull(value.ObservedPlaybackRate),
            PostActionErrorMilliseconds = FiniteOrNull(value.PostActionErrorMilliseconds),
            OutcomeCode = NormalizeAllowed(value.OutcomeCode, ActionOutcomes),
            Sequence = NextSequence(),
            SchemaVersion = SyncTelemetrySchema.SchemaVersion,
            ModelVersion = SyncTelemetrySchema.ModelVersion
        };
        Add(_actions, safe);
    }

    public void RecordManualEvent(SyncManualEventTelemetry value)
    {
        if (!IsValidSlot(value.SlotId) ||
            !TryNormalizeStamp(value.OccurredAt, out var occurredAt) ||
            !IsKnownManualEventType(value.EventType))
        {
            DropEvent();
            return;
        }

        var safe = value with
        {
            SessionId = SessionId,
            EventId = NormalizeOpaqueId("manual-event", value.EventId),
            StableChannelHash = NormalizeOpaqueId("channel", value.StableChannelHash),
            BroadcastSessionHash = NormalizeOpaqueId("broadcast", value.BroadcastSessionHash),
            EventType = NormalizeBucket(value.EventType),
            OccurredAt = occurredAt,
            AlgorithmPriorMilliseconds = FiniteOrNull(value.AlgorithmPriorMilliseconds),
            PreviousUserResidualMilliseconds = FiniteOrNull(value.PreviousUserResidualMilliseconds),
            NewUserResidualMilliseconds = FiniteOrNull(value.NewUserResidualMilliseconds),
            EffectiveDelayMilliseconds = FiniteOrNull(value.EffectiveDelayMilliseconds),
            SuggestionId = NormalizeOpaqueId("suggestion", value.SuggestionId),
            ContextBucket = NormalizeAllowed(value.ContextBucket, ContextBuckets),
            SourceEpoch = Math.Max(0, value.SourceEpoch),
            Sequence = NextSequence(),
            SchemaVersion = SyncTelemetrySchema.SchemaVersion,
            ModelVersion = SyncTelemetrySchema.ModelVersion
        };
        Add(_manualEvents, safe);
    }

    public SyncTelemetrySnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            return new SyncTelemetrySnapshot(
                SyncTelemetrySchema.SchemaVersion,
                SyncTelemetrySchema.ModelVersion,
                true,
                _clock.Capture().Utc,
                _sessions.ToArray(),
                _network.ToArray(),
                _playlists.ToArray(),
                _players.ToArray(),
                _estimates.ToArray(),
                _decisions.ToArray(),
                _actions.ToArray(),
                _manualEvents.ToArray(),
                _droppedEventCount);
        }
    }

    public SyncTelemetrySummary CreateSummary()
    {
        lock (_gate)
        {
            return new SyncTelemetrySummary(
                SyncTelemetrySchema.SchemaVersion,
                SyncTelemetrySchema.ModelVersion,
                true,
                _sessions.Count,
                _network.Count,
                _playlists.Count,
                _players.Count,
                _estimates.Count,
                _decisions.Count,
                _actions.Count,
                _manualEvents.Count,
                _droppedEventCount);
        }
    }

    private void Add<T>(Queue<T> queue, T value)
    {
        lock (_gate)
        {
            if (queue.Count == _capacity)
            {
                queue.Dequeue();
                Interlocked.Increment(ref _droppedEventCount);
            }

            queue.Enqueue(value);
        }
    }

    private SyncUrlIdentity SanitizeIdentity(SyncUrlIdentity value)
    {
        var scheme = value.SchemeBucket is "http" or "https" or "other" or "unknown"
            ? value.SchemeBucket
            : "unknown";
        var host = _privacy.CreateHostBucket(value.HostBucket);
        var path = IsSafePathBucket(value.PathBucket) ? value.PathBucket.ToLowerInvariant() : "invalid";
        return new SyncUrlIdentity(
            scheme,
            host,
            path,
            _privacy.CreateOpaqueIdentity(
                "url-identity",
                $"{scheme}\0{host}\0{path}\0{value.PersistenceHash}"));
    }

    private SyncPolicyDecision SanitizeDecision(SyncPolicyDecision value)
    {
        var intervalStart = FiniteOrNull(value.CommonIntervalStartMilliseconds);
        var intervalEnd = FiniteOrNull(value.CommonIntervalEndMilliseconds);
        if (intervalStart is not null && intervalEnd is not null && intervalStart > intervalEnd)
        {
            intervalStart = null;
            intervalEnd = null;
        }

        return value with
        {
            PolicyId = NormalizeAllowed(value.PolicyId, PolicyIds),
            State = NormalizeAllowed(value.State, PolicyStates),
            TargetMediaTimeSeconds = FiniteOrNull(value.TargetMediaTimeSeconds),
            ProposedCommand = NormalizeAllowed(value.ProposedCommand, CommandCodes),
            ProposedValue = FiniteOrNull(value.ProposedValue),
            Reason = NormalizeAllowed(value.Reason, DecisionReasons),
            CommonIntervalStartMilliseconds = intervalStart,
            CommonIntervalEndMilliseconds = intervalEnd,
            CombinedUncertaintyMilliseconds = NonNegativeFiniteOrNull(
                value.CombinedUncertaintyMilliseconds)
        };
    }

    private static IReadOnlyList<MediaTimeRange> NormalizeRanges(IReadOnlyList<MediaTimeRange>? ranges)
    {
        if (ranges is null)
        {
            return [];
        }

        return ranges
            .Where(range => range is not null && range.IsValid)
            .Select(range => new MediaTimeRange(range.StartSeconds, range.EndSeconds))
            .Take(32)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeCodes(IReadOnlyList<string>? codes)
    {
        return codes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => NormalizeAllowed(code, WarningCodes))
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToArray() ?? [];
    }

    private static bool TryNormalizeStamp(
        SyncTelemetryClockSample value,
        out SyncTelemetryClockSample normalized)
    {
        if (value.MonotonicTicks < 0)
        {
            normalized = default!;
            return false;
        }

        normalized = value with { Utc = value.Utc.ToUniversalTime() };
        return true;
    }

    private static bool TryNormalizeOptionalStamp(
        SyncTelemetryClockSample? value,
        out SyncTelemetryClockSample? normalized)
    {
        if (value is null)
        {
            normalized = null;
            return true;
        }

        if (!TryNormalizeStamp(value, out var required))
        {
            normalized = null;
            return false;
        }

        normalized = required;
        return true;
    }

    private static bool AreOrdered(
        SyncTelemetryClockSample started,
        SyncTelemetryClockSample? headers,
        SyncTelemetryClockSample? completed)
    {
        if (headers is not null && headers.MonotonicTicks < started.MonotonicTicks)
        {
            return false;
        }

        var latest = headers?.MonotonicTicks ?? started.MonotonicTicks;
        return completed is null || completed.MonotonicTicks >= latest;
    }

    private static double? FiniteOrNull(double? value) =>
        value is { } number && double.IsFinite(number) ? number : null;

    private static double? NonNegativeFiniteOrNull(double? value) =>
        value is { } number && double.IsFinite(number) && number >= 0 ? number : null;

    private static double? ConfidenceOrNull(double? value) =>
        value is { } number && double.IsFinite(number)
            ? Math.Clamp(number, 0, 1)
            : null;

    private static bool IsValidSlot(int slotId) =>
        slotId is >= 1 and <= SlotProfileGroupMapping.MaxSlotCount;

    private static bool IsKnownActionStage(string? stage) =>
        stage?.Trim().ToLowerInvariant() is "issued" or "applied" or "verified" or "failed" or "timed-out";

    private static bool IsKnownManualEventType(string? eventType) =>
        eventType?.Trim().ToLowerInvariant() is
            "user-adjusted" or "suggestion-shown" or "suggestion-accepted" or
            "suggestion-rejected" or "suggestion-reverted" or "alignment-confirmed";

    private long NextSequence() => Interlocked.Increment(ref _nextSequence);

    private void DropEvent() => Interlocked.Increment(ref _droppedEventCount);

    private string NormalizeOpaqueId(string domain, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return _privacy.CreateOpaqueIdentity(domain, value);
    }

    private static bool IsSafePathBucket(string? value)
    {
        if (value is "root" or "invalid")
        {
            return true;
        }

        return value is not null && Regex.IsMatch(
            value,
            @"\Adepth-[0-9](?:\.[a-z0-9]{1,12}|\.other)\z",
            RegexOptions.CultureInvariant);
    }

    private static string NormalizeContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var mediaType = value.Split(';', 2)[0].Trim().ToLowerInvariant();
        return mediaType is
            "application/vnd.apple.mpegurl" or "application/x-mpegurl" or
            "audio/mpegurl" or "audio/x-mpegurl" or "video/mp2t" or
            "application/octet-stream"
            ? mediaType
            : "unknown";
    }

    private static string NormalizeAllowed(
        string? value,
        IReadOnlySet<string> allowed,
        string fallback = "unknown")
    {
        var normalized = NormalizeBucket(value).Replace('_', '-');
        return allowed.Contains(normalized) ? normalized : fallback;
    }

    private static IReadOnlySet<string> CodeSet(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private static string NormalizeBucket(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return new string(value.Trim().ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '/' or ':' or '+')
            .Take(160)
            .ToArray());
    }

    private sealed class DisabledSyncTelemetryRecorder : ISyncTelemetryRecorder
    {
        private static readonly SyncTelemetrySnapshot DisabledSnapshot = new(
            SyncTelemetrySchema.SchemaVersion,
            SyncTelemetrySchema.ModelVersion,
            false,
            DateTimeOffset.UnixEpoch,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            0);

        public static DisabledSyncTelemetryRecorder Instance { get; } = new();

        public bool IsEnabled => false;

        public string SessionId => "";

        public SyncUrlIdentity CreateUrlIdentity(string? rawUrl) => SyncTelemetryPrivacy.EmptyUrlIdentity;

        public void RecordSession(SyncSessionTelemetry value)
        {
        }

        public void RecordNetwork(SyncNetworkTelemetry value)
        {
        }

        public void RecordPlaylist(SyncPlaylistTelemetry value)
        {
        }

        public void RecordPlayer(SyncPlayerTelemetry value)
        {
        }

        public void RecordEstimate(SyncEstimateTelemetry value)
        {
        }

        public void RecordDecision(SyncDecisionTelemetry value)
        {
        }

        public void RecordAction(SyncActionTelemetry value)
        {
        }

        public void RecordManualEvent(SyncManualEventTelemetry value)
        {
        }

        public SyncTelemetrySnapshot CreateSnapshot() => DisabledSnapshot;

        public SyncTelemetrySummary CreateSummary() => SyncTelemetrySummary.Disabled;
    }
}

public sealed class SyncTelemetryPrivacy
{
    private const int MaximumSanitizedTextLength = 2048;
    private static readonly Regex UrlPattern = new(
        @"https?://[^\s<>\""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SecretHeaderPattern = new(
        @"(?im)\b(authorization|proxy-authorization|cookie|set-cookie|x-api-key)\s*[:=]\s*[^\r\n]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BearerPattern = new(
        @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BasicPattern = new(
        @"(?i)\bbasic\s+[A-Za-z0-9+/=]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SecretPairPattern = new(
        @"(?i)\b(access_?token|refresh_?token|token|auth|authorization|api_?key|key|signature|sig|signed|policy|credential|password|passwd|secret|expires|x-amz-[a-z0-9_-]+)\s*([:=])\s*([^\s&;,]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex JwtPattern = new(
        @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly byte[] _identityKey;

    public SyncTelemetryPrivacy(byte[]? identityKey = null)
    {
        _identityKey = identityKey is { Length: >= 16 }
            ? identityKey.ToArray()
            : RandomNumberGenerator.GetBytes(32);
    }

    public static SyncUrlIdentity EmptyUrlIdentity { get; } = new(
        "unknown",
        "unknown",
        "invalid",
        "");

    public SyncUrlIdentity CreateUrlIdentity(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return EmptyUrlIdentity;
        }

        string schemeBucket;
        string hostBucket;
        string pathBucket;
        string canonical;

        if (Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            schemeBucket = uri.Scheme is "http" or "https" ? uri.Scheme.ToLowerInvariant() : "other";
            hostBucket = BucketHost(uri.IdnHost);
            pathBucket = BucketPath(uri.AbsolutePath);
            var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
            canonical = $"{schemeBucket}://{uri.IdnHost.ToLowerInvariant()}{port}{uri.AbsolutePath}";
        }
        else
        {
            schemeBucket = "unknown";
            hostBucket = "unknown";
            pathBucket = "invalid";
            canonical = "invalid-url";
        }

        return new SyncUrlIdentity(
            schemeBucket,
            hostBucket,
            pathBucket,
            CreateKeyedHash(canonical));
    }

    public string CreateOpaqueIdentity(string domain, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalizedDomain = string.IsNullOrWhiteSpace(domain)
            ? "opaque"
            : domain.Trim().ToLowerInvariant();
        return CreateKeyedHash($"{normalizedDomain}\0{value}");
    }

    public string CreateHostBucket(string? host) => BucketHost(host);

    public string SanitizeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var sanitized = UrlPattern.Replace(value, match => FormatSafeUrl(match.Value));
        sanitized = SecretHeaderPattern.Replace(sanitized, match => $"{match.Groups[1].Value}=[redacted]");
        sanitized = BearerPattern.Replace(sanitized, "Bearer [redacted]");
        sanitized = BasicPattern.Replace(sanitized, "Basic [redacted]");
        sanitized = SecretPairPattern.Replace(
            sanitized,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}[redacted]");
        sanitized = JwtPattern.Replace(sanitized, "[redacted-jwt]");

        return sanitized.Length <= MaximumSanitizedTextLength
            ? sanitized
            : sanitized[..MaximumSanitizedTextLength] + "…[truncated]";
    }

    public string SanitizeDiagnosticText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var sanitized = UrlPattern.Replace(value, match => FormatDiagnosticUrl(match.Value));
        sanitized = SecretHeaderPattern.Replace(sanitized, match => $"{match.Groups[1].Value}=[redacted]");
        sanitized = BearerPattern.Replace(sanitized, "Bearer [redacted]");
        sanitized = BasicPattern.Replace(sanitized, "Basic [redacted]");
        sanitized = SecretPairPattern.Replace(
            sanitized,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}[redacted]");
        sanitized = JwtPattern.Replace(sanitized, "[redacted-jwt]");

        return sanitized.Length <= MaximumSanitizedTextLength
            ? sanitized
            : sanitized[..MaximumSanitizedTextLength] + "…[truncated]";
    }

    private string FormatSafeUrl(string rawUrl)
    {
        var identity = CreateUrlIdentity(rawUrl.TrimEnd('.', ',', ')', ']', '}'));
        return $"[url:{identity.HostBucket}:{identity.PathBucket}:{identity.PersistenceHash}]";
    }

    private static string FormatDiagnosticUrl(string rawUrl)
    {
        var trimmed = rawUrl.TrimEnd('.', ',', ')', ']', '}');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return "[redacted-url]";
        }

        var builder = new UriBuilder(uri.Scheme, uri.IdnHost)
        {
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Path = uri.AbsolutePath,
            Query = "",
            Fragment = "",
            UserName = "",
            Password = ""
        };
        return builder.Uri.AbsoluteUri;
    }

    private string CreateKeyedHash(string value)
    {
        using var hmac = new HMACSHA256(_identityKey);
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static string BucketHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "unknown";
        }

        var normalized = host.Trim('.').ToLowerInvariant();
        if (System.Net.IPAddress.TryParse(normalized, out _))
        {
            return "ip-address";
        }

        var labels = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2)
        {
            return normalized;
        }

        var suffixLength = labels[^1].Length == 2 && labels[^2].Length <= 3 ? 3 : 2;
        return string.Join('.', labels.TakeLast(Math.Min(labels.Length, suffixLength)));
    }

    private static string BucketPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "root";
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var extension = Path.GetExtension(segments[^1]).ToLowerInvariant();
        var extensionBucket = extension.Length is > 1 and <= 12 &&
                              extension.Skip(1).All(char.IsLetterOrDigit)
            ? extension
            : ".other";
        return $"depth-{Math.Min(segments.Length, 9)}{extensionBucket}";
    }
}
