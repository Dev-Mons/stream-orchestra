namespace StreamOrchestra.App.Models;

public sealed class AppState
{
    public string? LastWorkspaceId { get; init; }

    public AppWindowState Window { get; init; } = new();

    public int? SelectedSlotId { get; init; }

    public WorkspacePreset? LastSession { get; init; }

    public bool IsExplorerPanelVisible { get; init; } = true;

    public bool AreSlotUrlEditorsVisible { get; init; } = true;

    public bool AreSlotControlBarsAlwaysVisible { get; init; } = true;
}
