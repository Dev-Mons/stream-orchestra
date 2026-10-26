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
    public async Task RunStartupCheckAsync_IgnoresThrottle()
    {
        var checker = new FakeUpdateChecker { NextUpdate = new AvailableUpdate("0.2.0") };
        var now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var state = new AutoUpdateState
        {
            Enabled = true,
            LastCheckUtc = now.AddMinutes(-1)
        };
        var service = new UpdateService(checker, state, TimeSpan.FromHours(6), () => now);

        var result = await service.RunStartupCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Available, result.Outcome);
        Assert.Equal(1, checker.CheckCallCount);
        Assert.Equal(now, service.CurrentState.LastCheckUtc);
    }

    [Fact]
    public async Task RunManualCheckAsync_KeepsReleaseNotesFromChecker()
    {
        var checker = new FakeUpdateChecker { NextUpdate = new AvailableUpdate("0.2.0", "- 슬롯을 창 밖에 놓으면 크롬으로 열기") };
        var service = new UpdateService(
            checker,
            new AutoUpdateState { Enabled = true },
            UpdateService.DefaultMinimumCheckInterval,
            () => DateTimeOffset.UtcNow);

        var result = await service.RunManualCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Available, result.Outcome);
        Assert.Equal("- 슬롯을 창 밖에 놓으면 크롬으로 열기", result.Update?.ReleaseNotes);
    }

    [Fact]
    public void AvailableUpdate_DefaultsReleaseNotesToNull()
    {
        Assert.Null(new AvailableUpdate("0.2.0").ReleaseNotes);
    }

    [Fact]
    public async Task RunStartupCheckAsync_ReturnsDisabled_WhenAutoUpdateOff()
    {
        var checker = new FakeUpdateChecker();
        var service = new UpdateService(
            checker,
            new AutoUpdateState { Enabled = false },
            UpdateService.DefaultMinimumCheckInterval,
            () => DateTimeOffset.UtcNow);

        var result = await service.RunStartupCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Disabled, result.Outcome);
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
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal("simulated network failure", result.Error?.Message);
    }

    [Fact]
    public async Task RunManualCheckAsync_ReturnsNotInstalled_WithoutCheckingOrRecordingTime_WhenNotInstalled()
    {
        var checker = new FakeUpdateChecker { IsInstalled = false, NextUpdate = new AvailableUpdate("0.2.0") };
        var service = new UpdateService(checker, new AutoUpdateState { Enabled = true });

        var result = await service.RunManualCheckAsync();

        Assert.Equal(UpdateCheckOutcome.NotInstalled, result.Outcome);
        Assert.Null(result.Update);
        Assert.Equal(0, checker.CheckCallCount);
        Assert.Null(service.CurrentState.LastCheckUtc);
    }

    [Fact]
    public async Task RunAutomaticCheckAsync_ReturnsNotInstalled_WhenNotInstalled()
    {
        var checker = new FakeUpdateChecker { IsInstalled = false };
        var service = new UpdateService(checker, new AutoUpdateState { Enabled = true });

        var result = await service.RunAutomaticCheckAsync();

        Assert.Equal(UpdateCheckOutcome.NotInstalled, result.Outcome);
        Assert.Equal(0, checker.CheckCallCount);
    }

    [Fact]
    public async Task RunAutomaticCheckAsync_ChecksAgainEveryThirtyMinutes()
    {
        var checker = new FakeUpdateChecker();
        var now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var service = new UpdateService(
            checker,
            new AutoUpdateState { Enabled = true },
            UpdateService.DefaultMinimumCheckInterval,
            () => now);

        await service.RunAutomaticCheckAsync();
        now = now.AddMinutes(29);
        var beforeInterval = await service.RunAutomaticCheckAsync();
        now = now.AddMinutes(1);
        var atInterval = await service.RunAutomaticCheckAsync();

        Assert.Equal(TimeSpan.FromMinutes(30), UpdateService.DefaultMinimumCheckInterval);
        Assert.Equal(UpdateCheckOutcome.Throttled, beforeInterval.Outcome);
        Assert.Equal(UpdateCheckOutcome.NoUpdate, atInterval.Outcome);
        Assert.Equal(2, checker.CheckCallCount);
    }

    [Fact]
    public void HasPromptBeenShown_TracksVersionsForThisRunOnly()
    {
        var state = new AutoUpdateState { Enabled = true };
        var service = new UpdateService(new FakeUpdateChecker(), state);

        Assert.False(service.HasPromptBeenShown("0.22.0"));

        service.MarkPromptShown("0.22.0");

        Assert.True(service.HasPromptBeenShown("0.22.0"));
        Assert.True(service.HasPromptBeenShown("0.22.0".ToUpperInvariant()));
        Assert.False(service.HasPromptBeenShown("0.23.0"));
        // 저장 상태에 남기지 않으므로 앱을 다시 켜면 한 번 더 안내한다.
        Assert.False(new UpdateService(new FakeUpdateChecker(), service.CurrentState).HasPromptBeenShown("0.22.0"));
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
    public async Task RunAutomaticCheckAsync_IsThrottled_AfterManualCheck()
    {
        var checker = new FakeUpdateChecker();
        var now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var service = new UpdateService(checker, new AutoUpdateState { Enabled = true }, TimeSpan.FromHours(6), () => now);

        await service.RunManualCheckAsync();
        var result = await service.RunAutomaticCheckAsync();

        Assert.Equal(UpdateCheckOutcome.Throttled, result.Outcome);
        Assert.Equal(1, checker.CheckCallCount);
    }

    [Fact]
    public async Task DownloadAndApplyAsync_SavesStateAfterDownloadBeforeRestart()
    {
        var events = new List<string>();
        var checker = new FakeUpdateChecker(events);
        var service = new UpdateService(checker, new AutoUpdateState());

        var restarting = await service.DownloadAndApplyAsync(() => events.Add("save"));

        Assert.True(restarting);
        Assert.Equal(["download", "save", "restart"], events);
    }

    [Fact]
    public async Task DownloadAndApplyAsync_ForwardsCancellationToken()
    {
        var checker = new FakeUpdateChecker();
        var service = new UpdateService(checker, new AutoUpdateState());
        using var cancellationTokenSource = new CancellationTokenSource();

        await service.DownloadAndApplyAsync(() => { }, cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, checker.DownloadCancellationToken);
    }

    [Fact]
    public async Task DownloadAndApplyAsync_DoesNotSaveOrRestart_WhenNoUpdateWasDownloaded()
    {
        var events = new List<string>();
        var checker = new FakeUpdateChecker(events) { DownloadSucceeds = false };
        var service = new UpdateService(checker, new AutoUpdateState());

        var restarting = await service.DownloadAndApplyAsync(() => events.Add("save"));

        Assert.False(restarting);
        Assert.Equal(["download"], events);
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
        private readonly List<string>? _events;

        public FakeUpdateChecker(List<string>? events = null)
        {
            _events = events;
        }

        public bool IsInstalled { get; set; } = true;

        public AvailableUpdate? NextUpdate { get; set; }

        public bool ThrowOnCheck { get; set; }

        public bool DownloadSucceeds { get; set; } = true;

        public int CheckCallCount { get; private set; }

        public CancellationToken DownloadCancellationToken { get; private set; }

        public Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            if (ThrowOnCheck)
            {
                throw new InvalidOperationException("simulated network failure");
            }

            return Task.FromResult(NextUpdate);
        }

        public Task<bool> DownloadUpdateAsync(CancellationToken cancellationToken = default)
        {
            DownloadCancellationToken = cancellationToken;
            _events?.Add("download");
            return Task.FromResult(DownloadSucceeds);
        }

        public void ApplyUpdateAndRestart()
        {
            _events?.Add("restart");
        }
    }
}
