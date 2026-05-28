using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Views;

public sealed class DockingOverlayPresenter
{
    private readonly Popup _popup;
    private readonly Border _highlight;

    public DockingOverlayPresenter()
    {
        _highlight = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(105, 47, 128, 237)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(230, 243, 246, 250)),
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false
        };

        _popup = new Popup
        {
            AllowsTransparency = true,
            Focusable = false,
            IsHitTestVisible = false,
            Placement = PlacementMode.Relative,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.None,
            Child = _highlight
        };
    }

    public void Show(StreamSlotView targetSlot, DockDirection direction)
    {
        if (direction == DockDirection.None)
        {
            Hide();
            return;
        }

        var width = Math.Max(1, targetSlot.ActualWidth);
        var height = Math.Max(1, targetSlot.ActualHeight);
        var rect = GetHighlightRect(width, height, direction);

        _popup.PlacementTarget = targetSlot;
        _popup.HorizontalOffset = rect.X;
        _popup.VerticalOffset = rect.Y;
        _highlight.Width = rect.Width;
        _highlight.Height = rect.Height;
        _popup.IsOpen = true;
    }

    public void Hide()
    {
        _popup.IsOpen = false;
    }

    private static Rect GetHighlightRect(double width, double height, DockDirection direction)
    {
        return direction switch
        {
            DockDirection.Left => new Rect(0, 0, width / 2, height),
            DockDirection.Right => new Rect(width / 2, 0, width / 2, height),
            DockDirection.Top => new Rect(0, 0, width, height / 2),
            DockDirection.Bottom => new Rect(0, height / 2, width, height / 2),
            DockDirection.Center => new Rect(width * 0.25, height * 0.25, width * 0.5, height * 0.5),
            _ => new Rect(0, 0, width, height)
        };
    }
}
