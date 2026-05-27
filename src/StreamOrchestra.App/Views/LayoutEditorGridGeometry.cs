using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Views;

public readonly record struct LayoutEditorSplitterSegment(int Boundary, int Start, int Span);

public static class LayoutEditorGridGeometry
{
    public static IReadOnlyList<LayoutEditorSplitterSegment> CreateVerticalSplitterSegments(LayoutPreset layout)
    {
        if (layout.GridColumns <= 1 || layout.GridRows <= 0)
        {
            return [];
        }

        var cells = CreateCells(layout);
        var segments = new List<LayoutEditorSplitterSegment>();
        for (var boundary = 1; boundary < layout.GridColumns; boundary++)
        {
            var start = -1;
            for (var y = 0; y < layout.GridRows; y++)
            {
                var isActive = cells[y, boundary - 1] != cells[y, boundary];
                if (isActive && start < 0)
                {
                    start = y;
                }
                else if (!isActive && start >= 0)
                {
                    segments.Add(new LayoutEditorSplitterSegment(boundary, start, y - start));
                    start = -1;
                }
            }

            if (start >= 0)
            {
                segments.Add(new LayoutEditorSplitterSegment(boundary, start, layout.GridRows - start));
            }
        }

        return segments;
    }

    public static IReadOnlyList<LayoutEditorSplitterSegment> CreateHorizontalSplitterSegments(LayoutPreset layout)
    {
        if (layout.GridRows <= 1 || layout.GridColumns <= 0)
        {
            return [];
        }

        var cells = CreateCells(layout);
        var segments = new List<LayoutEditorSplitterSegment>();
        for (var boundary = 1; boundary < layout.GridRows; boundary++)
        {
            var start = -1;
            for (var x = 0; x < layout.GridColumns; x++)
            {
                var isActive = cells[boundary - 1, x] != cells[boundary, x];
                if (isActive && start < 0)
                {
                    start = x;
                }
                else if (!isActive && start >= 0)
                {
                    segments.Add(new LayoutEditorSplitterSegment(boundary, start, x - start));
                    start = -1;
                }
            }

            if (start >= 0)
            {
                segments.Add(new LayoutEditorSplitterSegment(boundary, start, layout.GridColumns - start));
            }
        }

        return segments;
    }

    private static int[,] CreateCells(LayoutPreset layout)
    {
        var rows = Math.Max(0, layout.GridRows);
        var columns = Math.Max(0, layout.GridColumns);
        var cells = new int[rows, columns];

        foreach (var slot in layout.Slots)
        {
            var startX = Math.Clamp(slot.X, 0, columns);
            var endX = Math.Clamp(slot.X + slot.W, 0, columns);
            var startY = Math.Clamp(slot.Y, 0, rows);
            var endY = Math.Clamp(slot.Y + slot.H, 0, rows);

            for (var y = startY; y < endY; y++)
            {
                for (var x = startX; x < endX; x++)
                {
                    cells[y, x] = slot.SlotId;
                }
            }
        }

        return cells;
    }
}
