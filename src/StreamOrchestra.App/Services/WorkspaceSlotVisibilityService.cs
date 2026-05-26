using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class WorkspaceSlotVisibilityService
{
    public WorkspacePreset BlankHiddenSlots(WorkspacePreset workspace, LayoutPreset layout)
    {
        var visibleSlotIds = layout.Slots
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
