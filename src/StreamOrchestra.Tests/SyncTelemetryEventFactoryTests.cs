using System.Text.Json;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncTelemetryEventFactoryTests
{
    private static readonly byte[] IdentityKey = Enumerable.Repeat((byte)0x31, 32).ToArray();

    [Fact]
    public void ReducedNetworkEvent_RecordsAvailabilityWithoutPersistingRawRequest()
    {
        var recorder = Recorder();
        const string rawUri = "https://edge.example.test/live/manifest?token=network-secret";
        var value = SyncTelemetryEventFactory.CreateReducedNetworkEvent(
            recorder,
            1,
            rawUri,
            200,
            "application/vnd.apple.mpegurl; charset=utf-8",
            DateTimeOffset.Parse("2026-08-18T01:00:00Z"),
            100,
            150,
            hasDateHeader: true,
            ageHeader: "17",
            outcomeCode: "ok",
            navigationGeneration: 2);

        recorder.RecordNetwork(value);
        var persisted = Assert.Single(recorder.CreateSnapshot().Network);
        var json = JsonSerializer.Serialize(persisted);

        Assert.False(persisted.RequestStartObserved);
        Assert.True(persisted.HasDateHeader);
        Assert.True(persisted.HasAgeHeader);
        Assert.Equal(30, persisted.AgeSecondsBucket);
        Assert.True(persisted.IsExtensionlessResource);
        Assert.Equal("web-resource-response-reduced", persisted.CorrelationSource);
        Assert.Equal("unavailable", persisted.CorrelationStatus);
        Assert.DoesNotContain(rawUri, json, StringComparison.Ordinal);
        Assert.DoesNotContain("network-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CdpNetworkAndPlaylistEvents_PersistTheSameProtectedRequestLink()
    {
        var recorder = Recorder();
        const string rawUri = "https://edge.example.test/live/index.m3u8?token=secret";
        const string rawRequestId = "cdp-request-17";
        var started = new SyncTelemetryClockSample(
            DateTimeOffset.Parse("2026-08-18T01:00:00Z"),
            100);
        var headers = new SyncTelemetryClockSample(
            DateTimeOffset.Parse("2026-08-18T01:00:00.010Z"),
            110);
        var completed = new SyncTelemetryClockSample(
            DateTimeOffset.Parse("2026-08-18T01:00:00.020Z"),
            120);
        var correlation = new CdpNetworkCorrelationResult
        {
            Status = "correlated",
            RequestId = rawRequestId,
            FrameId = "frame-1",
            NavigationGeneration = 4,
            RequestStartedAt = started,
            HeadersReceivedAt = headers,
            BodyCompletedAt = completed,
            StatusCode = 200,
            MimeType = "application/vnd.apple.mpegurl",
            CacheBucket = "unknown",
            EncodedBodyLengthBucket = 4096,
            SchemaCompatible = true
        };
        var parsed = Parser().ParsePlaylist(new HlsPlaylistParseRequest(
            """
            #EXTM3U
            #EXT-X-TARGETDURATION:2
            #EXT-X-MEDIA-SEQUENCE:1
            #EXT-X-PROGRAM-DATE-TIME:2026-08-18T01:00:00Z
            #EXTINF:2,
            segment.ts
            """,
            rawUri,
            "application/vnd.apple.mpegurl",
            null,
            DateTimeOffset.Parse("2026-08-18T01:00:01Z")))!;

        recorder.RecordNetwork(SyncTelemetryEventFactory.CreateCdpNetworkEvent(
            recorder,
            1,
            rawUri,
            correlation,
            hasDateHeader: true,
            ageHeader: "0",
            outcomeCode: "ok"));
        recorder.RecordPlaylist(SyncTelemetryEventFactory.CreatePlaylistEvent(
            recorder,
            1,
            rawUri,
            "application/vnd.apple.mpegurl",
            parsed.Document,
            parsed.ProgressKey,
            null,
            DateTimeOffset.Parse("2026-08-18T01:00:01Z"),
            120,
            4,
            masterAssociationObserved: false,
            requestIdentity: rawRequestId));

        var snapshot = recorder.CreateSnapshot();
        var network = Assert.Single(snapshot.Network);
        var playlist = Assert.Single(snapshot.Playlists);
        var json = JsonSerializer.Serialize(snapshot);

        Assert.True(network.RequestStartObserved);
        Assert.False(network.IsReducedConfidenceSource);
        Assert.Equal("cdp-network", network.CorrelationSource);
        Assert.Equal("correlated", network.CorrelationStatus);
        Assert.Equal(network.RequestId, playlist.RequestId);
        Assert.NotEqual(rawRequestId, network.RequestId);
        Assert.DoesNotContain(rawUri, json, StringComparison.Ordinal);
        Assert.DoesNotContain(rawRequestId, json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaylistEvent_ProjectsPdtPrecisionLlHlsExtensionsAndTracking()
    {
        var recorder = Recorder();
        var parser = Parser();
        var parsed = parser.ParsePlaylist(new HlsPlaylistParseRequest(
            """
            #EXTM3U
            #EXT-X-TARGETDURATION:2
            #EXT-X-MEDIA-SEQUENCE:10
            #EXT-X-PART-INF:PART-TARGET=0.5
            #EXT-X-SOOP-TIMESTAMP:12345
            #EXT-X-PROGRAM-DATE-TIME:2026-08-18T01:00:00.123456Z
            #EXTINF:2,
            segment-10.ts
            """,
            "https://edge.example.test/live/index.m3u8?signature=secret",
            "application/vnd.apple.mpegurl",
            null,
            DateTimeOffset.Parse("2026-08-18T01:00:01Z")))!;
        var tracker = new HlsPlaylistTracker();
        var tracking = tracker.Observe(new HlsPlaylistTrackingContext
        {
            SlotId = 1,
            NavigationGeneration = 1,
            SessionIdentity = "runtime-session",
            TimelineLaneIdentity = "primary-video",
            PlaylistIdentityHash = parsed.Document.PlaylistIdentity.PersistenceHash,
            RenditionKind = parsed.Document.RenditionKind,
            SourceIdentity = "source",
            Document = parsed.Document,
            ProgressKey = parsed.ProgressKey!,
            ObservedMonotonicTicks = 100,
            MonotonicFrequency = 1000
        });
        var value = SyncTelemetryEventFactory.CreatePlaylistEvent(
            recorder,
            1,
            "https://edge.example.test/live/index.m3u8?signature=secret",
            "application/vnd.apple.mpegurl",
            parsed.Document,
            parsed.ProgressKey,
            tracking,
            DateTimeOffset.Parse("2026-08-18T01:00:01Z"),
            100,
            1,
            masterAssociationObserved: false);

        recorder.RecordPlaylist(value);
        var persisted = Assert.Single(recorder.CreateSnapshot().Playlists);
        var json = JsonSerializer.Serialize(persisted);

        Assert.Equal(1, persisted.ProgramDateTimeCount);
        Assert.Equal(1, persisted.ProgramDateTimeTimezoneCount);
        Assert.Equal("microseconds-or-finer", persisted.ProgramDateTimePrecisionBucket);
        Assert.True(persisted.HasLowLatencySyntax);
        Assert.Contains("ll-hls", persisted.ExtensionBuckets);
        Assert.Contains("ext-x-soop-timestamp", persisted.ExtensionBuckets);
        Assert.Equal("new-evidence", persisted.TrackingDisposition);
        Assert.Equal("initial-observation", persisted.EpochResetReason);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MasterEvent_CountsVariantAndAudioAssociationInputs()
    {
        var recorder = Recorder();
        var parsed = Parser().ParsePlaylist(new HlsPlaylistParseRequest(
            """
            #EXTM3U
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio",NAME="main",URI="audio"
            #EXT-X-STREAM-INF:BANDWIDTH=2000000,AUDIO="audio"
            video
            """,
            "https://edge.example.test/live/master",
            "application/x-mpegurl",
            null,
            DateTimeOffset.Parse("2026-08-18T01:00:00Z")))!;

        var value = SyncTelemetryEventFactory.CreatePlaylistEvent(
            recorder,
            1,
            "https://edge.example.test/live/master",
            "application/x-mpegurl",
            parsed.Document,
            parsed.ProgressKey,
            null,
            DateTimeOffset.Parse("2026-08-18T01:00:00Z"),
            10,
            1,
            false);

        Assert.Equal(1, value.VariantCount);
        Assert.Equal(1, value.AudioRenditionCount);
        Assert.True(value.IsExtensionlessResource);
        Assert.Equal("not-trackable", value.TrackingDisposition);
    }

    [Fact]
    public void PlayerEvent_HmacSeparatesChannelAndBroadcastAndKeepsStrata()
    {
        var recorder = Recorder();
        var snapshot = new SyncMemberSnapshot
        {
            HasVideo = true,
            CurrentTime = 100,
            BufferedRanges = [new MediaTimeRange(90, 110)],
            SeekableRanges = [new MediaTimeRange(80, 120)],
            EventKind = SyncPlayerEventKind.Frame,
            UsedVideoFrameCallback = true,
            FrameAgeMilliseconds = 12,
            PresentedFrames = 500,
            TotalVideoFrames = 510,
            PlayerProgressHealthy = true,
            HostReceivedMonotonicTicks = 500,
            ObservedAtUtc = DateTimeOffset.Parse("2026-08-18T01:00:00Z")
        };

        recorder.RecordPlayer(SyncTelemetryEventFactory.CreatePlayerEvent(
            recorder,
            1,
            snapshot,
            "same-input",
            "same-input",
            "1080p",
            "cdn-a",
            "medium",
            "unknown",
            "pdt",
            2,
            3));
        var persisted = Assert.Single(recorder.CreateSnapshot().Players);

        Assert.NotEqual("same-input", persisted.ChannelId);
        Assert.NotEqual("same-input", persisted.BroadcastSessionId);
        Assert.NotEqual(persisted.ChannelId, persisted.BroadcastSessionId);
        Assert.Equal("1080p", persisted.QualityBucket);
        Assert.Equal("cdn-a", persisted.CdnBucket);
        Assert.Equal("pdt", persisted.SourceBucket);
        Assert.Equal(12, persisted.FrameAgeMilliseconds);
    }

    private static SyncTelemetryRecorder Recorder() => SyncTelemetryRecorder.CreateEnabled(
        new SyncTelemetryRecorderOptions
        {
            SessionId = "factory-session",
            IdentityKey = IdentityKey
        });

    private static HlsTimelineParser Parser() => new(new SyncTelemetryPrivacy(IdentityKey));
}
