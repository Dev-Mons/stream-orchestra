using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Views;

/// <summary>
/// 탐색 패널에서 채널 드래그가 시작되면 영상 영역 상단에 레이아웃 템플릿 카드 리스트를 띄운다.
/// 카드는 "현재 보이는 슬롯 수 + 1"에 해당하는 정적 템플릿만 노출하며, 카드 위에 드롭하면
/// 해당 템플릿으로 즉시 전환된다(<see cref="CardChosen"/> 이벤트). 동적 레이아웃 도킹 오버레이를 대체한다.
/// </summary>
public sealed class LayoutCardPresenter
{
    private static readonly Brush OverlayBackground = new SolidColorBrush(Color.FromArgb(235, 18, 24, 32));
    private static readonly Brush CardBackground = new SolidColorBrush(Color.FromRgb(16, 24, 32));
    private static readonly Brush CardBorder = new SolidColorBrush(Color.FromRgb(45, 54, 66));
    private static readonly Brush CardBorderHighlight = new SolidColorBrush(Color.FromRgb(243, 246, 250));
    private static readonly Brush PrimaryText = Brushes.White;
    private static readonly Brush SecondaryText = new SolidColorBrush(Color.FromRgb(185, 194, 204));

    private readonly Popup _popup;
    private readonly Border _root;
    private readonly StackPanel _cardPanel;
    private readonly TextBlock _emptyMessage;

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

        var title = new TextBlock
        {
            Text = "채널을 카드 위에 드롭하면 레이아웃이 전환됩니다.",
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
        contentPanel.Children.Add(title);
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

    /// <summary>카드 선택 결과. 드래그 드롭이면 <paramref name="data"/>에 드롭 데이터가 들어오고, 키보드/클릭이면 null이다.</summary>
    public event Action<LayoutPreset, IDataObject?>? CardChosen;

    public bool IsOpen => _popup.IsOpen;

    public void Show(IReadOnlyList<LayoutPreset> candidates, FrameworkElement placementTarget)
    {
        _cardPanel.Children.Clear();

        if (candidates.Count == 0)
        {
            _emptyMessage.Visibility = Visibility.Visible;
        }
        else
        {
            _emptyMessage.Visibility = Visibility.Collapsed;
            foreach (var template in candidates)
            {
                _cardPanel.Children.Add(CreateCard(template));
            }
        }

        _root.Width = Math.Max(1, placementTarget.ActualWidth);
        _popup.PlacementTarget = placementTarget;
        _popup.HorizontalOffset = 0;
        _popup.VerticalOffset = 0;
        _popup.IsOpen = true;

        if (_cardPanel.Children.Count > 0 && _cardPanel.Children[0] is Button firstCard)
        {
            firstCard.Focus();
        }
    }

    public void Hide()
    {
        _popup.IsOpen = false;
        _cardPanel.Children.Clear();
    }

    private Button CreateCard(LayoutPreset template)
    {
        var layout = new StackPanel { Width = 150 };

        layout.Children.Add(new TextBlock
        {
            Text = "미리보기",
            Foreground = SecondaryText,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 3)
        });

        layout.Children.Add(LayoutPreviewBuilder.Build(template, 150, 84, showSlotNumbers: true));

        layout.Children.Add(new TextBlock
        {
            Text = template.Name,
            Foreground = PrimaryText,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 0)
        });

        layout.Children.Add(new TextBlock
        {
            Text = $"슬롯 {template.EffectiveSlotCount}개",
            Foreground = SecondaryText,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var card = new Button
        {
            Tag = template,
            Content = layout,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 8, 0),
            Background = CardBackground,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            Foreground = PrimaryText,
            Focusable = true,
            IsTabStop = true,
            AllowDrop = true,
            ToolTip = $"{template.Name} · 슬롯 {template.EffectiveSlotCount}개"
        };

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

        return card;
    }

    private static void OnCardDragOver(Button card, DragEventArgs e)
    {
        card.BorderBrush = CardBorderHighlight;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }
}
