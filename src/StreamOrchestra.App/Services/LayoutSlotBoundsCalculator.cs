using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class LayoutSlotBoundsCalculator
{
    public const double MinRelativeSize = 0.001;

    public static LayoutSlotBounds GetBounds(LayoutPreset layout, LayoutSlot slot)
    {
        if (HasExplicitBounds(slot))
        {
            return new LayoutSlotBounds(slot.Left, slot.Top, slot.Width, slot.Height);
        }

        var columnOffsets = CreateOffsets(layout.ColumnWeights, Math.Max(1, layout.GridColumns));
        var rowOffsets = CreateOffsets(layout.RowWeights, Math.Max(1, layout.GridRows));
        var x = Math.Clamp(slot.X, 0, columnOffsets.Length - 1);
        var y = Math.Clamp(slot.Y, 0, rowOffsets.Length - 1);
        var rightIndex = Math.Clamp(slot.X + Math.Max(1, slot.W), 0, columnOffsets.Length - 1);
        var bottomIndex = Math.Clamp(slot.Y + Math.Max(1, slot.H), 0, rowOffsets.Length - 1);
        var left = columnOffsets[x];
        var top = rowOffsets[y];
        return new LayoutSlotBounds(
            left,
            top,
            Math.Max(MinRelativeSize, columnOffsets[rightIndex] - left),
            Math.Max(MinRelativeSize, rowOffsets[bottomIndex] - top));
    }

    public static bool HasExplicitBounds(LayoutSlot slot)
    {
        return IsFinite(slot.Left)
               && IsFinite(slot.Top)
               && IsFinite(slot.Width)
               && IsFinite(slot.Height)
               && slot.Width > 0
               && slot.Height > 0;
    }

    public static bool IsValidExplicitBounds(LayoutSlot slot)
    {
        if (!HasExplicitBounds(slot))
        {
            return false;
        }

        return slot.Left >= 0
               && slot.Top >= 0
               && slot.Width >= MinRelativeSize
               && slot.Height >= MinRelativeSize
               && slot.Left + slot.Width <= 1 + MinRelativeSize
               && slot.Top + slot.Height <= 1 + MinRelativeSize;
    }

    private static double[] CreateOffsets(IReadOnlyList<double>? weights, int count)
    {
        var normalizedWeights = new double[count];
        var total = 0.0;
        for (var index = 0; index < count; index++)
        {
            var weight = weights is not null
                         && index < weights.Count
                         && IsFinite(weights[index])
                         && weights[index] > 0
                ? weights[index]
                : 1;
            normalizedWeights[index] = weight;
            total += weight;
        }

        if (total <= 0)
        {
            total = count;
            Array.Fill(normalizedWeights, 1);
        }

        var offsets = new double[count + 1];
        var accumulated = 0.0;
        for (var index = 0; index < count; index++)
        {
            offsets[index] = accumulated / total;
            accumulated += normalizedWeights[index];
        }

        offsets[count] = 1;
        return offsets;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
