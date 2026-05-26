using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SlotSelectionServiceTests
{
    [Fact]
    public void ResolveVisibleSlotId_KeepsRequestedSlotWhenVisible()
    {
        var service = new SlotSelectionService();
        var layout = CreateLayout([1, 2, 9]);

        var slotId = service.ResolveVisibleSlotId(layout, 9);

        Assert.Equal(9, slotId);
    }

    [Fact]
    public void ResolveVisibleSlotId_FallsBackToFirstVisibleSlotWhenRequestedSlotIsHidden()
    {
        var service = new SlotSelectionService();
        var layout = CreateLayout([3, 1, 9]);

        var slotId = service.ResolveVisibleSlotId(layout, 16);

        Assert.Equal(1, slotId);
    }

    [Fact]
    public void ResolveVisibleSlotId_KeepsNullSelectionUnset()
    {
        var service = new SlotSelectionService();
        var layout = CreateLayout([1, 2, 9]);

        var slotId = service.ResolveVisibleSlotId(layout, null);

        Assert.Null(slotId);
    }

    [Fact]
    public void ResolveVisibleSlotId_ReturnsNullWhenLayoutHasNoVisibleSlots()
    {
        var service = new SlotSelectionService();
        var layout = new LayoutPreset
        {
            Id = LayoutPresetIds.Default,
            Name = "Malformed",
            GridColumns = 4,
            GridRows = 4,
            Slots = null!
        };

        var slotId = service.ResolveVisibleSlotId(layout, 16);

        Assert.Null(slotId);
    }

    [Fact]
    public void ResolveVisibleSlotId_IgnoresNullSlotEntries()
    {
        var service = new SlotSelectionService();
        var layout = new LayoutPreset
        {
            Id = LayoutPresetIds.Default,
            Name = "Malformed",
            GridColumns = 4,
            GridRows = 4,
            Slots =
            [
                null!,
                new LayoutSlot { SlotId = 4, X = 0, Y = 0, W = 1, H = 1 }
            ]
        };

        var slotId = service.ResolveVisibleSlotId(layout, 16);

        Assert.Equal(4, slotId);
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public void IsSlotVisible_ReturnsWhetherSlotIsInLayout(int slotId, bool expected)
    {
        var service = new SlotSelectionService();
        var layout = CreateLayout([1, 2, 9]);

        var isVisible = service.IsSlotVisible(layout, slotId);

        Assert.Equal(expected, isVisible);
    }

    [Fact]
    public void IsSlotVisible_ReturnsFalseForNullSlotCollection()
    {
        var service = new SlotSelectionService();
        var layout = new LayoutPreset
        {
            Id = LayoutPresetIds.Default,
            Name = "Malformed",
            GridColumns = 4,
            GridRows = 4,
            Slots = null!
        };

        var isVisible = service.IsSlotVisible(layout, 1);

        Assert.False(isVisible);
    }

    [Fact]
    public void Methods_RejectNullLayout()
    {
        var service = new SlotSelectionService();

        Assert.Throws<ArgumentNullException>(() => service.ResolveVisibleSlotId(null!, 1));
        Assert.Throws<ArgumentNullException>(() => service.IsSlotVisible(null!, 1));
    }

    private static LayoutPreset CreateLayout(int[] slotIds)
    {
        return new LayoutPreset
        {
            Id = LayoutPresetIds.Default,
            Name = "Test",
            GridColumns = 4,
            GridRows = 4,
            Slots = slotIds
                .Select(slotId => new LayoutSlot { SlotId = slotId, X = 0, Y = 0, W = 1, H = 1 })
                .ToArray()
        };
    }
}
