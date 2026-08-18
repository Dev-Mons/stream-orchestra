using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncPlayerMediaClockTests
{
    [Fact]
    public void ResolveProjectsFreshRvfcMediaTimeToPageSampleTime()
    {
        var resolved = SyncPlayerMediaClock.Resolve(new SyncMemberSnapshot
        {
            CurrentTime = 12.8,
            PlaybackRate = 1.02,
            UsedVideoFrameCallback = true,
            PresentedMediaTimeSeconds = 12.5,
            FrameAgeMilliseconds = 250,
            PageSampleMonotonicMilliseconds = 1250,
            ExpectedDisplayMonotonicMilliseconds = 1000
        });

        Assert.Equal(SyncPlayerClockSource.RequestVideoFrameCallback, resolved.MediaClockSource);
        Assert.Equal(12.755, resolved.EffectiveMediaTimeSeconds, precision: 6);
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 2000.001)]
    public void ResolveUsesCurrentTimeFallbackWhenRvfcIsUnavailableOrStale(
        bool rvfcSupported,
        double frameAgeMilliseconds)
    {
        var resolved = SyncPlayerMediaClock.Resolve(new SyncMemberSnapshot
        {
            CurrentTime = 20.25,
            PlaybackRate = 1,
            UsedVideoFrameCallback = rvfcSupported,
            PresentedMediaTimeSeconds = 19,
            FrameAgeMilliseconds = frameAgeMilliseconds,
            PageSampleMonotonicMilliseconds = 2500,
            ExpectedDisplayMonotonicMilliseconds = 1000
        });

        Assert.Equal(SyncPlayerClockSource.CurrentTimeFallback, resolved.MediaClockSource);
        Assert.Equal(20.25, resolved.EffectiveMediaTimeSeconds);
    }
}
