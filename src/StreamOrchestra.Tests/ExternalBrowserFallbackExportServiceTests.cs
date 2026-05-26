using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class ExternalBrowserFallbackExportServiceTests : IDisposable
{
    private readonly string _dataFolder;

    public ExternalBrowserFallbackExportServiceTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void SaveScript_ReturnsUnavailableWhenWorkspaceIsMissing()
    {
        var service = new ExternalBrowserFallbackExportService();

        var result = service.SaveScript(
            workspace: null,
            _dataFolder,
            new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));

        Assert.False(result.ScriptSaved);
        Assert.Equal("No last saved session is available.", result.Reason);
        Assert.Null(result.ScriptPath);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void SaveScript_WritesScriptForLaunchableWorkspace()
    {
        Directory.CreateDirectory(_dataFolder);
        var executablePath = Path.Combine(_dataFolder, "browser.exe");
        File.WriteAllText(executablePath, "");
        var service = new ExternalBrowserFallbackExportService(
            new ExternalBrowserDiscoveryService(
            [
                new ExternalBrowserCandidate(
                    "portable_browser",
                    "Portable Browser",
                    [executablePath])
            ]));

        var result = service.SaveScript(
            CreateWorkspace(),
            _dataFolder,
            new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));

        Assert.True(result.ScriptSaved);
        Assert.Equal("Prepared 1 browser launch plan(s).", result.Reason);
        Assert.Equal(Path.Combine(_dataFolder, "external-browser-fallback-20260526-120000.ps1"), result.ScriptPath);
        Assert.NotNull(result.Plan);
        Assert.Equal(1, result.Plan.PlannedSlotCount);
        Assert.True(File.Exists(result.ScriptPath));
        Assert.Contains(executablePath, File.ReadAllText(result.ScriptPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }

    private static WorkspacePreset CreateWorkspace()
    {
        return new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Stream 1",
                    StreamUrl = "https://example.com/1",
                    ProfileGroupId = "A"
                },
                new WorkspaceSlot
                {
                    SlotId = 2,
                    StreamName = "Blank",
                    StreamUrl = "about:blank",
                    ProfileGroupId = "A"
                }
            ]
        };
    }
}
