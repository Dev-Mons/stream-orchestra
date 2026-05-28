using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Views;

public static class LayoutTreeRenderer
{
    public static FrameworkElement Build(
        LayoutTreeDocument tree,
        IReadOnlyDictionary<int, StreamSlotView> slotsById)
    {
        if (tree.Root is null)
        {
            return new Grid();
        }

        return BuildNode(tree.Root, slotsById);
    }

    private static FrameworkElement BuildNode(
        LayoutNode node,
        IReadOnlyDictionary<int, StreamSlotView> slotsById)
    {
        if (node is LeafLayoutNode leaf)
        {
            if (!slotsById.TryGetValue(leaf.SlotId, out var slot))
            {
                return CreateMissingSlotPlaceholder(leaf.SlotId);
            }

            RemoveFromCurrentParent(slot);
            return slot;
        }

        if (node is not SplitLayoutNode split || split.Children.Count == 0)
        {
            return new Grid();
        }

        return split.Orientation == SplitOrientation.Horizontal
            ? BuildHorizontalSplit(split, slotsById)
            : BuildVerticalSplit(split, slotsById);
    }

    private static Grid BuildHorizontalSplit(
        SplitLayoutNode split,
        IReadOnlyDictionary<int, StreamSlotView> slotsById)
    {
        var grid = new Grid();
        var weights = NormalizeWeights(split.Weights, split.Children.Count);

        for (var i = 0; i < split.Children.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(weights[i], GridUnitType.Star)
            });
            if (i < split.Children.Count - 1)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            }
        }

        for (var i = 0; i < split.Children.Count; i++)
        {
            var child = BuildNode(split.Children[i], slotsById);
            Grid.SetColumn(child, i * 2);
            grid.Children.Add(child);

            if (i < split.Children.Count - 1)
            {
                var splitter = CreateSplitter(isVertical: true);
                Grid.SetColumn(splitter, i * 2 + 1);
                grid.Children.Add(splitter);
            }
        }

        return grid;
    }

    private static Grid BuildVerticalSplit(
        SplitLayoutNode split,
        IReadOnlyDictionary<int, StreamSlotView> slotsById)
    {
        var grid = new Grid();
        var weights = NormalizeWeights(split.Weights, split.Children.Count);

        for (var i = 0; i < split.Children.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(weights[i], GridUnitType.Star)
            });
            if (i < split.Children.Count - 1)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            }
        }

        for (var i = 0; i < split.Children.Count; i++)
        {
            var child = BuildNode(split.Children[i], slotsById);
            Grid.SetRow(child, i * 2);
            grid.Children.Add(child);

            if (i < split.Children.Count - 1)
            {
                var splitter = CreateSplitter(isVertical: false);
                Grid.SetRow(splitter, i * 2 + 1);
                grid.Children.Add(splitter);
            }
        }

        return grid;
    }

    private static GridSplitter CreateSplitter(bool isVertical)
    {
        return new GridSplitter
        {
            Width = isVertical ? 6 : double.NaN,
            Height = isVertical ? double.NaN : 6,
            HorizontalAlignment = isVertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Stretch,
            VerticalAlignment = isVertical ? VerticalAlignment.Stretch : VerticalAlignment.Stretch,
            ResizeDirection = isVertical ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            Background = new SolidColorBrush(Color.FromRgb(29, 39, 51))
        };
    }

    private static Border CreateMissingSlotPlaceholder(int slotId)
    {
        return new Border
        {
            Margin = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(15, 20, 27)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 54, 66)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = $"Missing Slot {slotId}",
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
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

    private static void RemoveFromCurrentParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
        }
    }
}
