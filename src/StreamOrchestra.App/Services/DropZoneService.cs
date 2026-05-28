using System.Windows;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class DropZoneService
{
    private const double EdgeRatio = 0.25;

    public DockDirection Calculate(Rect bounds, Point pointer)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Contains(pointer))
        {
            return DockDirection.None;
        }

        var localX = pointer.X - bounds.X;
        var localY = pointer.Y - bounds.Y;
        var edgeWidth = bounds.Width * EdgeRatio;
        var edgeHeight = bounds.Height * EdgeRatio;

        if (localX <= edgeWidth)
        {
            return DockDirection.Left;
        }

        if (localX >= bounds.Width - edgeWidth)
        {
            return DockDirection.Right;
        }

        if (localY <= edgeHeight)
        {
            return DockDirection.Top;
        }

        if (localY >= bounds.Height - edgeHeight)
        {
            return DockDirection.Bottom;
        }

        return DockDirection.Center;
    }

    public static bool IsEdge(DockDirection direction)
    {
        return direction is DockDirection.Left or DockDirection.Right or DockDirection.Top or DockDirection.Bottom;
    }
}
