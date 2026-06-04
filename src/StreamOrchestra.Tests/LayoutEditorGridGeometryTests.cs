using StreamOrchestra.App.Models;
using StreamOrchestra.App.Views;

namespace StreamOrchestra.Tests;

public sealed class LayoutEditorGridGeometryTests
{
    [Fact]
    public void CreateVerticalSplitterSegments_SplitsContiguousBoundaryWhenSlotPairsDiffer()
    {
        var layout = CreateLayout(
            columns: 2,
            rows: 2,
            new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 },
            new LayoutSlot { SlotId = 2, X = 1, Y = 0, W = 1, H = 1 },
            new LayoutSlot { SlotId = 3, X = 0, Y = 1, W = 1, H = 1 },
            new LayoutSlot { SlotId = 4, X = 1, Y = 1, W = 1, H = 1 });

        var segments = LayoutEditorGridGeometry.CreateVerticalSplitterSegments(layout);

        Assert.Equal(
            [
                new LayoutEditorSplitterSegment(Boundary: 1, Start: 0, Span: 1),
                new LayoutEditorSplitterSegment(Boundary: 1, Start: 1, Span: 1)
            ],
            segments);
    }

    [Fact]
    public void CreateHorizontalSplitterSegments_SplitsContiguousBoundaryWhenSlotPairsDiffer()
    {
        var layout = CreateLayout(
            columns: 2,
            rows: 2,
            new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 },
            new LayoutSlot { SlotId = 2, X = 1, Y = 0, W = 1, H = 1 },
            new LayoutSlot { SlotId = 3, X = 0, Y = 1, W = 1, H = 1 },
            new LayoutSlot { SlotId = 4, X = 1, Y = 1, W = 1, H = 1 });

        var segments = LayoutEditorGridGeometry.CreateHorizontalSplitterSegments(layout);

        Assert.Equal(
            [
                new LayoutEditorSplitterSegment(Boundary: 1, Start: 0, Span: 1),
                new LayoutEditorSplitterSegment(Boundary: 1, Start: 1, Span: 1)
            ],
            segments);
    }

    [Fact]
    public void CreateVerticalSplitterSegments_KeepsSharedSpanningSlotEdgeTogether()
    {
        var layout = CreateLayout(
            columns: 2,
            rows: 2,
            new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 2 },
            new LayoutSlot { SlotId = 2, X = 1, Y = 0, W = 1, H = 1 },
            new LayoutSlot { SlotId = 3, X = 1, Y = 1, W = 1, H = 1 });

        var segments = LayoutEditorGridGeometry.CreateVerticalSplitterSegments(layout);

        Assert.Equal(
            [new LayoutEditorSplitterSegment(Boundary: 1, Start: 0, Span: 2)],
            segments);
    }

    [Fact]
    public void CreateVerticalSplitterSegments_LimitsLineToRowsWhereSlotsDiffer()
    {
        var layout = CreateLayout(
            columns: 2,
            rows: 2,
            new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 },
            new LayoutSlot { SlotId = 2, X = 1, Y = 0, W = 1, H = 1 },
            new LayoutSlot { SlotId = 3, X = 0, Y = 1, W = 2, H = 1 });

        var segments = LayoutEditorGridGeometry.CreateVerticalSplitterSegments(layout);

        Assert.Equal(
            [new LayoutEditorSplitterSegment(Boundary: 1, Start: 0, Span: 1)],
            segments);
    }

    [Fact]
    public void CreateHorizontalSplitterSegments_LimitsLineToColumnsWhereSlotsDiffer()
    {
        var layout = CreateLayout(
            columns: 2,
            rows: 2,
            new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 },
            new LayoutSlot { SlotId = 2, X = 0, Y = 1, W = 1, H = 1 },
            new LayoutSlot { SlotId = 3, X = 1, Y = 0, W = 1, H = 2 });

        var segments = LayoutEditorGridGeometry.CreateHorizontalSplitterSegments(layout);

        Assert.Equal(
            [new LayoutEditorSplitterSegment(Boundary: 1, Start: 0, Span: 1)],
            segments);
    }

    [Fact]
    public void CreateSplitterSegments_UpdatesLinesAfterMergedSlotsSpanOldBoundaries()
    {
        var layout = CreateLayout(
            columns: 2,
            rows: 2,
            new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 2, H = 1 },
            new LayoutSlot { SlotId = 2, X = 0, Y = 1, W = 1, H = 1 },
            new LayoutSlot { SlotId = 3, X = 1, Y = 1, W = 1, H = 1 });

        var verticalSegments = LayoutEditorGridGeometry.CreateVerticalSplitterSegments(layout);

        Assert.Equal(
            [new LayoutEditorSplitterSegment(Boundary: 1, Start: 1, Span: 1)],
            verticalSegments);
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
