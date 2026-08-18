using System.IO;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class StreamSyncPersistenceTests : IDisposable
{
    private readonly string _dataFolder = Path.Combine(
        Path.GetTempPath(),
        "StreamOrchestra.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoadWorkspaces_RoundTripsNormalizedSyncPreset()
    {
        var service = new PresetStorageService(_dataFolder);
        service.SaveWorkspaces([
            new WorkspacePreset
            {
                Id = "sync",
                Name = "Sync",
                Sync = new SyncGroupPreset
                {
                    MinimumSafetyDelayMs = 4200,
                    Members =
                    [
                        new SyncMemberPreset
                        {
                            SlotId = 3,
                            ManualDelayMs = 800,
                            DelayModelVersion = SyncManualDelaySchema.CurrentVersion,
                            AlgorithmPriorMs = 500,
                            UserResidualMs = 300,
                            CalibratedStreamUrl = "https://play.sooplive.com/channel?token=secret"
                        }
                    ]
                }
            }
        ]);

        var loaded = Assert.Single(service.LoadWorkspaces());
        Assert.Equal(4200, loaded.Sync.MinimumSafetyDelayMs);
        var member = Assert.Single(loaded.Sync.Members);
        Assert.Equal(3, member.SlotId);
        Assert.Equal(800, member.ManualDelayMs);
        Assert.Equal(500, member.AlgorithmPriorMs);
        Assert.Equal(300, member.UserResidualMs);
        Assert.Equal("https://play.sooplive.com/channel", member.CalibratedStreamUrl);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }
}
