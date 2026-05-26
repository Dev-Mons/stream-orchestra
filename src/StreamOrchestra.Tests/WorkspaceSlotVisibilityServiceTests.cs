using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class WorkspaceSlotVisibilityServiceTests
{
    [Fact]
    public void BlankHiddenSlots_BlanksSlotsOutsideTheCurrentLayout()
    {
        var service = new WorkspaceSlotVisibilityService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = LayoutPresetIds.Default,
            Slots =
            [
                CreateSlot(1, "https://example.com/1", muted: false, profileGroupId: "A"),
                CreateSlot(9, "https://example.com/9", muted: true, profileGroupId: "C"),
                CreateSlot(10, "https://example.com/10", muted: true, profileGroupId: "C")
            ]
        };
        var layout = new LayoutPreset
        {
            Id = LayoutPresetIds.Default,
            Name = "8 Small + 1 Main",
            GridColumns = 4,
            GridRows = 3,
            Slots =
            [
                new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 },
                new LayoutSlot { SlotId = 9, X = 2, Y = 1, W = 2, H = 2 }
            ]
        };

        var visibleWorkspace = service.BlankHiddenSlots(workspace, layout);

        Assert.Contains(visibleWorkspace.Slots, slot => slot.SlotId == 1 && slot.StreamUrl == "https://example.com/1");
        Assert.Contains(visibleWorkspace.Slots, slot => slot.SlotId == 9 && slot.StreamUrl == "https://example.com/9" && slot.Muted);
        Assert.Contains(visibleWorkspace.Slots, slot =>
            slot.SlotId == 10 &&
            slot.StreamUrl == "about:blank" &&
            slot.StreamName == "Empty" &&
            slot.Muted &&
            slot.ProfileGroupId == "C");
    }

    [Fact]
    public void BlankHiddenSlots_TreatsNullSlotCollectionAsEmpty()
    {
        var service = new WorkspaceSlotVisibilityService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = LayoutPresetIds.Default,
            Slots = null!
        };

        var visibleWorkspace = service.BlankHiddenSlots(workspace, CreateDefaultLayout());

        Assert.Empty(visibleWorkspace.Slots);
        Assert.Equal(LayoutPresetIds.Default, visibleWorkspace.LayoutId);
    }

    [Fact]
    public void BlankHiddenSlots_IgnoresNullSlotEntries()
    {
        var service = new WorkspaceSlotVisibilityService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = LayoutPresetIds.Default,
            Slots =
            [
                null!,
                CreateSlot(1, "https://example.com/1", muted: false, profileGroupId: "A"),
                CreateSlot(10, "https://example.com/10", muted: true, profileGroupId: "C")
            ]
        };

        var visibleWorkspace = service.BlankHiddenSlots(workspace, CreateDefaultLayout());

        Assert.Equal(2, visibleWorkspace.Slots.Count);
        Assert.Contains(visibleWorkspace.Slots, slot => slot.SlotId == 1 && slot.StreamUrl == "https://example.com/1");
        Assert.Contains(visibleWorkspace.Slots, slot => slot.SlotId == 10 && slot.StreamUrl == "about:blank");
    }

    [Fact]
    public void BlankHiddenSlots_TreatsNullLayoutSlotCollectionAsNoVisibleSlots()
    {
        var service = new WorkspaceSlotVisibilityService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = LayoutPresetIds.Default,
            Slots =
            [
                CreateSlot(1, "https://example.com/1", muted: false, profileGroupId: "A"),
                CreateSlot(9, "https://example.com/9", muted: true, profileGroupId: "C")
            ]
        };
        var layout = new LayoutPreset
        {
            Id = LayoutPresetIds.Default,
            Name = "Malformed",
            GridColumns = 4,
            GridRows = 3,
            Slots = null!
        };

        var visibleWorkspace = service.BlankHiddenSlots(workspace, layout);

        Assert.All(visibleWorkspace.Slots, slot => Assert.Equal("about:blank", slot.StreamUrl));
        Assert.Contains(visibleWorkspace.Slots, slot =>
            slot.SlotId == 9 &&
            slot.StreamName == "Empty" &&
            slot.Muted &&
            slot.ProfileGroupId == "C");
    }

    [Fact]
    public void BlankHiddenSlots_IgnoresNullAndInvalidLayoutSlotEntries()
    {
        var service = new WorkspaceSlotVisibilityService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = LayoutPresetIds.Default,
            Slots =
            [
                CreateSlot(1, "https://example.com/1", muted: false, profileGroupId: "A"),
                CreateSlot(2, "https://example.com/2", muted: false, profileGroupId: "A"),
                CreateSlot(16, "https://example.com/16", muted: true, profileGroupId: "D")
            ]
        };
        var layout = new LayoutPreset
        {
            Id = LayoutPresetIds.Default,
            Name = "Malformed",
            GridColumns = 4,
            GridRows = 3,
            Slots =
            [
                null!,
                new LayoutSlot { SlotId = 0, X = 0, Y = 0, W = 1, H = 1 },
                new LayoutSlot { SlotId = 2, X = 1, Y = 0, W = 1, H = 1 },
                new LayoutSlot { SlotId = 17, X = 2, Y = 0, W = 1, H = 1 }
            ]
        };

        var visibleWorkspace = service.BlankHiddenSlots(workspace, layout);

        Assert.Contains(visibleWorkspace.Slots, slot => slot.SlotId == 2 && slot.StreamUrl == "https://example.com/2");
        Assert.Contains(visibleWorkspace.Slots, slot => slot.SlotId == 1 && slot.StreamUrl == "about:blank");
        Assert.Contains(visibleWorkspace.Slots, slot =>
            slot.SlotId == 16 &&
            slot.StreamUrl == "about:blank" &&
            slot.Muted &&
            slot.ProfileGroupId == "D");
    }

    private static LayoutPreset CreateDefaultLayout()
    {
        return new LayoutPreset
        {
            Id = LayoutPresetIds.Default,
            Name = "8 Small + 1 Main",
            GridColumns = 4,
            GridRows = 3,
            Slots =
            [
                new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 },
                new LayoutSlot { SlotId = 9, X = 2, Y = 1, W = 2, H = 2 }
            ]
        };
    }

    private static WorkspaceSlot CreateSlot(
        int slotId,
        string streamUrl,
        bool muted,
        string profileGroupId)
    {
        return new WorkspaceSlot
        {
            SlotId = slotId,
            StreamName = $"Stream {slotId}",
            StreamUrl = streamUrl,
            Muted = muted,
            ProfileGroupId = profileGroupId
        };
    }
}
