using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class LayoutTreePresetConverterTests
{
    [Fact]
    public void Convert_PreservesVisibleSlotsAndApproximatesGridSplits()
    {
        var layout = new LayoutPreset
        {
            Id = "layout_test",
            Name = "Test",
            GridColumns = 2,
            GridRows = 2,
            Slots =
            [
                new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 },
                new LayoutSlot { SlotId = 2, X = 1, Y = 0, W = 1, H = 1 },
                new LayoutSlot { SlotId = 3, X = 0, Y = 1, W = 2, H = 1 }
            ]
        };

        var tree = LayoutTreePresetConverter.Convert(layout);

        Assert.Equal("preset:layout_test", tree.SourceLayoutId);
        Assert.Equal([1, 2, 3], LayoutTreePresetConverter.GetVisibleSlotIds(tree).Order().ToArray());
        Assert.IsType<SplitLayoutNode>(tree.Root);
    }

    [Fact]
    public void CreateSingleSlot_CreatesInitialOneScreenLayout()
    {
        var tree = LayoutTreePresetConverter.CreateSingleSlot(1);

        var leaf = Assert.IsType<LeafLayoutNode>(tree.Root);
        Assert.Equal(1, leaf.SlotId);
        Assert.Equal(leaf.Id, tree.ActiveLeafId);
    }
}
