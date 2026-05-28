using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class LayoutTreePersistenceTests
{
    [Fact]
    public void PresetStorageService_PreservesWorkspaceLayoutTree()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var service = new PresetStorageService(dataFolder);
            var workspace = new WorkspacePreset
            {
                Id = "workspace_dynamic",
                Name = "Dynamic",
                LayoutId = "dynamic",
                LayoutTree = new LayoutTreeDocument
                {
                    Root = new LeafLayoutNode { Id = "leaf-1", SlotId = 1, Items = [] },
                    ActiveLeafId = "leaf-1"
                },
                Slots = [new WorkspaceSlot { SlotId = 1, StreamUrl = "example.com/a", StreamName = "A" }]
            };

            service.SaveWorkspaces([workspace]);
            var loaded = service.LoadWorkspaces();

            Assert.Single(loaded);
            Assert.NotNull(loaded[0].LayoutTree);
            Assert.Equal("leaf-1", loaded[0].LayoutTree!.ActiveLeafId);
            Assert.Equal(1, Assert.IsType<LeafLayoutNode>(loaded[0].LayoutTree!.Root).SlotId);
        }
        finally
        {
            if (Directory.Exists(dataFolder))
            {
                Directory.Delete(dataFolder, recursive: true);
            }
        }
    }

    [Fact]
    public void WorkspaceRestoreService_UsesLayoutTreeWhenPresent()
    {
        var service = new WorkspaceRestoreService(
            new WorkspacePresetNormalizationService(new StreamNavigationService()),
            new WorkspaceSlotVisibilityService());
        var workspace = new WorkspacePreset
        {
            Id = "workspace_dynamic",
            Name = "Dynamic",
            LayoutId = "missing",
            LayoutTree = new LayoutTreeDocument
            {
                Root = new SplitLayoutNode
                {
                    Id = "split-root",
                    Orientation = SplitOrientation.Horizontal,
                    Weights = [1, 1],
                    Children =
                    [
                        new LeafLayoutNode { Id = "leaf-1", SlotId = 1, Items = [] },
                        new LeafLayoutNode { Id = "leaf-10", SlotId = 10, Items = [] }
                    ]
                },
                ActiveLeafId = "leaf-10"
            },
            Slots =
            [
                new WorkspaceSlot { SlotId = 1, StreamUrl = "example.com/1" },
                new WorkspaceSlot { SlotId = 10, StreamUrl = "example.com/10" }
            ]
        };

        var prepared = service.Prepare(workspace, [CreateLayout(LayoutPresetIds.Default, [1])]);

        Assert.NotNull(prepared.LayoutTree);
        Assert.Equal("dynamic", prepared.Workspace.LayoutId);
        Assert.Contains(prepared.Workspace.Slots, slot => slot.SlotId == 10 && slot.StreamUrl == "https://example.com/10");
    }

    private static LayoutPreset CreateLayout(string id, int[] slotIds)
    {
        return new LayoutPreset
        {
            Id = id,
            Name = id,
            GridColumns = 4,
            GridRows = 4,
            Slots = slotIds
                .Select(slotId => new LayoutSlot { SlotId = slotId, X = 0, Y = 0, W = 1, H = 1 })
                .ToArray()
        };
    }
}
