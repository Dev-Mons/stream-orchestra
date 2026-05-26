using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class AppWindowPlacementService
{
    public const double MinimumRestoreWidth = 640;
    public const double MinimumRestoreHeight = 360;

    public AppWindowState? NormalizeForRestore(
        AppWindowState? windowState,
        double virtualScreenLeft,
        double virtualScreenTop,
        double virtualScreenWidth,
        double virtualScreenHeight)
    {
        if (windowState is null ||
            !IsFinite(windowState.X) ||
            !IsFinite(windowState.Y) ||
            !IsFinite(windowState.Width) ||
            !IsFinite(windowState.Height) ||
            windowState.Width <= 0 ||
            windowState.Height <= 0)
        {
            return null;
        }

        if (!IsFinite(virtualScreenLeft) ||
            !IsFinite(virtualScreenTop) ||
            !IsFinite(virtualScreenWidth) ||
            !IsFinite(virtualScreenHeight) ||
            virtualScreenWidth <= 0 ||
            virtualScreenHeight <= 0)
        {
            return windowState;
        }

        var maxWidth = Math.Max(MinimumRestoreWidth, virtualScreenWidth);
        var maxHeight = Math.Max(MinimumRestoreHeight, virtualScreenHeight);
        var width = Math.Clamp(windowState.Width, MinimumRestoreWidth, maxWidth);
        var height = Math.Clamp(windowState.Height, MinimumRestoreHeight, maxHeight);
        var maxLeft = virtualScreenLeft + Math.Max(0, virtualScreenWidth - Math.Min(width, virtualScreenWidth));
        var maxTop = virtualScreenTop + Math.Max(0, virtualScreenHeight - Math.Min(height, virtualScreenHeight));

        return new AppWindowState
        {
            X = Math.Clamp(windowState.X, virtualScreenLeft, maxLeft),
            Y = Math.Clamp(windowState.Y, virtualScreenTop, maxTop),
            Width = width,
            Height = height,
            IsMaximized = windowState.IsMaximized
        };
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
