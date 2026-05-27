namespace StreamOrchestra.App.Models;

public sealed record ExternalBrowserWindowLayout(
    int GridColumns,
    int GridRows,
    int X,
    int Y,
    int W,
    int H,
    IReadOnlyList<double>? ColumnWeights = null,
    IReadOnlyList<double>? RowWeights = null);
