namespace StreamOrchestra.App.Models;

public sealed record ExternalBrowserFallbackPlan(
    bool CanLaunch,
    string Reason,
    int InstalledBrowserCount,
    int PlannedSlotCount,
    IReadOnlyList<ExternalBrowserSlotLaunchPlan> Slots);
