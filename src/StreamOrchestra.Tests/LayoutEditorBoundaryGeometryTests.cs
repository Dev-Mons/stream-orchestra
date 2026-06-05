using StreamOrchestra.App.Views;

namespace StreamOrchestra.Tests;

public sealed class LayoutEditorBoundaryGeometryTests
{
    [Fact]
    public void TryCreateMergedSlot_RejectsDiagonalSelection()
    {
        var slots = CreateTwoByTwoSlots();

        var merged = LayoutEditorBoundaryGeometry.TryCreateMergedSlot(
            slots,
            new HashSet<int> { 1, 4 },
            out _);

        Assert.False(merged);
    }

    [Fact]
    public void TryCreateMergedSlot_AllowsAdjacentSlotsThatFillRectangle()
    {
        var slots = CreateTwoByTwoSlots();

        var merged = LayoutEditorBoundaryGeometry.TryCreateMergedSlot(
            slots,
            new HashSet<int> { 1, 2 },
            out var mergedSlot);

        Assert.True(merged);
        Assert.Equal(1, mergedSlot.SlotId);
        Assert.Equal(0, mergedSlot.Left, 6);
        Assert.Equal(0, mergedSlot.Top, 6);
        Assert.Equal(1, mergedSlot.Width, 6);
        Assert.Equal(0.5, mergedSlot.Height, 6);
    }

    [Fact]
    public void TryCreateMergedSlot_RejectsLShapeSelection()
    {
        var slots = CreateTwoByTwoSlots();

        var merged = LayoutEditorBoundaryGeometry.TryCreateMergedSlot(
            slots,
            new HashSet<int> { 1, 2, 3 },
            out _);

        Assert.False(merged);
    }

    [Fact]
    public void TryCreateMergedSlot_RejectsAdjacentSlotsWithDifferentHeights()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.5, 0.6),
            Slot(2, 0.5, 0, 0.5, 0.4),
            Slot(3, 0.5, 0.4, 0.5, 0.6),
            Slot(4, 0, 0.6, 0.5, 0.4)
        };

        var merged = LayoutEditorBoundaryGeometry.TryCreateMergedSlot(
            slots,
            new HashSet<int> { 1, 2 },
            out _);

        Assert.False(merged);
    }

    [Fact]
    public void TryCreateMergedSlot_RejectsAdjacentSlotsWithDifferentWidths()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.6, 0.5),
            Slot(2, 0.6, 0, 0.4, 0.5),
            Slot(3, 0, 0.5, 0.4, 0.5),
            Slot(4, 0.4, 0.5, 0.6, 0.5)
        };

        var merged = LayoutEditorBoundaryGeometry.TryCreateMergedSlot(
            slots,
            new HashSet<int> { 1, 3 },
            out _);

        Assert.False(merged);
    }

    [Fact]
    public void CreateSharedBoundarySegments_ExtractsVerticalAndHorizontalBoundaries()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.5, 0.5),
            Slot(2, 0.5, 0, 0.5, 0.5),
            Slot(3, 0, 0.5, 0.5, 0.5)
        };

        var segments = LayoutEditorBoundaryGeometry.CreateSharedBoundarySegments(slots);

        var vertical = Assert.Single(segments.Where(segment =>
            segment.Direction == LayoutEditorBoundaryDirection.Vertical));
        Assert.Equal(0.5, vertical.Coordinate, 6);
        Assert.Equal(0, vertical.Start, 6);
        Assert.Equal(0.5, vertical.End, 6);
        Assert.Equal(1, vertical.NegativeSideSlotId);
        Assert.Equal(2, vertical.PositiveSideSlotId);

        var horizontal = Assert.Single(segments.Where(segment =>
            segment.Direction == LayoutEditorBoundaryDirection.Horizontal));
        Assert.Equal(0.5, horizontal.Coordinate, 6);
        Assert.Equal(0, horizontal.Start, 6);
        Assert.Equal(0.5, horizontal.End, 6);
        Assert.Equal(1, horizontal.NegativeSideSlotId);
        Assert.Equal(3, horizontal.PositiveSideSlotId);
    }

    [Fact]
    public void CreateBoundaryGroups_GroupsAlignedContinuousBoundaries()
    {
        var slots = CreateTwoRowThreeColumnSlots();

        var groups = LayoutEditorBoundaryGeometry.CreateBoundaryGroups(slots);

        var firstColumnBoundary = Assert.Single(groups.Where(group =>
            group.Direction == LayoutEditorBoundaryDirection.Vertical &&
            NearlyEqual(group.Coordinate, 1d / 3d)));
        Assert.Equal(0, firstColumnBoundary.Start, 6);
        Assert.Equal(1, firstColumnBoundary.End, 6);
        Assert.Equal(2, firstColumnBoundary.Segments.Count);
    }

    [Fact]
    public void CreateBoundaryGroups_GroupsVisuallyAlignedBoundariesWithTinyCoordinateDriftAndGap()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.3330, 0.5000),
            Slot(2, 0.3338, 0, 0.3320, 0.4993),
            Slot(3, 0.6660, 0, 0.3340, 0.5007),
            Slot(4, 0, 0.5000, 0.3330, 0.5000),
            Slot(5, 0.3338, 0.4993, 0.3320, 0.5007),
            Slot(6, 0.6660, 0.5007, 0.3340, 0.4993)
        };

        var groups = LayoutEditorBoundaryGeometry.CreateBoundaryGroups(slots);

        var horizontalGroup = Assert.Single(groups.Where(group =>
            group.Direction == LayoutEditorBoundaryDirection.Horizontal &&
            Math.Abs(group.Coordinate - 0.5) < 0.003));
        Assert.Equal(0, horizontalGroup.Start, 6);
        Assert.Equal(1, horizontalGroup.End, 6);
        Assert.Equal(3, horizontalGroup.Segments.Count);
    }

    [Fact]
    public void CreateIndividualHandles_ExposesOnlyExactSharedEdges()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.5, 0.5),
            Slot(2, 0.5, 0, 0.5, 0.5),
            Slot(3, 0, 0.5, 0.5, 0.25),
            Slot(4, 0.5, 0.5, 0.5, 0.5)
        };

        var handles = LayoutEditorBoundaryGeometry.CreateIndividualHandles(slots);

        var handle = Assert.Single(handles.Where(segment =>
            segment.Direction == LayoutEditorBoundaryDirection.Vertical));
        Assert.Equal(LayoutEditorBoundaryDirection.Vertical, handle.Direction);
        Assert.Equal(1, handle.NegativeSideSlotId);
        Assert.Equal(2, handle.PositiveSideSlotId);
        Assert.DoesNotContain(handles, segment =>
            segment.Direction == LayoutEditorBoundaryDirection.Vertical &&
            segment.NegativeSideSlotId == 3 &&
            segment.PositiveSideSlotId == 4);
    }

    [Fact]
    public void CreateIndividualHandles_RejectsPartiallyOverlappingSharedEdges()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.5, 0.5),
            Slot(2, 0.5, 0.25, 0.5, 0.5)
        };

        var handles = LayoutEditorBoundaryGeometry.CreateIndividualHandles(slots);

        Assert.Empty(handles);
    }

    [Fact]
    public void TryMoveBoundaryGroup_MovesAlignedDefaultGroupWithoutOpeningGaps()
    {
        var slots = CreateTwoRowThreeColumnSlots();
        var group = LayoutEditorBoundaryGeometry
            .CreateBoundaryGroups(slots)
            .Single(item => item.Direction == LayoutEditorBoundaryDirection.Vertical &&
                            NearlyEqual(item.Coordinate, 1d / 3d));

        var moved = LayoutEditorBoundaryGeometry.TryMoveBoundaryGroup(
            slots,
            group,
            0.4,
            snapThreshold: 0,
            out var nextSlots);

        Assert.True(moved);
        Assert.Equal(0.4, Find(nextSlots, 1).Right, 6);
        Assert.Equal(0.4, Find(nextSlots, 2).Left, 6);
        Assert.Equal(0.4, Find(nextSlots, 4).Right, 6);
        Assert.Equal(0.4, Find(nextSlots, 5).Left, 6);
        Assert.Equal(2d / 3d, Find(nextSlots, 3).Left, 6);
        Assert.Equal(Find(nextSlots, 1).Right, Find(nextSlots, 2).Left, 6);
        Assert.Equal(Find(nextSlots, 4).Right, Find(nextSlots, 5).Left, 6);
    }

    [Fact]
    public void TryMoveIndividualHandle_DoesNotMoveUnrelatedAlignedEdges()
    {
        var slots = CreateTwoRowThreeColumnSlots();
        var handle = LayoutEditorBoundaryGeometry
            .CreateIndividualHandles(slots)
            .Single(item => item.NegativeSideSlotId == 1 && item.PositiveSideSlotId == 2);

        var moved = LayoutEditorBoundaryGeometry.TryMoveIndividualHandle(
            slots,
            handle,
            0.4,
            snapThreshold: 0,
            out var nextSlots);

        Assert.True(moved);
        Assert.Equal(0.4, Find(nextSlots, 1).Right, 6);
        Assert.Equal(0.4, Find(nextSlots, 2).Left, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 4).Right, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 5).Left, 6);
    }

    [Fact]
    public void FindSnapCoordinate_UsesOnlySameDirectionOverlappingSpans()
    {
        var active = new LayoutEditorBoundarySegment(
            LayoutEditorBoundaryDirection.Vertical,
            0.3,
            0,
            0.5,
            1,
            2);
        var candidates = new[]
        {
            new LayoutEditorBoundarySegment(LayoutEditorBoundaryDirection.Horizontal, 0.48, 0, 0.5, 3, 4),
            new LayoutEditorBoundarySegment(LayoutEditorBoundaryDirection.Vertical, 0.49, 0.6, 0.9, 5, 6),
            new LayoutEditorBoundarySegment(LayoutEditorBoundaryDirection.Vertical, 0.5, 0.25, 0.75, 7, 8)
        };

        var coordinate = LayoutEditorBoundaryGeometry.FindSnapCoordinate(
            candidates,
            [active],
            0.48,
            snapThreshold: 0.05);

        Assert.Equal(0.5, coordinate, 6);
    }

    [Fact]
    public void TryMoveBoundaryGroup_DoesNotSnapToParallelBoundaryWithNoSpanOverlap()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.3, 0.4),
            Slot(2, 0.3, 0, 0.5, 0.4),
            Slot(3, 0, 0.6, 0.6, 0.4),
            Slot(4, 0.6, 0.6, 0.4, 0.4)
        };
        var group = LayoutEditorBoundaryGeometry
            .CreateBoundaryGroups(slots)
            .Single(item => item.Direction == LayoutEditorBoundaryDirection.Vertical &&
                            NearlyEqual(item.Coordinate, 0.3));

        var moved = LayoutEditorBoundaryGeometry.TryMoveBoundaryGroup(
            slots,
            group,
            0.58,
            snapThreshold: 0.05,
            out var nextSlots);

        Assert.True(moved);
        Assert.Equal(0.58, Find(nextSlots, 1).Right, 6);
        Assert.Equal(0.58, Find(nextSlots, 2).Left, 6);
    }

    [Fact]
    public void FindSnapCoordinate_TreatsTinySpanGapsAsCompatibleForSnapping()
    {
        var active = new LayoutEditorBoundarySegment(
            LayoutEditorBoundaryDirection.Vertical,
            0.3,
            0,
            0.5,
            1,
            2);
        var candidate = new LayoutEditorBoundarySegment(
            LayoutEditorBoundaryDirection.Vertical,
            0.5,
            0.5008,
            0.9,
            3,
            4);

        var coordinate = LayoutEditorBoundaryGeometry.FindSnapCoordinate(
            [candidate],
            [active],
            0.494,
            snapThreshold: 0.01);

        Assert.Equal(0.5, coordinate, 6);
    }

    [Fact]
    public void TryMoveBoundaryGroup_RejectsSnapCoordinateThatWouldViolateMinimumSize()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.4, 1),
            Slot(2, 0.4, 0, 0.4, 1),
            Slot(3, 0.8, 0, 0.2, 1)
        };
        var group = LayoutEditorBoundaryGeometry
            .CreateBoundaryGroups(slots)
            .Single(item => item.Direction == LayoutEditorBoundaryDirection.Vertical &&
                            NearlyEqual(item.Coordinate, 0.4));

        var moved = LayoutEditorBoundaryGeometry.TryMoveBoundaryGroup(
            slots,
            group,
            0.7985,
            snapThreshold: 0.01,
            out var nextSlots);

        Assert.True(moved);
        Assert.Equal(0.7985, Find(nextSlots, 1).Right, 6);
        Assert.Equal(0.7985, Find(nextSlots, 2).Left, 6);
    }

    [Fact]
    public void TryResetSlotBounds_PreservesCoveredTopologyForUnevenRowsWithoutCreatingHoles()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.25, 0.37),
            Slot(2, 0.25, 0, 0.26, 0.37),
            Slot(3, 0.51, 0, 0.28, 0.37),
            Slot(4, 0.79, 0, 0.21, 0.37),
            Slot(5, 0, 0.37, 0.31, 0.33),
            Slot(6, 0.31, 0.37, 0.38, 0.33),
            Slot(7, 0.69, 0.37, 0.31, 0.33),
            Slot(8, 0, 0.70, 0.31, 0.30),
            Slot(9, 0.31, 0.70, 0.38, 0.30),
            Slot(10, 0.69, 0.70, 0.31, 0.30)
        };

        var reset = LayoutEditorBoundaryGeometry.TryResetSlotBounds(slots, out var nextSlots);

        Assert.True(reset);
        AssertFullyCovered(nextSlots);
        Assert.Equal(Find(nextSlots, 8).Top, Find(nextSlots, 9).Top, 6);
        Assert.Equal(Find(nextSlots, 8).Top, Find(nextSlots, 10).Top, 6);
        Assert.True(Find(nextSlots, 8).Top > Find(nextSlots, 5).Top);
        Assert.Equal(1, Find(nextSlots, 10).Bottom, 6);
    }

    [Fact]
    public void TryAlignSlotLine_HorizontalEqualizesXBoundsForSelectedRow()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.20, 0.4),
            Slot(2, 0.20, 0, 0.50, 0.4),
            Slot(3, 0.70, 0, 0.30, 0.4),
            Slot(4, 0, 0.4, 0.45, 0.6),
            Slot(5, 0.45, 0.4, 0.55, 0.6)
        };

        var aligned = LayoutEditorBoundaryGeometry.TryAlignSlotLine(
            slots,
            selectedSlotId: 2,
            LayoutEditorLineAlignment.Horizontal,
            out var nextSlots);

        Assert.True(aligned);
        Assert.Equal(0, Find(nextSlots, 1).Left, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 1).Width, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 2).Left, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 2).Width, 6);
        Assert.Equal(2d / 3d, Find(nextSlots, 3).Left, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 3).Width, 6);
        Assert.Equal(0.45, Find(nextSlots, 5).Left, 6);
        Assert.Equal(0.55, Find(nextSlots, 5).Width, 6);
    }

    [Fact]
    public void TryAlignSlotLine_VerticalEqualizesYBoundsForSelectedColumn()
    {
        var slots = new[]
        {
            Slot(1, 0, 0, 0.3, 0.20),
            Slot(2, 0, 0.20, 0.3, 0.50),
            Slot(3, 0, 0.70, 0.3, 0.30),
            Slot(4, 0.3, 0, 0.7, 0.45),
            Slot(5, 0.3, 0.45, 0.7, 0.55)
        };

        var aligned = LayoutEditorBoundaryGeometry.TryAlignSlotLine(
            slots,
            selectedSlotId: 2,
            LayoutEditorLineAlignment.Vertical,
            out var nextSlots);

        Assert.True(aligned);
        Assert.Equal(0, Find(nextSlots, 1).Top, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 1).Height, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 2).Top, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 2).Height, 6);
        Assert.Equal(2d / 3d, Find(nextSlots, 3).Top, 6);
        Assert.Equal(1d / 3d, Find(nextSlots, 3).Height, 6);
        Assert.Equal(0.45, Find(nextSlots, 5).Top, 6);
        Assert.Equal(0.55, Find(nextSlots, 5).Height, 6);
    }

    private static LayoutEditorSlotBounds[] CreateTwoRowThreeColumnSlots()
    {
        const double column = 1d / 3d;
        return
        [
            Slot(1, 0, 0, column, 0.5),
            Slot(2, column, 0, column, 0.5),
            Slot(3, 2 * column, 0, column, 0.5),
            Slot(4, 0, 0.5, column, 0.5),
            Slot(5, column, 0.5, column, 0.5),
            Slot(6, 2 * column, 0.5, column, 0.5)
        ];
    }

    private static LayoutEditorSlotBounds[] CreateTwoByTwoSlots()
    {
        return
        [
            Slot(1, 0, 0, 0.5, 0.5),
            Slot(2, 0.5, 0, 0.5, 0.5),
            Slot(3, 0, 0.5, 0.5, 0.5),
            Slot(4, 0.5, 0.5, 0.5, 0.5)
        ];
    }

    private static LayoutEditorSlotBounds Slot(int slotId, double left, double top, double width, double height)
    {
        return new LayoutEditorSlotBounds(slotId, left, top, width, height);
    }

    private static LayoutEditorSlotBounds Find(IReadOnlyList<LayoutEditorSlotBounds> slots, int slotId)
    {
        return slots.Single(slot => slot.SlotId == slotId);
    }

    private static void AssertFullyCovered(IReadOnlyList<LayoutEditorSlotBounds> slots)
    {
        var xBoundaries = slots
            .SelectMany(slot => new[] { slot.Left, slot.Right })
            .Distinct()
            .Order()
            .ToArray();
        var yBoundaries = slots
            .SelectMany(slot => new[] { slot.Top, slot.Bottom })
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(0, xBoundaries[0], 6);
        Assert.Equal(1, xBoundaries[^1], 6);
        Assert.Equal(0, yBoundaries[0], 6);
        Assert.Equal(1, yBoundaries[^1], 6);

        for (var y = 0; y < yBoundaries.Length - 1; y++)
        {
            for (var x = 0; x < xBoundaries.Length - 1; x++)
            {
                var centerX = (xBoundaries[x] + xBoundaries[x + 1]) / 2;
                var centerY = (yBoundaries[y] + yBoundaries[y + 1]) / 2;
                var containingSlots = slots.Count(slot =>
                    slot.Left <= centerX &&
                    centerX <= slot.Right &&
                    slot.Top <= centerY &&
                    centerY <= slot.Bottom);
                Assert.Equal(1, containingSlots);
            }
        }
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) < 0.000001;
    }
}
