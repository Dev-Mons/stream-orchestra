namespace StreamOrchestra.App.Models;

public sealed class AppWindowState
{
    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; } = 1600;

    public double Height { get; init; } = 1000;

    public bool IsMaximized { get; init; }
}
