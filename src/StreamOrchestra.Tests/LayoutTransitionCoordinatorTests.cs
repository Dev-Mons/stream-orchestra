using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class LayoutTransitionCoordinatorTests
{
    [Fact]
    public async Task TryRunAsync_RejectsOverlappingTransitionAndAcceptsNextAfterCompletion()
    {
        var coordinator = new LayoutTransitionCoordinator();
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = coordinator.TryRunAsync(async () =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
        });
        await firstStarted.Task;

        Assert.True(coordinator.IsRunning);
        Assert.False(await coordinator.TryRunAsync(() => Task.CompletedTask));

        releaseFirst.SetResult();
        Assert.True(await first);
        Assert.False(coordinator.IsRunning);
        Assert.True(await coordinator.TryRunAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task TryRunAsync_ReleasesExecutionAfterFailure()
    {
        var coordinator = new LayoutTransitionCoordinator();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.TryRunAsync(() => throw new InvalidOperationException("failed")));

        Assert.False(coordinator.IsRunning);
        Assert.True(await coordinator.TryRunAsync(() => Task.CompletedTask));
    }
}
