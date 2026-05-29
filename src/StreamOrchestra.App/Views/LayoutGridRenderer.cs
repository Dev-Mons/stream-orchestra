using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Views;

/// <summary>
/// 정적 <see cref="LayoutPreset"/> 템플릿을 실제 <see cref="StreamSlotView"/>를 담은 WPF 그리드로 렌더링한다.
/// 동적 트리/스플리터 기반 렌더러(<c>LayoutTreeRenderer</c>)를 대체하며, 슬롯 비율을 실시간으로 재계산하지 않고
/// 템플릿에 고정된 행/열 정의만 사용한다.
/// </summary>
public static class LayoutGridRenderer
{
    public static FrameworkElement Build(
        LayoutPreset layout,
        IReadOnlyDictionary<int, StreamSlotView> slotsById)
    {
        var grid = new Grid { Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)) };

        for (var rowIndex = 0; rowIndex < Math.Max(1, layout.GridRows); rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(GetWeight(layout.RowWeights, rowIndex), GridUnitType.Star)
            });
        }

        for (var columnIndex = 0; columnIndex < Math.Max(1, layout.GridColumns); columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(GetWeight(layout.ColumnWeights, columnIndex), GridUnitType.Star)
            });
        }

        foreach (var slot in layout.Slots.OrderBy(slot => slot.SlotId))
        {
            FrameworkElement child = slotsById.TryGetValue(slot.SlotId, out var slotView)
                ? PrepareSlotView(slotView)
                : CreateMissingSlotPlaceholder(slot.SlotId);

            Grid.SetColumn(child, slot.X);
            Grid.SetRow(child, slot.Y);
            Grid.SetColumnSpan(child, Math.Max(1, slot.W));
            Grid.SetRowSpan(child, Math.Max(1, slot.H));
            grid.Children.Add(child);
        }

        return grid;
    }

    private static StreamSlotView PrepareSlotView(StreamSlotView slotView)
    {
        RemoveFromCurrentParent(slotView);
        return slotView;
    }

    private static double GetWeight(IReadOnlyList<double>? weights, int index)
    {
        return weights is not null && index >= 0 && index < weights.Count && weights[index] > 0
            ? weights[index]
            : 1;
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
