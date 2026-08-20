using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class WebViewBrowsingDataServiceTests
{
    [Theory]
    [InlineData(true, false, CoreWebView2BrowsingDataKinds.AllSite)]
    [InlineData(false, true, CoreWebView2BrowsingDataKinds.DiskCache)]
    [InlineData(
        true,
        true,
        CoreWebView2BrowsingDataKinds.AllSite | CoreWebView2BrowsingDataKinds.DiskCache)]
    public void GetDataKinds_ReturnsOnlySelectedBrowserData(
        bool clearSiteData,
        bool clearCache,
        CoreWebView2BrowsingDataKinds expected)
    {
        var options = new BrowserDataClearOptions(clearSiteData, clearCache);

        var result = WebViewBrowsingDataService.GetDataKinds(options);

        Assert.Equal(expected, result);
        Assert.False(result.HasFlag(CoreWebView2BrowsingDataKinds.AllProfile));
        Assert.False(result.HasFlag(CoreWebView2BrowsingDataKinds.BrowsingHistory));
        Assert.False(result.HasFlag(CoreWebView2BrowsingDataKinds.PasswordAutosave));
        Assert.False(result.HasFlag(CoreWebView2BrowsingDataKinds.Settings));
    }

    [Fact]
    public void GetDataKinds_RejectsEmptySelection()
    {
        var options = new BrowserDataClearOptions(false, false);

        Assert.Throws<ArgumentException>(() => WebViewBrowsingDataService.GetDataKinds(options));
    }
}
