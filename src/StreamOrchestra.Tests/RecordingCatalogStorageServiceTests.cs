using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class RecordingCatalogStorageServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"stream-orchestra-recordings-{Guid.NewGuid():N}");

    [Fact]
    public void SaveAndLoad_PreservesGlobalFolderAndBroadcastEntries()
    {
        var service = new RecordingCatalogStorageService(_folder);
        var addedAt = new DateTimeOffset(2026, 8, 10, 10, 30, 0, TimeSpan.FromHours(9));
        var state = new RecordingCatalogState(
            @"D:\All Recordings",
            [new RecordingCatalogItem(
                "one",
                "https://play.sooplive.com/channel/123",
                "채널",
                "실제 방송 제목",
                "1080",
                @"D:\cache\thumb.jpg",
                false,
                null,
                addedAt)]);

        service.Save(state);
        var loaded = service.Load(@"C:\Fallback");

        Assert.Equal(@"D:\All Recordings", loaded.OutputFolder);
        var item = Assert.Single(loaded.Items);
        Assert.Equal("one", item.Id);
        Assert.Equal("실제 방송 제목", item.DetailTitle);
        Assert.Equal(@"D:\cache\thumb.jpg", item.ThumbnailPath);
        Assert.Equal(addedAt, item.AddedAt);
    }

    [Fact]
    public void Normalize_DropsUnsupportedAndDuplicateUrls()
    {
        var state = new RecordingCatalogState(
            "",
            [
                CreateItem("one", "https://play.sooplive.com/channel/123"),
                CreateItem("two", "https://play.sooplive.com/channel/123"),
                CreateItem("three", "https://example.com/not-soop")
            ]);

        var normalized = RecordingCatalogStorageService.Normalize(state, @"C:\Default");

        Assert.Equal(@"C:\Default", normalized.OutputFolder);
        Assert.Single(normalized.Items);
        Assert.Equal("one", normalized.Items[0].Id);
    }

    private static RecordingCatalogItem CreateItem(string id, string url) => new(
        id,
        url,
        "채널",
        "방송",
        "best",
        null,
        false,
        null,
        DateTimeOffset.Now);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
