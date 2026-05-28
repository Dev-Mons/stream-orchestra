using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class LayoutTreeMutationServiceTests
{
    [Fact]
    public void InsertSplit_AddsNewLeafOnRequestedSideWithoutChangingExistingLeaf()
    {
        var service = new LayoutTreeMutationService();
        var root = new LeafLayoutNode { Id = "leaf-a", SlotId = 1, Items = [] };
        var newLeaf = new LeafLayoutNode { Id = "leaf-b", SlotId = 2, Items = [] };

        var result = service.InsertSplit(root, "leaf-a", newLeaf, DockDirection.Right);

        var split = Assert.IsType<SplitLayoutNode>(result);
        Assert.Equal(SplitOrientation.Horizontal, split.Orientation);
        Assert.Equal([1, 1], split.Weights);
        Assert.Collection(
            split.Children,
            child => Assert.Equal(1, Assert.IsType<LeafLayoutNode>(child).SlotId),
            child => Assert.Equal(2, Assert.IsType<LeafLayoutNode>(child).SlotId));
    }

    [Fact]
    public void InsertSplit_CanNestUnderExistingTree()
    {
        var service = new LayoutTreeMutationService();
        var root = new SplitLayoutNode
        {
            Id = "root",
            Orientation = SplitOrientation.Horizontal,
            Weights = [1, 1],
            Children =
            [
                new LeafLayoutNode { Id = "leaf-a", SlotId = 1, Items = [] },
                new LeafLayoutNode { Id = "leaf-b", SlotId = 2, Items = [] }
            ]
        };

        var result = service.InsertSplit(
            root,
            "leaf-a",
            new LeafLayoutNode { Id = "leaf-c", SlotId = 3, Items = [] },
            DockDirection.Bottom);

        var split = Assert.IsType<SplitLayoutNode>(result);
        var leftNestedSplit = Assert.IsType<SplitLayoutNode>(split.Children[0]);
        Assert.Equal(SplitOrientation.Vertical, leftNestedSplit.Orientation);
        Assert.Equal([1, 2, 3], LayoutTreePresetConverter.GetVisibleSlotIds(new LayoutTreeDocument { Root = result }).Order().ToArray());
    }
}
