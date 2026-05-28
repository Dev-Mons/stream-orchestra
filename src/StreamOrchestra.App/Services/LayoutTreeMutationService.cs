using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class LayoutTreeMutationService
{
    public LayoutNode InsertSplit(
        LayoutNode root,
        string targetLeafId,
        LeafLayoutNode newLeaf,
        DockDirection direction)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(newLeaf);

        if (!DropZoneService.IsEdge(direction))
        {
            throw new ArgumentException("A split insertion requires an edge direction.", nameof(direction));
        }

        var replaced = ReplaceTarget(root, targetLeafId, newLeaf, direction, out var wasReplaced);
        if (!wasReplaced)
        {
            throw new InvalidOperationException($"Leaf '{targetLeafId}' was not found.");
        }

        return Normalize(replaced);
    }

    public LayoutNode RemoveLeaf(LayoutNode root, string targetLeafId)
    {
        ArgumentNullException.ThrowIfNull(root);

        var nextRoot = RemoveTarget(root, targetLeafId, out var wasRemoved);
        if (!wasRemoved)
        {
            throw new InvalidOperationException($"Leaf '{targetLeafId}' was not found.");
        }

        if (nextRoot is null)
        {
            throw new InvalidOperationException("Cannot remove the only leaf.");
        }

        return Normalize(nextRoot);
    }

    private static LayoutNode ReplaceTarget(
        LayoutNode node,
        string targetLeafId,
        LeafLayoutNode newLeaf,
        DockDirection direction,
        out bool wasReplaced)
    {
        switch (node)
        {
            case LeafLayoutNode leaf when leaf.Id.Equals(targetLeafId, StringComparison.Ordinal):
                wasReplaced = true;
                return CreateSplitForDrop(leaf, newLeaf, direction);

            case LeafLayoutNode:
                wasReplaced = false;
                return node;

            case SplitLayoutNode split:
            {
                var children = new List<LayoutNode>(split.Children.Count);
                wasReplaced = false;
                foreach (var child in split.Children)
                {
                    if (wasReplaced)
                    {
                        children.Add(child);
                        continue;
                    }

                    var nextChild = ReplaceTarget(child, targetLeafId, newLeaf, direction, out var childReplaced);
                    wasReplaced = childReplaced;
                    children.Add(nextChild);
                }

                return split with { Children = children };
            }

            default:
                wasReplaced = false;
                return node;
        }
    }

    private static SplitLayoutNode CreateSplitForDrop(
        LeafLayoutNode targetLeaf,
        LeafLayoutNode newLeaf,
        DockDirection direction)
    {
        var orientation = direction is DockDirection.Left or DockDirection.Right
            ? SplitOrientation.Horizontal
            : SplitOrientation.Vertical;
        var children = direction is DockDirection.Left or DockDirection.Top
            ? new LayoutNode[] { newLeaf, targetLeaf }
            : [targetLeaf, newLeaf];

        return new SplitLayoutNode
        {
            Id = $"split_{Guid.NewGuid():N}",
            Orientation = orientation,
            Children = children,
            Weights = [1, 1]
        };
    }

    private static LayoutNode? RemoveTarget(LayoutNode node, string targetLeafId, out bool wasRemoved)
    {
        switch (node)
        {
            case LeafLayoutNode leaf when leaf.Id.Equals(targetLeafId, StringComparison.Ordinal):
                wasRemoved = true;
                return null;

            case LeafLayoutNode:
                wasRemoved = false;
                return node;

            case SplitLayoutNode split:
            {
                var children = new List<LayoutNode>(split.Children.Count);
                wasRemoved = false;
                foreach (var child in split.Children)
                {
                    if (wasRemoved)
                    {
                        children.Add(child);
                        continue;
                    }

                    var nextChild = RemoveTarget(child, targetLeafId, out var childRemoved);
                    wasRemoved = childRemoved;
                    if (nextChild is not null)
                    {
                        children.Add(nextChild);
                    }
                }

                return split with { Children = children };
            }

            default:
                wasRemoved = false;
                return node;
        }
    }

    private static LayoutNode Normalize(LayoutNode node)
    {
        if (node is not SplitLayoutNode split)
        {
            return node;
        }

        var normalizedChildren = split.Children
            .Select(Normalize)
            .ToArray();

        if (normalizedChildren.Length == 1)
        {
            return normalizedChildren[0];
        }

        return split with
        {
            Children = normalizedChildren,
            Weights = NormalizeWeights(split.Weights, normalizedChildren.Length)
        };
    }

    private static IReadOnlyList<double> NormalizeWeights(IReadOnlyList<double> weights, int count)
    {
        if (weights.Count == count && weights.All(weight => weight > 0))
        {
            return weights.ToArray();
        }

        return Enumerable.Repeat(1d, count).ToArray();
    }
}
