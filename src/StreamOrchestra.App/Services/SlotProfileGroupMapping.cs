namespace StreamOrchestra.App.Services;

public static class SlotProfileGroupMapping
{
    public const int SlotsPerProfileGroup = 3;
    public const int MaxSlotCount = 16;
    public static readonly IReadOnlyList<string> GroupIds = ["A", "B", "C", "D", "E"];

    public static string GetGroupIdForSlot(int slotId)
    {
        if (slotId is < 1 or > MaxSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slotId), slotId, "Slot id must be between 1 and 16.");
        }

        var groupIndex = Math.Min((slotId - 1) / SlotsPerProfileGroup, GroupIds.Count - 1);
        return GroupIds[groupIndex];
    }
}
