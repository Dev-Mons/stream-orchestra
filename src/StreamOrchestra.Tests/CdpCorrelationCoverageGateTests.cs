using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class CdpCorrelationCoverageGateTests
{
    [Fact]
    public void Evaluate_PassesOnlyWhenEveryRuntimeMeetsPreregisteredCoverageAndErrorLimits()
    {
        var first = Snapshot("runtime-a", Enumerable.Repeat("correlated", 100).ToArray());
        var second = Snapshot("runtime-b", Enumerable.Repeat("correlated", 100).ToArray());

        var report = CdpCorrelationCoverageGate.Evaluate([first, second]);

        Assert.True(report.HardSeekCoverageGatePassed);
        Assert.Equal(2, report.Runtimes.Count);
        Assert.All(report.Runtimes, runtime =>
        {
            Assert.Equal(100, runtime.AttemptCount);
            Assert.Equal(1, runtime.CoverageRate);
            Assert.True(runtime.GatePassed);
        });
        Assert.Empty(report.Reasons);
    }

    [Theory]
    [InlineData("ambiguous", "ambiguous-above-threshold")]
    [InlineData("invalid", "invalid-above-threshold")]
    [InlineData("runtime-mismatch", "runtime-mismatch")]
    public void Evaluate_KeepsHardSeekClosedForAssociationOrRuntimeFault(
        string faultStatus,
        string expectedReason)
    {
        var statuses = Enumerable.Repeat("correlated", 98)
            .Concat(Enumerable.Repeat(faultStatus, 2))
            .ToArray();

        var report = CdpCorrelationCoverageGate.Evaluate([Snapshot("runtime", statuses)]);

        Assert.False(report.HardSeekCoverageGatePassed);
        Assert.Contains(report.Reasons, reason => reason.EndsWith(expectedReason, StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_KeepsHardSeekClosedUntilMinimumSampleCountIsReached()
    {
        var report = CdpCorrelationCoverageGate.Evaluate(
            [Snapshot("runtime", Enumerable.Repeat("correlated", 99).ToArray())]);

        Assert.False(report.HardSeekCoverageGatePassed);
        Assert.Contains("runtime:insufficient-attempts", report.Reasons);
    }

    private static SyncTelemetrySnapshot Snapshot(string runtime, IReadOnlyList<string> statuses)
    {
        var now = DateTimeOffset.Parse("2026-08-18T00:00:00Z");
        var session = $"session-{runtime}";
        var events = statuses.Select((status, index) => new SyncNetworkTelemetry(
            session,
            1,
            $"request-{index}",
            new SyncUrlIdentity("https", "host", "depth-1.m3u8", $"url-{index}"),
            "playlist",
            new SyncTelemetryClockSample(now.AddMilliseconds(index), index + 1),
            new SyncTelemetryClockSample(now.AddMilliseconds(index + 1), index + 2),
            new SyncTelemetryClockSample(now.AddMilliseconds(index + 2), index + 3),
            200,
            "application/vnd.apple.mpegurl")
        {
            RequestStartObserved = status == "correlated",
            CorrelationSource = "cdp-network",
            CorrelationStatus = status
        }).ToArray();
        return new SyncTelemetrySnapshot(
            SyncTelemetrySchema.SchemaVersion,
            SyncTelemetrySchema.ModelVersion,
            true,
            now.AddMinutes(20),
            [new SyncSessionTelemetry(
                session,
                new SyncTelemetryClockSample(now, 1),
                "test",
                runtime,
                1000)],
            events,
            [],
            [],
            [],
            [],
            [],
            [],
            0);
    }
}
