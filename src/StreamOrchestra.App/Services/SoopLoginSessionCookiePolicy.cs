namespace StreamOrchestra.App.Services;

public static class SoopLoginSessionCookiePolicy
{
    public static readonly TimeSpan PersistenceLifetime = TimeSpan.FromDays(30);

    public static bool ShouldPersist(string? domain, bool isSession)
    {
        if (!isSession || string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        var normalizedDomain = domain.Trim().TrimStart('.');
        return normalizedDomain.Equals("sooplive.com", StringComparison.OrdinalIgnoreCase) ||
               normalizedDomain.Equals("sooplive.co.kr", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLoginCallback(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var isSoopLoginHost =
            IsHostOrSubdomain(uri.Host, "login.sooplive.com") ||
            IsHostOrSubdomain(uri.Host, "login.sooplive.co.kr");
        return isSoopLoginHost &&
               uri.AbsolutePath.Contains("callback_", StringComparison.OrdinalIgnoreCase);
    }

    public static DateTime CreateExpiration(DateTime utcNow) =>
        DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).Add(PersistenceLifetime);

    private static bool IsHostOrSubdomain(string host, string expectedHost) =>
        host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith('.' + expectedHost, StringComparison.OrdinalIgnoreCase);
}
