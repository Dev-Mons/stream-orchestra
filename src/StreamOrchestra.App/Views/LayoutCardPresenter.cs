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

    /// <summary>전체 레이아웃 중 하나를 직접 선택해 화면 수와 배치를 함께 전환할 때.</summary>
    Switch
}

/// <summary>
/// 영상 영역 상단에 레이아웃 카드 리스트를 띄운다.
/// - 추가(Add): 탐색 패널에서 채널 드래그가 시작되면 N+1 템플릿 카드를 노출하고, 카드 위에 드롭하면 전환한다.
/// - 제거(Remove): 슬롯 제거 버튼을 누르면 N-1 템플릿 카드를 노출하고, 카드를 클릭하면 전환한다.
/// 직접 전환(Switch)은 모든 템플릿을 화면 수별로 묶어 한 번에 표시한다.
/// 모든 모드의 첫 번째에는 "아무것도 안 함"(취소) 카드가 들어간다(<see cref="CardChosen"/>의 template이 null).
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
    private readonly ScrollViewer _cardScroller;
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

        _cardScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _cardPanel
        };

        var contentPanel = new Grid { Margin = new Thickness(10, 8, 10, 10) };
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_title, 0);
        Grid.SetRow(_emptyMessage, 1);
        Grid.SetRow(_cardScroller, 2);
        contentPanel.Children.Add(_title);
        contentPanel.Children.Add(_emptyMessage);
        contentPanel.Children.Add(_cardScroller);

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

    public void Show(
        IReadOnlyList<LayoutPreset> candidates,
        FrameworkElement placementTarget,
        LayoutCardMode mode,
        string? selectedLayoutId = null,
        int? currentPlayingCount = null,
        string? preferredStreamName = null)
    {
        _cardPanel.Children.Clear();

        _title.Text = mode switch
        {
            LayoutCardMode.Remove => "삭제 후 전환할 레이아웃을 선택하세요. ('아무것도 안 함'을 누르면 취소)",
            LayoutCardMode.Switch => string.IsNullOrWhiteSpace(preferredStreamName)
                ? "원하는 레이아웃을 바로 선택하세요. 화면을 줄이면 앞쪽 방송부터 유지됩니다. (ESC: 취소)"
                : $"원하는 레이아웃을 바로 선택하세요. 화면을 줄이면 '{preferredStreamName}' 방송을 우선 유지합니다. (ESC: 취소)",
            _ => "채널을 카드 위에 드롭하면 레이아웃이 전환됩니다."
        };

        ConfigureCardLayout(mode);

        _emptyMessage.Visibility = candidates.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        _emptyMessage.Text = mode == LayoutCardMode.Switch
            ? "선택할 수 있는 레이아웃이 없습니다. 설정 → 레이아웃에서 먼저 생성하세요."
            : "현재 화면 수에 맞는 레이아웃 템플릿이 없습니다.";

        if (mode == LayoutCardMode.Switch)
        {
            BuildDirectSelectionCards(candidates, selectedLayoutId, currentPlayingCount);
        }
        else
        {
            // 첫 번째 카드는 항상 "아무것도 안 함"(취소) 카드.
            _cardPanel.Children.Add(CreateCancelCard());
            foreach (var template in candidates)
            {
                _cardPanel.Children.Add(CreateCard(template));
            }
        }

        SetPlacementTarget(placementTarget);
        _popup.HorizontalOffset = 0;
        _popup.VerticalOffset = 0;
        _popup.IsOpen = true;
        RefreshPlacement(force: true);
        QueueRefreshPlacement();

        // 직접 전환은 메인 창이 ESC와 전환 단축키를 계속 받도록 포커스를 옮기지 않는다.
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

    private void ConfigureCardLayout(LayoutCardMode mode)
    {
        var isDirectSelection = mode == LayoutCardMode.Switch;
        _cardPanel.Orientation = isDirectSelection
            ? Orientation.Vertical
            : Orientation.Horizontal;
        _cardScroller.HorizontalScrollBarVisibility = isDirectSelection
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        _cardScroller.VerticalScrollBarVisibility = isDirectSelection
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
    }

    private void BuildDirectSelectionCards(
        IReadOnlyList<LayoutPreset> candidates,
        string? selectedLayoutId,
        int? currentPlayingCount)
    {
        var cancelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        cancelRow.Children.Add(CreateCancelCard());
        _cardPanel.Children.Add(cancelRow);

        foreach (var group in candidates.GroupBy(template => template.Slots.Count))
        {
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            section.Children.Add(new TextBlock
            {
                Text = $"{group.Key}화면 레이아웃",
                Foreground = PrimaryText,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 6)
            });

            var cards = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var template in group)
            {
                var isSelected = !string.IsNullOrWhiteSpace(selectedLayoutId) &&
                    template.Id.Equals(selectedLayoutId, StringComparison.OrdinalIgnoreCase);
                cards.Children.Add(CreateCard(template, isSelected, currentPlayingCount));
            }

            section.Children.Add(cards);
            _cardPanel.Children.Add(section);
        }
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

    private Button CreateCard(
        LayoutPreset template,
        bool isSelected = false,
        int? currentPlayingCount = null)
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

        var displayedSlotCount = currentPlayingCount is null
            ? template.EffectiveSlotCount
            : template.Slots.Count;
        content.Children.Add(new TextBlock
        {
            Text = $"슬롯 {displayedSlotCount}개",
            Foreground = SecondaryText,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });

        if (currentPlayingCount is { } playingCount)
        {
            var targetCount = template.Slots.Count;
            var differenceText = playingCount > targetCount
                ? $"방송 {playingCount - targetCount}개 종료"
                : playingCount < targetCount
                    ? $"빈 화면 {targetCount - playingCount}개 추가"
                    : "모든 방송 유지";
            content.Children.Add(new TextBlock
            {
                Text = differenceText,
                Foreground = playingCount > targetCount
                    ? new SolidColorBrush(Color.FromRgb(244, 197, 106))
                    : SecondaryText,
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        var card = CreateCardShell(tag: template, content: content, background: CardBackground);
        if (isSelected)
        {
            card.BorderBrush = CardBorderHighlight;
            card.BorderThickness = new Thickness(2);
        }

        card.ToolTip = isSelected
            ? $"{template.Name} · 슬롯 {displayedSlotCount}개 · 현재 레이아웃"
            : $"{template.Name} · 슬롯 {displayedSlotCount}개";
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
        _root.MaxHeight = Math.Max(220, _placementTarget.ActualHeight * 0.72);

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
