namespace StreamOrchestra.App.Services;

public sealed class SlotRemovalSelectionService
{
    private readonly HashSet<int> _selectedSlotIds = [];

    public int SelectedCount => _selectedSlotIds.Count;

    public bool HasSelection => _selectedSlotIds.Count > 0;

    public bool ToggleSlot(int slotId, int visibleSlotCount)
    {
        if (_selectedSlotIds.Remove(slotId))
        {
            return true;
        }

        if (_selectedSlotIds.Count >= Math.Max(0, visibleSlotCount - 1))
        {
            return false;
        }

        _selectedSlotIds.Add(slotId);
        return true;
    }

    public bool IsSelected(int slotId)
    {
        return _selectedSlotIds.Contains(slotId);
    }

    public int GetTargetSlotCount(int currentVisibleSlotCount)
    {
        return Math.Max(0, currentVisibleSlotCount - _selectedSlotIds.Count);
    }

    public IReadOnlyList<int> GetSelectedSlotIds()
    {
        return _selectedSlotIds
            .OrderBy(slotId => slotId)
            .ToArray();
    }

    public void Clear()
    {
        _selectedSlotIds.Clear();
    }
}
