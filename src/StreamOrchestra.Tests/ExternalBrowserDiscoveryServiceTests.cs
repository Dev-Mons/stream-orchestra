using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class ExternalBrowserDiscoveryServiceTests : IDisposable
{
    private readonly string _rootFolder;

    public ExternalBrowserDiscoveryServiceTests()
    {
        _rootFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootFolder);
    }

    [Fact]
    public void Discover_ReturnsInstalledBrowserWhenCandidateExecutableExists()
    {
        var executablePath = Path.Combine(_rootFolder, "chrome.exe");
        File.WriteAllText(executablePath, "");
        var service = new ExternalBrowserDiscoveryService(
        [
            new ExternalBrowserCandidate(
                "chrome",
                "Google Chrome",
                [Path.Combine(_rootFolder, "missing.exe"), executablePath])
        ]);

        var browsers = service.Discover();

        var browser = Assert.Single(browsers);
        Assert.Equal("chrome", browser.Id);
        Assert.True(browser.IsInstalled);
        Assert.Equal(executablePath, browser.ExecutablePath);
        Assert.Equal(2, browser.CandidatePaths.Count);
    }

    [Fact]
    public void Discover_ReturnsMissingBrowserWhenNoCandidateExecutableExists()
    {
        var service = new ExternalBrowserDiscoveryService(
        [
            new ExternalBrowserCandidate(
                "whale",
                "Naver Whale",
                [Path.Combine(_rootFolder, "whale.exe")])
        ]);

        var browsers = service.Discover();

        var browser = Assert.Single(browsers);
        Assert.Equal("whale", browser.Id);
        Assert.False(browser.IsInstalled);
        Assert.Null(browser.ExecutablePath);
    }

    [Fact]
    public void Discover_DefaultCandidatesIncludeChromiumFallbackBrowsers()
    {
        var service = new ExternalBrowserDiscoveryService();

        var browsers = service.Discover();
        var ids = browsers.Select(browser => browser.Id).ToArray();

        Assert.Contains("edge", ids);
        Assert.Contains("chrome", ids);
        Assert.Contains("whale", ids);
        Assert.Contains("brave", ids);
        Assert.Contains("vivaldi", ids);
    }

    [Fact]
    public void Discover_WithDataFolderIncludesCustomBrowserCandidates()
    {
        var executableFolder = Path.Combine(_rootFolder, "PortableBrowser");
        Directory.CreateDirectory(executableFolder);
        var executablePath = Path.Combine(executableFolder, "browser.exe");
        File.WriteAllText(executablePath, "");
        new ExternalBrowserCandidateStorageService(_rootFolder).SaveCandidates(
        [
            new ExternalBrowserCandidate(
                "portable_browser",
                "Portable Browser",
                [executablePath])
        ]);
        var service = new ExternalBrowserDiscoveryService(_rootFolder);

        var browsers = service.Discover();

        var browser = Assert.Single(browsers.Where(browser => browser.Id == "portable_browser"));
        Assert.Equal("Portable Browser", browser.Name);
        Assert.True(browser.IsInstalled);
        Assert.Equal(executablePath, browser.ExecutablePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootFolder))
        {
            Directory.Delete(_rootFolder, recursive: true);
        }
    }
}
