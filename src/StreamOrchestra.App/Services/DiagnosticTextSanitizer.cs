using System.Text.RegularExpressions;

namespace StreamOrchestra.App.Services;

public static partial class DiagnosticTextSanitizer
{
    private const int MaximumLength = 2048;

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var sanitized = UrlPattern().Replace(value, match => FormatUrl(match.Value));
        sanitized = SecretHeaderPattern().Replace(sanitized, match => $"{match.Groups[1].Value}=[redacted]");
        sanitized = BearerPattern().Replace(sanitized, "Bearer [redacted]");
        sanitized = BasicPattern().Replace(sanitized, "Basic [redacted]");
        sanitized = SecretPairPattern().Replace(
            sanitized,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}[redacted]");
        sanitized = JwtPattern().Replace(sanitized, "[redacted-jwt]");
        return sanitized.Length <= MaximumLength
            ? sanitized
            : sanitized[..MaximumLength] + "…[truncated]";
    }

    private static string FormatUrl(string rawUrl)
    {
        var trimmed = rawUrl.TrimEnd('.', ',', ')', ']', '}');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return "[redacted-url]";
        }

        var builder = new UriBuilder(uri.Scheme, uri.IdnHost)
        {
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Path = uri.AbsolutePath,
            Query = "",
            Fragment = "",
            UserName = "",
            Password = ""
        };
        return builder.Uri.AbsoluteUri;
    }

    [GeneratedRegex(@"https?://[^\s<>\""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?im)\b(authorization|proxy-authorization|cookie|set-cookie|x-api-key)\s*[:=]\s*[^\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretHeaderPattern();

    [GeneratedRegex(@"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(?i)\bbasic\s+[A-Za-z0-9+/=]+", RegexOptions.CultureInvariant)]
    private static partial Regex BasicPattern();

    [GeneratedRegex(@"(?i)\b(access_?token|refresh_?token|token|auth|authorization|api_?key|key|signature|sig|signed|policy|credential|password|passwd|secret|expires|x-amz-[a-z0-9_-]+)\s*([:=])\s*([^\s&;,]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPairPattern();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();
}
