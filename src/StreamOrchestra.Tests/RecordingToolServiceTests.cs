using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class RecordingToolServiceTests
{
    [Fact]
    public void TryParseExpectedSha256_ReadsWindowsExecutableEntry()
    {
        const string expected = "3a48cb955d55c8821b60ccbdbbc6f61bc958f2f3d3b7ad5eaf3d83a543293a27";
        var checksums = $"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  yt-dlp\n" +
                        $"{expected}  yt-dlp.exe\n";

        var parsed = RecordingToolService.TryParseExpectedSha256(checksums, out var hash);

        Assert.True(parsed);
        Assert.Equal(expected, hash);
    }

    [Fact]
    public void TryParseExpectedSha256_RejectsMalformedOrDifferentEntries()
    {
        var parsed = RecordingToolService.TryParseExpectedSha256(
            "not-a-hash  yt-dlp.exe\n" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  yt-dlp_arm64.exe",
            out var hash);

        Assert.False(parsed);
        Assert.Empty(hash);
    }

    [Fact]
    public void TryParseExpectedSha256_ReadsOfficialFfmpegArchiveEntry()
    {
        const string expected = "e887d4c7b3ef08bd3734f56b72afc2ff734cc53745be0a655fa5719bd9aac5cf";
        var checksums = $"{expected}  ffmpeg-master-latest-win64-gpl.zip\n";

        var parsed = RecordingToolService.TryParseExpectedSha256(
            checksums,
            "ffmpeg-master-latest-win64-gpl.zip",
            out var hash);

        Assert.True(parsed);
        Assert.Equal(expected, hash);
    }
}
