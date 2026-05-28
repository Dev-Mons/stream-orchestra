using System.Text.Json.Serialization;

namespace StreamOrchestra.App.Models;

public enum SplitOrientation
{
    Horizontal,
    Vertical
}

public enum DockDirection
{
    None,
    Center,
    Left,
    Right,
    Top,
    Bottom
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SplitLayoutNode), "split")]
[JsonDerivedType(typeof(LeafLayoutNode), "leaf")]
public abstract record LayoutNode
{
    public string Id { get; init; } = "";
}

public sealed record SplitLayoutNode : LayoutNode
{
    public SplitOrientation Orientation { get; init; }

    public IReadOnlyList<LayoutNode> Children { get; init; } = [];

    public IReadOnlyList<double> Weights { get; init; } = [];
}

public sealed record LeafLayoutNode : LayoutNode
{
    public int SlotId { get; init; }

    public IReadOnlyList<LeafContentRef> Items { get; init; } = [];

    public int ActiveItemIndex { get; init; }
}

public sealed record LeafContentRef(
    string Kind,
    int SlotId,
    string? StreamUrl,
    string? StreamName);
