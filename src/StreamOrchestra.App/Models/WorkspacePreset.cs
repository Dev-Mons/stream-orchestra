namespace StreamOrchestra.App.Models;

public sealed class WorkspacePreset
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string LayoutId { get; init; } = LayoutPresetIds.Default;

    public IReadOnlyList<WorkspaceSlot> Slots { get; init; } = [];
}
