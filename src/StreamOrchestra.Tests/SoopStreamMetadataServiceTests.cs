using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SoopStreamMetadataServiceTests
{
    [Fact]
    public void ParseMetadataJson_ReadsActualBroadcastIdentityAndThumbnail()
    {
        const string json = """
            {
              "title": "오늘의 실제 방송",
              "uploader": "별빛여행자",
              "thumbnail": "https://cdn.example.com/live.jpg"
            }
            """;

        var metadata = SoopStreamMetadataService.ParseMetadataJson(json);

        Assert.Equal("별빛여행자", metadata.DisplayName);
        Assert.Equal("오늘의 실제 방송", metadata.Title);
        Assert.Equal("https://cdn.example.com/live.jpg", metadata.ThumbnailUrl);
    }

    [Fact]
    public void BuildMetadataArguments_UsesReadOnlyYtDlpMetadataModeAndCredentials()
    {
        var request = new RecordingRequest(
            "https://play.sooplive.com/channel/123",
            @"D:\Recordings",
            "best",
            DateTimeOffset.Now,
            "account",
            "secret");

        var arguments = SoopStreamMetadataService.BuildMetadataArguments(request);

        Assert.Contains("--skip-download", arguments);
        Assert.Contains("--dump-single-json", arguments);
        Assert.Contains("account", arguments);
        Assert.Contains("secret", arguments);
        Assert.Equal(request.StreamUrl, arguments[^1]);
    }

    [Theory]
    [InlineData("<meta property=\"og:image\" content=\"https://liveimg.sooplive.com/m/123?456\">", "https://liveimg.sooplive.com/m/123?456")]
    [InlineData("<meta content='https://liveimg.sooplive.com/m/789?10&amp;type=live' name='twitter:image'>", "https://liveimg.sooplive.com/m/789?10&type=live")]
    public void ParsePageThumbnailUrl_ReadsSoopOpenGraphImage(string html, string expected)
    {
        var thumbnailUrl = SoopStreamMetadataService.ParsePageThumbnailUrl(html);

        Assert.Equal(expected, thumbnailUrl);
    }

    [Fact]
    public void ParsePageThumbnailUrl_RejectsNonHttpImageUrl()
    {
        var thumbnailUrl = SoopStreamMetadataService.ParsePageThumbnailUrl(
            "<meta property=\"og:image\" content=\"file:///private/live.jpg\">");

        Assert.Null(thumbnailUrl);
    }
}
