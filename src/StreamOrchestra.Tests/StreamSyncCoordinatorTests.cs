using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;
using System.Diagnostics;

namespace StreamOrchestra.Tests;

public sealed class StreamSyncCoordinatorTests
{
    [Theory]
    [InlineData(0, SyncCommandType.SetRate, 1)]
    [InlineData(150, SyncCommandType.SetRate, 1)]
    [InlineData(500, SyncCommandType.SetRate, 0.99)]
    [InlineData(-500, SyncCommandType.SetRate, 1.01)]
    [InlineData(1000, SyncCommandType.SetRate, 0.98)]
    [InlineData(1500, SyncCommandType.SetRate, 0.98)]
    public void CalculateCorrection_UsesDeadbandAndProportionalRate(
        double errorMs,
        SyncCommandType expectedType,
        double expectedValue)
    {
        var command = StreamSyncCoordinator.CalculateCorrection(errorMs);

        Assert.Equal(expectedType, command.Type);
        Assert.Equal(expectedValue, command.Value!.Value, precision: 3);
    }

    [Fact]
    public async Task Tick_AlignsAbsoluteMembersAndAppliesManualDelayAsAdditionalLatency()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197);
        var second = CreateTarget(2, now, currentTime: 197.5);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);

        Assert.True(await coordinator.StartAsync());
        first.Commands.Clear();
        second.Commands.Clear();
        await coordinator.TickAsync(now.AddMilliseconds(500));

        Assert.Equal(SyncRuntimeState.Running, coordinator.RuntimeState);
        Assert.Contains(first.Commands, command => command is { Type: SyncCommandType.SetRate, Value: 1 });
        Assert.Contains(second.Commands, command =>
            command.Type == SyncCommandType.SetRate && Math.Abs(command.Value!.Value - 0.99) < 0.001);

        coordinator.SetManualDelay(2, 1000);
        second.Commands.Clear();
        await coordinator.TickAsync(now.AddMilliseconds(750));
        Assert.Contains(second.Commands, command =>
            command.Type == SyncCommandType.Seek && Math.Abs(command.Value!.Value - 196) < 0.001);

        await coordinator.StopAsync();
        Assert.Contains(second.Commands, command => command.Type == SyncCommandType.ResetRate);
    }

    [Fact]
    public async Task Tick_UserDelayImmediatelySeeksInDegradedModeWithoutUnlockingAutomaticSeek()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197);
        var second = CreateTarget(2, now, currentTime: 197);
        first.Timeline = null;
        second.Timeline = null;
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        first.Commands.Clear();

        Assert.True(coordinator.SetManualDelay(1, 3000));
        await coordinator.TickAsync(now.AddMilliseconds(500));

        var userSeek = Assert.Single(first.Commands.Where(command => command.Type == SyncCommandType.Seek));
        Assert.Equal(192, userSeek.Value!.Value, precision: 3);

        first.Commands.Clear();
        var nextObservedAt = now.AddSeconds(1);
        first.Snapshot = AdvanceSnapshot(first.Snapshot!, 198, 201, nextObservedAt);
        second.Snapshot = AdvanceSnapshot(second.Snapshot!, 198, 201, nextObservedAt);
        await coordinator.TickAsync(nextObservedAt);

        Assert.DoesNotContain(first.Commands, command => command.Type == SyncCommandType.Seek);
        Assert.Contains(first.Commands, command =>
            command.Type == SyncCommandType.SetRate && command.Value == 0.98);
    }

    [Fact]
    public async Task Start_UserDelayPresetImmediatelySeeksInDegradedMode()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197);
        var second = CreateTarget(2, now, currentTime: 197);
        first.Timeline = null;
        second.Timeline = null;
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        Assert.True(coordinator.SetManualDelay(1, 3000));

        Assert.True(await coordinator.StartAsync());

        var userSeek = Assert.Single(first.Commands.Where(command => command.Type == SyncCommandType.Seek));
        Assert.Equal(192, userSeek.Value!.Value, precision: 3);
    }

    [Fact]
    public async Task Tick_EntersRecoveryAfterConfirmedBufferingAndRaisesSafetyDelay()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        var initialSafety = coordinator.EffectiveSafetyDelayMs;
        first.Commands.Clear();
        second.Commands.Clear();

        first.Snapshot = AdvanceSnapshot(first.Snapshot!, 197, 200.1, now.AddMilliseconds(100)) with
        {
            Buffering = true,
            ReadyState = 2
        };
        second.Snapshot = AdvanceSnapshot(second.Snapshot!, 197.1, 200.1, now.AddMilliseconds(100));
        await coordinator.TickAsync(now.AddMilliseconds(100));
        first.Snapshot = AdvanceSnapshot(first.Snapshot, 197, 201.1, now.AddMilliseconds(1100));
        second.Snapshot = AdvanceSnapshot(second.Snapshot, 198.1, 201.1, now.AddMilliseconds(1100));
        await coordinator.TickAsync(now.AddMilliseconds(1100));
        first.Snapshot = AdvanceSnapshot(first.Snapshot, 197, 201.7, now.AddMilliseconds(1700));
        second.Snapshot = AdvanceSnapshot(second.Snapshot, 198.7, 201.7, now.AddMilliseconds(1700));
        await coordinator.TickAsync(now.AddMilliseconds(1700));
        first.Snapshot = AdvanceSnapshot(first.Snapshot, 197, 202.2, now.AddMilliseconds(2200));
        second.Snapshot = AdvanceSnapshot(second.Snapshot, 199.2, 202.2, now.AddMilliseconds(2200));
        await coordinator.TickAsync(now.AddMilliseconds(2200));

        Assert.Equal(SyncRuntimeState.Recovering, coordinator.RuntimeState);
        Assert.True(coordinator.EffectiveSafetyDelayMs >= initialSafety + 1000);
        Assert.Contains(first.Commands, command => command.Type == SyncCommandType.Pause);
        Assert.Contains(second.Commands, command => command.Type == SyncCommandType.Pause);
        await coordinator.StopAsync();
        Assert.Contains(first.Commands, command => command.Type == SyncCommandType.Resume);
    }

    [Fact]
    public async Task Tick_DoesNotCorrectNormallyAdvancingPlaybackAgainstAStalePlaylistObservation()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        first.Commands.Clear();
        second.Commands.Clear();

        for (var tick = 1; tick <= 8; tick++)
        {
            var observedAt = now.AddMilliseconds(tick * 500);
            first.Snapshot = AdvanceSnapshot(
                first.Snapshot!, 197 + tick * 0.5, 200 + tick * 0.5, observedAt);
            second.Snapshot = AdvanceSnapshot(
                second.Snapshot!, 197 + tick * 0.5, 200 + tick * 0.5, observedAt);
            first.Timeline = first.Timeline! with { ProgressKeyHash = $"first-progress-{tick}" };
            second.Timeline = second.Timeline! with { ProgressKeyHash = $"second-progress-{tick}" };
            await coordinator.TickAsync(observedAt);
        }

        Assert.DoesNotContain(first.Commands, command => command.Type == SyncCommandType.Seek);
        Assert.DoesNotContain(second.Commands, command => command.Type == SyncCommandType.Seek);
        Assert.All(
            first.Commands.Concat(second.Commands).Where(command => command.Type == SyncCommandType.SetRate),
            command => Assert.Equal(1, command.Value));
    }

    [Fact]
    public async Task Tick_DoesNotPauseGroupForTransientBufferingWithoutSignificantDrift()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        first.Commands.Clear();
        second.Commands.Clear();

        first.Snapshot = first.Snapshot! with { Buffering = true, ReadyState = 2 };
        await coordinator.TickAsync(now.AddMilliseconds(100));
        await coordinator.TickAsync(now.AddMilliseconds(800));
        first.Snapshot = first.Snapshot with { Buffering = false, ReadyState = 4 };
        await coordinator.TickAsync(now.AddMilliseconds(1200));

        Assert.DoesNotContain(first.Commands, command => command.Type == SyncCommandType.Pause);
        Assert.DoesNotContain(second.Commands, command => command.Type == SyncCommandType.Pause);
        Assert.NotEqual(SyncRuntimeState.Recovering, coordinator.RuntimeState);
    }

    [Fact]
    public async Task Tick_LargeDriftUsesRateCorrectionWithoutAutomaticSeek()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 199);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);

        await coordinator.StartAsync();

        Assert.DoesNotContain(first.Commands, command => command.Type == SyncCommandType.Seek);
        Assert.Contains(first.Commands, command =>
            command.Type == SyncCommandType.SetRate && command.Value == 0.98);
    }

    [Fact]
    public async Task Tick_DoesNotControlSeekingOrNonProgressingPlayer()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197.8);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        first.Commands.Clear();

        first.Snapshot = first.Snapshot! with
        {
            Seeking = true,
            ObservedAtUtc = now.AddMilliseconds(500)
        };
        second.Snapshot = second.Snapshot! with { ObservedAtUtc = now.AddMilliseconds(500) };
        await coordinator.TickAsync(now.AddMilliseconds(500));
        Assert.DoesNotContain(first.Commands, command =>
            command.Type is SyncCommandType.Seek or SyncCommandType.SetRate);

        first.Commands.Clear();
        first.Snapshot = first.Snapshot with
        {
            Seeking = false,
            PlayerProgressHealthy = false,
            ObservedAtUtc = now.AddMilliseconds(1000)
        };
        second.Snapshot = second.Snapshot with { ObservedAtUtc = now.AddMilliseconds(1000) };
        await coordinator.TickAsync(now.AddMilliseconds(1000));
        Assert.DoesNotContain(first.Commands, command =>
            command.Type is SyncCommandType.Seek or SyncCommandType.SetRate);
    }

    [Fact]
    public async Task Tick_DoesNotIssueCommandForTargetInsideSeekableGap()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 198);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        first.Commands.Clear();

        first.Snapshot = first.Snapshot! with
        {
            CurrentTime = 198,
            SeekableRanges = [new MediaTimeRange(100, 195), new MediaTimeRange(197, 200)],
            ObservedAtUtc = now.AddMilliseconds(500)
        };
        second.Snapshot = second.Snapshot! with { ObservedAtUtc = now.AddMilliseconds(500) };
        coordinator.SetManualDelay(1, 1000);
        await coordinator.TickAsync(now.AddMilliseconds(500));

        Assert.DoesNotContain(first.Commands, command => command.Type == SyncCommandType.Seek);
        Assert.DoesNotContain(first.Commands, command => command.Type == SyncCommandType.SetRate);
    }

    [Fact]
    public async Task Tick_DoesNotSeekToAnUnbufferedTarget()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 199);
        var second = CreateTarget(2, now, currentTime: 197);
        first.Snapshot = first.Snapshot! with
        {
            BufferedRanges = [new MediaTimeRange(198, 200)],
            CurrentBufferedRange = new MediaTimeRange(198, 200)
        };
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);

        await coordinator.StartAsync();

        Assert.DoesNotContain(first.Commands, command => command.Type == SyncCommandType.Seek);
    }

    [Fact]
    public async Task Tick_RecordsTerminalCommandFailureAsDegraded()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197.7);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        first.CommandResultFactory = command => SyncCommandResult.Failed(command.CommandId, "delivery-failed");
        first.Commands.Clear();
        first.Snapshot = first.Snapshot! with { ObservedAtUtc = now.AddMilliseconds(500) };
        second.Snapshot = second.Snapshot! with { ObservedAtUtc = now.AddMilliseconds(500) };

        await coordinator.TickAsync(now.AddMilliseconds(500));

        Assert.NotEmpty(first.Commands);
        Assert.Contains(first.Commands, command => command.Type == SyncCommandType.ResetRate);
        Assert.Equal(SyncRuntimeState.Degraded, coordinator.RuntimeState);
    }

    [Theory]
    [InlineData("wrong-id")]
    [InlineData("applied-only")]
    [InlineData("timed-out")]
    public async Task Tick_DoesNotTreatIncompleteOrMismatchedCommandAsVerified(string resultKind)
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197.7);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        first.Commands.Clear();
        first.CommandResultFactory = command => resultKind switch
        {
            "wrong-id" => VerifiedResult(command, first) with { CommandId = "different-command" },
            "applied-only" => new SyncCommandResult
            {
                CommandId = command.CommandId,
                Stage = SyncCommandStage.Applied,
                WasApplied = true,
                ObservedPlaybackRate = command.Value
            },
            _ => new SyncCommandResult
            {
                CommandId = command.CommandId,
                Stage = SyncCommandStage.TimedOut
            }
        };
        first.Snapshot = first.Snapshot! with { ObservedAtUtc = now.AddMilliseconds(500) };
        second.Snapshot = second.Snapshot! with { ObservedAtUtc = now.AddMilliseconds(500) };

        await coordinator.TickAsync(now.AddMilliseconds(500));

        Assert.NotEmpty(first.Commands);
        Assert.Equal(SyncRuntimeState.Degraded, coordinator.RuntimeState);
    }

    [Fact]
    public async Task FailedSeekDoesNotStartCooldownAndVerifiedRetryCanRun()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        first.Commands.Clear();
        first.CommandResultFactory = command => command.Type == SyncCommandType.Seek
            ? SyncCommandResult.Failed(command.CommandId, "position-mismatch")
            : VerifiedResult(command, first);

        coordinator.SetManualDelay(1, 1000);
        await coordinator.TickAsync(now.AddMilliseconds(100));
        Assert.Single(first.Commands.Where(command => command.Type == SyncCommandType.Seek));

        first.CommandResultFactory = null;
        coordinator.SetManualDelay(1, 1000);
        await coordinator.TickAsync(now.AddMilliseconds(200));
        Assert.Equal(2, first.Commands.Count(command => command.Type == SyncCommandType.Seek));
    }

    [Fact]
    public async Task ResumeFailureAfterRecoveryKeepsCoordinatorDegraded()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();

        first.Snapshot = first.Snapshot! with
        {
            Buffering = true,
            ReadyState = 2,
            ObservedAtUtc = now.AddMilliseconds(100)
        };
        second.Snapshot = second.Snapshot! with { ObservedAtUtc = now.AddMilliseconds(100) };
        await coordinator.TickAsync(now.AddMilliseconds(100));
        first.Snapshot = first.Snapshot with { ObservedAtUtc = now.AddMilliseconds(4200) };
        second.Snapshot = second.Snapshot with { ObservedAtUtc = now.AddMilliseconds(4200) };
        await coordinator.TickAsync(now.AddMilliseconds(4200));
        Assert.Equal(SyncRuntimeState.Recovering, coordinator.RuntimeState);

        first.CommandResultFactory = command => command.Type == SyncCommandType.Resume
            ? SyncCommandResult.Failed(command.CommandId, "resume-rejected")
            : VerifiedResult(command, first);
        first.Snapshot = first.Snapshot with
        {
            Buffering = false,
            ReadyState = 4,
            BufferSec = 5,
            ObservedAtUtc = now.AddMilliseconds(4300)
        };
        second.Snapshot = second.Snapshot with
        {
            Buffering = false,
            ReadyState = 4,
            BufferSec = 5,
            ObservedAtUtc = now.AddMilliseconds(4300)
        };
        await coordinator.TickAsync(now.AddMilliseconds(4300));

        Assert.Equal(SyncRuntimeState.Degraded, coordinator.RuntimeState);
    }

    [Fact]
    public void ReconcileMemberStreamIdentity_PreservesMembershipButResetsManualDelay()
    {
        var now = DateTimeOffset.UtcNow;
        var target = CreateTarget(1, now, 197);
        var coordinator = new StreamSyncCoordinator([target]);
        coordinator.AddMember(1);
        coordinator.SetManualDelay(1, 2500);

        target.Url = "https://play.sooplive.com/another-channel?token=changed";
        Assert.True(coordinator.ReconcileMemberStreamIdentity(1));

        var member = Assert.Single(coordinator.CapturePreset().Members);
        Assert.Equal(0, member.ManualDelayMs);
        Assert.Equal("https://play.sooplive.com/another-channel", member.CalibratedStreamUrl);
    }

    [Fact]
    public void ReconcileMemberStreamIdentity_IgnoresQueryAndFragmentChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var target = CreateTarget(1, now, 197);
        var coordinator = new StreamSyncCoordinator([target]);
        coordinator.AddMember(1);
        coordinator.SetManualDelay(1, 1200);

        target.Url = "https://play.sooplive.com/channel-1?token=renewed#player";

        Assert.False(coordinator.ReconcileMemberStreamIdentity(1));
        Assert.Equal(1200, Assert.Single(coordinator.CapturePreset().Members).ManualDelayMs);
    }

    [Fact]
    public async Task Tick_CoordinatesSixteenMembersWithinTenMillisecondsOnAverage()
    {
        var now = DateTimeOffset.UtcNow;
        var targets = Enumerable.Range(1, 16)
            .Select(slotId => CreateTarget(slotId, now, currentTime: 197))
            .ToArray();
        var coordinator = new StreamSyncCoordinator(targets);
        foreach (var target in targets)
        {
            Assert.True(coordinator.AddMember(target.SlotId));
        }

        await coordinator.StartAsync();
        await coordinator.TickAsync(now.AddMilliseconds(100));
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 50; index++)
        {
            await coordinator.TickAsync(now.AddMilliseconds(200 + index * 10));
        }
        stopwatch.Stop();
        await coordinator.StopAsync();

        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds / 50 < 10,
            $"Average coordination tick was {stopwatch.Elapsed.TotalMilliseconds / 50:0.00} ms.");
    }

    private static FakeSyncTarget CreateTarget(
        int slotId,
        DateTimeOffset observedAt,
        double currentTime)
    {
        var observedMonotonicTicks = Stopwatch.GetTimestamp();
        return new FakeSyncTarget
        {
            SlotId = slotId,
            Url = $"https://play.sooplive.com/channel-{slotId}",
            Snapshot = new SyncMemberSnapshot
            {
                HasVideo = true,
                IsSoop = true,
                CurrentTime = currentTime,
                PlaybackRate = 1,
                ReadyState = 4,
                BufferSec = 5,
                SeekableStart = 100,
                SeekableEnd = 200,
                BufferedRanges = [new MediaTimeRange(100, 200)],
                SeekableRanges = [new MediaTimeRange(100, 200)],
                CurrentBufferedRange = new MediaTimeRange(100, 200),
                CurrentSeekableRange = new MediaTimeRange(100, 200),
                HostReceivedMonotonicTicks = observedMonotonicTicks,
                HostMonotonicFrequency = Stopwatch.Frequency,
                UsedVideoFrameCallback = true,
                PresentedMediaTimeSeconds = currentTime,
                FrameAgeMilliseconds = 0,
                PageSampleMonotonicMilliseconds = 1000,
                ExpectedDisplayMonotonicMilliseconds = 1000,
                EffectiveMediaTimeSeconds = currentTime,
                MediaClockSource = SyncPlayerClockSource.RequestVideoFrameCallback,
                ObservedAtUtc = observedAt
            },
            Timeline = new TimelineObservation
            {
                Source = SyncTimelineSource.ProgramDateTime,
                EdgeUtc = observedAt,
                MediaToUtcOffsetMs = observedAt.ToUnixTimeMilliseconds() - 200000,
                SegmentDurationSec = 1,
                Confidence = 1,
                ObservedAtUtc = observedAt,
                ObservedMonotonicTicks = observedMonotonicTicks,
                MonotonicFrequency = Stopwatch.Frequency,
                StaleAfterSeconds = 15,
                PlaylistIdentityHash = $"playlist-{slotId}",
                ProgressKeyHash = $"progress-{slotId}-0",
                SourceEpoch = 1,
                IsEpochStable = true,
                IndependentEvidenceCount = 3
            }
        };
    }

    private static SyncMemberSnapshot AdvanceSnapshot(
        SyncMemberSnapshot snapshot,
        double currentTime,
        double seekableEnd,
        DateTimeOffset observedAt) => snapshot with
    {
        CurrentTime = currentTime,
        EffectiveMediaTimeSeconds = currentTime,
        PresentedMediaTimeSeconds = currentTime,
        SeekableEnd = seekableEnd,
        BufferedRanges = [new MediaTimeRange(100, seekableEnd)],
        SeekableRanges = [new MediaTimeRange(100, seekableEnd)],
        CurrentBufferedRange = new MediaTimeRange(100, seekableEnd),
        CurrentSeekableRange = new MediaTimeRange(100, seekableEnd),
        HostReceivedMonotonicTicks = Stopwatch.GetTimestamp(),
        ObservedAtUtc = observedAt
    };

    private static SyncCommandResult VerifiedResult(SyncCommand command, FakeSyncTarget target) => new()
    {
        CommandId = command.CommandId,
        Stage = SyncCommandStage.Verified,
        WasApplied = true,
        WasVerified = true,
        ObservedMediaTimeSeconds = command.Value ?? target.Snapshot?.CurrentTime,
        ObservedPlaybackRate = command.Type == SyncCommandType.ResetRate
            ? 1
            : command.Value ?? target.Snapshot?.PlaybackRate,
        ObservedPaused = command.Type switch
        {
            SyncCommandType.Pause => true,
            SyncCommandType.Resume => false,
            _ => target.Snapshot?.Paused
        },
        OutcomeCode = "verified"
    };

    private sealed class FakeSyncTarget : IStreamSyncTarget
    {
        public required int SlotId { get; init; }

        public string Url { get; set; } = "about:blank";

        public string CurrentUrl => Url;

        public string CurrentStreamName => $"Stream {SlotId}";

        public string SyncDisplayName => CurrentStreamName;

        public SyncMemberSnapshot? Snapshot { get; set; }

        public TimelineObservation? Timeline { get; set; }

        public SyncMemberSnapshot? LatestSyncSnapshot => Snapshot;

        public TimelineObservation? LatestTimeline => Timeline;

        public List<SyncCommand> Commands { get; } = [];

        public Func<SyncCommand, SyncCommandResult>? CommandResultFactory { get; set; }

        public SyncBadgeState? Badge { get; private set; }

        public Task<SyncCommandResult> ExecuteSyncCommandAsync(SyncCommand command)
        {
            Commands.Add(command);
            return Task.FromResult(CommandResultFactory?.Invoke(command) ?? new SyncCommandResult
            {
                CommandId = command.CommandId,
                Stage = SyncCommandStage.Verified,
                WasApplied = true,
                WasVerified = true,
                ObservedMediaTimeSeconds = command.Type == SyncCommandType.Seek
                    ? command.Value
                    : Snapshot?.CurrentTime,
                ObservedPlaybackRate = command.Type switch
                {
                    SyncCommandType.SetRate => command.Value,
                    SyncCommandType.ResetRate => 1,
                    _ => Snapshot?.PlaybackRate
                },
                ObservedPaused = command.Type switch
                {
                    SyncCommandType.Pause => true,
                    SyncCommandType.Resume => false,
                    _ => Snapshot?.Paused
                },
                OutcomeCode = "verified"
            });
        }

        public void SetSyncBadge(SyncBadgeState state)
        {
            Badge = state;
        }
    }

}
