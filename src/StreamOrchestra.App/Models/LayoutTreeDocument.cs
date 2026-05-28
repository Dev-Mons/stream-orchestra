namespace StreamOrchestra.App.Models;

public sealed class LayoutTreeDocument
{
    public int Version { get; init; } = 1;

    public string SourceLayoutId { get; init; } = "";

    public LayoutNode? Root { get; init; }

    public string? ActiveLeafId { get; init; }
}
