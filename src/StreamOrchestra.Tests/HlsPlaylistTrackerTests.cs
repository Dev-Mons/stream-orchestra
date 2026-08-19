using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class HlsPlaylistTrackerTests
{
    private const long Frequency = 1000;
    private readonly HlsTimelineParser _parser = new(new HlsIdentityService(
        Enumerable.Repeat((byte)29, 32).ToArray()));

    [Fact]
    public void DuplicateDoesNotRefreshProgressAndBecomesStaleAfterManifestThreshold()
    {
        var tracker = new HlsPlaylistTracker();
        var parsed = Parse("progress/base.m3u8");
        var context = Context(parsed, tick: 0);

        var first = tracker.Observe(context);
        var duplicate = tracker.Observe(context with { ObservedMonotonicTicks = 5_000 });
        var boundary = tracker.Observe(context with { ObservedMonotonicTicks = 15_000 });
        var stale = tracker.Observe(context with { ObservedMonotonicTicks = 15_001 });

        Assert.Equal(HlsProgressDisposition.NewEvidence, first.Disposition);
        Assert.True(first.IsEstimatorEvidence);
        Assert.Equal(HlsProgressDisposition.Duplicate, duplicate.Disposition);
        Assert.Equal(HlsProgressDisposition.Duplicate, boundary.Disposition);
        Assert.Equal(HlsProgressDisposition.Stale, stale.Disposition);
        Assert.False(duplicate.IsEstimatorEvidence);
        Assert.False(stale.IsEstimatorEvidence);
        Assert.Equal(first.SourceEpoch, stale.SourceEpoch);
    }

    [Fact]
    public void ForwardProgressIsIndependentEvidenceButRollbackCannotReplaceAcceptedState()
    {
        var tracker = new HlsPlaylistTracker();
        var baseline = Parse("progress/base.m3u8");
        var forward = Parse("progress/forward.m3u8");
        var rollback = Parse("progress/rollback.m3u8");

        var first = tracker.Observe(Context(baseline, 0));
        var next = tracker.Observe(Context(forward, 1_000));
        var rolledBack = tracker.Observe(Context(rollback, 2_000));
        var acceptedStateStillCurrent = tracker.Observe(Context(forward, 3_000));

        Assert.Equal(first.SourceEpoch, next.SourceEpoch);
        Assert.True(next.IsEpochStable);
        Assert.Equal(2, next.IndependentEvidenceCount);
        Assert.Equal(HlsProgressDisposition.Rollback, rolledBack.Disposition);
        Assert.False(rolledBack.IsEstimatorEvidence);
        Assert.Equal(next.SourceEpoch, rolledBack.SourceEpoch);
        Assert.Equal(HlsProgressDisposition.Duplicate, acceptedStateStillCurrent.Disposition);
    }

    [Fact]
    public void DiscontinuityCreatesNewEpochAndTimelineUsesOnlyNewTailEpoch()
    {
        var tracker = new HlsPlaylistTracker();
        var before = Parse("epoch/discontinuity-before.m3u8");
        var after = Parse("epoch/discontinuity-after.m3u8");

        var first = tracker.Observe(Context(before, 0));
        var reset = tracker.Observe(Context(after, 1_000));

        Assert.Equal(7, before.Document.Segments[^1].DiscontinuitySequence);
        Assert.Equal(8, after.Document.Segments[^1].DiscontinuitySequence);
        Assert.Equal(first.SourceEpoch + 1, reset.SourceEpoch);
        Assert.Equal(HlsEpochResetReason.Discontinuity, reset.ResetReason);
        Assert.False(reset.IsEpochStable);
        Assert.Equal(DateTimeOffset.Parse("2026-08-18T01:00:04Z"), after.TimelineCandidate!.EdgeUtc);
    }

    [Fact]
    public void DiscontinuityIncreaseTakesPrecedenceOverMediaSequenceRestart()
    {
        var tracker = new HlsPlaylistTracker();
        var before = ParseText("""
            #EXTM3U
            #EXT-X-TARGETDURATION:4
            #EXT-X-MEDIA-SEQUENCE:100
            #EXT-X-DISCONTINUITY-SEQUENCE:7
            #EXT-X-PROGRAM-DATE-TIME:2026-08-18T00:00:00Z
            #EXTINF:4,
            seg100.ts
            """);
        var restarted = ParseText("""
            #EXTM3U
            #EXT-X-TARGETDURATION:4
            #EXT-X-MEDIA-SEQUENCE:0
            #EXT-X-DISCONTINUITY-SEQUENCE:8
            #EXT-X-PROGRAM-DATE-TIME:2026-08-18T01:00:00Z
            #EXTINF:4,
            seg0.ts
            """);

        var first = tracker.Observe(Context(before, 0));
        var reset = tracker.Observe(Context(restarted, 1_000));

        Assert.Equal(HlsProgressDisposition.NewEvidence, reset.Disposition);
        Assert.Equal(HlsEpochResetReason.Discontinuity, reset.ResetReason);
        Assert.Equal(first.SourceEpoch + 1, reset.SourceEpoch);
    }

    [Fact]
    public void ConfirmedForwardProgressAfterRollbackStartsNewEpoch()
    {
        var tracker = new HlsPlaylistTracker();
        var baseline = Parse("progress/base.m3u8");
        var rollback = Parse("progress/rollback.m3u8");
        var rollbackForward = ParseText("""
            #EXTM3U
            #EXT-X-TARGETDURATION:10
            #EXT-X-MEDIA-SEQUENCE:99
            #EXT-X-PROGRAM-DATE-TIME:2026-08-18T00:00:00Z
            #EXTINF:10,
            seg99.ts
            #EXTINF:10,
            seg100.ts
            """);

        var first = tracker.Observe(Context(baseline, 0));
        var rejected = tracker.Observe(Context(rollback, 1_000));
        var confirmed = tracker.Observe(Context(rollbackForward, 2_000));

        Assert.Equal(HlsProgressDisposition.Rollback, rejected.Disposition);
        Assert.Equal(HlsProgressDisposition.NewEvidence, confirmed.Disposition);
        Assert.Equal(HlsEpochResetReason.SequenceRollback, confirmed.ResetReason);
        Assert.Equal(first.SourceEpoch + 1, confirmed.SourceEpoch);
        Assert.False(confirmed.IsEpochStable);
    }

    [Fact]
    public void NavigationSessionRenditionAndSourceChangesResetEpochDeterministically()
    {
        var tracker = new HlsPlaylistTracker();
        var parsed = Parse("progress/base.m3u8");
        var initial = Context(parsed, 0);

        var first = tracker.Observe(initial);
        var navigation = tracker.Observe(initial with { NavigationGeneration = 2, ObservedMonotonicTicks = 1_000 });
        var session = tracker.Observe(initial with
        {
            NavigationGeneration = 2,
            SessionIdentity = "session-two",
            ObservedMonotonicTicks = 2_000
        });
        var source = tracker.Observe(initial with
        {
            NavigationGeneration = 2,
            SessionIdentity = "session-two",
            SourceIdentity = "source-two",
            ObservedMonotonicTicks = 3_000
        });

        Assert.Equal(HlsEpochResetReason.InitialObservation, first.ResetReason);
        Assert.Equal(HlsEpochResetReason.Navigation, navigation.ResetReason);
        Assert.Equal(HlsEpochResetReason.Session, session.ResetReason);
        Assert.Equal(HlsEpochResetReason.Source, source.ResetReason);
        Assert.Equal(first.SourceEpoch + 3, source.SourceEpoch);
    }

    [Fact]
    public void AudioAndVideoLanesNeverMutateEachOthersProgress()
    {
        var tracker = new HlsPlaylistTracker();
        var video = Parse("mixed/video.m3u8", "video/mp2t", "video.m3u8");
        var audio = Parse("mixed/audio.m3u8", "audio/aac", "audio.m3u8");

        var videoContext = Context(video, 0) with { TimelineLaneIdentity = "primary-video" };
        var videoFirst = tracker.Observe(videoContext);
        var audioFirst = tracker.Observe(Context(audio, 500) with { TimelineLaneIdentity = "audio:a1" });
        var videoDuplicate = tracker.Observe(videoContext with { ObservedMonotonicTicks = 1_000 });

        Assert.Equal(1, videoFirst.SourceEpoch);
        Assert.Equal(1, audioFirst.SourceEpoch);
        Assert.Equal(HlsProgressDisposition.Duplicate, videoDuplicate.Disposition);
        Assert.Equal(videoFirst.SourceEpoch, videoDuplicate.SourceEpoch);
    }

    [Fact]
    public void TailGapAdvancesProgressButIsNotTimelineEvidence()
    {
        var parsed = Parse("segments/gap-map-endlist.m3u8");
        var result = new HlsPlaylistTracker().Observe(Context(parsed, 0));

        Assert.Equal(HlsProgressDisposition.NewEvidence, result.Disposition);
        Assert.False(result.IsEstimatorEvidence);
    }

    [Fact]
    public void GapObservationDoesNotCountTowardEpochStability()
    {
        var tracker = new HlsPlaylistTracker();
        var gap = Parse("segments/gap-map-endlist.m3u8");
        var first = tracker.Observe(Context(gap, 0));
        var playableDocument = gap.Document with
        {
            Segments = gap.Document.Segments
                .Select((segment, index) => index == gap.Document.Segments.Count - 1
                    ? segment with { IsGap = false, MediaSequence = segment.MediaSequence + 1 }
                    : segment)
                .ToArray()
        };
        var playableProgress = gap.ProgressKey! with
        {
            PersistenceHash = "synthetic-next-progress",
            LastMediaSequence = gap.ProgressKey.LastMediaSequence + 1
        };
        var nextContext = Context(gap, 1_000) with
        {
            Document = playableDocument,
            ProgressKey = playableProgress
        };

        var second = tracker.Observe(nextContext);

        Assert.Equal(0, first.IndependentEvidenceCount);
        Assert.Equal(1, second.IndependentEvidenceCount);
        Assert.True(second.IsEstimatorEvidence);
        Assert.False(second.IsEpochStable);
    }

    [Fact]
    public void ValidatedThirtyThreeBitPtsRolloverDoesNotResetTimeline()
    {
        const long modulus = 1L << 33;
        var unwrapper = new HlsTimestampUnwrapper();

        var first = unwrapper.Observe(modulus - 45_000);
        var second = unwrapper.Observe(45_000);

        Assert.Equal(modulus - 45_000, first.UnwrappedValue);
        Assert.Equal(modulus + 45_000, second.UnwrappedValue);
        Assert.Equal(90_000, second.UnwrappedValue - first.UnwrappedValue);
        Assert.True(second.IsRollover);
        Assert.False(second.WasReset);
    }

    private HlsPlaylistTrackingContext Context(HlsPlaylistParseResult parsed, long tick) => new()
    {
        SlotId = 1,
        NavigationGeneration = 1,
        SessionIdentity = "session-one",
        TimelineLaneIdentity = "primary-video",
        PlaylistIdentityHash = parsed.Document.PlaylistIdentity.PersistenceHash,
        RenditionKind = parsed.Document.RenditionKind,
        SourceIdentity = "source-one",
        Document = parsed.Document,
        ProgressKey = parsed.ProgressKey!,
        ObservedMonotonicTicks = tick,
        MonotonicFrequency = Frequency
    };

    private HlsPlaylistParseResult Parse(
        string relativePath,
        string contentType = "application/vnd.apple.mpegurl",
        string requestName = "index.m3u8")
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Hls",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return Assert.IsType<HlsPlaylistParseResult>(_parser.ParsePlaylist(new HlsPlaylistParseRequest(
            File.ReadAllText(path),
            $"https://cdn.example.test/live/{requestName}?token=request-secret",
            contentType,
            null,
            DateTimeOffset.Parse("2026-08-18T00:00:30Z"))));
    }

    private HlsPlaylistParseResult ParseText(string text) =>
        Assert.IsType<HlsPlaylistParseResult>(_parser.ParsePlaylist(new HlsPlaylistParseRequest(
            text,
            "https://cdn.example.test/live/index.m3u8?token=request-secret",
            "application/vnd.apple.mpegurl",
            null,
            DateTimeOffset.Parse("2026-08-18T00:00:30Z"))));
}
