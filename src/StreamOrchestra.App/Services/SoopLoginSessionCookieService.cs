using Microsoft.Web.WebView2.Core;

namespace StreamOrchestra.App.Services;

public sealed class SoopLoginSessionCookieService
{
    private static readonly string[] CookieOrigins =
    [
        "https://www.sooplive.com/",
        "https://www.sooplive.co.kr/"
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<int> PersistAsync(CoreWebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);

        await _gate.WaitAsync();
        try
        {
            var cookieManager = webView.CookieManager;
            var expiration = SoopLoginSessionCookiePolicy.CreateExpiration(DateTime.UtcNow);
            var updatedCookieKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var updatedCount = 0;

            foreach (var origin in CookieOrigins)
            {
                var cookies = await cookieManager.GetCookiesAsync(origin);
                foreach (var cookie in cookies)
                {
                    if (!SoopLoginSessionCookiePolicy.ShouldPersist(cookie.Domain, cookie.IsSession))
                    {
                        continue;
                    }

                    var cookieKey = string.Join('\u001f', cookie.Domain, cookie.Path, cookie.Name);
                    if (!updatedCookieKeys.Add(cookieKey))
                    {
                        continue;
                    }

                    cookie.Expires = expiration;
                    cookieManager.AddOrUpdateCookie(cookie);
                    updatedCount++;
                }
            }

            return updatedCount;
        }
        finally
        {
            _gate.Release();
        }
    }
}
