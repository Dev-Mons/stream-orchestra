using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class WorkspaceSlotVisibilityService
{
    public WorkspacePreset BlankHiddenSlots(WorkspacePreset workspace, LayoutPreset layout)
    {
        IEnumerable<LayoutSlot?> sourceLayoutSlots = layout.Slots ?? [];
        var visibleSlotIds = sourceLayoutSlots
            .OfType<LayoutSlot>()
            .Where(slot => slot.SlotId is >= 1 and <= PlaybackTestPlanService.MaxSlotCount)
            .Select(slot => slot.SlotId)
            .ToHashSet();
        IEnumerable<WorkspaceSlot?> sourceSlots = workspace.Slots ?? [];

        return new WorkspacePreset
        {
            Id = workspace.Id,
            Name = workspace.Name,
            LayoutId = layout.Id,
            Slots = sourceSlots
                .OfType<WorkspaceSlot>()
                .Select(slot => visibleSlotIds.Contains(slot.SlotId) ? slot : BlankSlot(slot))
                .ToArray()
        };
    }

    public WorkspacePreset BlankHiddenSlots(WorkspacePreset workspace, LayoutTreeDocument layoutTree)
    {
        var visibleSlotIds = layoutTree.Root is null
            ? new HashSet<int>()
            : LayoutTreePresetConverter.GetVisibleSlotIds(layoutTree)
                .Where(slotId => slotId is >= 1 and <= PlaybackTestPlanService.MaxSlotCount)
                .ToHashSet();
        IEnumerable<WorkspaceSlot?> sourceSlots = workspace.Slots ?? [];

        return new WorkspacePreset
        {
            Id = workspace.Id,
            Name = workspace.Name,
            LayoutId = "dynamic",
            LayoutTree = layoutTree,
            Slots = sourceSlots
                .OfType<WorkspaceSlot>()
                .Select(slot => visibleSlotIds.Contains(slot.SlotId) ? slot : BlankSlot(slot))
                .ToArray()
        };
    }

    private static WorkspaceSlot BlankSlot(WorkspaceSlot slot)
    {
        return new WorkspaceSlot
        {
            SlotId = slot.SlotId,
            StreamName = "Empty",
            StreamUrl = "about:blank",
            Muted = slot.Muted,
            ProfileGroupId = slot.ProfileGroupId
        };
    }
}
