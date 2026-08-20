using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class WebViewBrowsingDataService
{
    // HWND_MESSAGE: 보이지 않는 임시 WebView를 만들어 현재 그룹의 기본 프로필에 접근한다.
    private static readonly IntPtr MessageOnlyWindow = new(-3);
    private readonly SemaphoreSlim _clearGate = new(1, 1);

    public async Task ClearAsync(
        CoreWebView2Environment environment,
        BrowserDataClearOptions options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        var dataKinds = GetDataKinds(options);
        await _clearGate.WaitAsync();
        CoreWebView2Controller? controller = null;
        try
        {
            controller = await environment.CreateCoreWebView2ControllerAsync(MessageOnlyWindow);
            controller.IsVisible = false;
            await controller.CoreWebView2.Profile.ClearBrowsingDataAsync(dataKinds);
        }
        finally
        {
            try
            {
                controller?.Close();
            }
            finally
            {
                _clearGate.Release();
            }
        }
    }

    public static CoreWebView2BrowsingDataKinds GetDataKinds(BrowserDataClearOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var dataKinds = (CoreWebView2BrowsingDataKinds)0;
        if (options.ClearSiteData)
        {
            // 쿠키와 Local Storage, IndexedDB, Service Worker 등 사이트 소유 데이터를 포함한다.
            dataKinds |= CoreWebView2BrowsingDataKinds.AllSite;
        }

        if (options.ClearCache)
        {
            dataKinds |= CoreWebView2BrowsingDataKinds.DiskCache;
        }

        if (dataKinds == 0)
        {
            throw new ArgumentException("At least one browser data kind must be selected.", nameof(options));
        }

        return dataKinds;
    }
}
