using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Views;

/// <summary>카드 오버레이의 용도.</summary>
public enum LayoutCardMode
{
    /// <summary>채널 드래그로 화면을 추가(N → N+1)할 때.</summary>
    Add,

    /// <summary>슬롯 제거 버튼으로 화면을 줄일(N → N-1) 때.</summary>
    Remove,

    /// <summary>왼쪽 Alt 키로 현재 화면 수(N)를 유지한 채 레이아웃만 전환할 때.</summary>
    Switch
}

/// <summary>
/// 영상 영역 상단에 레이아웃 카드 리스트를 띄운다.
/// - 추가(Add): 탐색 패널에서 채널 드래그가 시작되면 N+1 템플릿 카드를 노출하고, 카드 위에 드롭하면 전환한다.
/// - 제거(Remove): 슬롯 제거 버튼을 누르면 N-1 템플릿 카드를 노출하고, 카드를 클릭하면 전환한다.
/// 두 경우 모두 첫 번째에는 "아무것도 안 함"(취소) 카드가 항상 들어간다(<see cref="CardChosen"/>의 template이 null).
/// </summary>
public sealed class LayoutCardPresenter
{
    private static readonly Brush OverlayBackground = new SolidColorBrush(Color.FromArgb(235, 18, 24, 32));
    private static readonly Brush CardBackground = new SolidColorBrush(Color.FromRgb(16, 24, 32));
    private static readonly Brush CancelCardBackground = new SolidColorBrush(Color.FromRgb(36, 28, 28));
    private static readonly Brush CardBorder = new SolidColorBrush(Color.FromRgb(45, 54, 66));
    private static readonly Brush CardBorderHighlight = new SolidColorBrush(Color.FromRgb(243, 246, 250));
    private static readonly Brush PrimaryText = Brushes.White;
    private static readonly Brush SecondaryText = new SolidColorBrush(Color.FromRgb(185, 194, 204));
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;

    private readonly Popup _popup;
    private readonly Border _root;
    private readonly StackPanel _cardPanel;
    private readonly TextBlock _title;
    private readonly TextBlock _emptyMessage;
    private FrameworkElement? _placementTarget;
    private Rect _lastPlacementBounds = Rect.Empty;

    public LayoutCardPresenter()
    {
        _cardPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        _emptyMessage = new TextBlock
        {
            Text = "현재 화면 수에 맞는 레이아웃 템플릿이 없습니다.",
            Foreground = SecondaryText,
            FontSize = 13,
            Margin = new Thickness(4, 8, 4, 8),
            Visibility = Visibility.Collapsed
        };

        _title = new TextBlock
        {
            Foreground = SecondaryText,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 6)
        };

        var cardScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _cardPanel
        };

        var contentPanel = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
        contentPanel.Children.Add(_title);
        contentPanel.Children.Add(_emptyMessage);
        contentPanel.Children.Add(cardScroller);

        _root = new Border
        {
            Background = OverlayBackground,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = contentPanel
        };

        _popup = new Popup
        {
            AllowsTransparency = true,
            Focusable = false,
            Placement = PlacementMode.Relative,
            PopupAnimation = PopupAnimation.Fade,
            StaysOpen = true,
            Child = _root
        };
    }

    /// <summary>
    /// 카드 선택 결과. template이 null이면 "아무것도 안 함"(취소)이다.
    /// 드래그 드롭이면 <paramref name="data"/>에 드롭 데이터가 들어오고, 클릭/키보드면 null이다.
    /// </summary>
    public event Action<LayoutPreset?, IDataObject?>? CardChosen;

    public bool IsOpen => _popup.IsOpen;

    public void Show(IReadOnlyList<LayoutPreset> candidates, FrameworkElement placementTarget, LayoutCardMode mode)
    {
        _cardPanel.Children.Clear();

        _title.Text = mode switch
        {
            LayoutCardMode.Remove => "삭제 후 전환할 레이아웃을 선택하세요. ('아무것도 안 함'을 누르면 취소)",
            LayoutCardMode.Switch => "현재 화면 수에 맞는 레이아웃을 선택하세요. ('아무것도 안 함'을 누르면 취소)",
            _ => "채널을 카드 위에 드롭하면 레이아웃이 전환됩니다."
        };

        // 첫 번째 카드는 항상 "아무것도 안 함"(취소) 카드.
        _cardPanel.Children.Add(CreateCancelCard());

        _emptyMessage.Visibility = candidates.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var template in candidates)
        {
            _cardPanel.Children.Add(CreateCard(template));
        }

        SetPlacementTarget(placementTarget);
        _popup.HorizontalOffset = 0;
        _popup.VerticalOffset = 0;
        _popup.IsOpen = true;
        RefreshPlacement(force: true);
        QueueRefreshPlacement();

        // 전환(Switch)은 왼쪽 Alt 홀드로 동작하므로 팝업에 포커스를 주면
        // 메인 윈도우가 Alt 키 해제 이벤트를 받지 못한다. 이 모드에서는 포커스를 옮기지 않는다.
        if (mode != LayoutCardMode.Switch &&
            _cardPanel.Children.Count > 0 && _cardPanel.Children[0] is Button firstCard)
        {
            firstCard.Focus();
        }
    }

    public void Hide()
    {
        _popup.IsOpen = false;
        _cardPanel.Children.Clear();
        SetPlacementTarget(null);
    }

    public void RefreshPlacement()
    {
        RefreshPlacement(force: true);
    }

    private Button CreateCancelCard()
    {
        var content = new StackPanel
        {
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        content.Children.Add(new TextBlock
        {
            Text = "✕",
            Foreground = PrimaryText,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        content.Children.Add(new TextBlock
        {
            Text = "아무것도 안 함",
            Foreground = SecondaryText,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var card = CreateCardShell(tag: null, content: content, background: CancelCardBackground);
        card.ToolTip = "레이아웃을 변경하지 않습니다.";
        WireCard(card, template: null);
        return card;
    }

    private Button CreateCard(LayoutPreset template)
    {
        var content = new StackPanel { Width = 150 };

        content.Children.Add(new TextBlock
        {
            Text = "미리보기",
            Foreground = SecondaryText,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 3)
        });

        content.Children.Add(LayoutPreviewBuilder.Build(template, 150, 84, showSlotNumbers: true));

        content.Children.Add(new TextBlock
        {
            Text = template.Name,
            Foreground = PrimaryText,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 0)
        });

        content.Children.Add(new TextBlock
        {
            Text = $"슬롯 {template.EffectiveSlotCount}개",
            Foreground = SecondaryText,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var card = CreateCardShell(tag: template, content: content, background: CardBackground);
        card.ToolTip = $"{template.Name} · 슬롯 {template.EffectiveSlotCount}개";
        WireCard(card, template);
        return card;
    }

    private static Button CreateCardShell(LayoutPreset? tag, UIElement content, Brush background)
    {
        return new Button
        {
            Tag = tag,
            Content = content,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 8, 0),
            Background = background,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            Foreground = PrimaryText,
            Focusable = true,
            IsTabStop = true,
            AllowDrop = true
        };
    }

    private void WireCard(Button card, LayoutPreset? template)
    {
        card.Click += (_, _) => CardChosen?.Invoke(template, null);
        card.DragEnter += (_, e) => OnCardDragOver(card, e);
        card.DragOver += (_, e) => OnCardDragOver(card, e);
        card.DragLeave += (_, _) => card.BorderBrush = CardBorder;
        card.Drop += (_, e) =>
        {
            card.BorderBrush = CardBorder;
            CardChosen?.Invoke(template, e.Data);
            e.Handled = true;
        };
    }

    private static void OnCardDragOver(Button card, DragEventArgs e)
    {
        card.BorderBrush = CardBorderHighlight;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void SetPlacementTarget(FrameworkElement? placementTarget)
    {
        if (ReferenceEquals(_placementTarget, placementTarget))
        {
            return;
        }

        if (_placementTarget is not null)
        {
            _placementTarget.SizeChanged -= PlacementTarget_Changed;
            _placementTarget.LayoutUpdated -= PlacementTarget_LayoutUpdated;
        }

        _placementTarget = placementTarget;
        _lastPlacementBounds = Rect.Empty;
        _popup.PlacementTarget = placementTarget;

        if (_placementTarget is not null)
        {
            _placementTarget.SizeChanged += PlacementTarget_Changed;
            _placementTarget.LayoutUpdated += PlacementTarget_LayoutUpdated;
        }
    }

    private void PlacementTarget_Changed(object sender, SizeChangedEventArgs e)
    {
        RefreshPlacement(force: false);
    }

    private void PlacementTarget_LayoutUpdated(object? sender, EventArgs e)
    {
        RefreshPlacement(force: false);
    }

    private void QueueRefreshPlacement()
    {
        _placementTarget?.Dispatcher.BeginInvoke(
            () => RefreshPlacement(force: true),
            DispatcherPriority.Render);
    }

    private void RefreshPlacement(bool force)
    {
        if (!_popup.IsOpen || _placementTarget is null)
        {
            return;
        }

        _root.Width = Math.Max(1, _placementTarget.ActualWidth);

        var bounds = GetScreenBounds(_placementTarget);
        if (!force && !HasBoundsChanged(_lastPlacementBounds, bounds))
        {
            return;
        }

        _lastPlacementBounds = bounds;
        NudgePopupPlacement(_popup);
        SetPopupScreenPosition(_popup, bounds.TopLeft);
    }

    private static Rect GetScreenBounds(FrameworkElement element)
    {
        if (!element.IsVisible)
        {
            return Rect.Empty;
        }

        var topLeft = element.PointToScreen(new Point(0, 0));
        return new Rect(topLeft.X, topLeft.Y, element.ActualWidth, element.ActualHeight);
    }

    private static bool HasBoundsChanged(Rect previous, Rect current)
    {
        return previous.IsEmpty ||
               Math.Abs(previous.X - current.X) > 0.5 ||
               Math.Abs(previous.Y - current.Y) > 0.5 ||
               Math.Abs(previous.Width - current.Width) > 0.5 ||
               Math.Abs(previous.Height - current.Height) > 0.5;
    }

    private static void NudgePopupPlacement(Popup popup)
    {
        var offset = popup.HorizontalOffset;
        popup.HorizontalOffset = offset + 0.01;
        popup.HorizontalOffset = offset;
    }

    private static void SetPopupScreenPosition(Popup popup, Point screenPoint)
    {
        if (popup.Child is not { } child ||
            PresentationSource.FromVisual(child) is not HwndSource source ||
            source.Handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            source.Handle,
            IntPtr.Zero,
            (int)Math.Round(screenPoint.X),
            (int)Math.Round(screenPoint.Y),
            0,
            0,
            SetWindowPosNoSize | SetWindowPosNoZOrder | SetWindowPosNoActivate);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
