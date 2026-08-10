using StreamOrchestra.App.Models;

namespace StreamOrchestra.Tests;

public sealed class RecordingSessionViewModelTests
{
    [Fact]
    public void NewCatalogEntry_StartsIdleWithRecordAction()
    {
        var request = new RecordingRequest(
            "https://play.sooplive.com/streamer/12345",
            @"D:\Recordings",
            "best",
            DateTimeOffset.Now);
        using var session = new RecordingSessionViewModel("entry-1", request);

        Assert.Equal(RecordingSessionState.Idle, session.State);
        Assert.Equal("녹화 시작", session.PrimaryActionLabel);
        Assert.True(session.CanStart);
        Assert.False(session.CanStop);
    }

    [Fact]
    public void UpdateOutputFolder_ChangesTheSingleDestinationForTheNextRecording()
    {
        var request = new RecordingRequest(
            "https://play.sooplive.com/streamer/12345",
            @"D:\Old",
            "best",
            DateTimeOffset.Now);
        using var session = new RecordingSessionViewModel("entry-1", request);

        session.UpdateOutputFolder(@"E:\All Recordings");

        Assert.Equal(@"E:\All Recordings", session.OutputFolder);
        Assert.Equal(@"E:\All Recordings", session.Request.OutputFolder);
    }

    [Theory]
    [InlineData("https://play.sooplive.com/streamer/12345", "streamer", "streamer 방송 · 12345")]
    [InlineData("https://www.sooplive.com/channel-id", "channel-id", "channel-id 라이브 방송")]
    public void CreateFriendlyNames_UsesReadableChannelAndBroadcastLabels(
        string url,
        string expectedDisplayName,
        string expectedDetailTitle)
    {
        var result = RecordingSessionViewModel.CreateFriendlyNames(url);

        Assert.Equal(expectedDisplayName, result.DisplayName);
        Assert.Equal(expectedDetailTitle, result.DetailTitle);
    }

    [Theory]
    [InlineData("[download] 12.3MiB at 4.2MiB/s", "12.3 MB")]
    [InlineData("[download] 80% of 1.5GiB", "1.5 GB")]
    [InlineData("[download] 992KiB", "992 KB")]
    public void TryExtractTransferredSize_FormatsYtDlpProgressForPeople(
        string line,
        string expected)
    {
        var parsed = RecordingSessionViewModel.TryExtractTransferredSize(line, out var size);

        Assert.True(parsed);
        Assert.Equal(expected, size);
    }

    [Fact]
    public void TryExtractTransferredSize_RejectsLinesWithoutAFileSize()
    {
        var parsed = RecordingSessionViewModel.TryExtractTransferredSize(
            "[download] reconnecting to the live stream",
            out var size);

        Assert.False(parsed);
        Assert.Empty(size);
    }
}
