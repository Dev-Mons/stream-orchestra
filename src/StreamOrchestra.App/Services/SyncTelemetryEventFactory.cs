using System.Globalization;
using System.IO;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class SyncTelemetryEventFactory
{
    public static SyncNetworkTelemetry CreateReducedNetworkEvent(
        ISyncTelemetryRecorder recorder,
        int slotId,
        string rawRequestUri,
        int statusCode,
        string? contentType,
        DateTimeOffset observedAtUtc,
        long headersReceivedMonotonicTicks,
        long? bodyCompletedMonotonicTicks,
        bool hasDateHeader,
        string? ageHeader,
        string outcomeCode,
        int navigationGeneration,
        long sourceEpoch = 0,
        string? requestIdentity = null,
        string correlationSource = "web-resource-response-reduced",
        string correlationStatus = "unavailable")
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var stamp = new SyncTelemetryClockSample(
            observedAtUtc.ToUniversalTime(),
            Math.Max(0, headersReceivedMonotonicTicks));
        var age = ParseAgeBucket(ageHeader);
        return new SyncNetworkTelemetry(
            recorder.SessionId,
            slotId,
            requestIdentity ?? $"{navigationGeneration}:{rawRequestUri}:{headersReceivedMonotonicTicks}",
            recorder.CreateUrlIdentity(rawRequestUri),
            "playlist",
            stamp,
            stamp,
            bodyCompletedMonotonicTicks is { } completed
                ? new SyncTelemetryClockSample(observedAtUtc.ToUniversalTime(), Math.Max(0, completed))
                : null,
            statusCode,
            contentType ?? "",
            age is > 0 ? "hit" : "unknown",
            null,
            IsReducedConfidenceSource: true,
            outcomeCode,
            Math.Max(0, navigationGeneration),
            Math.Max(0, sourceEpoch))
        {
            RequestStartObserved = false,
            HasDateHeader = hasDateHeader,
            HasAgeHeader = ageHeader is not null,
            AgeSecondsBucket = age,
            IsExtensionlessResource = IsExtensionless(rawRequestUri),
            CorrelationSource = correlationSource,
            CorrelationStatus = correlationStatus
        };
    }

    public static SyncNetworkTelemetry CreateCdpNetworkEvent(
        ISyncTelemetryRecorder recorder,
        int slotId,
        string rawRequestUri,
        CdpNetworkCorrelationResult correlation,
        bool hasDateHeader,
        string? ageHeader,
        string outcomeCode,
        long sourceEpoch = 0)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(correlation);
        if (correlation.Status != "correlated" ||
            correlation.RequestStartedAt is null ||
            correlation.HeadersReceivedAt is null)
        {
            throw new ArgumentException("A complete correlated CDP lifecycle is required.", nameof(correlation));
        }

        var age = ParseAgeBucket(ageHeader);
        return new SyncNetworkTelemetry(
            recorder.SessionId,
            slotId,
            correlation.RequestId,
            recorder.CreateUrlIdentity(rawRequestUri),
            "playlist",
            correlation.RequestStartedAt,
            correlation.HeadersReceivedAt,
            correlation.BodyCompletedAt,
            correlation.StatusCode,
            correlation.MimeType,
            correlation.CacheBucket,
            correlation.EncodedBodyLengthBucket,
            IsReducedConfidenceSource: false,
            outcomeCode,
            correlation.NavigationGeneration,
            Math.Max(0, sourceEpoch))
        {
            RequestStartObserved = true,
            HasDateHeader = hasDateHeader,
            HasAgeHeader = ageHeader is not null,
            AgeSecondsBucket = age,
            IsExtensionlessResource = IsExtensionless(rawRequestUri),
            CorrelationSource = "cdp-network",
            CorrelationStatus = "correlated",
            FrameId = correlation.FrameId
        };
    }

    public static SyncPlaylistTelemetry CreatePlaylistEvent(
        ISyncTelemetryRecorder recorder,
        int slotId,
        string rawRequestUri,
        string? contentType,
        HlsPlaylistDocument document,
        HlsProgressKey? progressKey,
        HlsPlaylistTrackingResult? tracking,
        DateTimeOffset observedAtUtc,
        long observedMonotonicTicks,
        int navigationGeneration,
        bool masterAssociationObserved,
        string? requestIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(document);
        var pdtSegments = document.Segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment.ProgramDateTimeText))
            .ToArray();
        var extensionBuckets = document.PrivateExtensions
            .Select(extension => extension.NameBucket)
            .Concat(document.HasLowLatencySyntax ? ["ll-hls"] : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var privateExtension = document.PrivateExtensions
            .Select(extension => extension.NameBucket)
            .FirstOrDefault() ?? "none";
        var disposition = tracking?.Disposition switch
        {
            HlsProgressDisposition.NewEvidence => "new-evidence",
            HlsProgressDisposition.Duplicate => "duplicate",
            HlsProgressDisposition.Stale => "stale",
            HlsProgressDisposition.Rollback => "rollback",
            _ => "not-trackable"
        };
        var resetReason = tracking?.ResetReason switch
        {
            HlsEpochResetReason.InitialObservation => "initial-observation",
            HlsEpochResetReason.Navigation => "navigation",
            HlsEpochResetReason.Session => "session",
            HlsEpochResetReason.Rendition => "rendition",
            HlsEpochResetReason.Source => "source",
            HlsEpochResetReason.Discontinuity => "discontinuity",
            HlsEpochResetReason.SequenceRollback => "sequence-rollback",
            _ => "none"
        };
        return new SyncPlaylistTelemetry(
            recorder.SessionId,
            slotId,
            rawRequestUri,
            recorder.CreateUrlIdentity(rawRequestUri),
            PlaylistKind(document.Kind),
            RenditionKind(document.RenditionKind),
            progressKey?.PersistenceHash ?? "",
            Math.Max(0, tracking?.SourceEpoch ?? 0),
            new SyncTelemetryClockSample(
                observedAtUtc.ToUniversalTime(),
                Math.Max(0, observedMonotonicTicks)),
            IsDuplicate: tracking?.Disposition == HlsProgressDisposition.Duplicate,
            IsStale: tracking?.Disposition == HlsProgressDisposition.Stale,
            IsRollback: tracking?.Disposition == HlsProgressDisposition.Rollback,
            HasProgramDateTime: pdtSegments.Length > 0,
            HasPrivateTimestamp: document.PrivateExtensions.Count > 0,
            PrivateExtensionBucket: privateExtension,
            WarningCodes: document.WarningCodes,
            NavigationGeneration: Math.Max(0, navigationGeneration))
        {
            RequestId = requestIdentity ?? "",
            ProgramDateTimeCount = pdtSegments.Length,
            ProgramDateTimeTimezoneCount = pdtSegments.Count(segment => segment.ProgramDateTimeHasTimezone),
            ProgramDateTimePrecisionBucket = PrecisionBucket(pdtSegments),
            DiscontinuityCount = CountDiscontinuityTransitions(document.Segments),
            HasLowLatencySyntax = document.HasLowLatencySyntax,
            VariantCount = document.Variants.Count,
            VideoRenditionCount = document.Renditions.Count(item => item.Kind == HlsRenditionKind.Video),
            AudioRenditionCount = document.Renditions.Count(item => item.Kind == HlsRenditionKind.Audio),
            MasterAssociationObserved = masterAssociationObserved,
            TrackingDisposition = disposition,
            EpochResetReason = resetReason,
            IsSourceSwitch = tracking?.ResetReason is HlsEpochResetReason.Source or HlsEpochResetReason.Rendition,
            ContentTypeBucket = contentType ?? "",
            IsExtensionlessResource = IsExtensionless(rawRequestUri),
            ExtensionBuckets = extensionBuckets
        };
    }

    public static SyncPlayerTelemetry CreatePlayerEvent(
        ISyncTelemetryRecorder recorder,
        int slotId,
        SyncMemberSnapshot snapshot,
        string channelIdentity,
        string broadcastSessionIdentity,
        string qualityBucket,
        string cdnBucket,
        string pcLoadBucket,
        string networkBucket,
        string sourceBucket,
        long sourceEpoch,
        int navigationGeneration)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SyncPlayerTelemetry(
            recorder.SessionId,
            slotId,
            Math.Max(0, sourceEpoch),
            new SyncTelemetryClockSample(
                snapshot.ObservedAtUtc.ToUniversalTime(),
                Math.Max(0, snapshot.HostReceivedMonotonicTicks)),
            snapshot.CurrentTime,
            snapshot.BufferedRanges,
            snapshot.SeekableRanges,
            PlayerEvent(snapshot.EventKind),
            snapshot.UsedVideoFrameCallback,
            snapshot.PresentedMediaTimeSeconds,
            snapshot.ExpectedDisplayMonotonicMilliseconds,
            snapshot.DroppedVideoFrames,
            snapshot.TotalVideoFrames,
            snapshot.PageSampleMonotonicMilliseconds,
            Math.Max(0, navigationGeneration))
        {
            Buffering = snapshot.Buffering,
            PlayerProgressHealthy = snapshot.PlayerProgressHealthy,
            FrameAgeMilliseconds = snapshot.FrameAgeMilliseconds,
            PresentedFrames = snapshot.PresentedFrames,
            ChannelId = channelIdentity,
            BroadcastSessionId = broadcastSessionIdentity,
            QualityBucket = qualityBucket,
            CdnBucket = cdnBucket,
            PcLoadBucket = pcLoadBucket,
            NetworkBucket = networkBucket,
            PlaybackBucket = snapshot.Buffering ? "buffering" : "normal",
            SourceBucket = sourceBucket
        };
    }

    public static long? ParseAgeBucket(string? rawValue)
    {
        if (!long.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds < 0)
        {
            return null;
        }

        foreach (var upperBound in new long[] { 0, 1, 5, 15, 30, 60, 300, 1800, 3600, 21600, 86400 })
        {
            if (seconds <= upperBound)
            {
                return upperBound;
            }
        }

        return 86401;
    }

    private static string PlaylistKind(HlsPlaylistKind value) => value switch
    {
        HlsPlaylistKind.Master => "master",
        HlsPlaylistKind.Media => "media",
        _ => "unknown"
    };

    private static string RenditionKind(HlsRenditionKind value) => value switch
    {
        HlsRenditionKind.Video => "video",
        HlsRenditionKind.Audio => "audio",
        HlsRenditionKind.Subtitles => "subtitles",
        HlsRenditionKind.Variant => "variant",
        _ => "unknown"
    };

    private static string PlayerEvent(SyncPlayerEventKind value) => value switch
    {
        SyncPlayerEventKind.Frame => "frame",
        SyncPlayerEventKind.Waiting => "waiting",
        SyncPlayerEventKind.Stalled => "stalled",
        SyncPlayerEventKind.Error => "error",
        SyncPlayerEventKind.Seeking => "seeking",
        SyncPlayerEventKind.Seeked => "seeked",
        SyncPlayerEventKind.RateChange => "ratechange",
        _ => "sample"
    };

    private static int CountDiscontinuityTransitions(IReadOnlyList<HlsSegment> segments) => segments
        .Zip(segments.Skip(1), (left, right) => left.DiscontinuitySequence != right.DiscontinuitySequence)
        .Count(changed => changed);

    private static string PrecisionBucket(IReadOnlyList<HlsSegment> segments)
    {
        var maximumFractionDigits = segments
            .Select(segment => FractionDigits(segment.ProgramDateTimeText))
            .DefaultIfEmpty(-1)
            .Max();
        return maximumFractionDigits switch
        {
            < 0 => "none",
            0 => "seconds",
            <= 3 => "milliseconds",
            _ => "microseconds-or-finer"
        };
    }

    private static int FractionDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return -1;
        }

        var decimalIndex = value.IndexOf('.');
        if (decimalIndex < 0)
        {
            return 0;
        }

        var count = 0;
        for (var index = decimalIndex + 1; index < value.Length && char.IsDigit(value[index]); index++)
        {
            count++;
        }

        return count;
    }

    private static bool IsExtensionless(string rawUri) =>
        Uri.TryCreate(rawUri, UriKind.Absolute, out var uri) &&
        string.IsNullOrEmpty(Path.GetExtension(uri.AbsolutePath));
}
