namespace StreamOrchestra.App.Models;

public sealed class AppWindowRestorePlan
{
    public AppWindowState Bounds { get; init; } = new();

    public bool RestoreMaximizedAfterBoundsApplied { get; init; }
}
