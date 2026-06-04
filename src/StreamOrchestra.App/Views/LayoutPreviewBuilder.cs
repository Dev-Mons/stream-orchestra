using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public static class LayoutPreviewBuilder
{
    private static readonly string[] SlotColorValues =
    [
        "#2F80ED",
        "#EB5757",
        "#27AE60",
        "#F2C94C",
        "#9B51E0",
        "#56CCF2",
        "#F2994A",
        "#6FCF97",
        "#BB6BD9",
        "#219653",
        "#2D9CDB",
        "#F24E1E",
        "#BDBDBD",
        "#00A896",
        "#FF6B6B",
        "#7B61FF"
    ];

    public static FrameworkElement Build(LayoutPreset layout, double width, double height, bool showSlotNumbers)
    {
        var surfaceWidth = Math.Max(120, layout.GridColumns * 72);
        var surfaceHeight = Math.Max(90, layout.GridRows * 54);
        var canvas = new Canvas
        {
            Width = surfaceWidth,
            Height = surfaceHeight,
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 10))
        };

        foreach (var slot in layout.Slots.OrderBy(slot => slot.SlotId))
        {
            var bounds = LayoutSlotBoundsCalculator.GetBounds(layout, slot);
            var border = new Border
            {
                Margin = new Thickness(3),
                Background = GetSlotBrush(slot.SlotId),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 243, 246, 250)),
                BorderThickness = new Thickness(1)
            };

            if (showSlotNumbers)
            {
                border.Child = new TextBlock
                {
                    Text = slot.SlotId.ToString(),
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            Canvas.SetLeft(border, surfaceWidth * bounds.Left);
            Canvas.SetTop(border, surfaceHeight * bounds.Top);
            border.Width = Math.Max(1, surfaceWidth * bounds.Width);
            border.Height = Math.Max(1, surfaceHeight * bounds.Height);
            canvas.Children.Add(border);
        }

        return new Viewbox
        {
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            Child = canvas
        };
    }

    public static Brush GetSlotBrush(int slotId)
    {
        var converter = new BrushConverter();
        return (Brush)(converter.ConvertFromString(SlotColorValues[(slotId - 1) % SlotColorValues.Length])
                       ?? Brushes.SteelBlue);
    }
}
