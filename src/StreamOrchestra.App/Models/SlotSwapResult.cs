namespace StreamOrchestra.App.Models;

public sealed record SlotSwapResult(
    SlotRuntimeState SourceSlot,
    SlotRuntimeState TargetSlot);
