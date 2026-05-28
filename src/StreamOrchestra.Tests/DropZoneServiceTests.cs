using System.Windows;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class DropZoneServiceTests
{
    [Theory]
    [InlineData(10, 50, DockDirection.Left)]
    [InlineData(190, 50, DockDirection.Right)]
    [InlineData(100, 10, DockDirection.Top)]
    [InlineData(100, 90, DockDirection.Bottom)]
    [InlineData(100, 50, DockDirection.Center)]
    public void Calculate_ReturnsExpectedZone(double x, double y, DockDirection expected)
    {
        var service = new DropZoneService();

        var direction = service.Calculate(new Rect(0, 0, 200, 100), new Point(x, y));

        Assert.Equal(expected, direction);
    }

    [Fact]
    public void Calculate_ReturnsNoneOutsideBounds()
    {
        var service = new DropZoneService();

        var direction = service.Calculate(new Rect(0, 0, 200, 100), new Point(240, 50));

        Assert.Equal(DockDirection.None, direction);
    }

    [Theory]
    [InlineData(160, 300, DockDirection.Left)]
    [InlineData(640, 300, DockDirection.Right)]
    [InlineData(400, 120, DockDirection.Top)]
    [InlineData(400, 480, DockDirection.Bottom)]
    [InlineData(400, 300, DockDirection.Center)]
    public void Calculate_UsesGenerousEdgeZonesForLargeWebViewSlots(double x, double y, DockDirection expected)
    {
        var service = new DropZoneService();

        var direction = service.Calculate(new Rect(0, 0, 800, 600), new Point(x, y));

        Assert.Equal(expected, direction);
    }
}
