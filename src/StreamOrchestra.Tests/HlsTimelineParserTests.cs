using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;
using System.Text.Json;

namespace StreamOrchestra.Tests;

public sealed class HlsTimelineParserTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-08-18T00:00:30Z");
    private readonly HlsTimelineParser _parser = new(new HlsIdentityService(
        Enumerable.Repeat((byte)17, 32).ToArray()));

    [Theory]
    [InlineData("https://example.test/live/index.m3u8", null, true)]
    [InlineData("https://example.test/live/index.m3u", "video/mp4", true)]
    [InlineData("https://example.test/live/manifest", "application/vnd.apple.mpegurl; charset=utf-8", true)]
    [InlineData("https://example.test/live/manifest", "AUDIO/X-MPEGURL", true)]
    [InlineData("https://example.test/live/video.mp4", "video/mp4", false)]
    public void DetectsExtensionsAndHlsContentTypes(string uri, string? contentType, bool expected)
    {
        Assert.Equal(expected, HlsTimelineParser.IsHlsPlaylistResource(uri, contentType));
    }

    [Fact]
    public void ParseCompatibility_UsesExplicitPdtButNotPrivateTimestampAsPts()
    {
        var playlist = """
            #EXTM3U
            #EXT-X-FIRST-SEGMENT-TIMESTAMP:100000000
            #EXTINF:2.0,
            first.ts
            #EXT-X-PROGRAM-DATE-TIME:2026-08-10T12:00:00Z
            #EXTINF:3.0,
            second.ts
            #EXTINF:4.0,
            third.ts
            """;

        var result = _parser.Parse(playlist, responseDateUtc: null, ObservedAt);

        Assert.NotNull(result);
        Assert.Equal(SyncTimelineSource.ProgramDateTime, result!.Source);
        Assert.Equal(DateTimeOffset.Parse("2026-08-10T12:00:07Z"), result.EdgeUtc);
        Assert.Null(result.MediaToUtcOffsetMs);
        Assert.Equal(3, result.SegmentDurationSec);
    }

    [Fact]
    public void ParseCompatibility_DoesNotPromoteHttpDateOrPrivateTimestamp()
    {
        var responseDate = DateTimeOffset.Parse("2026-08-10T12:00:10Z");
        var playlist = """
            #EXTM3U
            #EXT-X-FIRST-SEGMENT-TIMESTAMP:100000000
            #EXTINF:2.0,
            first.ts
            """;

        Assert.Null(_parser.Parse(playlist, responseDate, responseDate));
    }

    [Fact]
    public void ParsesMasterVariantsAudioAndSafeResourceIdentities()
    {
        var master = ParseFixture(
            "mixed/master.m3u8",
            "https://cdn.example.test/root/master.m3u8?token=request-one").Document;

        Assert.Equal(HlsPlaylistKind.Master, master.Kind);
        Assert.Equal(HlsRenditionKind.Variant, master.RenditionKind);
        Assert.Empty(master.Segments);
        var variant = Assert.Single(master.Variants);
        Assert.Equal(2_500_000, variant.Bandwidth);
        Assert.Equal("1920x1080", variant.ResolutionBucket);
        Assert.Equal("avc1+mp4a", variant.CodecBucket);
        var audio = Assert.Single(master.Renditions);
        Assert.Equal(HlsRenditionKind.Audio, audio.Kind);
        Assert.Equal(variant.AudioGroupHash, audio.GroupHash);
        Assert.DoesNotContain("token", variant.Resource.PersistenceIdentity.ToString(), StringComparison.OrdinalIgnoreCase);

        var otherQuery = ParseFixture(
            "mixed/master.m3u8",
            "https://cdn.example.test/root/master.m3u8?token=request-two").Document;
        Assert.Equal(master.PlaylistIdentity.PersistenceHash, otherQuery.PlaylistIdentity.PersistenceHash);
    }

    [Fact]
    public void DistinguishesVideoAudioAndKeepsPlaylistProgressSeparate()
    {
        var master = ParseFixture(
            "mixed/master.m3u8",
            "https://cdn.example.test/root/master.m3u8?token=master-request");
        var video = ParseFixture(
            "mixed/video.m3u8",
            "https://cdn.example.test/root/video/index.m3u8?token=video-request");
        var audio = ParseFixture(
            "mixed/audio.m3u8",
            "https://cdn.example.test/root/audio/index.m3u8?token=audio-request");

        Assert.Equal(HlsRenditionKind.Unknown, video.Document.RenditionKind);
        var catalog = new HlsRenditionCatalog();
        catalog.Register(master.Document);
        video = catalog.Apply(video);
        var audioTimeline = Assert.IsType<TimelineObservation>(audio.TimelineCandidate);
        audio = catalog.Apply(audio with
        {
            Document = audio.Document with { RenditionKind = HlsRenditionKind.Unknown },
            TimelineCandidate = audioTimeline with { RenditionKind = HlsRenditionKind.Unknown }
        });

        Assert.Equal(HlsRenditionKind.Video, video.Document.RenditionKind);
        Assert.Equal(HlsRenditionKind.Audio, audio.Document.RenditionKind);
        Assert.NotEqual(video.ProgressKey!.PersistenceHash, audio.ProgressKey!.PersistenceHash);
        Assert.NotEqual(
            video.Document.PlaylistIdentity.PersistenceHash,
            audio.Document.PlaylistIdentity.PersistenceHash);
    }

    [Fact]
    public void ProgressKeyIncludesPdtMappingEvenWhenTailResourceIsUnchanged()
    {
        var original = File.ReadAllText(FixturePath("progress/base.m3u8"));
        var corrected = original.Replace(
            "2026-08-18T00:00:00Z",
            "2026-08-18T00:00:00.500Z",
            StringComparison.Ordinal);

        var first = ParseText(original);
        var second = ParseText(corrected);

        Assert.NotEmpty(first.ProgressKey!.TailProgramDateTimeHash);
        Assert.NotEqual(first.ProgressKey.PersistenceHash, second.ProgressKey!.PersistenceHash);
    }

    [Fact]
    public void ResolvesImplicitByteRangesFromPreviousSameResource()
    {
        var result = ParseText("""
            #EXTM3U
            #EXT-X-TARGETDURATION:4
            #EXT-X-MEDIA-SEQUENCE:1
            #EXT-X-BYTERANGE:100@0
            #EXTINF:4,
            media.m4s
            #EXT-X-BYTERANGE:125
            #EXTINF:4,
            media.m4s
            """);

        Assert.Equal(new HlsByteRange(100, 0), result.Document.Segments[0].Resource.ByteRange);
        Assert.Equal(new HlsByteRange(125, 100), result.Document.Segments[1].Resource.ByteRange);
        Assert.Equal(new HlsByteRange(125, 100), result.ProgressKey!.TailByteRange);
        Assert.DoesNotContain("invalid-byte-range", result.Document.WarningCodes);
    }

    [Fact]
    public void MultiplePdtUsesLatestExplicitAnchorWithoutPrecisionLoss()
    {
        var result = ParseFixture("pdt/multiple.m3u8");

        Assert.Equal(2, result.Document.Segments.Count(segment => segment.ProgramDateTimeUtc is not null));
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-18T00:00:04.123456Z"),
            result.Document.Segments[^1].ProgramDateTimeUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-18T00:00:06.123456Z"), result.TimelineCandidate!.EdgeUtc);
        Assert.DoesNotContain("pdt-discontinuous", result.Document.WarningCodes);
    }

    [Fact]
    public void TimezoneMissingPdtIsPreservedButNeverAssumedUtc()
    {
        var result = ParseFixture("pdt/timezone-missing.m3u8");
        var segment = Assert.Single(result.Document.Segments);

        Assert.Equal("2026-08-18T12:00:00.123", segment.ProgramDateTimeText);
        Assert.False(segment.ProgramDateTimeHasTimezone);
        Assert.Null(segment.ProgramDateTimeUtc);
        Assert.Contains("pdt-timezone-missing", result.Document.WarningCodes);
        Assert.Null(result.TimelineCandidate);
    }

    [Fact]
    public void ParsesGapMapByteRangeAndEndListWithoutPersistingSignedQuery()
    {
        var result = ParseFixture("segments/gap-map-endlist.m3u8");
        var document = result.Document;

        Assert.True(document.HasEndList);
        Assert.Equal(new HlsByteRange(720, 0), document.Segments[0].Map!.Resource.ByteRange);
        Assert.True(document.Segments[^1].IsGap);
        Assert.Equal(new HlsByteRange(1000, 720), document.Segments[^1].Resource.ByteRange);
        Assert.Equal(new HlsByteRange(1000, 720), result.ProgressKey!.TailByteRange);
        Assert.DoesNotContain("token", result.ProgressKey.PersistenceHash, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("segment-two", result.ProgressKey.PersistenceHash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedVendorTimestampRemainsUnverifiedAndDateIsMetadataOnly()
    {
        var result = ParseFixture(
            "private/malformed-vendor.m3u8",
            responseDateUtc: DateTimeOffset.Parse("2026-08-18T00:00:10Z"));

        Assert.Equal(2, result.Document.PrivateExtensions.Count);
        Assert.Contains(result.Document.PrivateExtensions, extension =>
            extension.NameBucket == "ext-x-first-segment-timestamp" && extension.ValidationStatus == "malformed");
        Assert.Contains("malformed-vendor-timestamp", result.Document.WarningCodes);
        Assert.Equal(DateTimeOffset.Parse("2026-08-18T00:00:10Z"), result.Document.ResponseDateUtc);
        Assert.Null(result.TimelineCandidate);
        var persistedShape = JsonSerializer.Serialize(result.Document);
        Assert.DoesNotContain("unverified-private-value", persistedShape, StringComparison.Ordinal);
        Assert.DoesNotContain("request-secret", persistedShape, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesLowLatencySyntaxAsStructureWithoutClaimingRuntimeSupport()
    {
        var result = ParseFixture("llhls/parts.m3u8");
        var document = result.Document;

        Assert.True(document.HasLowLatencySyntax);
        Assert.Equal(0.33334, document.PartTargetDurationSeconds!.Value, 5);
        Assert.True(document.ServerControl!.CanBlockReload);
        Assert.Equal(12, document.ServerControl.HoldBackSeconds);
        Assert.Equal(2, document.TrailingParts.Count);
        Assert.True(document.TrailingParts[0].IsIndependent);
        Assert.True(document.TrailingParts[1].IsGap);
        Assert.All(document.TrailingParts, part => Assert.Equal(301, part.MediaSequence));
        Assert.All(document.TrailingParts, part => Assert.Equal(0, part.DiscontinuitySequence));
        Assert.Equal(301, result.ProgressKey!.LastMediaSequence);
        Assert.Equal(1, result.ProgressKey.LastPartIndex);
        Assert.Single(document.PreloadHints);
        Assert.Single(document.RenditionReports);
    }

    [Fact]
    public void LowLatencyPartsCarrySkippedMsnAndTheirOwnDiscontinuity()
    {
        var result = ParseText("""
            #EXTM3U
            #EXT-X-TARGETDURATION:4
            #EXT-X-MEDIA-SEQUENCE:300
            #EXT-X-SKIP:SKIPPED-SEGMENTS=5
            #EXTINF:4,
            seg305.m4s
            #EXT-X-DISCONTINUITY
            #EXT-X-PART:DURATION=0.5,URI="part306.0.m4s"
            """);

        Assert.Equal(305, Assert.Single(result.Document.Segments).MediaSequence);
        var part = Assert.Single(result.Document.TrailingParts);
        Assert.Equal(306, part.MediaSequence);
        Assert.Equal(1, part.DiscontinuitySequence);
        Assert.Equal(306, result.ProgressKey!.LastMediaSequence);
        Assert.Equal(1, result.ProgressKey.LastDiscontinuitySequence);
        Assert.Null(result.TimelineCandidate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000")]
    [InlineData("not-hls")]
    public void RejectsOrDoesNotCreateTimelineForUnusablePlaylists(string playlist)
    {
        var result = _parser.ParsePlaylist(new HlsPlaylistParseRequest(
            playlist,
            "https://cdn.example.test/playlist.m3u8",
            null,
            null,
            ObservedAt));

        Assert.True(result is null || result.TimelineCandidate is null);
    }

    private HlsPlaylistParseResult ParseFixture(
        string relativePath,
        string requestUri = "https://cdn.example.test/live/index.m3u8?token=request-secret",
        string? contentType = "application/vnd.apple.mpegurl",
        DateTimeOffset? responseDateUtc = null)
    {
        var path = FixturePath(relativePath);
        return Assert.IsType<HlsPlaylistParseResult>(_parser.ParsePlaylist(new HlsPlaylistParseRequest(
            File.ReadAllText(path),
            requestUri,
            contentType,
            responseDateUtc,
            ObservedAt)));
    }

    private HlsPlaylistParseResult ParseText(string text) =>
        Assert.IsType<HlsPlaylistParseResult>(_parser.ParsePlaylist(new HlsPlaylistParseRequest(
            text,
            "https://cdn.example.test/live/index.m3u8?token=request-secret",
            "application/vnd.apple.mpegurl",
            null,
            ObservedAt)));

    private static string FixturePath(string relativePath) => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "Hls",
        relativePath.Replace('/', Path.DirectorySeparatorChar));
}
