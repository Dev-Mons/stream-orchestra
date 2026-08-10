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
    [InlineData(1500, SyncCommandType.Seek, 50)]
    public void CalculateCorrection_UsesDeadbandProportionalRateAndHardSeek(
        double errorMs,
        SyncCommandType expectedType,
        double expectedValue)
    {
        var command = StreamSyncCoordinator.CalculateCorrection(errorMs, mediaTarget: 50);

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

        first.Snapshot = first.Snapshot! with
        {
            Buffering = true,
            ReadyState = 2,
            ObservedAtUtc = now.AddMilliseconds(100),
            SeekableEnd = 200.1
        };
        second.Snapshot = second.Snapshot! with
        {
            CurrentTime = 197.1,
            ObservedAtUtc = now.AddMilliseconds(100),
            SeekableEnd = 200.1
        };
        await coordinator.TickAsync(now.AddMilliseconds(100));
        first.Snapshot = first.Snapshot with
        {
            ObservedAtUtc = now.AddMilliseconds(1100),
            SeekableEnd = 201.1
        };
        second.Snapshot = second.Snapshot with
        {
            CurrentTime = 198.1,
            ObservedAtUtc = now.AddMilliseconds(1100),
            SeekableEnd = 201.1
        };
        await coordinator.TickAsync(now.AddMilliseconds(1100));
        first.Snapshot = first.Snapshot with
        {
            ObservedAtUtc = now.AddMilliseconds(1700),
            SeekableEnd = 201.7
        };
        second.Snapshot = second.Snapshot with
        {
            CurrentTime = 198.7,
            ObservedAtUtc = now.AddMilliseconds(1700),
            SeekableEnd = 201.7
        };
        await coordinator.TickAsync(now.AddMilliseconds(1700));
        first.Snapshot = first.Snapshot with
        {
            ObservedAtUtc = now.AddMilliseconds(2200),
            SeekableEnd = 202.2
        };
        second.Snapshot = second.Snapshot with
        {
            CurrentTime = 199.2,
            ObservedAtUtc = now.AddMilliseconds(2200),
            SeekableEnd = 202.2
        };
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
            first.Snapshot = first.Snapshot! with
            {
                CurrentTime = 197 + tick * 0.5,
                SeekableEnd = 200 + tick * 0.5,
                ObservedAtUtc = observedAt
            };
            second.Snapshot = second.Snapshot! with
            {
                CurrentTime = 197 + tick * 0.5,
                SeekableEnd = 200 + tick * 0.5,
                ObservedAtUtc = observedAt
            };
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
    public async Task Tick_RequiresPersistentLargeDriftBeforeHardSeek()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTarget(1, now, currentTime: 197);
        var second = CreateTarget(2, now, currentTime: 197);
        var coordinator = new StreamSyncCoordinator([first, second]);
        coordinator.AddMember(1);
        coordinator.AddMember(2);
        await coordinator.StartAsync();
        first.Commands.Clear();

        for (var tick = 1; tick <= 2; tick++)
        {
            var observedAt = now.AddMilliseconds(tick * 500);
            first.Snapshot = first.Snapshot! with
            {
                CurrentTime = 199 + tick * 0.5,
                SeekableEnd = 200 + tick * 0.5,
                ObservedAtUtc = observedAt
            };
            second.Snapshot = second.Snapshot! with
            {
                CurrentTime = 197 + tick * 0.5,
                SeekableEnd = 200 + tick * 0.5,
                ObservedAtUtc = observedAt
            };
            await coordinator.TickAsync(observedAt);
        }

        Assert.DoesNotContain(first.Commands, command => command.Type == SyncCommandType.Seek);

        var thirdObservedAt = now.AddMilliseconds(1500);
        first.Snapshot = first.Snapshot! with
        {
            CurrentTime = 200.5,
            SeekableEnd = 201.5,
            ObservedAtUtc = thirdObservedAt
        };
        second.Snapshot = second.Snapshot! with
        {
            CurrentTime = 198.5,
            SeekableEnd = 201.5,
            ObservedAtUtc = thirdObservedAt
        };
        await coordinator.TickAsync(thirdObservedAt);

        Assert.Contains(first.Commands, command => command.Type == SyncCommandType.Seek);
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
                ObservedAtUtc = observedAt
            },
            Timeline = new TimelineObservation
            {
                Source = SyncTimelineSource.ProgramDateTime,
                EdgeUtc = observedAt,
                MediaToUtcOffsetMs = observedAt.ToUnixTimeMilliseconds() - 200000,
                SegmentDurationSec = 1,
                Confidence = 1,
                ObservedAtUtc = observedAt
            }
        };
    }

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

        public SyncBadgeState? Badge { get; private set; }

        public Task ExecuteSyncCommandAsync(SyncCommand command)
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }

        public void SetSyncBadge(SyncBadgeState state)
        {
            Badge = state;
        }
    }
}
