namespace StreamOrchestra.App.Services;

public static class WebViewPopupPolicy
{
    public static bool ShouldPreservePopupContext(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        if (IsHostOrSubdomain(uri.Host, "nid.naver.com"))
        {
            return true;
        }

        var isSoopLoginHost =
            IsHostOrSubdomain(uri.Host, "login.sooplive.com") ||
            IsHostOrSubdomain(uri.Host, "login.sooplive.co.kr");
        if (!isSoopLoginHost)
        {
            return false;
        }

        var isSocialLoginEntryPoint =
            uri.AbsolutePath.Equals("/afreeca/connect.php", StringComparison.OrdinalIgnoreCase) &&
            uri.Query.Contains("sns_code=", StringComparison.OrdinalIgnoreCase);
        return isSocialLoginEntryPoint ||
               uri.AbsolutePath.Contains("naver", StringComparison.OrdinalIgnoreCase) ||
               uri.Query.Contains("naver", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHostOrSubdomain(string host, string expectedHost) =>
        host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith('.' + expectedHost, StringComparison.OrdinalIgnoreCase);
}
