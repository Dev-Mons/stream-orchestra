using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class ChromeWindowPlacementWatcherTests
{
    [Fact]
    public void CalculateNormalBounds_CentersNormalSizeInWorkArea()
    {
        var bounds = ChromeWindowPlacementWatcher.CalculateNormalBounds(1280, 800, new ScreenRect(1920, 0, 3840, 1080));

        Assert.Equal(new ScreenRect(2240, 140, 3520, 940), bounds);
    }

    [Fact]
    public void CalculateNormalBounds_ShrinksWindowLargerThanWorkArea()
    {
        var bounds = ChromeWindowPlacementWatcher.CalculateNormalBounds(2560, 1440, new ScreenRect(0, 0, 1920, 1032));

        Assert.Equal(new ScreenRect(0, 0, 1920, 1032), bounds);
    }

    [Fact]
    public void CalculateNormalBounds_HandlesMonitorLeftOfPrimary()
    {
        var bounds = ChromeWindowPlacementWatcher.CalculateNormalBounds(1000, 600, new ScreenRect(-1920, 0, 0, 1080));

        Assert.Equal(new ScreenRect(-1460, 240, -460, 840), bounds);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 600)]
    public void CalculateNormalBounds_FillsWorkAreaWhenNormalSizeIsUnknown(int width, int height)
    {
        var workArea = new ScreenRect(0, 1080, 1920, 2280);

        var bounds = ChromeWindowPlacementWatcher.CalculateNormalBounds(width, height, workArea);

        Assert.Equal(workArea.Left, bounds.Left);
        Assert.Equal(workArea.Width, bounds.Width);
        Assert.True(bounds.Top >= workArea.Top);
        Assert.True(bounds.Bottom <= workArea.Bottom);
    }

    [Fact]
    public void ScreenRect_OffsetMovesBothCorners()
    {
        Assert.Equal(new ScreenRect(-10, 20, 90, 70), new ScreenRect(0, 0, 100, 50).Offset(-10, 20));
    }

    [Fact]
    public void Source_RestoresOnTargetMonitorBeforeMaximizing()
    {
        var text = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Services",
            "ChromeWindowPlacementWatcher.cs")));

        // 크롬이 이미 켜져 있으면 실행 옵션이 무시되므로 새 창이 화면에 나타나는 이벤트로 잡는다.
        Assert.Contains("EventObjectShow,", text);
        Assert.Contains("WinEventOutOfContext | WinEventSkipOwnProcess", text);
        // 같은 창 클래스를 쓰는 다른 앱과 섞이지 않게 크롬 실행 파일 경로로 확인한다.
        Assert.Contains("TryGetProcessPath(processId), _chromeExecutablePath, StringComparison.OrdinalIgnoreCase", text);
        // 이미 최대화된 창은 SW_SHOWMAXIMIZED만으로는 다른 모니터로 옮겨지지 않아 실제 크롬에서 실패했다.
        var normalIndex = text.IndexOf("placement.ShowCommand = ShowNormal;", StringComparison.Ordinal);
        var maximizeIndex = text.IndexOf("ShowWindow(window, ShowMaximized);", StringComparison.Ordinal);
        Assert.True(normalIndex >= 0);
        Assert.True(maximizeIndex > normalIndex);
        Assert.DoesNotContain("placement.ShowCommand = ShowMaximized;", text);
    }
}
