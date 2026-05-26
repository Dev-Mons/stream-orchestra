namespace StreamOrchestra.App.Services;

public sealed class StreamNavigationService
{
    public string NormalizeUrl(string url)
    {
        var trimmedUrl = url.Trim();
        if (string.IsNullOrWhiteSpace(trimmedUrl))
        {
            return "about:blank";
        }

        if (trimmedUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return "about:blank";
        }

        if (Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var absoluteUri))
        {
            return IsHttpOrHttps(absoluteUri) ? absoluteUri.ToString() : "about:blank";
        }

        var urlWithScheme = $"https://{trimmedUrl}";
        return Uri.TryCreate(urlWithScheme, UriKind.Absolute, out var inferredUri) &&
            IsHttpOrHttps(inferredUri) &&
            !string.IsNullOrWhiteSpace(inferredUri.Host)
                ? urlWithScheme
                : "about:blank";
    }

    public string CreateDisplayName(string? url)
    {
        var normalizedUrl = NormalizeUrl(url ?? "about:blank");
        if (normalizedUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return "Empty";
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return normalizedUrl;
        }

        var lastSegment = uri.Segments
            .Select(segment => segment.Trim('/'))
            .LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment));

        return string.IsNullOrWhiteSpace(lastSegment)
            ? uri.Host
            : Uri.UnescapeDataString(lastSegment);
    }

    public string CreateDisplayName(string? url, string? documentTitle)
    {
        var normalizedTitle = NormalizeDocumentTitle(documentTitle);
        if (!string.IsNullOrWhiteSpace(normalizedTitle) && !LooksLikeUrl(normalizedTitle))
        {
            return normalizedTitle;
        }

        return CreateDisplayName(url);
    }

    private static string NormalizeDocumentTitle(string? documentTitle)
    {
        if (string.IsNullOrWhiteSpace(documentTitle))
        {
            return "";
        }

        return string.Join(
            " ",
            documentTitle.Trim().Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool LooksLikeUrl(string value)
    {
        if (value.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out _);
    }

    private static bool IsHttpOrHttps(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
