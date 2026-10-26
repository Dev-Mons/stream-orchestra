using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class BrowserCacheUsageServiceTests : IDisposable
{
    private readonly string _groupFolder;

    public BrowserCacheUsageServiceTests()
    {
        _groupFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void GetCacheSizeBytes_CountsOnlyDiskCacheFolders()
    {
        var profileFolder = Path.Combine(_groupFolder, "EBWebView", "Default");
        WriteFile(Path.Combine(profileFolder, "Cache", "Cache_Data", "f_000001"), 1000);
        WriteFile(Path.Combine(profileFolder, "Cache", "Cache_Data", "data_1"), 200);
        WriteFile(Path.Combine(profileFolder, "Code Cache", "js", "index"), 500);
        // 로그인과 사이트 데이터는 캐시 정리 대상이 아니므로 세지 않는다.
        WriteFile(Path.Combine(profileFolder, "Network", "Cookies"), 300);
        WriteFile(Path.Combine(profileFolder, "Local Storage", "leveldb", "000003.log"), 400);

        var size = new BrowserCacheUsageService().GetCacheSizeBytes(CreateGroup());

        Assert.Equal(1700, size);
    }

    [Fact]
    public void GetCacheSizeBytes_ReturnsZeroForUnusedGroup()
    {
        Directory.CreateDirectory(_groupFolder);

        var size = new BrowserCacheUsageService().GetCacheSizeBytes(CreateGroup());

        Assert.Equal(0, size);
    }

    [Theory]
    [InlineData(0, "0MB")]
    [InlineData(64L * 1024 * 1024, "64MB")]
    [InlineData(520L * 1024 * 1024, "520MB")]
    [InlineData(1536L * 1024 * 1024, "1.5GB")]
    public void FormatSize_UsesMegabytesBelowOneGigabyte(long bytes, string expected)
    {
        Assert.Equal(expected, BrowserCacheUsageService.FormatSize(bytes));
    }

    public void Dispose()
    {
        if (Directory.Exists(_groupFolder))
        {
            Directory.Delete(_groupFolder, recursive: true);
        }
    }

    private ProfileGroup CreateGroup() => new("A", "SOOP Group A", _groupFolder);

    private static void WriteFile(string path, int length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }
}
