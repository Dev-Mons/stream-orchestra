using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SoopRecordingServiceTests
{
    [Theory]
    [InlineData("https://play.sooplive.com/channel/123", true)]
    [InlineData("https://www.sooplive.com/", true)]
    [InlineData("https://vod.sooplive.co.kr/player/123", true)]
    [InlineData("https://bj.afreecatv.com/channel", true)]
    [InlineData("https://sooplive.com.evil.example/live", false)]
    [InlineData("https://example.com/?next=sooplive.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void IsSupportedSoopUrl_AllowsOnlySoopAndLegacyHosts(string url, bool expected)
    {
        Assert.Equal(expected, SoopRecordingService.IsSupportedSoopUrl(url));
    }

    [Fact]
    public void BuildArguments_UsesArgumentListSafeValuesAndRequestedQuality()
    {
        var startedAt = new DateTimeOffset(2026, 7, 16, 21, 5, 7, TimeSpan.FromHours(9));
        var request = new RecordingRequest(
            "https://play.sooplive.com/a/1?name=a&next=b",
            @"D:\Recordings Folder",
            "720",
            startedAt);

        var arguments = SoopRecordingService.BuildArguments(request);

        Assert.Contains("b[height<=720]/b", arguments);
        Assert.Contains(@"D:\Recordings Folder", arguments);
        Assert.Contains("%(title).100B [%(id)s] 20260716_210507.%(ext)s", arguments);
        Assert.Equal(request.StreamUrl, arguments[^1]);
        Assert.Contains("--no-part", arguments);
        Assert.Contains("--hls-use-mpegts", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildArguments_UsesSingleCombinedBestAliasWithoutFormatWarning()
    {
        var request = new RecordingRequest(
            "https://play.sooplive.com/a/1",
            @"D:\Recordings",
            "best",
            DateTimeOffset.Now);

        var arguments = SoopRecordingService.BuildArguments(request);
        var formatIndex = arguments.ToList().IndexOf("--format");

        Assert.True(formatIndex >= 0);
        Assert.Equal("b", arguments[formatIndex + 1]);
    }

    [Fact]
    public void BuildArguments_AddsExplicitFfmpegLocationAsSeparateArgument()
    {
        var request = new RecordingRequest(
            "https://play.sooplive.com/a/1",
            @"D:\Recordings",
            "best",
            DateTimeOffset.Now);

        var arguments = SoopRecordingService.BuildArguments(request, @"C:\Tools Folder\ffmpeg.exe");
        var ffmpegIndex = arguments.ToList().IndexOf("--ffmpeg-location");

        Assert.True(ffmpegIndex >= 0);
        Assert.Equal(@"C:\Tools Folder\ffmpeg.exe", arguments[ffmpegIndex + 1]);
    }

    [Fact]
    public void BuildArguments_AddsCompleteSubscriberCredentialsAsSeparateArguments()
    {
        var request = new RecordingRequest(
            "https://play.sooplive.com/a/1",
            @"D:\Recordings",
            "best",
            DateTimeOffset.Now,
            " account-id ",
            "p a&ss");

        var arguments = SoopRecordingService.BuildArguments(request);
        var usernameIndex = arguments.ToList().IndexOf("--username");
        var passwordIndex = arguments.ToList().IndexOf("--password");

        Assert.True(usernameIndex >= 0);
        Assert.Equal("account-id", arguments[usernameIndex + 1]);
        Assert.True(passwordIndex >= 0);
        Assert.Equal("p a&ss", arguments[passwordIndex + 1]);
    }

    [Theory]
    [InlineData("account-id", null)]
    [InlineData(null, "password")]
    public void BuildArguments_RejectsIncompleteSubscriberCredentials(string? username, string? password)
    {
        var request = new RecordingRequest(
            "https://play.sooplive.com/a/1",
            @"D:\Recordings",
            "best",
            DateTimeOffset.Now,
            username,
            password);

        var exception = Assert.Throws<ArgumentException>(() => SoopRecordingService.BuildArguments(request));

        Assert.Contains("SOOP ID와 비밀번호", exception.Message);
    }
}
