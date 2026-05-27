namespace StreamOrchestra.App.Models;

public sealed class LayoutPreset
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public int GridColumns { get; init; }

    public int GridRows { get; init; }

    public IReadOnlyList<double> ColumnWeights { get; init; } = [];

    public IReadOnlyList<double> RowWeights { get; init; } = [];

    public IReadOnlyList<LayoutSlot> Slots { get; init; } = [];
}
