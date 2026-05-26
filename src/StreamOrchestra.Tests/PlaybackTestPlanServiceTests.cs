using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class PlaybackTestPlanServiceTests
{
    [Theory]
    [InlineData(4, new[] { 1, 2, 3, 4 }, new[] { 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 })]
    [InlineData(8, new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, new[] { 9, 10, 11, 12, 13, 14, 15, 16 })]
    [InlineData(9, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, new[] { 10, 11, 12, 13, 14, 15, 16 })]
    [InlineData(12, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, new[] { 13, 14, 15, 16 })]
    [InlineData(16, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, new int[] { })]
    public void CreatePlan_ActivatesFirstSlotsAndBlanksTheRest(
        int targetCount,
        int[] expectedActiveSlotIds,
        int[] expectedInactiveSlotIds)
    {
        var service = new PlaybackTestPlanService();

        var plan = service.CreatePlan(targetCount);

        Assert.Equal(targetCount, plan.TargetPlaybackCount);
        Assert.Equal(expectedActiveSlotIds, plan.ActiveSlotIds);
        Assert.Equal(expectedInactiveSlotIds, plan.InactiveSlotIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void CreatePlan_RejectsUnsupportedSlotCounts(int targetCount)
    {
        var service = new PlaybackTestPlanService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreatePlan(targetCount));
    }

    [Fact]
    public void CreateIsolatedSlotPlan_ActivatesSelectedSlotsAndBlanksTheRest()
    {
        var service = new PlaybackTestPlanService();

        var plan = service.CreateIsolatedSlotPlan([8, 5, 6, 7]);

        Assert.Equal(4, plan.TargetPlaybackCount);
        Assert.Equal([5, 6, 7, 8], plan.ActiveSlotIds);
        Assert.Equal([1, 2, 3, 4, 9, 10, 11, 12, 13, 14, 15, 16], plan.InactiveSlotIds);
    }

    [Fact]
    public void CreateIsolatedSlotPlan_RejectsEmptySelection()
    {
        var service = new PlaybackTestPlanService();

        Assert.Throws<ArgumentException>(() => service.CreateIsolatedSlotPlan([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void CreateIsolatedSlotPlan_RejectsOutOfRangeSlots(int slotId)
    {
        var service = new PlaybackTestPlanService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateIsolatedSlotPlan([slotId]));
    }
}
