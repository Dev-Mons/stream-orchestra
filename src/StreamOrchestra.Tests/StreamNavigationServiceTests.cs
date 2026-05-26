using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class StreamNavigationServiceTests
{
    [Theory]
    [InlineData("", "about:blank")]
    [InlineData("   ", "about:blank")]
    [InlineData("about:blank", "about:blank")]
    [InlineData("ABOUT:BLANK", "about:blank")]
    [InlineData("www.sooplive.co.kr", "https://www.sooplive.co.kr")]
    [InlineData("https://www.sooplive.co.kr", "https://www.sooplive.co.kr/")]
    [InlineData("javascript:alert(1)", "about:blank")]
    [InlineData("file:///C:/Temp/test.html", "about:blank")]
    [InlineData("ftp://example.com/stream", "about:blank")]
    [InlineData("not a url", "about:blank")]
    [InlineData("http://", "about:blank")]
    [InlineData("https://", "about:blank")]
    [InlineData("http://?x", "about:blank")]
    [InlineData("http:/example.com", "about:blank")]
    [InlineData("http://example .com", "about:blank")]
    public void NormalizeUrl_NormalizesUserInput(string input, string expected)
    {
        var service = new StreamNavigationService();

        var normalizedUrl = service.NormalizeUrl(input);

        Assert.Equal(expected, normalizedUrl);
    }

    [Theory]
    [InlineData(null, "Empty")]
    [InlineData("", "Empty")]
    [InlineData("about:blank", "Empty")]
    [InlineData("https://www.sooplive.co.kr", "www.sooplive.co.kr")]
    [InlineData("https://www.sooplive.co.kr/streamer123", "streamer123")]
    [InlineData("www.sooplive.co.kr/streamer%20123", "streamer 123")]
    public void CreateDisplayName_DerivesReadableNameFromUrl(string? input, string expected)
    {
        var service = new StreamNavigationService();

        var displayName = service.CreateDisplayName(input);

        Assert.Equal(expected, displayName);
    }

    [Theory]
    [InlineData("https://www.sooplive.co.kr/streamer123", "Streamer 123 - SOOP", "Streamer 123 - SOOP")]
    [InlineData("https://www.sooplive.co.kr/streamer123", "  Streamer   123  ", "Streamer 123")]
    [InlineData("https://www.sooplive.co.kr/streamer123", "", "streamer123")]
    [InlineData("https://www.sooplive.co.kr/streamer123", "about:blank", "streamer123")]
    [InlineData("https://www.sooplive.co.kr/streamer123", "https://www.sooplive.co.kr/streamer123", "streamer123")]
    public void CreateDisplayName_UsesDocumentTitleWhenReadable(
        string url,
        string documentTitle,
        string expected)
    {
        var service = new StreamNavigationService();

        var displayName = service.CreateDisplayName(url, documentTitle);

        Assert.Equal(expected, displayName);
    }
}
