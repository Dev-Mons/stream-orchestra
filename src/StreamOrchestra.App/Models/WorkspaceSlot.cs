namespace StreamOrchestra.App.Models;

public sealed class WorkspaceSlot
{
    public int SlotId { get; init; }

    public string StreamName { get; init; } = "";

    public string StreamUrl { get; init; } = "about:blank";

    public bool Muted { get; init; }

    public string ProfileGroupId { get; init; } = "";
}
