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

        if (HasMalformedHttpScheme(trimmedUrl))
        {
            return "about:blank";
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

        var pathSegments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(Uri.UnescapeDataString)
            .ToArray();
        var lastSegment = pathSegments.LastOrDefault(segment => !IsNumericIdentifier(segment))
            ?? pathSegments.LastOrDefault();

        return string.IsNullOrWhiteSpace(lastSegment) || IsNumericIdentifier(lastSegment)
            ? uri.Host
            : lastSegment;
    }

    public string CreateDisplayName(string? url, string? documentTitle)
    {
        var normalizedTitle = NormalizeDocumentTitle(documentTitle);
        if (IsMeaningfulDisplayName(normalizedTitle))
        {
            return normalizedTitle;
        }

        return CreateDisplayName(url);
    }

    public bool IsMeaningfulDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeDocumentTitle(value);
        if (normalized.Equals("SOOP", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("embed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("main", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("player", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
            LooksLikeUrl(normalized))
        {
            return false;
        }

        return !IsNumericIdentifier(normalized);
    }

    private static bool IsNumericIdentifier(string value)
    {
        return value.Length > 0 && value.All(char.IsDigit);
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

    private static bool HasMalformedHttpScheme(string value)
    {
        return value.Contains("://", StringComparison.Ordinal) ||
            value.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
    }
}
