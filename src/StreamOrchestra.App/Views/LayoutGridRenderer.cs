using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

/// <summary>
/// 정적 <see cref="LayoutPreset"/> 템플릿을 실제 <see cref="StreamSlotView"/>를 담은 WPF 표면으로 렌더링한다.
/// 슬롯별 normalized bounds가 있으면 독립 좌표로 배치하고, 기존 grid 템플릿은 bounds로 변환해 호환한다.
/// </summary>
public static class LayoutGridRenderer
{
    public static FrameworkElement Build(
        LayoutPreset layout,
        IReadOnlyDictionary<int, StreamSlotView> slotsById)
    {
        var canvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var placements = new List<(FrameworkElement Child, LayoutSlotBounds Bounds)>();

        foreach (var slot in layout.Slots.OrderBy(slot => slot.SlotId))
        {
            FrameworkElement child = slotsById.TryGetValue(slot.SlotId, out var slotView)
                ? PrepareSlotView(slotView)
                : CreateMissingSlotPlaceholder(slot.SlotId);

            canvas.Children.Add(child);
            placements.Add((child, LayoutSlotBoundsCalculator.GetBounds(layout, slot)));
        }

        canvas.SizeChanged += (_, _) => ArrangeChildren(canvas, placements);
        canvas.Loaded += (_, _) => ArrangeChildren(canvas, placements);
        return canvas;
    }

    private static StreamSlotView PrepareSlotView(StreamSlotView slotView)
    {
        RemoveFromCurrentParent(slotView);
        return slotView;
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

    private static void ArrangeChildren(
        FrameworkElement surface,
        IReadOnlyList<(FrameworkElement Child, LayoutSlotBounds Bounds)> placements)
    {
        var width = Math.Max(0, surface.ActualWidth);
        var height = Math.Max(0, surface.ActualHeight);
        foreach (var (child, bounds) in placements)
        {
            Canvas.SetLeft(child, width * bounds.Left);
            Canvas.SetTop(child, height * bounds.Top);
            child.Width = Math.Max(1, width * bounds.Width);
            child.Height = Math.Max(1, height * bounds.Height);
        }
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
