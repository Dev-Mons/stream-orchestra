using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class SyncPlayerMediaClock
{
    private const double MaximumFrameAgeMilliseconds = 2000;
    private const double MaximumProjectionMilliseconds = 2000;

    public static SyncMemberSnapshot Resolve(SyncMemberSnapshot snapshot)
    {
        var fallback = double.IsFinite(snapshot.CurrentTime) ? snapshot.CurrentTime : 0;
        if (!snapshot.UsedVideoFrameCallback ||
            snapshot.PresentedMediaTimeSeconds is not { } frameMediaTime ||
            !double.IsFinite(frameMediaTime) ||
            snapshot.FrameAgeMilliseconds is not { } frameAge ||
            !double.IsFinite(frameAge) ||
            frameAge is < 0 or > MaximumFrameAgeMilliseconds ||
            snapshot.PageSampleMonotonicMilliseconds is not { } sampledAt ||
            snapshot.ExpectedDisplayMonotonicMilliseconds is not { } expectedDisplayAt ||
            !double.IsFinite(sampledAt) ||
            !double.IsFinite(expectedDisplayAt))
        {
            return snapshot with
            {
                EffectiveMediaTimeSeconds = fallback,
                MediaClockSource = SyncPlayerClockSource.CurrentTimeFallback
            };
        }

        var projectionMilliseconds = snapshot.Paused || snapshot.Seeking
            ? 0
            : Math.Clamp(sampledAt - expectedDisplayAt, 0, MaximumProjectionMilliseconds);
        var rate = double.IsFinite(snapshot.PlaybackRate) ? snapshot.PlaybackRate : 1;
        var effectiveMediaTime = frameMediaTime + projectionMilliseconds / 1000 * rate;
        if (!double.IsFinite(effectiveMediaTime))
        {
            return snapshot with
            {
                EffectiveMediaTimeSeconds = fallback,
                MediaClockSource = SyncPlayerClockSource.CurrentTimeFallback
            };
        }

        return snapshot with
        {
            EffectiveMediaTimeSeconds = effectiveMediaTime,
            MediaClockSource = SyncPlayerClockSource.RequestVideoFrameCallback
        };
    }

    public static double GetEffectiveMediaTime(SyncMemberSnapshot snapshot) =>
        double.IsFinite(snapshot.EffectiveMediaTimeSeconds) &&
        (snapshot.MediaClockSource == SyncPlayerClockSource.RequestVideoFrameCallback ||
         snapshot.EffectiveMediaTimeSeconds != 0 || snapshot.CurrentTime == 0)
            ? snapshot.EffectiveMediaTimeSeconds
            : snapshot.CurrentTime;
}
