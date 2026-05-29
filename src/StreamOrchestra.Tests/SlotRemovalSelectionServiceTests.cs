using System.Reflection;

namespace StreamOrchestra.Tests;

public sealed class SlotRemovalSelectionServiceTests
{
    [Fact]
    public void ToggleSlot_SelectsAndDeselectsMultipleSlots()
    {
        var service = CreateService();

        Assert.True(ToggleSlot(service, slotId: 2, visibleSlotCount: 5));
        Assert.True(ToggleSlot(service, slotId: 4, visibleSlotCount: 5));

        Assert.Equal([2, 4], GetSelectedSlotIds(service));
        Assert.Equal(3, GetTargetSlotCount(service, currentVisibleSlotCount: 5));

        Assert.True(ToggleSlot(service, slotId: 2, visibleSlotCount: 5));

        Assert.Equal([4], GetSelectedSlotIds(service));
        Assert.Equal(4, GetTargetSlotCount(service, currentVisibleSlotCount: 5));
    }

    [Fact]
    public void ToggleSlot_KeepsAtLeastOneVisibleSlot()
    {
        var service = CreateService();

        Assert.True(ToggleSlot(service, slotId: 1, visibleSlotCount: 2));
        Assert.False(ToggleSlot(service, slotId: 2, visibleSlotCount: 2));

        Assert.Equal([1], GetSelectedSlotIds(service));
        Assert.Equal(1, GetTargetSlotCount(service, currentVisibleSlotCount: 2));

        Assert.True(ToggleSlot(service, slotId: 1, visibleSlotCount: 2));
        Assert.True(ToggleSlot(service, slotId: 2, visibleSlotCount: 2));

        Assert.Equal([2], GetSelectedSlotIds(service));
    }

    private static object CreateService()
    {
        var type = Type.GetType(
            "StreamOrchestra.App.Services.SlotRemovalSelectionService, StreamOrchestra.App",
            throwOnError: false);
        Assert.NotNull(type);
        return Activator.CreateInstance(type!)!;
    }

    private static bool ToggleSlot(object service, int slotId, int visibleSlotCount)
    {
        return (bool)Invoke(service, "ToggleSlot", slotId, visibleSlotCount);
    }

    private static int GetTargetSlotCount(object service, int currentVisibleSlotCount)
    {
        return (int)Invoke(service, "GetTargetSlotCount", currentVisibleSlotCount);
    }

    private static int[] GetSelectedSlotIds(object service)
    {
        return ((IEnumerable<int>)Invoke(service, "GetSelectedSlotIds")).ToArray();
    }

    private static object Invoke(object service, string methodName, params object[] parameters)
    {
        var method = service.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        return method!.Invoke(service, parameters)!;
    }
}
