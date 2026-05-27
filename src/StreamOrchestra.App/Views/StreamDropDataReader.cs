using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

internal static class StreamDropDataReader
{
    private static readonly Regex HtmlHrefPattern = new(
        "href\\s*=\\s*(?:\"(?<url>[^\"]+)\"|'(?<url>[^']+)'|(?<url>[^\\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PlainTextUrlPattern = new(
        "https?://[^\\s\"'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryGetDroppedStream(
        IDataObject data,
        StreamNavigationService navigationService,
        out string url,
        out string? streamName)
    {
        url = "";
        streamName = null;

        if (TryGetStringData(data, StreamDragDataFormats.StreamName, out var droppedStreamName))
        {
            streamName = droppedStreamName.Trim();
        }

        if (TryGetStringData(data, StreamDragDataFormats.StreamUrl, out var customUrl) &&
            TryNormalizeDroppedUrl(customUrl, navigationService, out url))
        {
            return true;
        }

        if (TryGetStringData(data, DataFormats.Html, out var html) &&
            TryExtractUrlFromHtml(html, out var htmlUrl) &&
            TryNormalizeDroppedUrl(htmlUrl, navigationService, out url))
        {
            return true;
        }

        foreach (var format in new[] { DataFormats.UnicodeText, DataFormats.Text, "UniformResourceLocatorW", "UniformResourceLocator" })
        {
            if (TryGetStringData(data, format, out var text) &&
                TryNormalizeDroppedUrl(text, navigationService, out url))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryNormalizeDroppedText(
        string candidate,
        StreamNavigationService navigationService,
        out string url)
    {
        return TryNormalizeDroppedUrl(candidate, navigationService, out url);
    }

    private static bool TryNormalizeDroppedUrl(
        string candidate,
        StreamNavigationService navigationService,
        out string url)
    {
        url = navigationService.NormalizeUrl(candidate);
        if (!url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var match = PlainTextUrlPattern.Match(candidate);
        if (!match.Success)
        {
            return false;
        }

        url = navigationService.NormalizeUrl(match.Value);
        return !url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractUrlFromHtml(string html, out string url)
    {
        url = "";
        var match = HtmlHrefPattern.Match(html);
        if (!match.Success)
        {
            return false;
        }

        url = WebUtility.HtmlDecode(match.Groups["url"].Value);
        return !string.IsNullOrWhiteSpace(url);
    }

    private static bool TryGetStringData(IDataObject data, string format, out string value)
    {
        value = "";
        if (!data.GetDataPresent(format))
        {
            return false;
        }

        value = data.GetData(format) switch
        {
            string text => text,
            MemoryStream stream => ReadStreamText(stream, format),
            byte[] bytes => ReadBytesText(bytes, format),
            _ => ""
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ReadStreamText(MemoryStream stream, string format)
    {
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, GetEncoding(format), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return reader.ReadToEnd().TrimEnd('\0');
        }
        finally
        {
            stream.Position = position;
        }
    }

    private static string ReadBytesText(byte[] bytes, string format)
    {
        return GetEncoding(format).GetString(bytes).TrimEnd('\0');
    }

    private static Encoding GetEncoding(string format)
    {
        return format.Equals("UniformResourceLocator", StringComparison.Ordinal)
            ? Encoding.Default
            : Encoding.Unicode;
    }
}
