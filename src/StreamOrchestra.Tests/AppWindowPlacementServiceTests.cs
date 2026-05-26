using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class AppWindowPlacementServiceTests
{
    [Fact]
    public void NormalizeForRestore_KeepsValidWindowState()
    {
        var service = new AppWindowPlacementService();
        var windowState = new AppWindowState
        {
            X = 100,
            Y = 80,
            Width = 1280,
            Height = 720,
            IsMaximized = true
        };

        var normalized = service.NormalizeForRestore(windowState, 0, 0, 1920, 1080);

        Assert.NotNull(normalized);
        Assert.Equal(100, normalized.X);
        Assert.Equal(80, normalized.Y);
        Assert.Equal(1280, normalized.Width);
        Assert.Equal(720, normalized.Height);
        Assert.True(normalized.IsMaximized);
    }

    [Theory]
    [MemberData(nameof(InvalidWindowStates))]
    public void NormalizeForRestore_RejectsInvalidWindowState(AppWindowState windowState)
    {
        var service = new AppWindowPlacementService();

        var normalized = service.NormalizeForRestore(windowState, 0, 0, 1920, 1080);

        Assert.Null(normalized);
    }

    [Fact]
    public void NormalizeForRestore_ClampsOffscreenAndOversizedWindowState()
    {
        var service = new AppWindowPlacementService();
        var windowState = new AppWindowState
        {
            X = -500,
            Y = 5000,
            Width = 200,
            Height = 5000
        };

        var normalized = service.NormalizeForRestore(windowState, 0, 0, 1920, 1080);

        Assert.NotNull(normalized);
        Assert.Equal(0, normalized.X);
        Assert.Equal(0, normalized.Y);
        Assert.Equal(AppWindowPlacementService.MinimumRestoreWidth, normalized.Width);
        Assert.Equal(1080, normalized.Height);
    }

    public static IEnumerable<object[]> InvalidWindowStates()
    {
        yield return [new AppWindowState { X = double.NaN, Y = 0, Width = 1280, Height = 720 }];
        yield return [new AppWindowState { X = 0, Y = double.PositiveInfinity, Width = 1280, Height = 720 }];
        yield return [new AppWindowState { X = 0, Y = 0, Width = 0, Height = 720 }];
        yield return [new AppWindowState { X = 0, Y = 0, Width = 1280, Height = -1 }];
        yield return [new AppWindowState { X = 0, Y = 0, Width = double.NegativeInfinity, Height = 720 }];
    }
}
