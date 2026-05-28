using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task RunAutomaticCheckAsync_ReturnsDisabled_WhenAutoUpdateOff()
    {
        var checker = new FakeUpdateChecker();
        var service = new UpdateService(
            checker,
            new AutoUpdateState { Enabled = false },
            UpdateService.DefaultMinimumCheckInterval,
            () => DateTimeOffset.UtcNow);

        var result = await service.RunAutomaticCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Disabled, result.Outcome);
        Assert.Equal(0, checker.CheckCallCount);
    }

    [Fact]
    public async Task RunAutomaticCheckAsync_ReturnsThrottled_WithinInterval()
    {
        var checker = new FakeUpdateChecker();
        var now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var state = new AutoUpdateState
        {
            Enabled = true,
            LastCheckUtc = now.AddHours(-1)
        };
        var service = new UpdateService(checker, state, TimeSpan.FromHours(6), () => now);

        var result = await service.RunAutomaticCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Throttled, result.Outcome);
        Assert.Equal(0, checker.CheckCallCount);
    }

    [Fact]
    public async Task RunAutomaticCheckAsync_ReturnsAvailable_WhenUpdatePresent()
    {
        var checker = new FakeUpdateChecker { NextUpdate = new AvailableUpdate("0.2.0") };
        var now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var service = new UpdateService(checker, new AutoUpdateState { Enabled = true }, TimeSpan.FromHours(6), () => now);

        var result = await service.RunAutomaticCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Available, result.Outcome);
        Assert.Equal("0.2.0", result.Update?.Version);
        Assert.Equal(now, service.CurrentState.LastCheckUtc);
    }

    [Fact]
    public async Task RunAutomaticCheckAsync_ReturnsNoUpdate_WhenCheckerReturnsNull()
    {
        var checker = new FakeUpdateChecker { NextUpdate = null };
        var now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var service = new UpdateService(checker, new AutoUpdateState { Enabled = true }, TimeSpan.FromHours(6), () => now);

        var result = await service.RunAutomaticCheckAsync();

        Assert.Equal(UpdateCheckOutcome.NoUpdate, result.Outcome);
        Assert.Equal(now, service.CurrentState.LastCheckUtc);
    }

    [Fact]
    public async Task RunAutomaticCheckAsync_ReturnsSkipped_WhenVersionMatchesSkipped()
    {
        var checker = new FakeUpdateChecker { NextUpdate = new AvailableUpdate("0.2.0") };
        var state = new AutoUpdateState { Enabled = true, SkippedVersion = "0.2.0" };
        var service = new UpdateService(checker, state, TimeSpan.FromHours(6), () => DateTimeOffset.UtcNow);

        var result = await service.RunAutomaticCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task RunAutomaticCheckAsync_ReturnsAvailable_WhenNewerThanSkipped()
    {
        var checker = new FakeUpdateChecker { NextUpdate = new AvailableUpdate("0.3.0") };
        var state = new AutoUpdateState { Enabled = true, SkippedVersion = "0.2.0" };
        var service = new UpdateService(checker, state, TimeSpan.FromHours(6), () => DateTimeOffset.UtcNow);

        var result = await service.RunAutomaticCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Available, result.Outcome);
    }

    [Fact]
    public async Task RunAutomaticCheckAsync_ReturnsFailed_OnCheckerException()
    {
        var checker = new FakeUpdateChecker { ThrowOnCheck = true };
        var service = new UpdateService(checker, new AutoUpdateState { Enabled = true }, TimeSpan.FromHours(6), () => DateTimeOffset.UtcNow);

        var result = await service.RunAutomaticCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task RunManualCheckAsync_IgnoresThrottle()
    {
        var checker = new FakeUpdateChecker { NextUpdate = new AvailableUpdate("0.2.0") };
        var now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var state = new AutoUpdateState { Enabled = true, LastCheckUtc = now.AddMinutes(-1) };
        var service = new UpdateService(checker, state, TimeSpan.FromHours(6), () => now);

        var result = await service.RunManualCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Available, result.Outcome);
        Assert.Equal(1, checker.CheckCallCount);
    }

    [Fact]
    public void SkipVersion_UpdatesState()
    {
        var service = new UpdateService(new FakeUpdateChecker(), new AutoUpdateState { Enabled = true });

        service.SkipVersion("0.5.0");

        Assert.Equal("0.5.0", service.CurrentState.SkippedVersion);
    }

    [Fact]
    public void SkipVersion_IgnoresEmpty()
    {
        var service = new UpdateService(new FakeUpdateChecker(), new AutoUpdateState { Enabled = true, SkippedVersion = "0.5.0" });

        service.SkipVersion("");

        Assert.Equal("0.5.0", service.CurrentState.SkippedVersion);
    }

    private sealed class FakeUpdateChecker : IUpdateChecker
    {
        public AvailableUpdate? NextUpdate { get; set; }

        public bool ThrowOnCheck { get; set; }

        public int CheckCallCount { get; private set; }

        public int ApplyCallCount { get; private set; }

        public Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            if (ThrowOnCheck)
            {
                throw new InvalidOperationException("simulated network failure");
            }

            return Task.FromResult(NextUpdate);
        }

        public Task DownloadAndApplyAsync(CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            return Task.CompletedTask;
        }
    }
}
