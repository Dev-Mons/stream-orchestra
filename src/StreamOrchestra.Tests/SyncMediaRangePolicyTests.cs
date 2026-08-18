using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncMediaRangePolicyTests
{
    [Fact]
    public void NormalizePreservesRealGapsAndMergesOverlaps()
    {
        var ranges = SyncMediaRangePolicy.Normalize([
            new MediaTimeRange(110, 120),
            new MediaTimeRange(100, 105),
            new MediaTimeRange(104.5, 106),
            new MediaTimeRange(double.NaN, 200)
        ]);

        Assert.Equal([new MediaTimeRange(100, 106), new MediaTimeRange(110, 120)], ranges);
        Assert.Null(SyncMediaRangePolicy.FindContainingRange(ranges, 107));
        Assert.Equal(new MediaTimeRange(110, 120), SyncMediaRangePolicy.FindContainingRange(ranges, 115));
    }

    [Fact]
    public void HighConfidenceSeekRequiresExplicitBufferedAndSeekableCoverage()
    {
        var snapshot = new SyncMemberSnapshot
        {
            SeekableStart = 100,
            SeekableEnd = 120,
            SeekableRanges = [new MediaTimeRange(100, 120)],
            BufferedRanges = [new MediaTimeRange(100, 105), new MediaTimeRange(110, 120)]
        };

        Assert.False(SyncMediaRangePolicy.IsHighConfidenceSeekTargetValid(snapshot, 107));
        Assert.True(SyncMediaRangePolicy.IsHighConfidenceSeekTargetValid(snapshot, 115));
        Assert.False(SyncMediaRangePolicy.IsHighConfidenceSeekTargetValid(
            snapshot with { BufferedRanges = [] },
            115));
    }

    [Fact]
    public void PlayableRangesAreTheContiguousSeekableBufferedIntersection()
    {
        var snapshot = new SyncMemberSnapshot
        {
            SeekableRanges = [new MediaTimeRange(100, 105), new MediaTimeRange(110, 120)],
            BufferedRanges = [new MediaTimeRange(102, 115)]
        };

        Assert.Equal(
            [new MediaTimeRange(102, 105), new MediaTimeRange(110, 115)],
            SyncMediaRangePolicy.GetPlayableRanges(snapshot));
    }

    [Fact]
    public void SeekValidationUsesContiguousRangesInsteadOfScalarEnvelope()
    {
        var snapshot = new SyncMemberSnapshot
        {
            SeekableStart = 100,
            SeekableEnd = 120,
            SeekableRanges = [new MediaTimeRange(100, 105), new MediaTimeRange(110, 120)]
        };

        Assert.False(SyncMediaRangePolicy.IsSeekTargetValid(snapshot, 107));
        Assert.True(SyncMediaRangePolicy.IsSeekTargetValid(snapshot, 115));
    }
}
