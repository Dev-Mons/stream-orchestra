using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SlotSwapServiceTests
{
    [Fact]
    public void SwapStreams_SwapsOnlyStreamIdentity()
    {
        var service = new SlotSwapService();
        var sourceSlot = new SlotRuntimeState(
            SlotId: 2,
            StreamName: "Source Stream",
            StreamUrl: "https://example.com/source",
            IsMuted: true,
            ProfileGroupId: "A");
        var targetSlot = new SlotRuntimeState(
            SlotId: 9,
            StreamName: "Target Stream",
            StreamUrl: "https://example.com/target",
            IsMuted: false,
            ProfileGroupId: "C");

        var result = service.SwapStreams(sourceSlot, targetSlot);

        Assert.Equal("Target Stream", result.SourceSlot.StreamName);
        Assert.Equal("https://example.com/target", result.SourceSlot.StreamUrl);
        Assert.Equal(sourceSlot.SlotId, result.SourceSlot.SlotId);
        Assert.Equal(sourceSlot.IsMuted, result.SourceSlot.IsMuted);
        Assert.Equal(sourceSlot.ProfileGroupId, result.SourceSlot.ProfileGroupId);

        Assert.Equal("Source Stream", result.TargetSlot.StreamName);
        Assert.Equal("https://example.com/source", result.TargetSlot.StreamUrl);
        Assert.Equal(targetSlot.SlotId, result.TargetSlot.SlotId);
        Assert.Equal(targetSlot.IsMuted, result.TargetSlot.IsMuted);
        Assert.Equal(targetSlot.ProfileGroupId, result.TargetSlot.ProfileGroupId);
    }

    [Fact]
    public void SwapStreams_DoesNothingWhenSourceAndTargetAreSameSlot()
    {
        var service = new SlotSwapService();
        var slot = new SlotRuntimeState(
            SlotId: 2,
            StreamName: "Source Stream",
            StreamUrl: "https://example.com/source",
            IsMuted: true,
            ProfileGroupId: "A");

        var result = service.SwapStreams(slot, slot);

        Assert.Equal(slot, result.SourceSlot);
        Assert.Equal(slot, result.TargetSlot);
    }

    [Fact]
    public void SwapStreams_NormalizesStreamIdentityWithoutMovingSlotState()
    {
        var service = new SlotSwapService();
        var sourceSlot = new SlotRuntimeState(
            SlotId: 2,
            StreamName: null!,
            StreamUrl: " example.com/source ",
            IsMuted: true,
            ProfileGroupId: "A");
        var targetSlot = new SlotRuntimeState(
            SlotId: 9,
            StreamName: "Stale Blank Name",
            StreamUrl: " ",
            IsMuted: false,
            ProfileGroupId: "C");

        var result = service.SwapStreams(sourceSlot, targetSlot);

        Assert.Equal("Empty", result.SourceSlot.StreamName);
        Assert.Equal("about:blank", result.SourceSlot.StreamUrl);
        Assert.True(result.SourceSlot.IsMuted);
        Assert.Equal("A", result.SourceSlot.ProfileGroupId);

        Assert.Equal("source", result.TargetSlot.StreamName);
        Assert.Equal("https://example.com/source", result.TargetSlot.StreamUrl);
        Assert.False(result.TargetSlot.IsMuted);
        Assert.Equal("C", result.TargetSlot.ProfileGroupId);
    }

    [Fact]
    public void SwapStreams_RejectsNullRuntimeState()
    {
        var service = new SlotSwapService();
        var slot = new SlotRuntimeState(
            SlotId: 2,
            StreamName: "Stream",
            StreamUrl: "https://example.com/stream",
            IsMuted: true,
            ProfileGroupId: "A");

        Assert.Throws<ArgumentNullException>(() => service.SwapStreams(null!, slot));
        Assert.Throws<ArgumentNullException>(() => service.SwapStreams(slot, null!));
    }
}
