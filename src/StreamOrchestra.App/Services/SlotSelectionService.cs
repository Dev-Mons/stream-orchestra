using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class SlotSelectionService
{
    public int? ResolveVisibleSlotId(LayoutPreset layout, int? requestedSlotId)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (requestedSlotId is null)
        {
            return null;
        }

        var visibleSlotIds = GetVisibleSlotIds(layout)
            .Order()
            .ToArray();
        if (visibleSlotIds.Contains(requestedSlotId.Value))
        {
            return requestedSlotId.Value;
        }

        return visibleSlotIds
            .Select(slotId => (int?)slotId)
            .FirstOrDefault();
    }

    public bool IsSlotVisible(LayoutPreset layout, int slotId)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return GetVisibleSlotIds(layout).Contains(slotId);
    }

    private static IEnumerable<int> GetVisibleSlotIds(LayoutPreset layout)
    {
        IEnumerable<LayoutSlot?> sourceSlots = layout.Slots ?? [];
        return sourceSlots
            .OfType<LayoutSlot>()
            .Select(slot => slot.SlotId);
    }
}
