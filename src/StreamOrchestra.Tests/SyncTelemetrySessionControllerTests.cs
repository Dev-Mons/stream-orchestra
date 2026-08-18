using System.Diagnostics;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncTelemetrySessionControllerTests
{
    [Fact]
    public void Controller_IsDisabledUntilSessionIsExplicitlyStarted()
    {
        var controller = new SyncTelemetrySessionController();

        Assert.False(controller.IsEnabled);
        Assert.Equal("", controller.SessionId);
        Assert.False(controller.HasCompletedSession);
        Assert.Null(controller.CompletedSnapshot);
        Assert.Same(SyncTelemetrySummary.Disabled, controller.CreateSummary());
    }

    [Fact]
    public void StartAndStopSession_SwitchesStableWrapperAndRetainsBoundedSnapshot()
    {
        var controller = new SyncTelemetrySessionController();
        var enabledChanges = new List<bool>();
        controller.EnabledChanged += enabledChanges.Add;
        var started = controller.StartSession(new SyncTelemetryRecorderOptions
        {
            SessionId = "explicit-consent-session",
            AppVersion = "0.10.1",
            RuntimeBucket = "webview-1",
            MaxEventsPerCategory = 2
        });

        Assert.True(started);
        Assert.True(controller.IsEnabled);
        Assert.False(controller.StartSession());

        var sample = new SyncTelemetryClockSample(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
        controller.RecordManualEvent(new SyncManualEventTelemetry(
            controller.SessionId,
            1,
            "manual-event",
            "channel-identity",
            "broadcast-session",
            "alignment-confirmed",
            sample,
            100,
            0,
            50,
            150,
            IsStableFinalAcceptance: true,
            IsIndependentSession: true));

        var snapshot = controller.StopSession();

        Assert.NotNull(snapshot);
        Assert.False(controller.IsEnabled);
        Assert.True(controller.HasCompletedSession);
        Assert.Same(snapshot, controller.CompletedSnapshot);
        Assert.Single(snapshot!.Sessions);
        Assert.Single(snapshot.ManualEvents);
        Assert.NotEqual("explicit-consent-session", snapshot.Sessions[0].SessionId);
        Assert.NotEqual("channel-identity", snapshot.ManualEvents[0].StableChannelHash);
        Assert.Same(SyncTelemetrySummary.Disabled, controller.CreateSummary());
        Assert.Null(controller.StopSession());
        Assert.Equal([true, false], enabledChanges);
    }

    [Fact]
    public void StartingAnotherSession_DiscardsOnlyThePreviousCompletedSnapshot()
    {
        var controller = new SyncTelemetrySessionController();
        Assert.True(controller.StartSession());
        var first = controller.StopSession();
        Assert.Same(first, controller.CompletedSnapshot);

        Assert.True(controller.StartSession());

        Assert.True(controller.IsEnabled);
        Assert.False(controller.HasCompletedSession);
        Assert.Null(controller.CompletedSnapshot);
    }

    [Fact]
    public void DeleteCompletedSession_IsImmediateAndIdempotent()
    {
        var controller = new SyncTelemetrySessionController();
        Assert.True(controller.StartSession());
        Assert.NotNull(controller.StopSession());

        Assert.True(controller.DeleteCompletedSession());
        Assert.False(controller.HasCompletedSession);
        Assert.Null(controller.CompletedSnapshot);
        Assert.False(controller.DeleteCompletedSession());
    }
}
