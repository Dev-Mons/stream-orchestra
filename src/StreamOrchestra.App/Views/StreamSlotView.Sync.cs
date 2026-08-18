using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class StreamSlotView
{
    private static readonly JsonSerializerOptions SyncJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HlsTimelineParser _hlsTimelineParser = new();
    private readonly HlsPlaylistTracker _hlsPlaylistTracker = new();
    private readonly HlsRenditionCatalog _hlsRenditionCatalog = new();
    private readonly SyncTimelineEstimator _syncTimelineEstimator = new();
    private readonly ISyncTelemetryRecorder _syncTelemetryRecorder = SyncTelemetryRecorder.Disabled;
    private string _syncHlsSessionIdentity = Guid.NewGuid().ToString("N");
    private readonly Dictionary<ulong, SyncPlayerCandidate> _syncPlayerCandidates = [];
    private readonly Dictionary<string, PendingSyncCommand> _pendingSyncCommands = new(StringComparer.Ordinal);
    private string? _syncBridgeScriptId;
    private int _syncNavigationGeneration;
    private CoreWebView2Frame? _syncCommandFrame;
    private Point? _lastSyncPopupAnchor;
    private SyncBadgeState? _lastSyncBadgeState;
    private string _syncBroadcasterName = "";
    private int _syncBroadcasterNameConfidence;

    public SyncMemberSnapshot? LatestSyncSnapshot { get; private set; }

    public TimelineObservation? LatestTimeline { get; private set; }

    public SyncEstimatorShadowResult? LatestEstimatorShadow { get; private set; }

    public string SyncQualityBucket => string.IsNullOrWhiteSpace(_preferredQualityKey)
        ? "unknown"
        : _preferredQualityKey;

    public string SyncBroadcastSessionIdentity => _syncHlsSessionIdentity;

    public string SyncDisplayName => _syncBroadcasterNameConfidence >= 2 &&
                                     _navigationService.IsMeaningfulDisplayName(_syncBroadcasterName)
        ? _syncBroadcasterName
        : _navigationService.IsMeaningfulDisplayName(CurrentStreamName)
            ? CurrentStreamName
            : _navigationService.IsMeaningfulDisplayName(_syncBroadcasterName)
                ? _syncBroadcasterName
                : "방송 정보 확인 중";

    private async Task InitializeStreamSyncAsync()
    {
        if (_syncBridgeScriptId is not null || Browser.CoreWebView2 is null)
        {
            return;
        }

        _syncBridgeScriptId = await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            CreateStreamSyncBridgeScript());
        Browser.CoreWebView2.NavigationStarting += (_, _) => ResetStreamSyncObservations();
        Browser.CoreWebView2.WebMessageReceived += (_, args) =>
            ReceiveSyncMessage(frame: null, args);
        Browser.CoreWebView2.FrameCreated += (_, args) => WireSyncFrame(args.Frame);
        Browser.CoreWebView2.WebResourceResponseReceived += ReceiveHlsResponse;
    }

    private void ResetStreamSyncObservations()
    {
        _syncNavigationGeneration++;
        _syncHlsSessionIdentity = Guid.NewGuid().ToString("N");
        _hlsRenditionCatalog.Clear();
        _syncTimelineEstimator.Reset("primary-video");
        _syncPlayerCandidates.Clear();
        _syncCommandFrame = null;
        CompletePendingSyncCommands("navigation-reset");
        LatestSyncSnapshot = null;
        LatestTimeline = null;
        LatestEstimatorShadow = null;
        _syncBroadcasterName = "";
        _syncBroadcasterNameConfidence = 0;
    }

    private void WireSyncFrame(CoreWebView2Frame frame)
    {
        var frameId = frame.FrameId;
        frame.WebMessageReceived += (_, args) => ReceiveSyncMessage(frame, args);
        frame.FrameCreated += (_, args) => WireSyncFrame(args.Frame);
        frame.Destroyed += (_, _) =>
        {
            _syncPlayerCandidates.Remove(frameId);
            if (ReferenceEquals(_syncCommandFrame, frame))
            {
                _syncCommandFrame = null;
                SelectPrimarySyncCandidate();
            }
        };
    }

    private void ReceiveSyncMessage(CoreWebView2Frame? frame, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsTrustedSoopSource(args.Source))
        {
            return;
        }

        SyncStatusMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<SyncStatusMessage>(args.WebMessageAsJson, SyncJsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (message is null)
        {
            return;
        }

        if (message.Type.Equals("stream-sync-command-result", StringComparison.OrdinalIgnoreCase))
        {
            ReceiveSyncCommandResult(message);
            return;
        }

        if (!message.Type.Equals("stream-sync-status", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        UpdateSyncBroadcasterName(message.StreamName, message.StreamNameConfidence);
        var now = DateTimeOffset.UtcNow;
        var hostReceivedMonotonic = Stopwatch.GetTimestamp();
        var bufferedRanges = NormalizeBridgeRanges(message.BufferedRanges);
        var seekableRanges = NormalizeBridgeRanges(message.SeekableRanges);
        var currentBufferedRange = SyncMediaRangePolicy.FindContainingRange(
            bufferedRanges,
            message.CurrentTime);
        var currentSeekableRange = SyncMediaRangePolicy.FindContainingRange(
            seekableRanges,
            message.CurrentTime);
        var snapshot = SyncPlayerMediaClock.Resolve(new SyncMemberSnapshot
        {
            HasVideo = message.HasVideo,
            IsSoop = true,
            CurrentTime = message.CurrentTime,
            PlaybackRate = message.PlaybackRate,
            Paused = message.Paused,
            ReadyState = message.ReadyState,
            Buffering = message.Buffering,
            BufferSec = currentBufferedRange is null
                ? null
                : Math.Max(0, currentBufferedRange.EndSeconds - message.CurrentTime),
            SeekableStart = seekableRanges.FirstOrDefault()?.StartSeconds,
            SeekableEnd = seekableRanges.LastOrDefault()?.EndSeconds,
            BufferedRanges = bufferedRanges,
            SeekableRanges = seekableRanges,
            CurrentBufferedRange = currentBufferedRange,
            CurrentSeekableRange = currentSeekableRange,
            Seeking = message.Seeking,
            NetworkState = message.NetworkState,
            EventKind = ParsePlayerEventKind(message.EventKind),
            PageSampleMonotonicMilliseconds = FiniteOrNull(message.PageSampleMonotonicMilliseconds),
            PageEventMonotonicMilliseconds = FiniteOrNull(message.PageEventMonotonicMilliseconds),
            HostReceivedMonotonicTicks = hostReceivedMonotonic,
            HostMonotonicFrequency = Stopwatch.Frequency,
            UsedVideoFrameCallback = message.UsedVideoFrameCallback,
            PresentedMediaTimeSeconds = FiniteOrNull(message.PresentedMediaTimeSeconds),
            FrameAgeMilliseconds = FiniteOrNull(message.FrameAgeMilliseconds),
            ExpectedDisplayMonotonicMilliseconds = FiniteOrNull(
                message.ExpectedDisplayMonotonicMilliseconds),
            FrameProcessingDurationSeconds = FiniteOrNull(message.FrameProcessingDurationSeconds),
            PresentedFrames = NonNegativeOrNull(message.PresentedFrames),
            DroppedVideoFrames = NonNegativeOrNull(message.DroppedVideoFrames),
            TotalVideoFrames = NonNegativeOrNull(message.TotalVideoFrames),
            PlayerProgressHealthy = message.PlayerProgressHealthy,
            PlayerEventSequence = Math.Max(0, message.PlayerEventSequence),
            LastBufferEventAt = message.LastBufferEventAt,
            ViewportArea = Math.Max(0, message.Width) * Math.Max(0, message.Height),
            ObservedAtUtc = now
        });
        var key = frame?.FrameId ?? 0;
        _syncPlayerCandidates[key] = new SyncPlayerCandidate(frame, snapshot);
        SelectPrimarySyncCandidate();
    }

    private void ReceiveSyncCommandResult(SyncStatusMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.CommandId) ||
            !_pendingSyncCommands.TryGetValue(message.CommandId, out var pending))
        {
            return;
        }

        var stage = message.CommandStage?.Trim().ToLowerInvariant();
        if (stage == "applied")
        {
            pending.AppliedAtUtc ??= DateTimeOffset.UtcNow;
            pending.AppliedMonotonicTicks ??= Stopwatch.GetTimestamp();
            return;
        }

        _pendingSyncCommands.Remove(message.CommandId);
        var observedMediaTime = FiniteOrNull(message.CurrentTime);
        var observedRate = FiniteOrNull(message.PlaybackRate);
        var wasVerified = stage == "verified" &&
                          pending.AppliedAtUtc is not null &&
                          pending.AppliedMonotonicTicks is not null &&
                          VerifyCommandResult(
            pending.Command,
            observedMediaTime,
            observedRate,
            message.Paused);
        pending.Completion.TrySetResult(new SyncCommandResult
        {
            CommandId = message.CommandId,
            Stage = wasVerified ? SyncCommandStage.Verified : SyncCommandStage.Failed,
            WasApplied = message.CommandApplied,
            WasVerified = wasVerified,
            ObservedMediaTimeSeconds = observedMediaTime,
            ObservedPlaybackRate = observedRate,
            ObservedPaused = message.Paused,
            OutcomeCode = wasVerified
                ? "verified"
                : stage == "failed"
                    ? NormalizeCommandOutcome(message.CommandOutcome)
                    : "verification-mismatch",
            IssuedAtUtc = pending.IssuedAtUtc,
            AppliedAtUtc = pending.AppliedAtUtc,
            VerifiedAtUtc = wasVerified ? DateTimeOffset.UtcNow : null,
            IssuedMonotonicTicks = pending.IssuedMonotonicTicks,
            AppliedMonotonicTicks = pending.AppliedMonotonicTicks,
            VerifiedMonotonicTicks = wasVerified ? Stopwatch.GetTimestamp() : null
        });
    }

    private void CompletePendingSyncCommands(string outcome)
    {
        foreach (var pending in _pendingSyncCommands.Values)
        {
            pending.Completion.TrySetResult(new SyncCommandResult
            {
                CommandId = pending.Command.CommandId,
                Stage = SyncCommandStage.Failed,
                WasApplied = pending.AppliedAtUtc is not null,
                OutcomeCode = outcome,
                IssuedAtUtc = pending.IssuedAtUtc,
                AppliedAtUtc = pending.AppliedAtUtc,
                IssuedMonotonicTicks = pending.IssuedMonotonicTicks,
                AppliedMonotonicTicks = pending.AppliedMonotonicTicks
            });
        }

        _pendingSyncCommands.Clear();
    }

    private static IReadOnlyList<MediaTimeRange> NormalizeBridgeRanges(double[][]? ranges)
    {
        if (ranges is null)
        {
            return [];
        }

        return SyncMediaRangePolicy.Normalize(ranges
            .Where(range => range is { Length: >= 2 })
            .Select(range => new MediaTimeRange(range[0], range[1])));
    }

    private static SyncPlayerEventKind ParsePlayerEventKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "frame" => SyncPlayerEventKind.Frame,
            "waiting" => SyncPlayerEventKind.Waiting,
            "stalled" => SyncPlayerEventKind.Stalled,
            "error" => SyncPlayerEventKind.Error,
            "seeking" => SyncPlayerEventKind.Seeking,
            "seeked" => SyncPlayerEventKind.Seeked,
            "ratechange" => SyncPlayerEventKind.RateChange,
            _ => SyncPlayerEventKind.Sample
        };

    private static double? FiniteOrNull(double? value) =>
        value is { } number && double.IsFinite(number) ? number : null;

    private static long? NonNegativeOrNull(long? value) => value is >= 0 ? value : null;

    private static bool VerifyCommandResult(
        SyncCommand command,
        double? observedMediaTime,
        double? observedRate,
        bool paused) => command.Type switch
    {
        SyncCommandType.Seek => command.Value is { } target &&
                                observedMediaTime is { } mediaTime &&
                                Math.Abs(mediaTime - target) <= 0.5,
        SyncCommandType.SetRate => command.Value is { } target &&
                                   observedRate is { } rate &&
                                   Math.Abs(rate - target) <= 0.005,
        SyncCommandType.ResetRate => observedRate is { } rate && Math.Abs(rate - 1) <= 0.005,
        SyncCommandType.Pause => paused,
        SyncCommandType.Resume => !paused,
        _ => false
    };

    private static string NormalizeCommandOutcome(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "video-unavailable" => "video-unavailable",
        "invalid-value" => "invalid-value",
        "invalid-range" => "invalid-range",
        "rate-mismatch" => "rate-mismatch",
        "rate-not-progressing" => "rate-not-progressing",
        "position-mismatch" => "position-mismatch",
        "seeked-timeout" => "seeked-timeout",
        "pause-mismatch" => "pause-mismatch",
        "resume-mismatch" => "resume-mismatch",
        "resume-rejected" => "resume-rejected",
        "unsupported-command" => "unsupported-command",
        "command-exception" => "command-exception",
        _ => "command-failed"
    };

    private void UpdateSyncBroadcasterName(string? streamName, int confidence)
    {
        if (!_navigationService.IsMeaningfulDisplayName(streamName))
        {
            return;
        }

        var normalized = string.Join(
            " ",
            streamName!.Trim().Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        var normalizedConfidence = Math.Clamp(confidence, 0, 3);
        if (_syncBroadcasterName.Equals(normalized, StringComparison.Ordinal))
        {
            _syncBroadcasterNameConfidence = Math.Max(
                _syncBroadcasterNameConfidence,
                normalizedConfidence);
            return;
        }

        if (!string.IsNullOrEmpty(_syncBroadcasterName) &&
            normalizedConfidence <= _syncBroadcasterNameConfidence)
        {
            return;
        }

        _syncBroadcasterName = normalized;
        _syncBroadcasterNameConfidence = normalizedConfidence;
        PlaybackStateChanged?.Invoke(this);
    }

    private void SelectPrimarySyncCandidate()
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2);
        foreach (var staleKey in _syncPlayerCandidates
                     .Where(item => item.Value.Snapshot.ObservedAtUtc < cutoff)
                     .Select(item => item.Key)
                     .ToArray())
        {
            _syncPlayerCandidates.Remove(staleKey);
        }

        var primary = _syncPlayerCandidates.Values
            .Where(candidate => candidate.Snapshot.HasVideo)
            .OrderByDescending(candidate => candidate.Snapshot.ViewportArea)
            .ThenByDescending(candidate => candidate.Snapshot.ObservedAtUtc)
            .FirstOrDefault();
        if (primary is null)
        {
            return;
        }

        LatestSyncSnapshot = primary.Snapshot;
        _syncCommandFrame = primary.Frame;
    }

    private async void ReceiveHlsResponse(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (args.Response.StatusCode is < 200 or >= 300)
        {
            return;
        }

        var generation = _syncNavigationGeneration;
        var headersReceivedMonotonic = Stopwatch.GetTimestamp();
        DateTimeOffset? responseDateUtc = null;
        string? contentType = null;
        try
        {
            contentType = args.Response.Headers.GetHeader("Content-Type");
            var dateHeader = args.Response.Headers.GetHeader("Date");
            if (DateTimeOffset.TryParse(
                    dateHeader,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedDate))
            {
                responseDateUtc = parsedDate.ToUniversalTime();
            }
        }
        catch
        {
            // Response metadata is optional. The playlist body is still parsed conservatively.
        }

        if (!HlsTimelineParser.IsHlsPlaylistResource(args.Request.Uri, contentType))
        {
            return;
        }

        try
        {
            await using var content = await args.Response.GetContentAsync();
            if (content is null)
            {
                return;
            }

            using var reader = new StreamReader(content);
            var playlist = await reader.ReadToEndAsync();
            var observedAt = DateTimeOffset.UtcNow;
            var bodyCompletedMonotonic = Stopwatch.GetTimestamp();
            var parseResult = await Task.Run(() =>
                _hlsTimelineParser.ParsePlaylist(new HlsPlaylistParseRequest(
                    playlist,
                    args.Request.Uri,
                    contentType,
                    responseDateUtc,
                    observedAt)));
            if (parseResult is null || generation != _syncNavigationGeneration)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() => UpdateHlsObservation(
                parseResult,
                generation,
                headersReceivedMonotonic,
                bodyCompletedMonotonic));
        }
        catch
        {
            // HLS responses can expire or be consumed by the player before inspection completes.
        }
    }

    private void UpdateHlsObservation(
        HlsPlaylistParseResult parseResult,
        int generation,
        long headersReceivedMonotonic,
        long bodyCompletedMonotonic)
    {
        if (parseResult.Document.Kind == HlsPlaylistKind.Master)
        {
            _hlsRenditionCatalog.Register(parseResult.Document);
            return;
        }

        parseResult = _hlsRenditionCatalog.Apply(parseResult);
        var document = parseResult.Document;
        var progressKey = parseResult.ProgressKey;
        var observation = parseResult.TimelineCandidate;
        if (generation != _syncNavigationGeneration ||
            document.Kind != HlsPlaylistKind.Media ||
            progressKey is null ||
            observation is null ||
            document.RenditionKind != HlsRenditionKind.Video)
        {
            return;
        }

        const string laneIdentity = "primary-video";
        var tracking = _hlsPlaylistTracker.Observe(new HlsPlaylistTrackingContext
        {
            SlotId = this.SlotId,
            NavigationGeneration = generation,
            SessionIdentity = _syncHlsSessionIdentity,
            TimelineLaneIdentity = laneIdentity,
            PlaylistIdentityHash = document.PlaylistIdentity.PersistenceHash,
            RenditionKind = document.RenditionKind,
            SourceIdentity = $"{document.PlaylistIdentity.HostBucket}:pdt",
            Document = document,
            ProgressKey = progressKey,
            ObservedMonotonicTicks = bodyCompletedMonotonic,
            MonotonicFrequency = Stopwatch.Frequency
        });
        var trackedObservation = observation with
        {
            ObservedMonotonicTicks = bodyCompletedMonotonic,
            MonotonicFrequency = Stopwatch.Frequency,
            StaleAfterSeconds = Math.Max(
                0.001,
                (document.TargetDurationSeconds ?? 6) * 1.5),
            SourceEpoch = tracking.SourceEpoch,
            IsEpochStable = tracking.IsEpochStable,
            IndependentEvidenceCount = tracking.IndependentEvidenceCount,
            NetworkCapability = SyncNetworkObservationCapability.WebResourceResponseReduced,
            ResponseHeadersMonotonicTicks = headersReceivedMonotonic,
            BodyCompletedMonotonicTicks = bodyCompletedMonotonic
        };
        UpdateShadowEstimator(trackedObservation, tracking.Disposition);
        if (tracking.IsEstimatorEvidence)
        {
            UpdateTimelineObservation(trackedObservation);
        }
    }

    private void UpdateShadowEstimator(
        TimelineObservation observation,
        HlsProgressDisposition disposition)
    {
        if (LatestSyncSnapshot is not { SeekableEnd: { } seekableEnd } snapshot ||
            observation.ObservedMonotonicTicks <= 0 ||
            observation.MonotonicFrequency <= 0 ||
            snapshot.HostReceivedMonotonicTicks <= 0 ||
            snapshot.HostMonotonicFrequency != observation.MonotonicFrequency ||
            observation.ObservedMonotonicTicks < snapshot.HostReceivedMonotonicTicks)
        {
            return;
        }

        var snapshotAgeSeconds =
            (observation.ObservedMonotonicTicks - snapshot.HostReceivedMonotonicTicks) /
            (double)observation.MonotonicFrequency;
        if (!double.IsFinite(snapshotAgeSeconds) || snapshotAgeSeconds > 2)
        {
            return;
        }

        var projectedSeekableEnd = seekableEnd +
                                   (snapshot.Paused ? 0 : snapshotAgeSeconds * snapshot.PlaybackRate);
        var rawOffsetMilliseconds = (observation.EdgeUtc - DateTimeOffset.UnixEpoch).TotalMilliseconds -
                                    projectedSeekableEnd * 1000;
        if (!double.IsFinite(rawOffsetMilliseconds))
        {
            return;
        }

        var controllabilityScore = snapshot.PlayerProgressHealthy &&
                                   !snapshot.Seeking &&
                                   snapshot.MediaClockSource ==
                                   SyncPlayerClockSource.RequestVideoFrameCallback &&
                                   snapshot.SeekableRanges.Count > 0
            ? 1
            : 0;
        var estimatorObservation = new SyncEstimatorObservation
        {
            LaneIdentity = "primary-video",
            SourceIdentity = $"{observation.PlaylistIdentityHash}:{observation.Source}",
            SourceEpoch = observation.SourceEpoch,
            ObservationIdentity = $"{observation.ProgressKeyHash}:{observation.ObservedMonotonicTicks}",
            ProgressKeyIdentity = observation.ProgressKeyHash,
            RawOffsetMilliseconds = rawOffsetMilliseconds,
            ActiveBaselineOffsetMilliseconds = rawOffsetMilliseconds,
            ObservedAtUtc = observation.ObservedAtUtc,
            ObservedMonotonicTicks = observation.ObservedMonotonicTicks,
            MonotonicFrequency = observation.MonotonicFrequency,
            Disposition = ToEstimatorDisposition(disposition),
            IsEpochStable = observation.IsEpochStable,
            IndependentEvidenceCount = observation.IndependentEvidenceCount,
            ControllabilityScore = controllabilityScore
        };
        LatestEstimatorShadow = _syncTimelineEstimator.Observe(estimatorObservation);
        if (!_syncTelemetryRecorder.IsEnabled)
        {
            return;
        }

        RecordEstimatorTelemetry(
            observation,
            estimatorObservation,
            LatestEstimatorShadow.ActiveBaseline,
            "active-baseline");
        RecordEstimatorTelemetry(
            observation,
            estimatorObservation,
            LatestEstimatorShadow.KalmanCandidate,
            "shadow-candidate");
        RecordEstimatorTelemetry(
            observation,
            estimatorObservation,
            LatestEstimatorShadow.HuberCandidate,
            "shadow-candidate");
    }

    private void RecordEstimatorTelemetry(
        TimelineObservation observation,
        SyncEstimatorObservation raw,
        SyncEstimatorEstimate estimate,
        string estimatorRole)
    {
        _syncTelemetryRecorder.RecordEstimate(new SyncEstimateTelemetry(
            _syncTelemetryRecorder.SessionId,
            SlotId,
            estimate.EstimatorId,
            raw.SourceIdentity,
            observation.SourceEpoch,
            new SyncTelemetryClockSample(
                observation.ObservedAtUtc,
                observation.ObservedMonotonicTicks),
            raw.RawOffsetMilliseconds,
            estimate.OffsetMilliseconds,
            estimate.DriftMillisecondsPerSecond,
            estimate.StandardDeviationMilliseconds,
            estimate.ObservationAccepted,
            estimate.RejectionReason,
            estimate.TimelineScore,
            estimate.BiasScore,
            estimate.ControllabilityScore,
            raw.ObservationIdentity,
            raw.ProgressKeyIdentity,
            estimatorRole,
            estimate.PredictionLowerMilliseconds,
            estimate.PredictionUpperMilliseconds,
            estimate.InnovationMilliseconds,
            _syncNavigationGeneration,
            ChangePointSuspected: LatestEstimatorShadow?.ChangePointSuspected == true));
    }

    private static SyncEstimatorObservationDisposition ToEstimatorDisposition(
        HlsProgressDisposition disposition) => disposition switch
    {
        HlsProgressDisposition.NewEvidence => SyncEstimatorObservationDisposition.NewEvidence,
        HlsProgressDisposition.Duplicate => SyncEstimatorObservationDisposition.Duplicate,
        HlsProgressDisposition.Stale => SyncEstimatorObservationDisposition.Stale,
        HlsProgressDisposition.Rollback => SyncEstimatorObservationDisposition.Rollback,
        _ => SyncEstimatorObservationDisposition.Invalid
    };

    private void UpdateTimelineObservation(TimelineObservation observation)
    {
        var previous = LatestTimeline;
        if (previous is not null &&
            !previous.PlaylistIdentityHash.Equals(observation.PlaylistIdentityHash, StringComparison.Ordinal))
        {
            var selectionHold = TimeSpan.FromSeconds(Math.Max(3, previous.SegmentDurationSec * 1.5));
            if (observation.ObservedAtUtc - previous.ObservedAtUtc <= selectionHold)
            {
                return;
            }
        }

        if (previous is not null &&
            previous.PlaylistIdentityHash.Equals(observation.PlaylistIdentityHash, StringComparison.Ordinal) &&
            previous.SourceEpoch == observation.SourceEpoch)
        {
            if (observation.ObservedMonotonicTicks <= previous.ObservedMonotonicTicks ||
                observation.EdgeUtc <= previous.EdgeUtc)
            {
                return;
            }

            var elapsedSeconds = (observation.ObservedMonotonicTicks - previous.ObservedMonotonicTicks) /
                                 (double)Stopwatch.Frequency;
            var maximumExpectedJumpMilliseconds = Math.Max(
                8000,
                Math.Max(previous.SegmentDurationSec, observation.SegmentDurationSec) * 3000);
            var projectedPreviousEdge = previous.EdgeUtc.AddSeconds(elapsedSeconds);
            if (Math.Abs((observation.EdgeUtc - projectedPreviousEdge).TotalMilliseconds) >
                maximumExpectedJumpMilliseconds)
            {
                return;
            }
        }

        LatestTimeline = observation;
    }

    public async Task<SyncCommandResult> ExecuteSyncCommandAsync(SyncCommand command)
    {
        if (Browser.CoreWebView2 is null)
        {
            return SyncCommandResult.Failed(command.CommandId, "webview-unavailable");
        }

        var action = command.Type switch
        {
            SyncCommandType.SetRate => "rate",
            SyncCommandType.Seek => "seek",
            SyncCommandType.Pause => "pause",
            SyncCommandType.Resume => "resume",
            SyncCommandType.ResetRate => "reset-rate",
            _ => ""
        };
        if (string.IsNullOrEmpty(action))
        {
            return SyncCommandResult.Failed(command.CommandId, "unsupported-command");
        }

        var completion = new TaskCompletionSource<SyncCommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingSyncCommand(
            command,
            completion,
            DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp());
        _pendingSyncCommands[command.CommandId] = pending;

        var json = JsonSerializer.Serialize(new
        {
            type = "stream-sync-command",
            commandId = command.CommandId,
            action,
            value = command.Value
        });

        try
        {
            if (_syncCommandFrame is { } frame && frame.IsDestroyed() == 0)
            {
                frame.PostWebMessageAsJson(json);
            }
            else
            {
                Browser.CoreWebView2.PostWebMessageAsJson(json);
            }
        }
        catch
        {
            _pendingSyncCommands.Remove(command.CommandId);
            return SyncCommandResult.Failed(command.CommandId, "delivery-failed");
        }

        var completed = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMilliseconds(1800)));
        if (completed == completion.Task)
        {
            return await completion.Task;
        }

        _pendingSyncCommands.Remove(command.CommandId);
        return new SyncCommandResult
        {
            CommandId = command.CommandId,
            Stage = SyncCommandStage.TimedOut,
            OutcomeCode = "timed-out",
            IssuedAtUtc = pending.IssuedAtUtc,
            AppliedAtUtc = pending.AppliedAtUtc,
            IssuedMonotonicTicks = pending.IssuedMonotonicTicks,
            AppliedMonotonicTicks = pending.AppliedMonotonicTicks
        };
    }

    public void SetSyncBadge(SyncBadgeState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetSyncBadge(state));
            return;
        }

        if (!state.IsVisible)
        {
            SyncStatusPopup.IsOpen = false;
            _lastSyncPopupAnchor = null;
            _lastSyncBadgeState = null;
            return;
        }

        if (_lastSyncBadgeState is { } previous &&
            previous.RuntimeState == state.RuntimeState &&
            previous.TimelineSource == state.TimelineSource &&
            previous.Text.Equals(state.Text, StringComparison.Ordinal) &&
            SyncStatusPopup.IsOpen)
        {
            return;
        }

        _lastSyncBadgeState = state;
        SyncStatusTextBlock.Text = state.Text;
        var color = state.RuntimeState switch
        {
            SyncRuntimeState.Running => Color.FromRgb(31, 122, 78),
            SyncRuntimeState.Recovering => Color.FromRgb(180, 58, 58),
            SyncRuntimeState.Preparing or SyncRuntimeState.Waiting or SyncRuntimeState.Degraded =>
                Color.FromRgb(154, 105, 24),
            _ => Color.FromRgb(62, 73, 86)
        };
        SyncStatusBorder.Background = new SolidColorBrush(Color.FromArgb(225, color.R, color.G, color.B));
        SyncStatusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 226, 232, 240));
        SyncStatusPopup.IsOpen = true;
        RefreshSyncBadgePlacement(forceRefresh: true);
        QueueRefreshOpenOverlayPlacement();
    }

    private void RefreshSyncBadgePlacement(bool forceRefresh)
    {
        if (!IsLoaded || !SyncStatusPopup.IsOpen)
        {
            return;
        }

        var anchor = GetSlotScreenPoint(12, 12);
        if (!forceRefresh && !HasPointChanged(_lastSyncPopupAnchor, anchor))
        {
            return;
        }

        _lastSyncPopupAnchor = anchor;
        NudgePopupPlacement(SyncStatusPopup);
        SetPopupScreenPosition(SyncStatusPopup, anchor);
    }

    private static bool IsTrustedSoopSource(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("sooplive.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sooplive.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("sooplive.co.kr", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sooplive.co.kr", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateStreamSyncBridgeScript()
    {
        return """
(() => {
  if (window.__streamOrchestraSyncBridgeInstalled) return;

  const host = location.hostname.toLowerCase();
  const isSoop = host === "sooplive.com" || host.endsWith(".sooplive.com") ||
    host === "sooplive.co.kr" || host.endsWith(".sooplive.co.kr");
  if (!isSoop || !window.chrome?.webview) return;

  window.__streamOrchestraSyncBridgeInstalled = true;
  const videoStates = new WeakMap();

  function stateFor(video) {
    let state = videoStates.get(video);
    if (!state) {
      state = {
        lastBufferEventAt: 0,
        lastPlayerEventAt: 0,
        eventSequence: 0,
        latestFrameMetadata: null,
        previousSampleMediaTime: null,
        previousPresentedFrames: null
      };
      videoStates.set(video, state);
    }
    return state;
  }

  function mediaRanges(ranges) {
    const result = [];
    for (let index = 0; index < Math.min(ranges.length, 32); index += 1) {
      result.push([ranges.start(index), ranges.end(index)]);
    }
    return result;
  }

  function containingRange(ranges, mediaTime) {
    return ranges.find(range => mediaTime >= range[0] && mediaTime <= range[1]) || null;
  }

  function primaryVideo() {
    return Array.from(document.querySelectorAll("video"))
      .sort((left, right) => {
        const leftScore = (left.paused ? 0 : 1e12) + left.clientWidth * left.clientHeight;
        const rightScore = (right.paused ? 0 : 1e12) + right.clientWidth * right.clientHeight;
        return rightScore - leftScore;
      })[0] || null;
  }

  function attach(video) {
    if (!video || video.__streamOrchestraSyncWired) return;
    video.__streamOrchestraSyncWired = true;
    const state = stateFor(video);
    for (const eventName of ["waiting", "stalled", "error", "seeking", "seeked", "ratechange"]) {
      video.addEventListener(eventName, () => {
        state.lastPlayerEventAt = performance.now();
        state.eventSequence += 1;
        if (eventName === "waiting" || eventName === "stalled" || eventName === "error") {
          state.lastBufferEventAt = Date.now();
        }
        postStatus(eventName, video);
      }, true);
    }

    if (typeof video.requestVideoFrameCallback === "function") {
      const onFrame = (now, metadata) => {
        state.latestFrameMetadata = {
          callbackNow: now,
          mediaTime: metadata.mediaTime,
          expectedDisplayTime: metadata.expectedDisplayTime,
          processingDuration: metadata.processingDuration,
          presentedFrames: metadata.presentedFrames
        };
        if (video.isConnected) video.requestVideoFrameCallback(onFrame);
      };
      video.requestVideoFrameCallback(onFrame);
    }
  }

  function cleanStreamName(value) {
    const normalized = String(value || "").replace(/\s+/g, " ").trim();
    const withoutServiceName = normalized
      .replace(/\s*(?:[-|·]\s*)?(?:SOOP|AfreecaTV)\s*$/i, "")
      .trim();
    if (!withoutServiceName ||
        /^(?:SOOP|embed|about:blank)$/i.test(withoutServiceName) ||
        /^\d{6,}$/.test(withoutServiceName) ||
        /^https?:\/\//i.test(withoutServiceName) ||
        withoutServiceName.length > 80) {
      return "";
    }

    return withoutServiceName;
  }

  function broadcasterIdentity() {
    const selectors = [
      ".broadcast_information .nickname",
      ".broadcast_info .nickname",
      ".broadcast_information .bj_name",
      ".broadcast_info .bj_name",
      "#player_info .nickname",
      "#bj_nick",
      ".bj_name",
      "[class*='broadcast'] [class*='nickname']",
      "[class*='station'] [class*='nickname']"
    ];
    for (const selector of selectors) {
      const name = cleanStreamName(document.querySelector(selector)?.textContent);
      if (name) return { name, confidence: 3 };
    }

    for (const selector of ["meta[name='author']", "meta[property='og:profile:username']"]) {
      const name = cleanStreamName(document.querySelector(selector)?.content);
      if (name) return { name, confidence: 3 };
    }

    for (const script of document.querySelectorAll("script[type='application/ld+json']")) {
      try {
        const values = JSON.parse(script.textContent || "null");
        const items = Array.isArray(values) ? values : [values];
        for (const item of items) {
          const name = cleanStreamName(item?.author?.name || item?.creator?.name);
          if (name) return { name, confidence: 3 };
        }
      } catch {}
    }

    const openGraphTitle = cleanStreamName(
      document.querySelector("meta[property='og:title']")?.content);
    if (openGraphTitle) return { name: openGraphTitle, confidence: 2 };

    const title = cleanStreamName(document.title);
    return { name: title, confidence: title ? 1 : 0 };
  }

  function postStatus(eventKind = "sample", sourceVideo = null) {
    const selectedVideo = primaryVideo();
    if (sourceVideo && sourceVideo !== selectedVideo) return;
    const video = sourceVideo || selectedVideo;
    const identity = broadcasterIdentity();
    attach(video);
    if (!video) {
      window.chrome.webview.postMessage({
        type: "stream-sync-status",
        hasVideo: false,
        eventKind,
        pageSampleMonotonicMilliseconds: performance.now(),
        streamName: identity.name,
        streamNameConfidence: identity.confidence
      });
      return;
    }

    const buffered = mediaRanges(video.buffered);
    const seekable = mediaRanges(video.seekable);
    const state = stateFor(video);
    const pageSampleNow = performance.now();
    const frameAgeMilliseconds = state.latestFrameMetadata
      ? pageSampleNow - state.latestFrameMetadata.callbackNow
      : null;
    const frameMetadata = state.latestFrameMetadata &&
      frameAgeMilliseconds >= 0 && frameAgeMilliseconds <= 2000
      ? state.latestFrameMetadata
      : null;
    const currentBuffered = containingRange(buffered, video.currentTime);
    let quality = null;
    try { quality = video.getVideoPlaybackQuality?.() || null; } catch {}
    const droppedFrames = Number.isFinite(quality?.droppedVideoFrames)
      ? quality.droppedVideoFrames
      : Number.isFinite(video.webkitDroppedFrameCount) ? video.webkitDroppedFrameCount : null;
    const totalFrames = Number.isFinite(quality?.totalVideoFrames)
      ? quality.totalVideoFrames
      : Number.isFinite(video.webkitDecodedFrameCount) ? video.webkitDecodedFrameCount : null;
    const presentedFrames = Number.isFinite(frameMetadata?.presentedFrames)
      ? frameMetadata.presentedFrames
      : totalFrames;
    const progressedByTime = state.previousSampleMediaTime === null ||
      video.currentTime > state.previousSampleMediaTime + 0.001;
    const progressedByFrame = presentedFrames !== null &&
      (state.previousPresentedFrames === null || presentedFrames > state.previousPresentedFrames);
    const playerProgressHealthy = video.paused || video.seeking || progressedByTime || progressedByFrame;
    state.previousSampleMediaTime = video.currentTime;
    state.previousPresentedFrames = presentedFrames;
    window.chrome.webview.postMessage({
      type: "stream-sync-status",
      hasVideo: true,
      streamName: identity.name,
      streamNameConfidence: identity.confidence,
      currentTime: video.currentTime,
      playbackRate: video.playbackRate,
      paused: video.paused,
      seeking: video.seeking,
      readyState: video.readyState,
      networkState: video.networkState,
      buffering: video.readyState < 3 && !video.paused,
      bufferedRanges: buffered,
      seekableRanges: seekable,
      eventKind,
      pageSampleMonotonicMilliseconds: pageSampleNow,
      pageEventMonotonicMilliseconds: state.lastPlayerEventAt || null,
      playerEventSequence: state.eventSequence,
      usedVideoFrameCallback: typeof video.requestVideoFrameCallback === "function",
      presentedMediaTimeSeconds: frameMetadata?.mediaTime ?? null,
      frameAgeMilliseconds: frameMetadata ? frameAgeMilliseconds : null,
      expectedDisplayMonotonicMilliseconds: frameMetadata?.expectedDisplayTime ?? null,
      frameProcessingDurationSeconds: frameMetadata?.processingDuration ?? null,
      presentedFrames,
      droppedVideoFrames: droppedFrames,
      totalVideoFrames: totalFrames,
      playerProgressHealthy,
      lastBufferEventAt: state.lastBufferEventAt,
      width: video.clientWidth || video.videoWidth || 0,
      height: video.clientHeight || video.videoHeight || 0
    });
  }

  function postCommandResult(video, message, stage, outcome, applied) {
    window.chrome.webview.postMessage({
      type: "stream-sync-command-result",
      commandId: String(message.commandId || ""),
      commandStage: stage,
      commandOutcome: outcome,
      commandApplied: applied,
      currentTime: video && Number.isFinite(video.currentTime) ? video.currentTime : 0,
      playbackRate: video && Number.isFinite(video.playbackRate) ? video.playbackRate : 1,
      paused: video ? video.paused : true
    });
  }

  window.chrome.webview.addEventListener("message", event => {
    const message = event.data;
    if (!message || message.type !== "stream-sync-command") return;
    const video = primaryVideo();
    if (!video) {
      postCommandResult(null, message, "failed", "video-unavailable", false);
      return;
    }

    try {
      if (message.action === "rate") {
        const rate = Number(message.value);
        if (!Number.isFinite(rate)) {
          postCommandResult(video, message, "failed", "invalid-value", false);
          return;
        }
        if (Math.abs(video.playbackRate - rate) >= 0.002) video.playbackRate = rate;
        postCommandResult(video, message, "applied", "rate-assigned", true);
        const rateStartMediaTime = video.currentTime;
        setTimeout(() => {
          const state = stateFor(video);
          const frameAdvanced = Number.isFinite(state.latestFrameMetadata?.mediaTime) &&
            state.latestFrameMetadata.mediaTime > rateStartMediaTime + 0.001;
          const mediaAdvanced = video.paused || video.currentTime > rateStartMediaTime + 0.001 || frameAdvanced;
          const verified = Math.abs(video.playbackRate - rate) <= 0.005 && mediaAdvanced;
          postCommandResult(video, message, verified ? "verified" : "failed",
            verified ? "rate-confirmed" : "rate-not-progressing", true);
        }, 250);
      } else if (message.action === "seek") {
        const target = Number(message.value);
        const seekable = mediaRanges(video.seekable);
        if (!Number.isFinite(target) ||
            !seekable.some(range => target >= range[0] && target <= range[1])) {
          postCommandResult(video, message, "failed", "invalid-range", false);
          return;
        }
        let settled = false;
        const finishSeek = () => {
          if (settled) return;
          settled = true;
          const verified = Math.abs(video.currentTime - target) <= 0.5;
          postCommandResult(
            video,
            message,
            verified ? "verified" : "failed",
            verified ? "position-confirmed" : "position-mismatch",
            true);
        };
        video.addEventListener("seeked", finishSeek, { once: true, capture: true });
        video.currentTime = target;
        postCommandResult(video, message, "applied", "seek-assigned", true);
        setTimeout(() => {
          if (settled) return;
          settled = true;
          postCommandResult(video, message, "failed", "seeked-timeout", true);
        }, 1500);
      } else if (message.action === "pause") {
        video.pause();
        postCommandResult(video, message, "applied", "pause-requested", true);
        setTimeout(() => postCommandResult(
          video, message, video.paused ? "verified" : "failed",
          video.paused ? "pause-confirmed" : "pause-mismatch", true), 0);
      } else if (message.action === "resume") {
        postCommandResult(video, message, "applied", "resume-requested", true);
        video.play().then(
          () => postCommandResult(video, message, !video.paused ? "verified" : "failed",
            !video.paused ? "resume-confirmed" : "resume-mismatch", true),
          () => postCommandResult(video, message, "failed", "resume-rejected", true));
      } else if (message.action === "reset-rate") {
        video.playbackRate = 1;
        postCommandResult(video, message, "applied", "reset-rate-assigned", true);
        setTimeout(() => postCommandResult(
          video, message, Math.abs(video.playbackRate - 1) <= 0.005 ? "verified" : "failed",
          Math.abs(video.playbackRate - 1) <= 0.005 ? "rate-confirmed" : "rate-mismatch", true), 100);
      } else {
        postCommandResult(video, message, "failed", "unsupported-command", false);
      }
    } catch {
      postCommandResult(video, message, "failed", "command-exception", false);
    }
  });

  setInterval(postStatus, 500);
  postStatus();
})();
""";
    }

    private sealed record SyncPlayerCandidate(
        CoreWebView2Frame? Frame,
        SyncMemberSnapshot Snapshot);

    private sealed class PendingSyncCommand
    {
        public PendingSyncCommand(
            SyncCommand command,
            TaskCompletionSource<SyncCommandResult> completion,
            DateTimeOffset issuedAtUtc,
            long issuedMonotonicTicks)
        {
            Command = command;
            Completion = completion;
            IssuedAtUtc = issuedAtUtc;
            IssuedMonotonicTicks = issuedMonotonicTicks;
        }

        public SyncCommand Command { get; }

        public TaskCompletionSource<SyncCommandResult> Completion { get; }

        public DateTimeOffset IssuedAtUtc { get; }

        public long IssuedMonotonicTicks { get; }

        public DateTimeOffset? AppliedAtUtc { get; set; }

        public long? AppliedMonotonicTicks { get; set; }
    }

    private sealed class SyncStatusMessage
    {
        public string Type { get; init; } = "";

        public bool HasVideo { get; init; }

        public string? StreamName { get; init; }

        public int StreamNameConfidence { get; init; }

        public double CurrentTime { get; init; }

        public double PlaybackRate { get; init; } = 1;

        public bool Paused { get; init; }

        public bool Seeking { get; init; }

        public int ReadyState { get; init; }

        public int NetworkState { get; init; }

        public bool Buffering { get; init; }

        public double? BufferSec { get; init; }

        public double? SeekableStart { get; init; }

        public double? SeekableEnd { get; init; }

        public double[][]? BufferedRanges { get; init; }

        public double[][]? SeekableRanges { get; init; }

        public string? EventKind { get; init; }

        public double? PageSampleMonotonicMilliseconds { get; init; }

        public double? PageEventMonotonicMilliseconds { get; init; }

        public bool UsedVideoFrameCallback { get; init; }

        public double? PresentedMediaTimeSeconds { get; init; }

        public double? FrameAgeMilliseconds { get; init; }

        public double? ExpectedDisplayMonotonicMilliseconds { get; init; }

        public double? FrameProcessingDurationSeconds { get; init; }

        public long? PresentedFrames { get; init; }

        public long? DroppedVideoFrames { get; init; }

        public long? TotalVideoFrames { get; init; }

        public bool PlayerProgressHealthy { get; init; } = true;

        public long PlayerEventSequence { get; init; }

        public string? CommandId { get; init; }

        public string? CommandStage { get; init; }

        public string? CommandOutcome { get; init; }

        public bool CommandApplied { get; init; }

        public long LastBufferEventAt { get; init; }

        public double Width { get; init; }

        public double Height { get; init; }
    }
}
