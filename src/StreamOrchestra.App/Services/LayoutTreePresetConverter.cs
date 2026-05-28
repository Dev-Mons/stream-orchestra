using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class LayoutTreePresetConverter
{
    public static LayoutTreeDocument CreateSingleSlot(int slotId)
    {
        var leaf = CreateLeaf(slotId);
        return new LayoutTreeDocument
        {
            SourceLayoutId = "dynamic",
            Root = leaf,
            ActiveLeafId = leaf.Id
        };
    }

    public static LayoutTreeDocument Convert(LayoutPreset layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var slots = layout.Slots
            .Where(slot => slot.SlotId is >= 1 and <= PlaybackTestPlanService.MaxSlotCount)
            .OrderBy(slot => slot.Y)
            .ThenBy(slot => slot.X)
            .ThenBy(slot => slot.SlotId)
            .ToArray();

        var root = BuildNode(slots, 0, 0, Math.Max(1, layout.GridColumns), Math.Max(1, layout.GridRows));
        return new LayoutTreeDocument
        {
            SourceLayoutId = $"preset:{layout.Id}",
            Root = root,
            ActiveLeafId = GetLeaves(root).FirstOrDefault()?.Id
        };
    }

    public static IReadOnlyList<int> GetVisibleSlotIds(LayoutTreeDocument tree)
    {
        return tree.Root is null
            ? []
            : GetLeaves(tree.Root).Select(leaf => leaf.SlotId).ToArray();
    }

    public static LeafLayoutNode CreateLeaf(int slotId)
    {
        return new LeafLayoutNode
        {
            Id = $"leaf_{slotId}_{Guid.NewGuid():N}",
            SlotId = slotId,
            Items = []
        };
    }

    public static IEnumerable<LeafLayoutNode> GetLeaves(LayoutNode node)
    {
        if (node is LeafLayoutNode currentLeaf)
        {
            yield return currentLeaf;
            yield break;
        }

        if (node is not SplitLayoutNode split)
        {
            yield break;
        }

        foreach (var child in split.Children)
        {
            foreach (var childLeaf in GetLeaves(child))
            {
                yield return childLeaf;
            }
        }
    }

    private static LayoutNode BuildNode(IReadOnlyList<LayoutSlot> slots, int x, int y, int width, int height)
    {
        if (slots.Count == 0)
        {
            return CreateLeaf(1);
        }

        if (slots.Count == 1)
        {
            return CreateLeaf(slots[0].SlotId);
        }

        var verticalBoundary = FindVerticalBoundary(slots, x, width);
        if (verticalBoundary is not null)
        {
            var left = slots.Where(slot => slot.X + slot.W <= verticalBoundary.Value).ToArray();
            var right = slots.Where(slot => slot.X >= verticalBoundary.Value).ToArray();
            return new SplitLayoutNode
            {
                Id = $"split_{Guid.NewGuid():N}",
                Orientation = SplitOrientation.Horizontal,
                Children =
                [
                    BuildNode(left, x, y, verticalBoundary.Value - x, height),
                    BuildNode(right, verticalBoundary.Value, y, x + width - verticalBoundary.Value, height)
                ],
                Weights = [verticalBoundary.Value - x, x + width - verticalBoundary.Value]
            };
        }

        var horizontalBoundary = FindHorizontalBoundary(slots, y, height);
        if (horizontalBoundary is not null)
        {
            var top = slots.Where(slot => slot.Y + slot.H <= horizontalBoundary.Value).ToArray();
            var bottom = slots.Where(slot => slot.Y >= horizontalBoundary.Value).ToArray();
            return new SplitLayoutNode
            {
                Id = $"split_{Guid.NewGuid():N}",
                Orientation = SplitOrientation.Vertical,
                Children =
                [
                    BuildNode(top, x, y, width, horizontalBoundary.Value - y),
                    BuildNode(bottom, x, horizontalBoundary.Value, width, y + height - horizontalBoundary.Value)
                ],
                Weights = [horizontalBoundary.Value - y, y + height - horizontalBoundary.Value]
            };
        }

        return BuildBalancedFallback(slots);
    }

    private static int? FindVerticalBoundary(IReadOnlyList<LayoutSlot> slots, int x, int width)
    {
        return Enumerable.Range(x + 1, Math.Max(0, width - 1))
            .Where(boundary => slots.All(slot => slot.X + slot.W <= boundary || slot.X >= boundary))
            .Where(boundary => slots.Any(slot => slot.X + slot.W <= boundary) && slots.Any(slot => slot.X >= boundary))
            .OrderBy(boundary => Math.Abs(boundary - (x + width / 2d)))
            .Cast<int?>()
            .FirstOrDefault();
    }

    private static int? FindHorizontalBoundary(IReadOnlyList<LayoutSlot> slots, int y, int height)
    {
        return Enumerable.Range(y + 1, Math.Max(0, height - 1))
            .Where(boundary => slots.All(slot => slot.Y + slot.H <= boundary || slot.Y >= boundary))
            .Where(boundary => slots.Any(slot => slot.Y + slot.H <= boundary) && slots.Any(slot => slot.Y >= boundary))
            .OrderBy(boundary => Math.Abs(boundary - (y + height / 2d)))
            .Cast<int?>()
            .FirstOrDefault();
    }

    private static LayoutNode BuildBalancedFallback(IReadOnlyList<LayoutSlot> slots)
    {
        if (slots.Count == 1)
        {
            return CreateLeaf(slots[0].SlotId);
        }

        var midpoint = slots.Count / 2;
        return new SplitLayoutNode
        {
            Id = $"split_{Guid.NewGuid():N}",
            Orientation = SplitOrientation.Horizontal,
            Children =
            [
                BuildBalancedFallback(slots.Take(midpoint).ToArray()),
                BuildBalancedFallback(slots.Skip(midpoint).ToArray())
            ],
            Weights = [midpoint, slots.Count - midpoint]
        };
    }
}
