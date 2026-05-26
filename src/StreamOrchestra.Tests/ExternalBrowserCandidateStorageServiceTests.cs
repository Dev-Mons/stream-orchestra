using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class ExternalBrowserCandidateStorageServiceTests : IDisposable
{
    private readonly string _dataFolder;

    public ExternalBrowserCandidateStorageServiceTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void LoadCandidates_NormalizesCustomBrowserEntries()
    {
        var service = new ExternalBrowserCandidateStorageService(_dataFolder);
        Directory.CreateDirectory(_dataFolder);
        File.WriteAllText(
            service.CandidatesFilePath,
            """
            [
              {
                "id": " Portable Chrome ",
                "name": " ",
                "candidatePaths": [
                  " C:\\Browsers\\PortableChrome\\chrome.exe ",
                  "",
                  "C:\\Browsers\\PortableChrome\\chrome.exe"
                ]
              },
              {
                "id": " ",
                "name": "Ignored",
                "candidatePaths": ["C:\\Browsers\\ignored.exe"]
              }
            ]
            """);

        var candidates = service.LoadCandidates();

        var candidate = Assert.Single(candidates);
        Assert.Equal("portable_chrome", candidate.Id);
        Assert.Equal("portable_chrome", candidate.Name);
        Assert.Equal(["C:\\Browsers\\PortableChrome\\chrome.exe"], candidate.CandidatePaths);
    }

    [Fact]
    public void MergeCandidates_CombinesDuplicateIdsAndPreservesPrimaryName()
    {
        var mergedCandidates = ExternalBrowserCandidateStorageService.MergeCandidates(
            [
                new ExternalBrowserCandidate(
                    "chrome",
                    "Google Chrome",
                    ["C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"])
            ],
            [
                new ExternalBrowserCandidate(
                    " Chrome ",
                    "Portable Chrome",
                    ["C:\\Portable\\chrome.exe"])
            ]);

        var candidate = Assert.Single(mergedCandidates);
        Assert.Equal("chrome", candidate.Id);
        Assert.Equal("Google Chrome", candidate.Name);
        Assert.Equal(
            [
                "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
                "C:\\Portable\\chrome.exe"
            ],
            candidate.CandidatePaths);
    }

    [Fact]
    public void LoadCandidates_QuarantinesCorruptJsonAndReturnsEmptyList()
    {
        var service = new ExternalBrowserCandidateStorageService(_dataFolder);
        Directory.CreateDirectory(_dataFolder);
        File.WriteAllText(service.CandidatesFilePath, "{ invalid json");

        var candidates = service.LoadCandidates();

        Assert.Empty(candidates);
        Assert.False(File.Exists(service.CandidatesFilePath));
        Assert.Single(Directory.GetFiles(_dataFolder, "external-browsers.json.corrupt.*"));
    }

    [Fact]
    public void LoadCandidates_IgnoresNullEntriesBeforeNormalization()
    {
        var service = new ExternalBrowserCandidateStorageService(_dataFolder);
        Directory.CreateDirectory(_dataFolder);
        File.WriteAllText(
            service.CandidatesFilePath,
            """
            [
              null,
              {
                "id": "Portable Browser",
                "name": "Portable Browser",
                "candidatePaths": ["C:\\Browsers\\Portable\\browser.exe"]
              }
            ]
            """);

        var candidates = service.LoadCandidates();

        var candidate = Assert.Single(candidates);
        Assert.Equal("portable_browser", candidate.Id);
        Assert.Equal("Portable Browser", candidate.Name);
    }

    [Fact]
    public void LoadCandidates_IgnoresEntriesWithNullRequiredFields()
    {
        var service = new ExternalBrowserCandidateStorageService(_dataFolder);
        Directory.CreateDirectory(_dataFolder);
        File.WriteAllText(
            service.CandidatesFilePath,
            """
            [
              {
                "id": null,
                "name": "Missing Id",
                "candidatePaths": ["C:\\Browsers\\Ignored\\browser.exe"]
              },
              {
                "id": "Missing Paths",
                "name": "Missing Paths",
                "candidatePaths": null
              },
              {
                "id": " Valid Browser ",
                "name": null,
                "candidatePaths": [
                  null,
                  " ",
                  " C:\\Browsers\\Valid\\browser.exe "
                ]
              }
            ]
            """);

        var candidates = service.LoadCandidates();

        var candidate = Assert.Single(candidates);
        Assert.Equal("valid_browser", candidate.Id);
        Assert.Equal("valid_browser", candidate.Name);
        Assert.Equal(["C:\\Browsers\\Valid\\browser.exe"], candidate.CandidatePaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }
}
