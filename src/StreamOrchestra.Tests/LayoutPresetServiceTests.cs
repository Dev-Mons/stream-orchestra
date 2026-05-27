using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class LayoutPresetServiceTests
{
    [Fact]
    public void LoadFromFile_LoadsConfiguredLayouts()
    {
        var service = new LayoutPresetService();
        var path = Path.Combine(AppContext.BaseDirectory, "data", "layouts.json");

        var layouts = service.LoadFromFile(path);

        Assert.Collection(
            layouts,
            layout =>
            {
                Assert.Equal("layout_8_small_1_main", layout.Id);
                Assert.Equal(4, layout.GridColumns);
                Assert.Equal(3, layout.GridRows);
                Assert.Equal(9, layout.Slots.Count);
                Assert.Contains(layout.Slots, slot => slot.SlotId == 9 && slot.X == 2 && slot.Y == 1 && slot.W == 2 && slot.H == 2);
            },
            layout =>
            {
                Assert.Equal("layout_4x4", layout.Id);
                Assert.Equal(4, layout.GridColumns);
                Assert.Equal(4, layout.GridRows);
                Assert.Equal(16, layout.Slots.Count);
                Assert.Contains(layout.Slots, slot => slot.SlotId == 16 && slot.X == 3 && slot.Y == 3 && slot.W == 1 && slot.H == 1);
            });
    }

    [Fact]
    public void LoadFromFile_DefaultLayoutMatchesPlanGeometry()
    {
        var service = new LayoutPresetService();
        var path = Path.Combine(AppContext.BaseDirectory, "data", "layouts.json");

        var layout = service.LoadFromFile(path).Single(candidate => candidate.Id == LayoutPresetIds.Default);

        Assert.Equal("8 Small + 1 Main", layout.Name);
        Assert.Equal(4, layout.GridColumns);
        Assert.Equal(3, layout.GridRows);
        Assert.Collection(
            layout.Slots.OrderBy(slot => slot.SlotId),
            slot => AssertSlot(slot, 1, 0, 0, 1, 1),
            slot => AssertSlot(slot, 2, 1, 0, 1, 1),
            slot => AssertSlot(slot, 3, 2, 0, 1, 1),
            slot => AssertSlot(slot, 4, 3, 0, 1, 1),
            slot => AssertSlot(slot, 5, 0, 1, 1, 1),
            slot => AssertSlot(slot, 6, 1, 1, 1, 1),
            slot => AssertSlot(slot, 7, 0, 2, 1, 1),
            slot => AssertSlot(slot, 8, 1, 2, 1, 1),
            slot => AssertSlot(slot, 9, 2, 1, 2, 2));
    }

    [Fact]
    public void LoadFromFile_TournamentLayoutMatchesPlanGeometry()
    {
        var service = new LayoutPresetService();
        var path = Path.Combine(AppContext.BaseDirectory, "data", "layouts.json");

        var layout = service.LoadFromFile(path).Single(candidate => candidate.Id == LayoutPresetIds.Tournament);

        Assert.Equal("4x4 Tournament", layout.Name);
        Assert.Equal(4, layout.GridColumns);
        Assert.Equal(4, layout.GridRows);
        Assert.Collection(
            layout.Slots.OrderBy(slot => slot.SlotId),
            slot => AssertSlot(slot, 1, 0, 0, 1, 1),
            slot => AssertSlot(slot, 2, 1, 0, 1, 1),
            slot => AssertSlot(slot, 3, 2, 0, 1, 1),
            slot => AssertSlot(slot, 4, 3, 0, 1, 1),
            slot => AssertSlot(slot, 5, 0, 1, 1, 1),
            slot => AssertSlot(slot, 6, 1, 1, 1, 1),
            slot => AssertSlot(slot, 7, 2, 1, 1, 1),
            slot => AssertSlot(slot, 8, 3, 1, 1, 1),
            slot => AssertSlot(slot, 9, 0, 2, 1, 1),
            slot => AssertSlot(slot, 10, 1, 2, 1, 1),
            slot => AssertSlot(slot, 11, 2, 2, 1, 1),
            slot => AssertSlot(slot, 12, 3, 2, 1, 1),
            slot => AssertSlot(slot, 13, 0, 3, 1, 1),
            slot => AssertSlot(slot, 14, 1, 3, 1, 1),
            slot => AssertSlot(slot, 15, 2, 3, 1, 1),
            slot => AssertSlot(slot, 16, 3, 3, 1, 1));
    }

    [Fact]
    public void SelectDefaultLayout_UsesMvpDefaultLayout()
    {
        var layouts = new[]
        {
            CreateLayout(LayoutPresetIds.Tournament, 16),
            CreateLayout(LayoutPresetIds.Default, 9)
        };

        var selectedLayout = LayoutPresetService.SelectDefaultLayout(layouts);

        Assert.Equal(LayoutPresetIds.Default, selectedLayout.Id);
    }

    [Fact]
    public void LoadFromDefaultLocation_UsesOnlyBuiltInLayoutsWhenCustomFolderIsNotConfigured()
    {
        var service = new LayoutPresetService();

        var layouts = service.LoadFromDefaultLocation();

        Assert.Equal([LayoutPresetIds.Default, LayoutPresetIds.Tournament], layouts.Select(layout => layout.Id).ToArray());
    }

    [Fact]
    public void SaveCustomLayouts_PersistsLayoutsInConfiguredDataFolder()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var service = new LayoutPresetService(dataFolder);
            var customLayout = CreateNonOverlappingLayout("custom_layout_two_up", "Two Up", 2, 1, [1, 2]);

            service.SaveCustomLayouts([customLayout]);
            var loadedLayouts = service.LoadCustomLayouts();

            Assert.Equal(Path.Combine(dataFolder, "custom-layouts.json"), service.CustomLayoutFilePath);
            Assert.Single(loadedLayouts);
            Assert.Equal("custom_layout_two_up", loadedLayouts[0].Id);
            Assert.Equal("Two Up", loadedLayouts[0].Name);
            Assert.Equal(2, loadedLayouts[0].Slots.Count);
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
    public void CreateCustomLayoutId_UsesUniqueCustomPrefix()
    {
        var existingLayouts = new[]
        {
            CreateLayout("custom_layout_focus", 1),
            CreateLayout("custom_layout_focus_2", 1)
        };

        var layoutId = LayoutPresetService.CreateCustomLayoutId("Focus", existingLayouts);

        Assert.Equal("custom_layout_focus_3", layoutId);
    }

    [Fact]
    public void SelectPlaybackTestLayout_KeepsCurrentLayoutWhenItShowsEnoughSlots()
    {
        var defaultLayout = CreateLayout(LayoutPresetIds.Default, 9);
        var layouts = new[]
        {
            CreateLayout(LayoutPresetIds.Tournament, 16),
            defaultLayout
        };

        var selectedLayout = LayoutPresetService.SelectPlaybackTestLayout(layouts, defaultLayout, 8);

        Assert.Same(defaultLayout, selectedLayout);
    }

    [Fact]
    public void SelectPlaybackTestLayout_UsesTournamentLayoutForLargerPlaybackTests()
    {
        var defaultLayout = CreateLayout(LayoutPresetIds.Default, 9);
        var tournamentLayout = CreateLayout(LayoutPresetIds.Tournament, 16);
        var layouts = new[] { defaultLayout, tournamentLayout };

        var selectedLayout = LayoutPresetService.SelectPlaybackTestLayout(layouts, defaultLayout, 12);

        Assert.Same(tournamentLayout, selectedLayout);
    }

    [Fact]
    public void SelectPlaybackTestLayout_RequiresTheActualPlaybackSlots()
    {
        var defaultLayout = CreateLayout(LayoutPresetIds.Default, [1, 2, 3, 4, 5, 6, 7, 8, 10]);
        var tournamentLayout = CreateLayout(LayoutPresetIds.Tournament, Enumerable.Range(1, 16).ToArray());
        var layouts = new[] { defaultLayout, tournamentLayout };

        var selectedLayout = LayoutPresetService.SelectPlaybackTestLayout(layouts, defaultLayout, 9);

        Assert.Same(tournamentLayout, selectedLayout);
    }

    [Fact]
    public void SelectPlaybackTestLayout_RejectsMissingVisibleSlotCapacity()
    {
        var defaultLayout = CreateLayout(LayoutPresetIds.Default, 8);
        var layouts = new[] { defaultLayout };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LayoutPresetService.SelectPlaybackTestLayout(layouts, defaultLayout, 12));

        Assert.Contains("No layout can show playback test slot(s): 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12.", exception.Message);
    }

    [Fact]
    public void SelectLayoutContainingSlots_KeepsCurrentLayoutWhenAllTargetSlotsAreVisible()
    {
        var defaultLayout = CreateLayout(LayoutPresetIds.Default, [1, 2, 3, 4, 9]);
        var tournamentLayout = CreateLayout(LayoutPresetIds.Tournament, Enumerable.Range(1, 16).ToArray());
        var layouts = new[] { defaultLayout, tournamentLayout };

        var selectedLayout = LayoutPresetService.SelectLayoutContainingSlots(layouts, defaultLayout, [1, 4, 9]);

        Assert.Same(defaultLayout, selectedLayout);
    }

    [Fact]
    public void SelectLayoutContainingSlots_UsesTournamentLayoutWhenCurrentLayoutHidesTargetSlots()
    {
        var defaultLayout = CreateLayout(LayoutPresetIds.Default, [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        var tournamentLayout = CreateLayout(LayoutPresetIds.Tournament, Enumerable.Range(1, 16).ToArray());
        var layouts = new[] { defaultLayout, tournamentLayout };

        var selectedLayout = LayoutPresetService.SelectLayoutContainingSlots(layouts, defaultLayout, [9, 10, 11, 12]);

        Assert.Same(tournamentLayout, selectedLayout);
    }

    [Fact]
    public void SelectLayoutContainingSlots_RejectsWhenNoLayoutContainsTargetSlots()
    {
        var defaultLayout = CreateLayout(LayoutPresetIds.Default, [1, 2, 3, 4]);
        var layouts = new[] { defaultLayout };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LayoutPresetService.SelectLayoutContainingSlots(layouts, defaultLayout, [1, 9]));

        Assert.Contains("No layout can show target slot(s): 1, 9.", exception.Message);
    }

    [Fact]
    public void Validate_RejectsSlotsOutsideGridBounds()
    {
        var invalidLayout = """
            [
              {
                "id": "invalid",
                "name": "Invalid",
                "gridColumns": 4,
                "gridRows": 4,
                "slots": [
                  { "slotId": 1, "x": 3, "y": 0, "w": 2, "h": 1 }
                ]
              }
            ]
            """;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, invalidLayout);

        try
        {
            var service = new LayoutPresetService();

            var exception = Assert.Throws<InvalidOperationException>(() => service.LoadFromFile(path));

            Assert.Contains("exceeds the grid bounds", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_RejectsNullLayoutEntries()
    {
        var invalidLayout = """
            [
              null
            ]
            """;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, invalidLayout);

        try
        {
            var service = new LayoutPresetService();

            var exception = Assert.Throws<InvalidOperationException>(() => service.LoadFromFile(path));

            Assert.Contains("Layout entry is required.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_RejectsNullSlotCollection()
    {
        var invalidLayout = """
            [
              {
                "id": "invalid_slots",
                "name": "Invalid Slots",
                "gridColumns": 4,
                "gridRows": 4,
                "slots": null
              }
            ]
            """;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, invalidLayout);

        try
        {
            var service = new LayoutPresetService();

            var exception = Assert.Throws<InvalidOperationException>(() => service.LoadFromFile(path));

            Assert.Contains("Layout invalid_slots must contain at least one slot.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_RejectsNullSlotEntries()
    {
        var invalidLayout = """
            [
              {
                "id": "invalid_slot_entry",
                "name": "Invalid Slot Entry",
                "gridColumns": 4,
                "gridRows": 4,
                "slots": [
                  null
                ]
              }
            ]
            """;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, invalidLayout);

        try
        {
            var service = new LayoutPresetService();

            var exception = Assert.Throws<InvalidOperationException>(() => service.LoadFromFile(path));

            Assert.Contains("Layout invalid_slot_entry contains a null slot entry.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_RejectsOverlappingSlotCells()
    {
        var invalidLayout = """
            [
              {
                "id": "invalid_overlap",
                "name": "Invalid Overlap",
                "gridColumns": 4,
                "gridRows": 4,
                "slots": [
                  { "slotId": 1, "x": 0, "y": 0, "w": 2, "h": 2 },
                  { "slotId": 2, "x": 1, "y": 1, "w": 1, "h": 1 }
                ]
              }
            ]
            """;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, invalidLayout);

        try
        {
            var service = new LayoutPresetService();

            var exception = Assert.Throws<InvalidOperationException>(() => service.LoadFromFile(path));

            Assert.Contains("overlaps another slot", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static LayoutPreset CreateLayout(string id, int visibleSlotCount)
    {
        return CreateLayout(id, Enumerable.Range(1, visibleSlotCount).ToArray());
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

    private static LayoutPreset CreateNonOverlappingLayout(
        string id,
        string name,
        int columns,
        int rows,
        int[] slotIds)
    {
        return new LayoutPreset
        {
            Id = id,
            Name = name,
            GridColumns = columns,
            GridRows = rows,
            Slots = slotIds
                .Select((slotId, index) => new LayoutSlot
                {
                    SlotId = slotId,
                    X = index % columns,
                    Y = index / columns,
                    W = 1,
                    H = 1
                })
                .ToArray()
        };
    }

    private static void AssertSlot(LayoutSlot slot, int slotId, int x, int y, int width, int height)
    {
        Assert.Equal(slotId, slot.SlotId);
        Assert.Equal(x, slot.X);
        Assert.Equal(y, slot.Y);
        Assert.Equal(width, slot.W);
        Assert.Equal(height, slot.H);
    }
}
