using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class LayoutTransitionPlannerTests
{
    private readonly LayoutTransitionPlanner _planner = new();

    [Fact]
    public void CreatePlan_TenToOne_RetainsSelectedBroadcastAndClosesTheOthers()
    {
        var current = CreateRowLayout("ten", 10);
        var target = CreateRowLayout("one", 1);

        var plan = _planner.CreatePlan(current, target, CreatePlayingSlots(10), preferredSlotId: 7);

        var assignment = Assert.Single(plan.Assignments);
        Assert.Equal(1, assignment.TargetSlotId);
        Assert.Equal(7, assignment.SourceSlotId);
        Assert.Equal("https://example.test/7", assignment.StreamUrl);
        Assert.Equal(9, plan.ClosedStreamCount);
        Assert.DoesNotContain(7, plan.ClosedSourceSlotIds);
        Assert.Equal(1, plan.PreferredTargetSlotId);
    }

    [Fact]
    public void CreatePlan_ShrinkWithoutSelection_RetainsBroadcastsInSpatialOrder()
    {
        var current = CreateLayout(
            "spatial",
            new LayoutSlot { SlotId = 3, Left = 0, Top = 0, Width = 0.5, Height = 1 },
            new LayoutSlot { SlotId = 1, Left = 0.5, Top = 0, Width = 0.5, Height = 1 },
            new LayoutSlot { SlotId = 2, Left = 0, Top = 0.5, Width = 1, Height = 0.5 });
        var target = CreateRowLayout("one", 1);

        var plan = _planner.CreatePlan(current, target, CreatePlayingSlots(3), preferredSlotId: null);

        Assert.Equal(3, Assert.Single(plan.Assignments).SourceSlotId);
        Assert.Equal([1, 2], plan.ClosedSourceSlotIds);
    }

    [Fact]
    public void CreatePlan_TenToFour_PutsSelectedBroadcastFirstAndPreservesOtherMatchingSlots()
    {
        var plan = _planner.CreatePlan(
            CreateRowLayout("ten", 10),
            CreateRowLayout("four", 4),
            CreatePlayingSlots(10),
            preferredSlotId: 7);

        Assert.Equal(7, plan.Assignments[0].SourceSlotId);
        Assert.Equal(2, plan.Assignments[1].SourceSlotId);
        Assert.Equal(3, plan.Assignments[2].SourceSlotId);
        Assert.Equal(1, plan.Assignments[3].SourceSlotId);
        Assert.Equal(6, plan.ClosedStreamCount);
    }

    [Fact]
    public void CreatePlan_Grow_PreservesMatchingSlotsAndLeavesNewRegionsBlank()
    {
        var plan = _planner.CreatePlan(
            CreateRowLayout("two", 2),
            CreateRowLayout("four", 4),
            CreatePlayingSlots(2),
            preferredSlotId: 2);

        Assert.Equal([1, 2, null, null], plan.Assignments.Select(item => item.SourceSlotId).ToArray());
        Assert.Empty(plan.ClosedSourceSlotIds);
    }

    [Fact]
    public void CreatePlan_SameCapacity_PreservesSlotIdentityEvenWhenGeometryChanges()
    {
        var current = CreateRowLayout("row", 2);
        var target = CreateLayout(
            "columns-reversed",
            new LayoutSlot { SlotId = 2, Left = 0, Top = 0, Width = 0.5, Height = 1 },
            new LayoutSlot { SlotId = 1, Left = 0.5, Top = 0, Width = 0.5, Height = 1 });

        var plan = _planner.CreatePlan(current, target, CreatePlayingSlots(2), preferredSlotId: 2);

        Assert.Equal(2, plan.Assignments[0].TargetSlotId);
        Assert.Equal(2, plan.Assignments[0].SourceSlotId);
        Assert.Equal(1, plan.Assignments[1].TargetSlotId);
        Assert.Equal(1, plan.Assignments[1].SourceSlotId);
        Assert.Empty(plan.ClosedSourceSlotIds);
    }

    [Fact]
    public void CreatePlan_IgnoresBlankPreferredSlotAndRetainsPlayingBroadcast()
    {
        var states = new[]
        {
            CreateSlotState(1, "about:blank"),
            CreateSlotState(2, "https://example.test/2")
        };

        var plan = _planner.CreatePlan(
            CreateRowLayout("two", 2),
            CreateRowLayout("one", 1),
            states,
            preferredSlotId: 1);

        Assert.Equal(2, Assert.Single(plan.Assignments).SourceSlotId);
        Assert.Null(plan.PreferredSourceSlotId);
    }

    [Fact]
    public void CreatePlan_UsesRenderedTargetRegionsInsteadOfLegacySlotCountMetadata()
    {
        var target = new LayoutPreset
        {
            Id = "legacy",
            Name = "legacy",
            SlotCount = 10,
            GridColumns = 1,
            GridRows = 1,
            Slots = [new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 }]
        };

        var plan = _planner.CreatePlan(
            CreateRowLayout("three", 3),
            target,
            CreatePlayingSlots(3),
            preferredSlotId: 2);

        Assert.Single(plan.Assignments);
        Assert.Equal(2, plan.Assignments[0].SourceSlotId);
        Assert.Equal(2, plan.ClosedStreamCount);
    }

    private static SlotRuntimeState[] CreatePlayingSlots(int count) =>
        Enumerable.Range(1, count)
            .Select(slotId => CreateSlotState(slotId, $"https://example.test/{slotId}"))
            .ToArray();

    private static SlotRuntimeState CreateSlotState(int slotId, string url) =>
        new(slotId, $"Stream {slotId}", url, false, $"group-{slotId}");

    private static LayoutPreset CreateRowLayout(string id, int slotCount) =>
        CreateLayout(
            id,
            Enumerable.Range(1, slotCount)
                .Select(slotId => new LayoutSlot
                {
                    SlotId = slotId,
                    X = slotId - 1,
                    Y = 0,
                    W = 1,
                    H = 1
                })
                .ToArray());

    private static LayoutPreset CreateLayout(string id, params LayoutSlot[] slots) =>
        new()
        {
            Id = id,
            Name = id,
            GridColumns = Math.Max(1, slots.Length),
            GridRows = 1,
            Slots = slots
        };
}
