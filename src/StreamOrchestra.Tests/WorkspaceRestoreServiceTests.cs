using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class WorkspaceRestoreServiceTests
{
    [Fact]
    public void Prepare_NormalizesPresetAndBlanksSlotsOutsideResolvedLayout()
    {
        var service = CreateService();
        var workspace = new WorkspacePreset
        {
            Id = " workspace_test ",
            Name = " Test ",
            LayoutId = "missing_layout",
            Slots =
            [
                new WorkspaceSlot { SlotId = 1, StreamName = "One", StreamUrl = "example.com/1", Muted = true },
                new WorkspaceSlot { SlotId = 10, StreamName = "Ten", StreamUrl = "example.com/10", Muted = true }
            ]
        };

        var prepared = service.Prepare(workspace, CreateLayouts());

        Assert.Equal(LayoutPresetIds.Default, prepared.Layout.Id);
        Assert.Equal(LayoutPresetIds.Default, prepared.Workspace.LayoutId);
        Assert.Equal("workspace_test", prepared.Workspace.Id);
        Assert.Equal("Test", prepared.Workspace.Name);
        Assert.Equal(16, prepared.Workspace.Slots.Count);
        Assert.Contains(prepared.Workspace.Slots, slot =>
            slot.SlotId == 1 &&
            slot.StreamUrl == "https://example.com/1" &&
            slot.Muted &&
            slot.ProfileGroupId == "A");
        Assert.Contains(prepared.Workspace.Slots, slot =>
            slot.SlotId == 10 &&
            slot.StreamName == "Empty" &&
            slot.StreamUrl == "about:blank" &&
            slot.Muted &&
            slot.ProfileGroupId == "D");
    }

    [Fact]
    public void Prepare_PreservesTournamentSlotsWhenTournamentLayoutIsSelected()
    {
        var service = CreateService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_tournament",
            Name = "Tournament",
            LayoutId = LayoutPresetIds.Tournament,
            Slots =
            [
                new WorkspaceSlot { SlotId = 16, StreamName = "Sixteen", StreamUrl = "example.com/16" }
            ]
        };

        var prepared = service.Prepare(workspace, CreateLayouts());

        Assert.Equal(LayoutPresetIds.Tournament, prepared.Layout.Id);
        Assert.Equal(LayoutPresetIds.Tournament, prepared.Workspace.LayoutId);
        Assert.Contains(prepared.Workspace.Slots, slot =>
            slot.SlotId == 16 &&
            slot.StreamName == "Sixteen" &&
            slot.StreamUrl == "https://example.com/16" &&
            slot.ProfileGroupId == "E");
    }

    [Fact]
    public void Prepare_RejectsEmptyLayoutList()
    {
        var service = CreateService();
        var workspace = new WorkspacePreset { Id = "workspace_test", Name = "Test" };

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare(workspace, []));

        Assert.Contains("At least one layout preset is required.", exception.Message);
    }

    private static WorkspaceRestoreService CreateService()
    {
        var navigationService = new StreamNavigationService();
        return new WorkspaceRestoreService(
            new WorkspacePresetNormalizationService(navigationService),
            new WorkspaceSlotVisibilityService());
    }

    private static LayoutPreset[] CreateLayouts()
    {
        return
        [
            CreateLayout(LayoutPresetIds.Default, [1, 2, 3, 4, 5, 6, 7, 8, 9]),
            CreateLayout(LayoutPresetIds.Tournament, Enumerable.Range(1, 16).ToArray())
        ];
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
