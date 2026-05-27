using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamOrchestra.App.Models;

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
        var grid = new Grid
        {
            Width = Math.Max(120, layout.GridColumns * 72),
            Height = Math.Max(90, layout.GridRows * 54),
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 10))
        };

        for (var rowIndex = 0; rowIndex < layout.GridRows; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(GetWeight(layout.RowWeights, rowIndex), GridUnitType.Star)
            });
        }

        for (var columnIndex = 0; columnIndex < layout.GridColumns; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(GetWeight(layout.ColumnWeights, columnIndex), GridUnitType.Star)
            });
        }

        foreach (var slot in layout.Slots.OrderBy(slot => slot.SlotId))
        {
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

            Grid.SetColumn(border, slot.X);
            Grid.SetRow(border, slot.Y);
            Grid.SetColumnSpan(border, slot.W);
            Grid.SetRowSpan(border, slot.H);
            grid.Children.Add(border);
        }

        return new Viewbox
        {
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            Child = grid
        };
    }

    private static double GetWeight(IReadOnlyList<double>? weights, int index)
    {
        return weights is not null && index >= 0 && index < weights.Count && weights[index] > 0
            ? weights[index]
            : 1;
    }

    public static Brush GetSlotBrush(int slotId)
    {
        var converter = new BrushConverter();
        return (Brush)(converter.ConvertFromString(SlotColorValues[(slotId - 1) % SlotColorValues.Length])
                       ?? Brushes.SteelBlue);
    }
}
