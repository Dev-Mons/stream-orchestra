using System.IO;
using System.Text.Json;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class PresetStorageAutoUpdateTests : IDisposable
{
    private readonly string _dataFolder;

    public PresetStorageAutoUpdateTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void LoadAppState_FillsDefaultAutoUpdate_WhenJsonMissingField()
    {
        Directory.CreateDirectory(_dataFolder);
        var path = Path.Combine(_dataFolder, "appstate.json");
        File.WriteAllText(path, "{\"lastWorkspaceId\":null}");

        var service = new PresetStorageService(_dataFolder);
        var loaded = service.LoadAppState();

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.AutoUpdate);
        Assert.True(loaded.AutoUpdate.Enabled);
        Assert.Null(loaded.AutoUpdate.SkippedVersion);
        Assert.Null(loaded.AutoUpdate.LastCheckUtc);
    }

    [Fact]
    public void SaveAndLoadAppState_RoundTripsAutoUpdate()
    {
        var service = new PresetStorageService(_dataFolder);
        var now = DateTimeOffset.UtcNow;
        var state = new AppState
        {
            AutoUpdate = new AutoUpdateState
            {
                Enabled = false,
                SkippedVersion = "1.2.3",
                LastCheckUtc = now
            }
        };

        service.SaveAppState(state);
        var loaded = service.LoadAppState();

        Assert.NotNull(loaded);
        Assert.False(loaded!.AutoUpdate.Enabled);
        Assert.Equal("1.2.3", loaded.AutoUpdate.SkippedVersion);
        Assert.Equal(now, loaded.AutoUpdate.LastCheckUtc);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }
}
