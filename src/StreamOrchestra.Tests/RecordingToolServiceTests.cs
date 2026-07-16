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
}
