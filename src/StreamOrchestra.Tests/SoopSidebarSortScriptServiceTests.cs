using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SoopSidebarSortScriptServiceTests
{
    [Fact]
    public void CreateScript_RestrictsSortingToSoopHosts()
    {
        var script = SoopSidebarSortScriptService.CreateScript();

        Assert.Contains("sooplive.co.kr", script);
        Assert.Contains("sooplive.com", script);
        Assert.Contains("play.sooplive.com", script);
        Assert.Contains("allowedHosts", script);
    }

    [Fact]
    public void CreateScript_InstallsObserverAndClickRefreshHooks()
    {
        var script = SoopSidebarSortScriptService.CreateScript();

        Assert.Contains("MutationObserver", script);
        Assert.Contains("document.addEventListener(\"click\"", script);
        Assert.Contains("requestSort(\"mutation\")", script);
        Assert.Contains("requestSort(\"click\")", script);
    }

    [Fact]
    public void CreateScript_ParsesKoreanViewerCountsAndSortsDescending()
    {
        var script = SoopSidebarSortScriptService.CreateScript();

        Assert.Contains("parseViewerCount", script);
        Assert.Contains("억", script);
        Assert.Contains("만", script);
        Assert.Contains("items.sort((left, right) => right.viewerCount - left.viewerCount)", script);
    }
}
