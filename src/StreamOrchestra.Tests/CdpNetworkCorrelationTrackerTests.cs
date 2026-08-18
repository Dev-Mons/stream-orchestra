using System.Diagnostics;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class CdpNetworkCorrelationTrackerTests
{
    private static readonly DateTimeOffset HostUtc = DateTimeOffset.Parse("2026-08-18T03:00:00Z");

    [Fact]
    public void RuntimeValidator_RequiresExpectedBrowserVersionShape()
    {
        var compatible = CdpRuntimeSchemaValidator.ValidateBrowserVersion(
            """{"protocolVersion":"1.3","product":"Chrome/140.0.0.0"}""",
            "140.0");
        var mismatch = CdpRuntimeSchemaValidator.ValidateBrowserVersion(
            """{"protocolVersion":"1.3"}""",
            "140.0");

        Assert.True(compatible.IsCompatible);
        Assert.Equal("compatible", compatible.Reason);
        Assert.False(mismatch.IsCompatible);
        Assert.Equal("browser-version-schema-mismatch", mismatch.Reason);
    }

    [Fact]
    public void CompleteLifecycle_CorrelatesRequestFrameNavigationAndOrderedTimestamps()
    {
        var tracker = Tracker();
        const string url = "https://edge.example.test/live/video.m3u8?token=memory-only";
        var startTicks = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveRequestWillBeSent(
            Request("request-1", "frame-7", url, 100, 1_777_000_000),
            startTicks,
            HostUtc,
            navigationGeneration: 4));
        Assert.True(tracker.ObserveResponseReceived(
            Response("request-1", url, 100.2, 200, "application/vnd.apple.mpegurl"),
            startTicks + Stopwatch.Frequency / 5,
            HostUtc.AddMilliseconds(200)));
        Assert.True(tracker.ObserveLoadingFinished(
            Finished("request-1", 100.4, 1500),
            startTicks + Stopwatch.Frequency * 2 / 5,
            HostUtc.AddMilliseconds(400)));

        var result = tracker.MatchResponse(
            url,
            200,
            "application/vnd.apple.mpegurl; charset=utf-8",
            4,
            startTicks + Stopwatch.Frequency / 2);

        Assert.Equal("correlated", result.Status);
        Assert.Equal("request-1", result.RequestId);
        Assert.Equal("frame-7", result.FrameId);
        Assert.Equal(4, result.NavigationGeneration);
        Assert.True(result.SchemaCompatible);
        Assert.NotNull(result.RequestStartedAt);
        Assert.NotNull(result.HeadersReceivedAt);
        Assert.NotNull(result.BodyCompletedAt);
        Assert.True(result.RequestStartedAt!.MonotonicTicks <= result.HeadersReceivedAt!.MonotonicTicks);
        Assert.True(result.HeadersReceivedAt.MonotonicTicks <= result.BodyCompletedAt!.MonotonicTicks);
        Assert.Equal(2048, result.EncodedBodyLengthBucket);
        Assert.Equal(0, tracker.TrackedRequestCount);
    }

    [Fact]
    public void SameUrlParallelRequests_AreAmbiguousAndNeverGuessed()
    {
        var tracker = Tracker();
        const string url = "https://edge.example.test/live/video.m3u8";
        var ticks = Stopwatch.GetTimestamp();
        foreach (var requestId in new[] { "request-a", "request-b" })
        {
            Assert.True(tracker.ObserveRequestWillBeSent(
                Request(requestId, "frame", url, 100, 1_777_000_000),
                ticks,
                HostUtc,
                2));
            Assert.True(tracker.ObserveResponseReceived(
                Response(requestId, url, 100.1, 200, "application/vnd.apple.mpegurl"),
                ticks + 1,
                HostUtc));
        }

        var result = tracker.MatchResponse(url, 200, "application/vnd.apple.mpegurl", 2, ticks + 2);

        Assert.Equal("ambiguous", result.Status);
        Assert.Equal("", result.RequestId);
    }

    [Fact]
    public void MalformedRequiredEventSchema_ForcesRuntimeMismatchFallback()
    {
        var tracker = Tracker();
        var ticks = Stopwatch.GetTimestamp();

        Assert.False(tracker.ObserveRequestWillBeSent(
            """{"requestId":"missing-request-object","timestamp":1}""",
            ticks,
            HostUtc,
            1));
        var result = tracker.MatchResponse(
            "https://example.test/live.m3u8",
            200,
            "application/vnd.apple.mpegurl",
            1,
            ticks);

        Assert.False(tracker.IsSchemaCompatible);
        Assert.Equal("runtime-mismatch", result.Status);
        Assert.False(result.SchemaCompatible);
    }

    [Fact]
    public void ReversedLifecycleOrStatusMismatch_IsInvalid()
    {
        var tracker = Tracker();
        const string url = "https://edge.example.test/live/video.m3u8";
        var ticks = Stopwatch.GetTimestamp();
        Assert.True(tracker.ObserveRequestWillBeSent(
            Request("request", "frame", url, 100, 1_777_000_000),
            ticks,
            HostUtc,
            1));
        Assert.True(tracker.ObserveResponseReceived(
            Response("request", url, 100.2, 201, "application/vnd.apple.mpegurl"),
            ticks + 1,
            HostUtc));
        Assert.True(tracker.ObserveLoadingFinished(
            Finished("request", 100.1, 10),
            ticks + 2,
            HostUtc));

        var result = tracker.MatchResponse(url, 200, "application/vnd.apple.mpegurl", 1, ticks + 3);

        Assert.Equal("invalid", result.Status);
    }

    [Fact]
    public void LoadingFailedLifecycle_IsNeverPromotedToCorrelated()
    {
        var tracker = Tracker();
        const string url = "https://edge.example.test/live/video.m3u8";
        var ticks = Stopwatch.GetTimestamp();
        Assert.True(tracker.ObserveRequestWillBeSent(
            Request("request", "frame", url, 100, 1_777_000_000),
            ticks,
            HostUtc,
            1));
        Assert.True(tracker.ObserveResponseReceived(
            Response("request", url, 100.1, 200, "application/vnd.apple.mpegurl"),
            ticks + 1,
            HostUtc));
        Assert.True(tracker.ObserveLoadingFailed(
            """{"requestId":"request","timestamp":100.2,"errorText":"cancelled"}""",
            ticks + 2,
            HostUtc));

        var result = tracker.MatchResponse(url, 200, "application/vnd.apple.mpegurl", 1, ticks + 3);

        Assert.Equal("invalid", result.Status);
    }

    [Fact]
    public void SubFiveMillisecondRuntimeClockSkew_IsClampedButLargerReversalStaysInvalid()
    {
        const string url = "https://edge.example.test/live/video.m3u8";
        var ticks = Stopwatch.GetTimestamp();
        var small = Tracker();
        Assert.True(small.ObserveRequestWillBeSent(
            Request("small", "frame", url, 100, 1_777_000_000),
            ticks,
            HostUtc,
            1));
        Assert.True(small.ObserveResponseReceived(
            Response("small", url, 100.2, 200, "application/vnd.apple.mpegurl"),
            ticks + 1,
            HostUtc));
        Assert.True(small.ObserveLoadingFinished(
            Finished("small", 100.1998, 10),
            ticks + 2,
            HostUtc));

        var adjusted = small.MatchResponse(
            url,
            200,
            "application/vnd.apple.mpegurl",
            1,
            ticks + 3);

        Assert.Equal("correlated", adjusted.Status);
        Assert.True(adjusted.LifecycleClockSkewAdjusted);
        Assert.Equal(
            adjusted.HeadersReceivedAt!.MonotonicTicks,
            adjusted.BodyCompletedAt!.MonotonicTicks);

        var large = Tracker();
        Assert.True(large.ObserveRequestWillBeSent(
            Request("large", "frame", url, 100, 1_777_000_000),
            ticks,
            HostUtc,
            1));
        Assert.True(large.ObserveResponseReceived(
            Response("large", url, 100.2, 200, "application/vnd.apple.mpegurl"),
            ticks + 1,
            HostUtc));
        Assert.True(large.ObserveLoadingFinished(
            Finished("large", 100.19, 10),
            ticks + 2,
            HostUtc));

        var invalid = large.MatchResponse(
            url,
            200,
            "application/vnd.apple.mpegurl",
            1,
            ticks + 3);

        Assert.Equal("invalid", invalid.Status);
    }

    [Fact]
    public void ExtremeFiniteClockValues_DoNotEscapeFaultFallback()
    {
        var tracker = Tracker();
        const string url = "https://edge.example.test/live/video.m3u8";
        var ticks = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveRequestWillBeSent(
            Request("request", "frame", url, 1e300, 1e300),
            ticks,
            HostUtc,
            1));
        Assert.True(tracker.ObserveResponseReceived(
            Response("request", url, -1e300, 200, "application/vnd.apple.mpegurl"),
            ticks + 1,
            HostUtc));

        var result = tracker.MatchResponse(url, 200, "application/vnd.apple.mpegurl", 1, ticks + 2);

        Assert.Equal("invalid", result.Status);
    }

    private static CdpNetworkCorrelationTracker Tracker() => new(new CdpRuntimeCompatibility(
        true,
        "1.3",
        "Chrome/140.0.0.0",
        "140.0",
        "compatible"));

    private static string Request(
        string requestId,
        string frameId,
        string url,
        double timestamp,
        double wallTime) => $$"""
        {
          "requestId":"{{requestId}}",
          "loaderId":"loader",
          "frameId":"{{frameId}}",
          "timestamp":{{timestamp}},
          "wallTime":{{wallTime}},
          "request":{"url":"{{url}}"}
        }
        """;

    private static string Response(
        string requestId,
        string url,
        double timestamp,
        int status,
        string mimeType) => $$"""
        {
          "requestId":"{{requestId}}",
          "timestamp":{{timestamp}},
          "response":{
            "url":"{{url}}",
            "status":{{status}},
            "mimeType":"{{mimeType}}",
            "fromDiskCache":false,
            "fromServiceWorker":false
          }
        }
        """;

    private static string Finished(string requestId, double timestamp, double length) => $$"""
        {"requestId":"{{requestId}}","timestamp":{{timestamp}},"encodedDataLength":{{length}}}
        """;
}
