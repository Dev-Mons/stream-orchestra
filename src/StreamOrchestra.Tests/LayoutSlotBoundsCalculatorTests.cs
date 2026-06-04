using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class LayoutSlotBoundsCalculatorTests
{
    [Fact]
    public void GetBounds_UsesExplicitNormalizedBoundsWhenPresent()
    {
        var layout = CreateLayout(
            columns: 2,
            rows: 2,
            new LayoutSlot
            {
                SlotId = 1,
                X = 0,
                Y = 0,
                W = 1,
                H = 1,
                Left = 0.13,
                Top = 0.17,
                Width = 0.31,
                Height = 0.37
            });

        var bounds = LayoutSlotBoundsCalculator.GetBounds(layout, layout.Slots.Single());

        Assert.Equal(new LayoutSlotBounds(0.13, 0.17, 0.31, 0.37), bounds);
    }

    [Fact]
    public void GetBounds_ConvertsLegacyGridCoordinatesUsingWeights()
    {
        var layout = CreateLayout(
            columns: 4,
            rows: 3,
            new LayoutSlot { SlotId = 9, X = 2, Y = 1, W = 2, H = 2 });
        layout = new LayoutPreset
        {
            Id = layout.Id,
            Name = layout.Name,
            GridColumns = layout.GridColumns,
            GridRows = layout.GridRows,
            ColumnWeights = [1, 1, 2, 2],
            RowWeights = [1, 2, 2],
            Slots = layout.Slots
        };

        var bounds = LayoutSlotBoundsCalculator.GetBounds(layout, layout.Slots.Single());

        Assert.Equal(1d / 3d, bounds.Left, 3);
        Assert.Equal(0.2d, bounds.Top, 3);
        Assert.Equal(2d / 3d, bounds.Width, 3);
        Assert.Equal(0.8d, bounds.Height, 3);
    }

    private static LayoutPreset CreateLayout(int columns, int rows, params LayoutSlot[] slots)
    {
        return new LayoutPreset
        {
            Id = "test",
            Name = "Test",
            GridColumns = columns,
            GridRows = rows,
            ColumnWeights = Enumerable.Repeat(1d, columns).ToArray(),
            RowWeights = Enumerable.Repeat(1d, rows).ToArray(),
            Slots = slots
        };
    }
}
