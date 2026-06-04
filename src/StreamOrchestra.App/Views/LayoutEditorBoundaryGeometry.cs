using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public enum LayoutEditorBoundaryDirection
{
    Vertical,
    Horizontal
}

public enum LayoutEditorLineAlignment
{
    Horizontal,
    Vertical
}

public readonly record struct LayoutEditorSlotBounds(
    int SlotId,
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public readonly record struct LayoutEditorBoundarySegment(
    LayoutEditorBoundaryDirection Direction,
    double Coordinate,
    double Start,
    double End,
    int NegativeSideSlotId,
    int PositiveSideSlotId);

public sealed record LayoutEditorBoundaryGroup(
    LayoutEditorBoundaryDirection Direction,
    double Coordinate,
    double Start,
    double End,
    IReadOnlyList<LayoutEditorBoundarySegment> Segments);

public static class LayoutEditorBoundaryGeometry
{
    public const double GeometryTolerance = 0.000001;
    public const double VisualAlignmentTolerance = 0.003;

    public static bool TryAlignSlotLine(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        int selectedSlotId,
        LayoutEditorLineAlignment alignment,
        out IReadOnlyList<LayoutEditorSlotBounds> nextSlots)
    {
        nextSlots = slots.ToArray();
        var selectedSlotIndex = slots.ToList().FindIndex(slot => slot.SlotId == selectedSlotId);
        if (selectedSlotIndex < 0)
        {
            return false;
        }

        var selectedSlot = slots[selectedSlotIndex];
        var lineSlots = alignment == LayoutEditorLineAlignment.Horizontal
            ? FindConnectedHorizontalLine(slots, selectedSlot)
            : FindConnectedVerticalLine(slots, selectedSlot);
        if (lineSlots.Count <= 1)
        {
            return false;
        }

        var nextById = slots.ToDictionary(slot => slot.SlotId);
        if (alignment == LayoutEditorLineAlignment.Horizontal)
        {
            var orderedLineSlots = lineSlots
                .OrderBy(slot => slot.Left)
                .ThenBy(slot => slot.SlotId)
                .ToArray();
            var left = orderedLineSlots.Min(slot => slot.Left);
            var right = orderedLineSlots.Max(slot => slot.Right);
            var width = (right - left) / orderedLineSlots.Length;
            for (var index = 0; index < orderedLineSlots.Length; index++)
            {
                var slot = orderedLineSlots[index];
                nextById[slot.SlotId] = slot with
                {
                    Left = left + index * width,
                    Width = width
                };
            }
        }
        else
        {
            var orderedLineSlots = lineSlots
                .OrderBy(slot => slot.Top)
                .ThenBy(slot => slot.SlotId)
                .ToArray();
            var top = orderedLineSlots.Min(slot => slot.Top);
            var bottom = orderedLineSlots.Max(slot => slot.Bottom);
            var height = (bottom - top) / orderedLineSlots.Length;
            for (var index = 0; index < orderedLineSlots.Length; index++)
            {
                var slot = orderedLineSlots[index];
                nextById[slot.SlotId] = slot with
                {
                    Top = top + index * height,
                    Height = height
                };
            }
        }

        var candidateSlots = slots
            .Select(slot => nextById[slot.SlotId])
            .ToArray();
        if (!ValidateSlots(candidateSlots))
        {
            return false;
        }

        nextSlots = candidateSlots;
        return true;
    }

    public static bool TryResetSlotBounds(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        out IReadOnlyList<LayoutEditorSlotBounds> nextSlots)
    {
        nextSlots = slots.ToArray();
        if (slots.Count == 0 ||
            !TryCreateSlotGrid(slots, out var rects, out var columnCount, out var rowCount))
        {
            return false;
        }

        var columnOffsets = CreateOffsets(CreateBalancedTrackWeights(rects, columnCount, isHorizontal: true));
        var rowOffsets = CreateOffsets(CreateBalancedTrackWeights(rects, rowCount, isHorizontal: false));
        var resetSlots = rects
            .Select(rect => new LayoutEditorSlotBounds(
                rect.SlotId,
                columnOffsets[rect.Left],
                rowOffsets[rect.Top],
                columnOffsets[rect.Right] - columnOffsets[rect.Left],
                rowOffsets[rect.Bottom] - rowOffsets[rect.Top]))
            .OrderBy(slot => slot.SlotId)
            .ToArray();
        if (!ValidateSlots(resetSlots))
        {
            return false;
        }

        nextSlots = resetSlots;
        return true;
    }

    public static IReadOnlyList<LayoutEditorBoundarySegment> CreateSharedBoundarySegments(
        IReadOnlyList<LayoutEditorSlotBounds> slots)
    {
        var segments = new List<LayoutEditorBoundarySegment>();
        for (var firstIndex = 0; firstIndex < slots.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < slots.Count; secondIndex++)
            {
                var first = slots[firstIndex];
                var second = slots[secondIndex];
                if (TryCreateVerticalSegment(first, second, out var verticalSegment))
                {
                    segments.Add(verticalSegment);
                }

                if (TryCreateHorizontalSegment(first, second, out var horizontalSegment))
                {
                    segments.Add(horizontalSegment);
                }
            }
        }

        return segments
            .OrderBy(segment => segment.Direction)
            .ThenBy(segment => segment.Coordinate)
            .ThenBy(segment => segment.Start)
            .ThenBy(segment => segment.End)
            .ThenBy(segment => segment.NegativeSideSlotId)
            .ThenBy(segment => segment.PositiveSideSlotId)
            .ToArray();
    }

    public static IReadOnlyList<LayoutEditorBoundaryGroup> CreateBoundaryGroups(
        IReadOnlyList<LayoutEditorSlotBounds> slots)
    {
        var segments = CreateSharedBoundarySegments(slots);
        var groups = new List<LayoutEditorBoundaryGroup>();
        foreach (var direction in new[]
                 {
                     LayoutEditorBoundaryDirection.Vertical,
                     LayoutEditorBoundaryDirection.Horizontal
                 })
        {
            var remainingSegments = segments
                .Where(segment => segment.Direction == direction)
                .OrderBy(segment => segment.Coordinate)
                .ThenBy(segment => segment.Start)
                .ThenBy(segment => segment.End)
                .ToList();
            while (remainingSegments.Count > 0)
            {
                var seed = remainingSegments[0];
                var alignedSegments = remainingSegments
                    .Where(segment => AreVisuallyAligned(segment.Coordinate, seed.Coordinate))
                    .OrderBy(segment => segment.Start)
                    .ThenBy(segment => segment.End)
                    .ToArray();
                remainingSegments.RemoveAll(segment => AreVisuallyAligned(segment.Coordinate, seed.Coordinate));

                var currentSegments = new List<LayoutEditorBoundarySegment>();
                var currentEnd = 0.0;
                foreach (var segment in alignedSegments)
                {
                    if (currentSegments.Count == 0)
                    {
                        currentEnd = segment.End;
                        currentSegments.Add(segment);
                    }
                    else if (segment.Start > currentEnd + VisualAlignmentTolerance)
                    {
                        groups.Add(CreateGroup(direction, currentSegments));
                        currentSegments = [segment];
                        currentEnd = segment.End;
                    }
                    else
                    {
                        currentEnd = Math.Max(currentEnd, segment.End);
                        currentSegments.Add(segment);
                    }
                }

                if (currentSegments.Count > 0)
                {
                    groups.Add(CreateGroup(direction, currentSegments));
                }
            }
        }

        return groups
            .OrderBy(group => group.Direction)
            .ThenBy(group => group.Coordinate)
            .ThenBy(group => group.Start)
            .ToArray();
    }

    public static IReadOnlyList<LayoutEditorBoundarySegment> CreateIndividualHandles(
        IReadOnlyList<LayoutEditorSlotBounds> slots)
    {
        var slotsById = slots.ToDictionary(slot => slot.SlotId);
        return CreateSharedBoundarySegments(slots)
            .Where(segment => IsExactSharedEdge(segment, slotsById))
            .ToArray();
    }

    public static double FindSnapCoordinate(
        IReadOnlyList<LayoutEditorBoundarySegment> candidates,
        IReadOnlyList<LayoutEditorBoundarySegment> activeSegments,
        double targetCoordinate,
        double snapThreshold)
    {
        if (activeSegments.Count == 0 || snapThreshold <= 0)
        {
            return targetCoordinate;
        }

        var direction = activeSegments[0].Direction;
        var bestCoordinate = targetCoordinate;
        var bestDistance = snapThreshold + GeometryTolerance;
        foreach (var candidate in candidates)
        {
            if (!IsEligibleSnapCandidate(candidate, activeSegments, direction))
            {
                continue;
            }

            var distance = Math.Abs(candidate.Coordinate - targetCoordinate);
            if (distance <= snapThreshold + GeometryTolerance && distance < bestDistance)
            {
                bestDistance = distance;
                bestCoordinate = candidate.Coordinate;
            }
        }

        return bestCoordinate;
    }

    public static bool TryMoveBoundaryGroup(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        LayoutEditorBoundaryGroup group,
        double targetCoordinate,
        double snapThreshold,
        out IReadOnlyList<LayoutEditorSlotBounds> nextSlots)
    {
        return TryMoveSegments(slots, group.Segments, targetCoordinate, snapThreshold, out nextSlots);
    }

    public static bool TryMoveIndividualHandle(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        LayoutEditorBoundarySegment handle,
        double targetCoordinate,
        double snapThreshold,
        out IReadOnlyList<LayoutEditorSlotBounds> nextSlots)
    {
        return TryMoveSegments(slots, [handle], targetCoordinate, snapThreshold, out nextSlots);
    }

    private static bool TryMoveSegments(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        IReadOnlyList<LayoutEditorBoundarySegment> activeSegments,
        double targetCoordinate,
        double snapThreshold,
        out IReadOnlyList<LayoutEditorSlotBounds> nextSlots)
    {
        nextSlots = slots.ToArray();
        if (activeSegments.Count == 0 ||
            activeSegments.Any(segment => segment.Direction != activeSegments[0].Direction))
        {
            return false;
        }

        var coordinate = FindValidSnapCoordinate(slots, activeSegments, targetCoordinate, snapThreshold);
        if (!TryApplySegments(slots, activeSegments, coordinate, out var candidateSlots))
        {
            return false;
        }

        nextSlots = candidateSlots;
        return true;
    }

    private static double FindValidSnapCoordinate(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        IReadOnlyList<LayoutEditorBoundarySegment> activeSegments,
        double targetCoordinate,
        double snapThreshold)
    {
        if (snapThreshold <= 0)
        {
            return targetCoordinate;
        }

        var direction = activeSegments[0].Direction;
        var bestCoordinate = targetCoordinate;
        var bestDistance = snapThreshold + GeometryTolerance;
        foreach (var candidate in CreateSharedBoundarySegments(slots))
        {
            if (!IsEligibleSnapCandidate(candidate, activeSegments, direction))
            {
                continue;
            }

            var distance = Math.Abs(candidate.Coordinate - targetCoordinate);
            if (distance > snapThreshold + GeometryTolerance || distance >= bestDistance)
            {
                continue;
            }

            if (!TryApplySegments(slots, activeSegments, candidate.Coordinate, out _))
            {
                continue;
            }

            bestDistance = distance;
            bestCoordinate = candidate.Coordinate;
        }

        return bestCoordinate;
    }

    private static bool TryApplySegments(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        IReadOnlyList<LayoutEditorBoundarySegment> activeSegments,
        double coordinate,
        out IReadOnlyList<LayoutEditorSlotBounds> nextSlots)
    {
        nextSlots = slots.ToArray();
        var nextById = slots.ToDictionary(slot => slot.SlotId);
        foreach (var segment in activeSegments)
        {
            if (!nextById.TryGetValue(segment.NegativeSideSlotId, out var negativeSlot) ||
                !nextById.TryGetValue(segment.PositiveSideSlotId, out var positiveSlot))
            {
                return false;
            }

            if (segment.Direction == LayoutEditorBoundaryDirection.Vertical)
            {
                nextById[segment.NegativeSideSlotId] = negativeSlot with
                {
                    Width = coordinate - negativeSlot.Left
                };
                nextById[segment.PositiveSideSlotId] = positiveSlot with
                {
                    Left = coordinate,
                    Width = positiveSlot.Right - coordinate
                };
            }
            else
            {
                nextById[segment.NegativeSideSlotId] = negativeSlot with
                {
                    Height = coordinate - negativeSlot.Top
                };
                nextById[segment.PositiveSideSlotId] = positiveSlot with
                {
                    Top = coordinate,
                    Height = positiveSlot.Bottom - coordinate
                };
            }
        }

        var candidateSlots = slots
            .Select(slot => nextById[slot.SlotId])
            .ToArray();
        if (!ValidateSlots(candidateSlots) ||
            !ActiveBoundariesRemainClosed(candidateSlots, activeSegments, coordinate))
        {
            return false;
        }

        nextSlots = candidateSlots;
        return true;
    }

    private static bool ValidateSlots(IReadOnlyList<LayoutEditorSlotBounds> slots)
    {
        var minSize = LayoutSlotBoundsCalculator.MinRelativeSize;
        foreach (var slot in slots)
        {
            if (!IsFinite(slot.Left) ||
                !IsFinite(slot.Top) ||
                !IsFinite(slot.Width) ||
                !IsFinite(slot.Height) ||
                slot.Left < -GeometryTolerance ||
                slot.Top < -GeometryTolerance ||
                slot.Right > 1 + GeometryTolerance ||
                slot.Bottom > 1 + GeometryTolerance ||
                slot.Width < minSize - GeometryTolerance ||
                slot.Height < minSize - GeometryTolerance)
            {
                return false;
            }
        }

        for (var firstIndex = 0; firstIndex < slots.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < slots.Count; secondIndex++)
            {
                if (Overlaps(slots[firstIndex], slots[secondIndex]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ActiveBoundariesRemainClosed(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        IReadOnlyList<LayoutEditorBoundarySegment> activeSegments,
        double coordinate)
    {
        var slotsById = slots.ToDictionary(slot => slot.SlotId);
        foreach (var segment in activeSegments)
        {
            if (!slotsById.TryGetValue(segment.NegativeSideSlotId, out var negativeSlot) ||
                !slotsById.TryGetValue(segment.PositiveSideSlotId, out var positiveSlot))
            {
                return false;
            }

            if (segment.Direction == LayoutEditorBoundaryDirection.Vertical)
            {
                if (!AreClose(negativeSlot.Right, coordinate) ||
                    !AreClose(positiveSlot.Left, coordinate) ||
                    negativeSlot.Top > segment.Start + GeometryTolerance ||
                    positiveSlot.Top > segment.Start + GeometryTolerance ||
                    negativeSlot.Bottom < segment.End - GeometryTolerance ||
                    positiveSlot.Bottom < segment.End - GeometryTolerance)
                {
                    return false;
                }
            }
            else if (!AreClose(negativeSlot.Bottom, coordinate) ||
                     !AreClose(positiveSlot.Top, coordinate) ||
                     negativeSlot.Left > segment.Start + GeometryTolerance ||
                     positiveSlot.Left > segment.Start + GeometryTolerance ||
                     negativeSlot.Right < segment.End - GeometryTolerance ||
                     positiveSlot.Right < segment.End - GeometryTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<LayoutEditorSlotBounds> FindConnectedHorizontalLine(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        LayoutEditorSlotBounds selectedSlot)
    {
        var candidates = slots
            .Where(slot =>
                AreVisuallyAligned(slot.Top, selectedSlot.Top) &&
                AreVisuallyAligned(slot.Bottom, selectedSlot.Bottom))
            .OrderBy(slot => slot.Left)
            .ThenBy(slot => slot.SlotId)
            .ToArray();
        var selectedIndex = Array.FindIndex(candidates, slot => slot.SlotId == selectedSlot.SlotId);
        if (selectedIndex < 0)
        {
            return [];
        }

        var startIndex = selectedIndex;
        while (startIndex > 0 &&
               candidates[startIndex - 1].Right >= candidates[startIndex].Left - VisualAlignmentTolerance)
        {
            startIndex--;
        }

        var endIndex = selectedIndex;
        while (endIndex < candidates.Length - 1 &&
               candidates[endIndex].Right >= candidates[endIndex + 1].Left - VisualAlignmentTolerance)
        {
            endIndex++;
        }

        return candidates[startIndex..(endIndex + 1)];
    }

    private static IReadOnlyList<LayoutEditorSlotBounds> FindConnectedVerticalLine(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        LayoutEditorSlotBounds selectedSlot)
    {
        var candidates = slots
            .Where(slot =>
                AreVisuallyAligned(slot.Left, selectedSlot.Left) &&
                AreVisuallyAligned(slot.Right, selectedSlot.Right))
            .OrderBy(slot => slot.Top)
            .ThenBy(slot => slot.SlotId)
            .ToArray();
        var selectedIndex = Array.FindIndex(candidates, slot => slot.SlotId == selectedSlot.SlotId);
        if (selectedIndex < 0)
        {
            return [];
        }

        var startIndex = selectedIndex;
        while (startIndex > 0 &&
               candidates[startIndex - 1].Bottom >= candidates[startIndex].Top - VisualAlignmentTolerance)
        {
            startIndex--;
        }

        var endIndex = selectedIndex;
        while (endIndex < candidates.Length - 1 &&
               candidates[endIndex].Bottom >= candidates[endIndex + 1].Top - VisualAlignmentTolerance)
        {
            endIndex++;
        }

        return candidates[startIndex..(endIndex + 1)];
    }

    private static bool TryCreateSlotGrid(
        IReadOnlyList<LayoutEditorSlotBounds> slots,
        out IReadOnlyList<SlotGridRect> rects,
        out int columnCount,
        out int rowCount)
    {
        rects = [];
        var xBoundaries = CreateMergedBoundaries(slots.SelectMany(slot => new[] { slot.Left, slot.Right }));
        var yBoundaries = CreateMergedBoundaries(slots.SelectMany(slot => new[] { slot.Top, slot.Bottom }));
        columnCount = xBoundaries.Length - 1;
        rowCount = yBoundaries.Length - 1;
        if (columnCount <= 0 ||
            rowCount <= 0 ||
            !AreClose(xBoundaries[0], 0) ||
            !AreClose(xBoundaries[^1], 1) ||
            !AreClose(yBoundaries[0], 0) ||
            !AreClose(yBoundaries[^1], 1))
        {
            return false;
        }

        var gridRects = new List<SlotGridRect>();
        var cells = new int[rowCount, columnCount];
        foreach (var slot in slots)
        {
            if (!TryGetBoundaryIndex(xBoundaries, slot.Left, out var left) ||
                !TryGetBoundaryIndex(xBoundaries, slot.Right, out var right) ||
                !TryGetBoundaryIndex(yBoundaries, slot.Top, out var top) ||
                !TryGetBoundaryIndex(yBoundaries, slot.Bottom, out var bottom) ||
                right <= left ||
                bottom <= top)
            {
                return false;
            }

            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    if (cells[y, x] != 0)
                    {
                        return false;
                    }

                    cells[y, x] = slot.SlotId;
                }
            }

            gridRects.Add(new SlotGridRect(slot.SlotId, left, top, right, bottom));
        }

        for (var y = 0; y < rowCount; y++)
        {
            for (var x = 0; x < columnCount; x++)
            {
                if (cells[y, x] == 0)
                {
                    return false;
                }
            }
        }

        rects = gridRects;
        return true;
    }

    private static double[] CreateMergedBoundaries(IEnumerable<double> coordinates)
    {
        var sortedCoordinates = coordinates
            .Where(IsFinite)
            .Concat([0, 1])
            .Order()
            .ToArray();
        var boundaries = new List<double>();
        var group = new List<double>();
        foreach (var coordinate in sortedCoordinates)
        {
            if (group.Count == 0 || coordinate - group[^1] <= VisualAlignmentTolerance)
            {
                group.Add(coordinate);
                continue;
            }

            boundaries.Add(NormalizeBoundary(group.Average()));
            group = [coordinate];
        }

        if (group.Count > 0)
        {
            boundaries.Add(NormalizeBoundary(group.Average()));
        }

        return boundaries
            .Distinct()
            .Order()
            .ToArray();
    }

    private static double NormalizeBoundary(double coordinate)
    {
        if (Math.Abs(coordinate) <= VisualAlignmentTolerance)
        {
            return 0;
        }

        if (Math.Abs(1 - coordinate) <= VisualAlignmentTolerance)
        {
            return 1;
        }

        return Math.Clamp(coordinate, 0, 1);
    }

    private static bool TryGetBoundaryIndex(IReadOnlyList<double> boundaries, double coordinate, out int index)
    {
        index = -1;
        var bestDistance = VisualAlignmentTolerance + GeometryTolerance;
        for (var candidateIndex = 0; candidateIndex < boundaries.Count; candidateIndex++)
        {
            var distance = Math.Abs(boundaries[candidateIndex] - coordinate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                index = candidateIndex;
            }
        }

        return index >= 0;
    }

    private static double[] CreateBalancedTrackWeights(
        IReadOnlyList<SlotGridRect> rects,
        int count,
        bool isHorizontal)
    {
        var weights = new double[count];
        for (var index = 0; index < count; index++)
        {
            var maxSpan = 1;
            foreach (var rect in rects)
            {
                var start = isHorizontal ? rect.Left : rect.Top;
                var end = isHorizontal ? rect.Right : rect.Bottom;
                if (start <= index && index < end)
                {
                    maxSpan = Math.Max(maxSpan, end - start);
                }
            }

            weights[index] = 1d / maxSpan;
        }

        return weights;
    }

    private static double[] CreateOffsets(IReadOnlyList<double> weights)
    {
        var total = weights.Sum(weight => weight > 0 && IsFinite(weight) ? weight : 0);
        if (total <= 0)
        {
            total = Math.Max(1, weights.Count);
        }

        var offsets = new double[weights.Count + 1];
        var accumulated = 0.0;
        for (var index = 0; index < weights.Count; index++)
        {
            offsets[index] = accumulated / total;
            accumulated += weights[index] > 0 && IsFinite(weights[index]) ? weights[index] : 1;
        }

        offsets[^1] = 1;
        return offsets;
    }

    private static LayoutEditorBoundaryGroup CreateGroup(
        LayoutEditorBoundaryDirection direction,
        IReadOnlyList<LayoutEditorBoundarySegment> segments)
    {
        return new LayoutEditorBoundaryGroup(
            direction,
            segments.Average(segment => segment.Coordinate),
            segments.Min(segment => segment.Start),
            segments.Max(segment => segment.End),
            segments.ToArray());
    }

    private static bool TryCreateVerticalSegment(
        LayoutEditorSlotBounds first,
        LayoutEditorSlotBounds second,
        out LayoutEditorBoundarySegment segment)
    {
        if (AreVisuallyAligned(first.Right, second.Left))
        {
            return TryCreateVerticalSegment(first, second, (first.Right + second.Left) / 2, out segment);
        }

        if (AreVisuallyAligned(second.Right, first.Left))
        {
            return TryCreateVerticalSegment(second, first, (second.Right + first.Left) / 2, out segment);
        }

        segment = default;
        return false;
    }

    private static bool TryCreateVerticalSegment(
        LayoutEditorSlotBounds leftSlot,
        LayoutEditorSlotBounds rightSlot,
        double coordinate,
        out LayoutEditorBoundarySegment segment)
    {
        var start = Math.Max(leftSlot.Top, rightSlot.Top);
        var end = Math.Min(leftSlot.Bottom, rightSlot.Bottom);
        if (end - start <= GeometryTolerance)
        {
            segment = default;
            return false;
        }

        segment = new LayoutEditorBoundarySegment(
            LayoutEditorBoundaryDirection.Vertical,
            coordinate,
            start,
            end,
            leftSlot.SlotId,
            rightSlot.SlotId);
        return true;
    }

    private static bool TryCreateHorizontalSegment(
        LayoutEditorSlotBounds first,
        LayoutEditorSlotBounds second,
        out LayoutEditorBoundarySegment segment)
    {
        if (AreVisuallyAligned(first.Bottom, second.Top))
        {
            return TryCreateHorizontalSegment(first, second, (first.Bottom + second.Top) / 2, out segment);
        }

        if (AreVisuallyAligned(second.Bottom, first.Top))
        {
            return TryCreateHorizontalSegment(second, first, (second.Bottom + first.Top) / 2, out segment);
        }

        segment = default;
        return false;
    }

    private static bool TryCreateHorizontalSegment(
        LayoutEditorSlotBounds topSlot,
        LayoutEditorSlotBounds bottomSlot,
        double coordinate,
        out LayoutEditorBoundarySegment segment)
    {
        var start = Math.Max(topSlot.Left, bottomSlot.Left);
        var end = Math.Min(topSlot.Right, bottomSlot.Right);
        if (end - start <= GeometryTolerance)
        {
            segment = default;
            return false;
        }

        segment = new LayoutEditorBoundarySegment(
            LayoutEditorBoundaryDirection.Horizontal,
            coordinate,
            start,
            end,
            topSlot.SlotId,
            bottomSlot.SlotId);
        return true;
    }

    private static bool IsExactSharedEdge(
        LayoutEditorBoundarySegment segment,
        IReadOnlyDictionary<int, LayoutEditorSlotBounds> slotsById)
    {
        if (!slotsById.TryGetValue(segment.NegativeSideSlotId, out var negativeSlot) ||
            !slotsById.TryGetValue(segment.PositiveSideSlotId, out var positiveSlot))
        {
            return false;
        }

        return segment.Direction == LayoutEditorBoundaryDirection.Vertical
            ? AreClose(negativeSlot.Right, positiveSlot.Left) &&
              AreClose(negativeSlot.Top, positiveSlot.Top) &&
              AreClose(negativeSlot.Bottom, positiveSlot.Bottom)
            : AreClose(negativeSlot.Bottom, positiveSlot.Top) &&
              AreClose(negativeSlot.Left, positiveSlot.Left) &&
              AreClose(negativeSlot.Right, positiveSlot.Right);
    }

    private static bool IsEligibleSnapCandidate(
        LayoutEditorBoundarySegment candidate,
        IReadOnlyList<LayoutEditorBoundarySegment> activeSegments,
        LayoutEditorBoundaryDirection direction)
    {
        return candidate.Direction == direction &&
               !activeSegments.Any(active => IsSameSegment(active, candidate)) &&
               activeSegments.Any(active => SpansOverlap(active.Start, active.End, candidate.Start, candidate.End));
    }

    private static bool IsSameSegment(LayoutEditorBoundarySegment first, LayoutEditorBoundarySegment second)
    {
        return first.Direction == second.Direction &&
               first.NegativeSideSlotId == second.NegativeSideSlotId &&
               first.PositiveSideSlotId == second.PositiveSideSlotId;
    }

    private static bool SpansOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd)
    {
        return Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart) >= -VisualAlignmentTolerance;
    }

    private static bool Overlaps(LayoutEditorSlotBounds first, LayoutEditorSlotBounds second)
    {
        return OverlapLength(first.Left, first.Right, second.Left, second.Right) > GeometryTolerance &&
               OverlapLength(first.Top, first.Bottom, second.Top, second.Bottom) > GeometryTolerance;
    }

    private static double OverlapLength(double firstStart, double firstEnd, double secondStart, double secondEnd)
    {
        return Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart);
    }

    private static bool AreClose(double first, double second)
    {
        return Math.Abs(first - second) <= GeometryTolerance;
    }

    private static bool AreVisuallyAligned(double first, double second)
    {
        return Math.Abs(first - second) <= VisualAlignmentTolerance;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private readonly record struct SlotGridRect(
        int SlotId,
        int Left,
        int Top,
        int Right,
        int Bottom);
}
