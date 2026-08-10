using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class HlsTimelineParserTests
{
    private readonly HlsTimelineParser _parser = new();

    [Fact]
    public void Parse_UsesProgramDateTimeAtItsSegmentBoundary()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-10T12:00:10Z");
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

        var result = _parser.Parse(playlist, responseDateUtc: null, observedAt);

        Assert.NotNull(result);
        Assert.Equal(SyncTimelineSource.ProgramDateTime, result!.Source);
        Assert.Equal(DateTimeOffset.Parse("2026-08-10T12:00:07Z"), result.EdgeUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-10T12:00:00Z").ToUnixTimeMilliseconds() - 12000,
            result.MediaToUtcOffsetMs);
        Assert.Equal(3, result.SegmentDurationSec);
        Assert.Equal(1, result.Confidence);
    }

    [Fact]
    public void Parse_UsesCdnDateWhenFirstPtsIsAvailable()
    {
        var responseDate = DateTimeOffset.Parse("2026-08-10T12:00:10Z");
        var playlist = """
            #EXTM3U
            #EXT-X-FIRST-SEGMENT-TIMESTAMP:100000000
            #EXTINF:2.0,
            first.ts
            #EXTINF:3.0,
            second.ts
            """;

        var result = _parser.Parse(playlist, responseDate, responseDate);

        Assert.NotNull(result);
        Assert.Equal(SyncTimelineSource.CdnDate, result!.Source);
        Assert.Equal(responseDate, result.EdgeUtc);
        Assert.Equal(responseDate.ToUnixTimeMilliseconds() - 15000, result.MediaToUtcOffsetMs);
        Assert.Equal(2.5, result.SegmentDurationSec);
    }

    [Fact]
    public void Parse_AllowsProgramDateTimeWithoutFirstPts()
    {
        var playlist = """
            #EXTM3U
            #EXT-X-PROGRAM-DATE-TIME:2026-08-10T12:00:00Z
            #EXTINF:2.0,
            first.ts
            """;

        var result = _parser.Parse(playlist, responseDateUtc: null, DateTimeOffset.UtcNow);

        Assert.NotNull(result);
        Assert.Equal(DateTimeOffset.Parse("2026-08-10T12:00:02Z"), result!.EdgeUtc);
        Assert.Null(result.MediaToUtcOffsetMs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000\nvariant.m3u8")]
    [InlineData("#EXTM3U\n#EXTINF:not-a-number,\nsegment.ts")]
    public void Parse_RejectsPlaylistWithoutUsableMediaSegments(string playlist)
    {
        Assert.Null(_parser.Parse(playlist, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }
}
