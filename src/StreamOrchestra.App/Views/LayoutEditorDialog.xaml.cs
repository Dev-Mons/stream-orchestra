using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class LayoutEditorDialog : Window
{
    private const double MinWeight = 0.2;
    private const double ZoneGap = 3;
    private const double SplitterThickness = 8;

    private readonly LayoutPresetService _layoutPresetService;
    private readonly List<LayoutPreset> _customLayouts;
    private int[,] _zoneCells = new int[1, 1] { { 1 } };
    private double[] _columnWeights = [1];
    private double[] _rowWeights = [1];
    private readonly HashSet<int> _selectedZoneIds = new();
    private bool _isRefreshingEditor;
    private bool _isRefreshingLayoutName;
    private bool _hasUnsavedEdits;
    private LayoutPreset? _selectedCustomLayout;
    private string _editingLayoutName = "Custom Layout";

    private Canvas? _editorCanvas;
    private readonly List<(FrameworkElement Container, int SlotId, TextBlock SizeTextBlock)> _editorZoneButtons = new();
    private readonly List<(Thumb Thumb, LayoutEditorSplitterSegment Segment)> _editorColumnSplitters = new();
    private readonly List<(Thumb Thumb, LayoutEditorSplitterSegment Segment)> _editorRowSplitters = new();
    private Size _editorSurfaceSize = new(760, 440);

    public LayoutEditorDialog(
        LayoutPresetService layoutPresetService,
        IReadOnlyList<LayoutPreset> customLayouts,
        LayoutPreset? currentLayout)
    {
        _layoutPresetService = layoutPresetService;
        _customLayouts = customLayouts.ToList();

        InitializeComponent();
        SplitEditorHost.SizeChanged += SplitEditorHost_SizeChanged;
        RefreshCustomLayoutList();
        ResetZoneEditor("Custom Layout");

        var selectedLayout = currentLayout is null
            ? null
            : _customLayouts.FirstOrDefault(layout => layout.Id.Equals(currentLayout.Id, StringComparison.OrdinalIgnoreCase));

        if (selectedLayout is null)
        {
            return;
        }

        RefreshCustomLayoutList(selectedLayout);
        LoadCustomLayoutIntoZoneEditor(selectedLayout);
    }

    public bool HasCustomLayoutChanges { get; private set; }

    /// <summary>"저장 후 적용"으로 닫혔을 때 메인 화면에 즉시 적용할 레이아웃 ID. 그 외에는 null.</summary>
    public string? AppliedLayoutId { get; private set; }

    private void RefreshCustomLayoutList(LayoutPreset? selectedLayout = null)
    {
        var orderedLayouts = _customLayouts
            .OrderBy(layout => layout.Name)
            .ToArray();

        CustomLayoutListPanel.Children.Clear();
        _isRefreshingEditor = true;
        _selectedCustomLayout = selectedLayout is null
            ? null
            : orderedLayouts.FirstOrDefault(layout =>
                layout.Id.Equals(selectedLayout.Id, StringComparison.OrdinalIgnoreCase));

        foreach (var layout in orderedLayouts)
        {
            var isSelected = _selectedCustomLayout?.Id.Equals(layout.Id, StringComparison.OrdinalIgnoreCase) == true;
            var button = CreateLayoutCardButton(layout, width: 214, height: 92, isSelected: isSelected);
            button.Click += CustomLayoutButton_Click;
            CustomLayoutListPanel.Children.Add(button);
        }

        _isRefreshingEditor = false;
    }

    private static Button CreateLayoutCardButton(LayoutPreset layout, double width, double height, bool isSelected)
    {
        var button = new Button
        {
            Tag = layout,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)),
            BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(243, 246, 250))
                : new SolidColorBrush(Color.FromRgb(45, 54, 66)),
            BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
            Foreground = Brushes.White
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = layout.Name,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{layout.GridColumns}x{layout.GridRows} / {layout.Slots.Count} slots",
            Foreground = new SolidColorBrush(Color.FromRgb(185, 194, 204)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(LayoutPreviewBuilder.Build(layout, width, height, showSlotNumbers: false));

        button.Content = panel;
        return button;
    }

    private void CustomLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingEditor)
        {
            return;
        }

        if (sender is Button { Tag: LayoutPreset layout })
        {
            _selectedCustomLayout = layout;
            RefreshCustomLayoutList(layout);
            LoadCustomLayoutIntoZoneEditor(layout);
        }
    }

    private void LayoutNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRefreshingLayoutName)
        {
            return;
        }

        _editingLayoutName = LayoutNameTextBox.Text;
        _hasUnsavedEdits = true;
        UpdateEditorChrome();
    }

    private void NewCustomLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedCustomLayout = null;
        RefreshCustomLayoutList();
        ResetZoneEditor(CreateCustomLayoutName());
        DialogStatusTextBlock.Text = "새 사용자 지정 레이아웃을 시작했습니다.";
    }

    private string CreateCustomLayoutName()
    {
        const string baseName = "Custom Layout";
        var existingNames = _customLayouts
            .Select(layout => layout.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName} {index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void VerticalSplitButton_Click(object sender, RoutedEventArgs e)
    {
        SplitSelectedZone(SplitAxis.Vertical);
    }

    private void HorizontalSplitButton_Click(object sender, RoutedEventArgs e)
    {
        SplitSelectedZone(SplitAxis.Horizontal);
    }

    private void RemoveSelectedSlotButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedZone();
    }

    private void MergeSelectedZonesButton_Click(object sender, RoutedEventArgs e)
    {
        MergeSelectedZones();
    }

    private void ResetZoneSizeButton_Click(object sender, RoutedEventArgs e)
    {
        (_columnWeights, _rowWeights) = ComputeDesignBalancedWeights(_zoneCells);
        _hasUnsavedEdits = true;
        RefreshZoneEditorSurface();
        DialogStatusTextBlock.Text = "현재 레이아웃 디자인에 맞춰 비율을 초기화했습니다.";
    }

    private void SplitSelectedZone(SplitAxis axis)
    {
        if (!TryGetSingleSelectedZone("분할할 슬롯을 선택하세요.", out var selectedZoneId, out var selectedRect))
        {
            return;
        }

        if (GetZoneRects().Count >= PlaybackTestPlanService.MaxSlotCount)
        {
            DialogStatusTextBlock.Text = $"최대 {PlaybackTestPlanService.MaxSlotCount}개 슬롯까지 만들 수 있습니다.";
            return;
        }

        var newZoneId = GetNextZoneId();
        if (axis == SplitAxis.Vertical)
        {
            SplitZoneVertically(selectedZoneId, selectedRect, newZoneId);
            DialogStatusTextBlock.Text = "선택 슬롯을 좌/우로 세로분할했습니다.";
        }
        else
        {
            SplitZoneHorizontally(selectedZoneId, selectedRect, newZoneId);
            DialogStatusTextBlock.Text = "선택 슬롯을 상/하로 가로분할했습니다.";
        }

        _selectedZoneIds.Clear();
        _selectedZoneIds.Add(newZoneId);
        _hasUnsavedEdits = true;
        RefreshZoneEditorSurface();
    }

    private void SplitZoneVertically(int selectedZoneId, ZoneRect selectedRect, int newZoneId)
    {
        if (selectedRect.W == 1)
        {
            InsertColumn(selectedRect.X + selectedRect.W, selectedZoneId, newZoneId, selectedRect.Y, selectedRect.H);
            return;
        }

        var leftWidth = selectedRect.W / 2;
        var splitX = selectedRect.X + leftWidth;
        for (var y = selectedRect.Y; y < selectedRect.Y + selectedRect.H; y++)
        {
            for (var x = splitX; x < selectedRect.X + selectedRect.W; x++)
            {
                if (_zoneCells[y, x] == selectedZoneId)
                {
                    _zoneCells[y, x] = newZoneId;
                }
            }
        }
    }

    private void SplitZoneHorizontally(int selectedZoneId, ZoneRect selectedRect, int newZoneId)
    {
        if (selectedRect.H == 1)
        {
            InsertRow(selectedRect.Y + selectedRect.H, selectedZoneId, newZoneId, selectedRect.X, selectedRect.W);
            return;
        }

        var topHeight = selectedRect.H / 2;
        var splitY = selectedRect.Y + topHeight;
        for (var y = splitY; y < selectedRect.Y + selectedRect.H; y++)
        {
            for (var x = selectedRect.X; x < selectedRect.X + selectedRect.W; x++)
            {
                if (_zoneCells[y, x] == selectedZoneId)
                {
                    _zoneCells[y, x] = newZoneId;
                }
            }
        }
    }

    private void InsertColumn(int insertX, int selectedZoneId, int newZoneId, int selectedY, int selectedHeight)
    {
        var rows = _zoneCells.GetLength(0);
        var columns = _zoneCells.GetLength(1);
        var nextCells = new int[rows, columns + 1];

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns + 1; x++)
            {
                if (x < insertX)
                {
                    nextCells[y, x] = _zoneCells[y, x];
                }
                else if (x == insertX)
                {
                    nextCells[y, x] = _zoneCells[y, Math.Max(0, insertX - 1)];
                }
                else
                {
                    nextCells[y, x] = _zoneCells[y, x - 1];
                }
            }
        }

        for (var y = selectedY; y < selectedY + selectedHeight; y++)
        {
            if (nextCells[y, insertX] == selectedZoneId)
            {
                nextCells[y, insertX] = newZoneId;
            }
        }

        _zoneCells = nextCells;
        _columnWeights = InsertSplitWeight(_columnWeights, insertX);
    }

    private void InsertRow(int insertY, int selectedZoneId, int newZoneId, int selectedX, int selectedWidth)
    {
        var rows = _zoneCells.GetLength(0);
        var columns = _zoneCells.GetLength(1);
        var nextCells = new int[rows + 1, columns];

        for (var y = 0; y < rows + 1; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                if (y < insertY)
                {
                    nextCells[y, x] = _zoneCells[y, x];
                }
                else if (y == insertY)
                {
                    nextCells[y, x] = _zoneCells[Math.Max(0, insertY - 1), x];
                }
                else
                {
                    nextCells[y, x] = _zoneCells[y - 1, x];
                }
            }
        }

        for (var x = selectedX; x < selectedX + selectedWidth; x++)
        {
            if (nextCells[insertY, x] == selectedZoneId)
            {
                nextCells[insertY, x] = newZoneId;
            }
        }

        _zoneCells = nextCells;
        _rowWeights = InsertSplitWeight(_rowWeights, insertY);
    }

    private void RemoveSelectedZone()
    {
        if (!TryGetSingleSelectedZone("제거할 슬롯을 선택하세요.", out var selectedZoneId, out var selectedRect))
        {
            return;
        }

        if (GetZoneRects().Count <= 1)
        {
            DialogStatusTextBlock.Text = "마지막 슬롯은 제거할 수 없습니다.";
            return;
        }

        var candidates = GetAdjacentZoneCandidates(selectedRect, selectedZoneId);
        foreach (var candidateZoneId in candidates)
        {
            var candidateCells = CloneCells(_zoneCells);
            ReplaceZone(candidateCells, selectedZoneId, candidateZoneId);
            if (TryGetZoneRects(candidateCells, out _))
            {
                _zoneCells = candidateCells;
                _selectedZoneIds.Clear();
                _selectedZoneIds.Add(candidateZoneId);
                _hasUnsavedEdits = true;
                RefreshZoneEditorSurface();
                DialogStatusTextBlock.Text = "선택 슬롯을 제거하고 인접 슬롯을 확장했습니다.";
                return;
            }
        }

        DialogStatusTextBlock.Text = "이 슬롯은 제거 후 직사각형 zone을 유지할 인접 슬롯이 없습니다.";
    }

    private void MergeSelectedZones()
    {
        if (_selectedZoneIds.Count < 2)
        {
            DialogStatusTextBlock.Text = "Ctrl+클릭으로 병합할 슬롯을 둘 이상 선택하세요.";
            return;
        }

        var selectedCells = new List<CellPoint>();
        var targetZoneId = _selectedZoneIds.Min();
        for (var y = 0; y < _zoneCells.GetLength(0); y++)
        {
            for (var x = 0; x < _zoneCells.GetLength(1); x++)
            {
                if (_selectedZoneIds.Contains(_zoneCells[y, x]))
                {
                    selectedCells.Add(new CellPoint(x, y));
                }
            }
        }

        var minX = selectedCells.Min(cell => cell.X);
        var maxX = selectedCells.Max(cell => cell.X);
        var minY = selectedCells.Min(cell => cell.Y);
        var maxY = selectedCells.Max(cell => cell.Y);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (!_selectedZoneIds.Contains(_zoneCells[y, x]))
                {
                    DialogStatusTextBlock.Text = "선택한 슬롯들이 하나의 직사각형을 완전히 채울 때만 병합할 수 있습니다.";
                    return;
                }
            }
        }

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                _zoneCells[y, x] = targetZoneId;
            }
        }

        _selectedZoneIds.Clear();
        _selectedZoneIds.Add(targetZoneId);
        _hasUnsavedEdits = true;
        RefreshZoneEditorSurface();
        DialogStatusTextBlock.Text = "선택한 슬롯을 병합했습니다.";
    }

    private bool TryGetSingleSelectedZone(string emptySelectionMessage, out int selectedZoneId, out ZoneRect selectedRect)
    {
        selectedZoneId = 0;
        selectedRect = default;

        if (_selectedZoneIds.Count == 0)
        {
            DialogStatusTextBlock.Text = emptySelectionMessage;
            return false;
        }

        if (_selectedZoneIds.Count > 1)
        {
            DialogStatusTextBlock.Text = "이 작업은 슬롯을 하나만 선택해야 합니다.";
            return false;
        }

        selectedZoneId = _selectedZoneIds.Single();
        var zoneId = selectedZoneId;
        var rects = GetZoneRects();
        selectedRect = rects.Single(rect => rect.ZoneId == zoneId);
        return true;
    }

    private void LoadCustomLayoutIntoZoneEditor(LayoutPreset layout)
    {
        LoadZoneEditorFromLayout(layout.Name, layout);
        _selectedCustomLayout = layout;
        DialogStatusTextBlock.Text = $"사용자 지정 선택: {layout.Name}";
    }

    private void ResetZoneEditor(string layoutName)
    {
        _zoneCells = new int[1, 1] { { 1 } };
        _columnWeights = [1];
        _rowWeights = [1];
        _selectedZoneIds.Clear();
        _selectedZoneIds.Add(1);
        _editingLayoutName = layoutName;
        _hasUnsavedEdits = false;
        SetLayoutNameText(layoutName);
        RefreshZoneEditorSurface();
    }

    private void LoadZoneEditorFromLayout(string layoutName, LayoutPreset layout)
    {
        _zoneCells = new int[layout.GridRows, layout.GridColumns];
        _columnWeights = NormalizeWeights(layout.ColumnWeights, layout.GridColumns);
        _rowWeights = NormalizeWeights(layout.RowWeights, layout.GridRows);
        foreach (var slot in layout.Slots)
        {
            for (var y = slot.Y; y < slot.Y + slot.H; y++)
            {
                for (var x = slot.X; x < slot.X + slot.W; x++)
                {
                    _zoneCells[y, x] = slot.SlotId;
                }
            }
        }

        _selectedZoneIds.Clear();
        _selectedZoneIds.Add(layout.Slots.OrderBy(slot => slot.SlotId).FirstOrDefault()?.SlotId ?? 1);
        _editingLayoutName = layoutName;
        _hasUnsavedEdits = false;
        SetLayoutNameText(layoutName);
        RefreshZoneEditorSurface();
    }

    private void SetLayoutNameText(string layoutName)
    {
        _isRefreshingLayoutName = true;
        LayoutNameTextBox.Text = layoutName;
        _isRefreshingLayoutName = false;
    }

    private void RefreshZoneEditorSurface()
    {
        CompactRedundantBoundaries();
        NormalizeZoneIdsFromVisualOrder();
        UpdateEditorChrome();

        if (!TryCreateLayoutFromEditor("preview", _editingLayoutName, out var layout, out var error))
        {
            SplitEditorHost.Content = new TextBlock
            {
                Text = error,
                Foreground = new SolidColorBrush(Color.FromRgb(185, 194, 204)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(12)
            };
            return;
        }

        SplitEditorHost.Content = BuildZoneEditorGrid(layout);
    }

    // 헤더 요약, 미저장 표시, 선택 컨텍스트 바의 활성/비활성·툴팁을 현재 상태에 맞춘다.
    private void UpdateEditorChrome()
    {
        // InitializeComponent 도중 TextChanged가 먼저 발생할 수 있어, 컨트롤이 준비되기 전이면 건너뛴다.
        if (VerticalSplitButton is null)
        {
            return;
        }

        var zoneCount = GetZoneRects().Count;
        var columns = _zoneCells.GetLength(1);
        var rows = _zoneCells.GetLength(0);

        LayoutSummaryTextBlock.Text = zoneCount > 0 ? $"{columns}x{rows} · 슬롯 {zoneCount}개" : "";
        UnsavedIndicator.Visibility = _hasUnsavedEdits ? Visibility.Visible : Visibility.Collapsed;

        var selectionCount = _selectedZoneIds.Count;
        SelectionInfoTextBlock.Text = selectionCount == 0
            ? "선택 없음"
            : $"선택: 슬롯 {string.Join(", ", _selectedZoneIds.OrderBy(id => id))}";

        var single = selectionCount == 1;
        var atMax = zoneCount >= PlaybackTestPlanService.MaxSlotCount;
        var splitDisabledReason = !single
            ? "분할하려면 슬롯을 하나만 선택하세요."
            : atMax ? $"최대 {PlaybackTestPlanService.MaxSlotCount}개 슬롯까지 만들 수 있습니다." : null;

        SetActionButton(VerticalSplitButton, splitDisabledReason, "세로 선 기준 좌/우 분할");
        SetActionButton(HorizontalSplitButton, splitDisabledReason, "가로 선 기준 상/하 분할");
        SetActionButton(
            RemoveSelectedSlotButton,
            !single ? "제거하려면 슬롯을 하나만 선택하세요." : zoneCount <= 1 ? "마지막 한 슬롯은 제거할 수 없습니다." : null,
            "선택 슬롯을 제거하고 인접 슬롯을 확장");
        SetActionButton(
            MergeSelectedZonesButton,
            selectionCount < 2 ? "Ctrl+클릭으로 슬롯을 둘 이상 선택하세요." : null,
            "선택한 인접 슬롯을 하나로 병합");
    }

    private static void SetActionButton(Button button, string? disabledReason, string enabledTooltip)
    {
        button.IsEnabled = disabledReason is null;
        button.ToolTip = disabledReason ?? enabledTooltip;
    }

    private FrameworkElement BuildZoneEditorGrid(LayoutPreset layout)
    {
        var canvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _editorCanvas = canvas;
        _editorZoneButtons.Clear();
        _editorColumnSplitters.Clear();
        _editorRowSplitters.Clear();

        foreach (var slot in layout.Slots.OrderBy(slot => slot.SlotId))
        {
            var container = CreateZoneElement(slot.SlotId, out var sizeTextBlock);
            canvas.Children.Add(container);
            _editorZoneButtons.Add((container, slot.SlotId, sizeTextBlock));
        }

        foreach (var segment in LayoutEditorGridGeometry.CreateVerticalSplitterSegments(layout))
        {
            var thumb = CreateSplitter(isVertical: true);
            thumb.Tag = segment.Boundary;
            thumb.DragDelta += ColumnSplitter_DragDelta;
            thumb.DragCompleted += Splitter_DragCompleted;
            canvas.Children.Add(thumb);
            _editorColumnSplitters.Add((thumb, segment));
        }

        foreach (var segment in LayoutEditorGridGeometry.CreateHorizontalSplitterSegments(layout))
        {
            var thumb = CreateSplitter(isVertical: false);
            thumb.Tag = segment.Boundary;
            thumb.DragDelta += RowSplitter_DragDelta;
            thumb.DragCompleted += Splitter_DragCompleted;
            canvas.Children.Add(thumb);
            _editorRowSplitters.Add((thumb, segment));
        }

        RepositionSurface();
        return canvas;
    }

    private static StackPanel CreateZoneButtonContent(int slotId, out TextBlock sizeTextBlock)
    {
        var slotNumberTextBlock = new TextBlock
        {
            Text = slotId.ToString(),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        sizeTextBlock = new TextBlock
        {
            Text = "0x0",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Opacity = 0.78,
            Margin = new Thickness(0, 2, 0, 0),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(slotNumberTextBlock);
        panel.Children.Add(sizeTextBlock);
        return panel;
    }

    // zone 한 칸: 선택용 본체 + 마우스를 올리면 나타나는 분할/제거 핸들.
    private FrameworkElement CreateZoneElement(int slotId, out TextBlock sizeTextBlock)
    {
        var isSelected = _selectedZoneIds.Contains(slotId);

        var zoneBorder = new Border
        {
            Background = LayoutPreviewBuilder.GetSlotBrush(slotId),
            BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(243, 246, 250))
                : new SolidColorBrush(Color.FromRgb(45, 54, 66)),
            BorderThickness = isSelected ? new Thickness(4) : new Thickness(1),
            Child = CreateZoneButtonContent(slotId, out sizeTextBlock)
        };

        var handles = CreateZoneHandles(slotId);

        var container = new Grid
        {
            Tag = slotId,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            ToolTip = "클릭: 선택 (Ctrl+클릭: 다중 선택) · 마우스를 올리면 분할/제거 핸들이 나타납니다."
        };
        container.Children.Add(zoneBorder);
        container.Children.Add(handles);

        container.MouseEnter += (_, _) => handles.Visibility = Visibility.Visible;
        container.MouseLeave += (_, _) => handles.Visibility = Visibility.Collapsed;
        container.MouseLeftButtonUp += Zone_Click;
        return container;
    }

    private UIElement CreateZoneHandles(int slotId)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 5, 0),
            Visibility = Visibility.Collapsed
        };

        panel.Children.Add(CreateHandleButton("⬌", "세로 선 기준 좌/우 분할",
            () => SplitZoneFromHandle(slotId, SplitAxis.Vertical)));
        panel.Children.Add(CreateHandleButton("⬍", "가로 선 기준 상/하 분할",
            () => SplitZoneFromHandle(slotId, SplitAxis.Horizontal)));
        panel.Children.Add(CreateHandleButton("✕", "이 슬롯 제거",
            () => RemoveZoneFromHandle(slotId)));
        return panel;
    }

    private static Button CreateHandleButton(string glyph, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(3, 0, 0, 0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1F, 0x29, 0x37)),
            Foreground = new SolidColorBrush(Color.FromRgb(243, 246, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(243, 246, 250)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = tooltip
        };
        button.Click += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return button;
    }

    private void SplitZoneFromHandle(int slotId, SplitAxis axis)
    {
        SetSingleSelection(slotId);
        SplitSelectedZone(axis);
    }

    private void RemoveZoneFromHandle(int slotId)
    {
        SetSingleSelection(slotId);
        RemoveSelectedZone();
    }

    private void SetSingleSelection(int zoneId)
    {
        _selectedZoneIds.Clear();
        _selectedZoneIds.Add(zoneId);
    }

    private static Thumb CreateSplitter(bool isVertical)
    {
        return new Thumb
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0x9B, 0xC2, 0xCC)),
            BorderThickness = new Thickness(0),
            Cursor = isVertical ? Cursors.SizeWE : Cursors.SizeNS,
            Template = CreateSplitterTemplate()
        };
    }

    private static ControlTemplate CreateSplitterTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(
            Border.BackgroundProperty,
            new System.Windows.Data.Binding
            {
                RelativeSource = new System.Windows.Data.RelativeSource(
                    System.Windows.Data.RelativeSourceMode.TemplatedParent),
                Path = new PropertyPath(nameof(Control.Background))
            });

        return new ControlTemplate(typeof(Thumb)) { VisualTree = border };
    }

    private void SplitEditorHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _editorSurfaceSize = new Size(
            Math.Max(160, e.NewSize.Width),
            Math.Max(120, e.NewSize.Height));
        RepositionSurface();
    }

    private void RepositionSurface()
    {
        if (_editorCanvas is null)
        {
            return;
        }

        var width = Math.Max(160, _editorSurfaceSize.Width);
        var height = Math.Max(120, _editorSurfaceSize.Height);
        _editorCanvas.Width = width;
        _editorCanvas.Height = height;

        var columnOffsets = CumulativeOffsets(_columnWeights, width);
        var rowOffsets = CumulativeOffsets(_rowWeights, height);
        var rects = GetZoneRects().ToDictionary(rect => rect.ZoneId);

        foreach (var (container, slotId, sizeTextBlock) in _editorZoneButtons)
        {
            if (!rects.TryGetValue(slotId, out var rect))
            {
                continue;
            }

            var left = columnOffsets[rect.X];
            var right = columnOffsets[rect.X + rect.W];
            var top = rowOffsets[rect.Y];
            var bottom = rowOffsets[rect.Y + rect.H];
            var slotWidth = Math.Max(0, right - left);
            var slotHeight = Math.Max(0, bottom - top);

            Canvas.SetLeft(container, left + ZoneGap);
            Canvas.SetTop(container, top + ZoneGap);
            container.Width = Math.Max(0, slotWidth - 2 * ZoneGap);
            container.Height = Math.Max(0, slotHeight - 2 * ZoneGap);
            sizeTextBlock.Text = FormatSlotSizeLabel(slotWidth, slotHeight);
        }

        foreach (var (thumb, segment) in _editorColumnSplitters)
        {
            var top = rowOffsets[segment.Start];
            var bottom = rowOffsets[segment.Start + segment.Span];

            Canvas.SetLeft(thumb, columnOffsets[segment.Boundary] - SplitterThickness / 2);
            Canvas.SetTop(thumb, top);
            thumb.Width = SplitterThickness;
            thumb.Height = Math.Max(0, bottom - top);
        }

        foreach (var (thumb, segment) in _editorRowSplitters)
        {
            var left = columnOffsets[segment.Start];
            var right = columnOffsets[segment.Start + segment.Span];

            Canvas.SetLeft(thumb, left);
            Canvas.SetTop(thumb, rowOffsets[segment.Boundary] - SplitterThickness / 2);
            thumb.Width = Math.Max(0, right - left);
            thumb.Height = SplitterThickness;
        }
    }

    private static string FormatSlotSizeLabel(double width, double height)
    {
        var roundedWidth = Math.Max(0, (int)Math.Round(width, MidpointRounding.AwayFromZero));
        var roundedHeight = Math.Max(0, (int)Math.Round(height, MidpointRounding.AwayFromZero));
        return $"{roundedWidth}x{roundedHeight}";
    }

    private static double[] CumulativeOffsets(IReadOnlyList<double> weights, double total)
    {
        var sum = 0.0;
        foreach (var weight in weights)
        {
            sum += weight > 0 ? weight : 0;
        }

        if (sum <= 0)
        {
            sum = Math.Max(1, weights.Count);
        }

        var offsets = new double[weights.Count + 1];
        var accumulated = 0.0;
        for (var index = 0; index < weights.Count; index++)
        {
            offsets[index] = total * accumulated / sum;
            accumulated += weights[index] > 0 ? weights[index] : 0;
        }

        offsets[weights.Count] = total;
        return offsets;
    }

    private void ColumnSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: int boundary } thumb)
        {
            return;
        }

        var total = _columnWeights.Sum(weight => weight > 0 ? weight : 0);
        if (total <= 0)
        {
            return;
        }

        var deltaWeight = e.HorizontalChange / Math.Max(1, _editorSurfaceSize.Width) * total;
        var draggedItem = _editorColumnSplitters
            .FirstOrDefault(item => ReferenceEquals(item.Thumb, thumb));
        if (!ReferenceEquals(draggedItem.Thumb, thumb)
            || !TryGetResizeIndexGroups(
                boundary,
                _columnWeights.Length,
                _editorColumnSplitters
                    .Where(item => !ReferenceEquals(item.Thumb, thumb) && item.Segment.Boundary != boundary)
                    .Select(item => item.Segment.Boundary),
                out var leftColumns,
                out var rightColumns)
            || !TryApplyWeightDelta(_columnWeights, leftColumns, rightColumns, deltaWeight))
        {
            return;
        }

        RepositionSurface();
    }

    private void RowSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: int boundary } thumb)
        {
            return;
        }

        var total = _rowWeights.Sum(weight => weight > 0 ? weight : 0);
        if (total <= 0)
        {
            return;
        }

        var deltaWeight = e.VerticalChange / Math.Max(1, _editorSurfaceSize.Height) * total;
        var draggedItem = _editorRowSplitters
            .FirstOrDefault(item => ReferenceEquals(item.Thumb, thumb));
        if (!ReferenceEquals(draggedItem.Thumb, thumb)
            || !TryGetResizeIndexGroups(
                boundary,
                _rowWeights.Length,
                _editorRowSplitters
                    .Where(item => !ReferenceEquals(item.Thumb, thumb) && item.Segment.Boundary != boundary)
                    .Select(item => item.Segment.Boundary),
                out var topRows,
                out var bottomRows)
            || !TryApplyWeightDelta(_rowWeights, topRows, bottomRows, deltaWeight))
        {
            return;
        }

        RepositionSurface();
    }

    private static bool TryGetResizeIndexGroups(
        int boundary,
        int count,
        IEnumerable<int> anchorBoundaries,
        out int[] leadingIndexes,
        out int[] trailingIndexes)
    {
        leadingIndexes = [];
        trailingIndexes = [];
        if (boundary <= 0 || boundary >= count)
        {
            return false;
        }

        var anchors = anchorBoundaries
            .Where(anchor => anchor > 0 && anchor < count)
            .Distinct()
            .OrderBy(anchor => anchor)
            .ToArray();
        var previousAnchor = anchors.LastOrDefault(anchor => anchor < boundary);
        var nextAnchor = anchors.FirstOrDefault(anchor => anchor > boundary);
        if (nextAnchor == 0)
        {
            nextAnchor = count;
        }

        leadingIndexes = Enumerable.Range(previousAnchor, boundary - previousAnchor).ToArray();
        trailingIndexes = Enumerable.Range(boundary, nextAnchor - boundary).ToArray();
        return leadingIndexes.Length > 0 && trailingIndexes.Length > 0;
    }

    private static bool TryApplyWeightDelta(
        double[] weights,
        IReadOnlyList<int> leadingIndexes,
        IReadOnlyList<int> trailingIndexes,
        double deltaWeight)
    {
        if (Math.Abs(deltaWeight) <= double.Epsilon)
        {
            return false;
        }

        var growIndexes = deltaWeight > 0 ? leadingIndexes : trailingIndexes;
        var shrinkIndexes = deltaWeight > 0 ? trailingIndexes : leadingIndexes;
        var amount = Math.Abs(deltaWeight);
        var available = shrinkIndexes.Sum(index => Math.Max(0, weights[index] - MinWeight));
        if (available <= 0)
        {
            return false;
        }

        var applied = Math.Min(amount, available);
        DistributeWeight(weights, growIndexes, applied);
        DistributeWeight(weights, shrinkIndexes, -applied);
        return true;
    }

    private static void DistributeWeight(double[] weights, IReadOnlyList<int> indexes, double amount)
    {
        if (indexes.Count == 0 || Math.Abs(amount) <= double.Epsilon)
        {
            return;
        }

        if (amount > 0)
        {
            var add = amount / indexes.Count;
            foreach (var index in indexes)
            {
                weights[index] += add;
            }

            return;
        }

        var remaining = -amount;
        foreach (var index in indexes)
        {
            if (remaining <= 0)
            {
                break;
            }

            var available = Math.Max(0, weights[index] - MinWeight);
            var used = Math.Min(available, remaining);
            weights[index] -= used;
            remaining -= used;
        }
    }

    private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _columnWeights = NormalizeWeightAverage(_columnWeights);
        _rowWeights = NormalizeWeightAverage(_rowWeights);
        _hasUnsavedEdits = true;
        RefreshZoneEditorSurface();
        DialogStatusTextBlock.Text = "분할선을 드래그해 열/행 비율을 조정했습니다.";
    }

    private void Zone_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int zoneId })
        {
            return;
        }

        SelectZone(zoneId);
    }

    private void SelectZone(int zoneId)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_selectedZoneIds.Add(zoneId))
            {
                _selectedZoneIds.Remove(zoneId);
            }
        }
        else
        {
            _selectedZoneIds.Clear();
            _selectedZoneIds.Add(zoneId);
        }

        RefreshZoneEditorSurface();
        DialogStatusTextBlock.Text = _selectedZoneIds.Count == 1
            ? $"선택 슬롯: {_selectedZoneIds.Single()}"
            : $"선택 슬롯: {string.Join(", ", _selectedZoneIds.OrderBy(id => id))}";
    }

    private void SaveCustomLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (SaveCustomLayoutFromEditor(out var savedLayout))
        {
            DialogStatusTextBlock.Text = $"사용자 지정 레이아웃 저장됨: {savedLayout.Name}";
        }
    }

    private bool SaveCustomLayoutFromEditor(out LayoutPreset savedLayout)
    {
        savedLayout = null!;
        var selectedCustomLayout = _selectedCustomLayout;
        var layoutName = LayoutNameTextBox.Text;
        var layoutId = selectedCustomLayout?.Id
                       ?? LayoutPresetService.CreateCustomLayoutId(layoutName, _customLayouts.ToArray());

        if (!TryCreateLayoutFromEditor(layoutId, layoutName, out var layout, out var error))
        {
            DialogStatusTextBlock.Text = error;
            return false;
        }

        var nextCustomLayouts = _customLayouts
            .Where(candidate => !candidate.Id.Equals(layout.Id, StringComparison.OrdinalIgnoreCase))
            .Append(layout)
            .OrderBy(candidate => candidate.Name)
            .ToArray();

        try
        {
            LayoutPresetService.Validate(nextCustomLayouts);
            _layoutPresetService.SaveCustomLayouts(nextCustomLayouts);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            DialogStatusTextBlock.Text = ex.Message;
            return false;
        }

        _customLayouts.Clear();
        _customLayouts.AddRange(nextCustomLayouts);
        HasCustomLayoutChanges = true;
        _selectedCustomLayout = layout;
        _editingLayoutName = layout.Name;
        _hasUnsavedEdits = false;
        SetLayoutNameText(layout.Name);
        savedLayout = layout;
        RefreshCustomLayoutList(layout);
        UpdateEditorChrome();

        return true;
    }

    private void SaveAndApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveCustomLayoutFromEditor(out var savedLayout))
        {
            return;
        }

        AppliedLayoutId = savedLayout.Id;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DeleteCustomLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCustomLayout is not LayoutPreset selectedLayout)
        {
            DialogStatusTextBlock.Text = "삭제할 사용자 지정 레이아웃을 선택하세요.";
            return;
        }

        var nextCustomLayouts = _customLayouts
            .Where(layout => !layout.Id.Equals(selectedLayout.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        try
        {
            _layoutPresetService.SaveCustomLayouts(nextCustomLayouts);
        }
        catch (IOException ex)
        {
            DialogStatusTextBlock.Text = ex.Message;
            return;
        }

        _customLayouts.Clear();
        _customLayouts.AddRange(nextCustomLayouts);
        HasCustomLayoutChanges = true;
        _selectedCustomLayout = null;
        RefreshCustomLayoutList();
        ResetZoneEditor(CreateCustomLayoutName());
        DialogStatusTextBlock.Text = $"사용자 지정 레이아웃을 삭제했습니다: {selectedLayout.Name}";

        DialogStatusTextBlock.Text = $"사용자 지정 레이아웃 삭제됨: {selectedLayout.Name}";
    }

    private bool TryCreateLayoutFromEditor(string layoutId, string layoutName, out LayoutPreset layout, out string error)
    {
        layout = null!;
        error = "";
        var name = layoutName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = CreateCustomLayoutName();
        }

        if (!TryGetZoneRects(_zoneCells, out var rects))
        {
            error = "모든 zone은 하나의 직사각형이어야 합니다.";
            return false;
        }

        layout = new LayoutPreset
        {
            Id = layoutId,
            Name = name,
            GridColumns = _zoneCells.GetLength(1),
            GridRows = _zoneCells.GetLength(0),
            ColumnWeights = NormalizeWeights(_columnWeights, _zoneCells.GetLength(1)),
            RowWeights = NormalizeWeights(_rowWeights, _zoneCells.GetLength(0)),
            Slots = rects
                .OrderBy(rect => rect.ZoneId)
                .Select(rect => new LayoutSlot
                {
                    SlotId = rect.ZoneId,
                    X = rect.X,
                    Y = rect.Y,
                    W = rect.W,
                    H = rect.H
                })
                .ToArray()
        };

        try
        {
            LayoutPresetService.Validate([layout]);
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }

        return true;
    }

    // '비율 초기화': 분할 구조는 그대로 두고, 현재 레이아웃 디자인에 맞춰 균형 비율을 계산한다.
    // 각 트랙의 가중치 = 1 / (그 트랙에 걸친 zone들의 최대 span).
    // 예) 한 zone이 2행에 걸치면 그 두 행은 각각 1/2이 되어, 큰 화면이 작은 화면 2개 합과 같아진다.
    internal static (double[] ColumnWeights, double[] RowWeights) ComputeDesignBalancedWeights(int[,] cells)
    {
        var columns = cells.GetLength(1);
        var rows = cells.GetLength(0);
        if (!TryGetZoneRects(cells, out var rects))
        {
            return (CreateDefaultWeights(columns), CreateDefaultWeights(rows));
        }

        var columnWeights = new double[columns];
        for (var x = 0; x < columns; x++)
        {
            var maxWidth = 1;
            foreach (var rect in rects)
            {
                if (rect.X <= x && x < rect.X + rect.W && rect.W > maxWidth)
                {
                    maxWidth = rect.W;
                }
            }

            columnWeights[x] = 1.0 / maxWidth;
        }

        var rowWeights = new double[rows];
        for (var y = 0; y < rows; y++)
        {
            var maxHeight = 1;
            foreach (var rect in rects)
            {
                if (rect.Y <= y && y < rect.Y + rect.H && rect.H > maxHeight)
                {
                    maxHeight = rect.H;
                }
            }

            rowWeights[y] = 1.0 / maxHeight;
        }

        return (NormalizeWeightAverage(columnWeights), NormalizeWeightAverage(rowWeights));
    }

    // 병합/제거 등으로 생긴, 어떤 zone도 가르지 않는 중복 경계선을 없애 그리드를 최소화한다.
    // 시각 비율은 보존(접히는 트랙의 가중치를 이웃에 합산)하면서 행/열 개수만 줄여,
    // '비율 초기화'가 항상 현재 레이아웃 디자인 기준으로 동작하게 한다.
    private void CompactRedundantBoundaries()
    {
        var (cells, columnWeights, rowWeights) =
            CompactRedundantTracks(_zoneCells, _columnWeights, _rowWeights);
        _zoneCells = cells;
        _columnWeights = columnWeights;
        _rowWeights = rowWeights;
    }

    internal static (int[,] Cells, double[] ColumnWeights, double[] RowWeights) CompactRedundantTracks(
        int[,] cells, double[] columnWeights, double[] rowWeights)
    {
        var (columnCells, compactedColumnWeights) = CompactColumns(cells, columnWeights);
        var (rowCells, compactedRowWeights) = CompactRows(columnCells, rowWeights);
        return (rowCells, compactedColumnWeights, compactedRowWeights);
    }

    private static (int[,] Cells, double[] Weights) CompactColumns(int[,] cells, double[] weights)
    {
        var rows = cells.GetLength(0);
        var columns = cells.GetLength(1);
        if (columns <= 1)
        {
            return (cells, weights);
        }

        // 첫 열은 항상 유지. 경계선 x는 어떤 행에서든 좌/우 zone이 다르면 '실제' 경계다.
        var keep = new bool[columns];
        keep[0] = true;
        for (var x = 1; x < columns; x++)
        {
            for (var y = 0; y < rows; y++)
            {
                if (cells[y, x] != cells[y, x - 1])
                {
                    keep[x] = true;
                    break;
                }
            }
        }

        var keptColumns = keep.Count(value => value);
        if (keptColumns == columns)
        {
            return (cells, weights);
        }

        var columnMap = new int[columns];
        var newWeights = new double[keptColumns];
        var target = -1;
        for (var x = 0; x < columns; x++)
        {
            if (keep[x])
            {
                target++;
            }

            columnMap[x] = target;
            newWeights[target] += x < weights.Length && weights[x] > 0 ? weights[x] : 0;
        }

        var newCells = new int[rows, keptColumns];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                newCells[y, columnMap[x]] = cells[y, x];
            }
        }

        return (newCells, NormalizeWeightAverage(newWeights));
    }

    private static (int[,] Cells, double[] Weights) CompactRows(int[,] cells, double[] weights)
    {
        var rows = cells.GetLength(0);
        var columns = cells.GetLength(1);
        if (rows <= 1)
        {
            return (cells, weights);
        }

        var keep = new bool[rows];
        keep[0] = true;
        for (var y = 1; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                if (cells[y, x] != cells[y - 1, x])
                {
                    keep[y] = true;
                    break;
                }
            }
        }

        var keptRows = keep.Count(value => value);
        if (keptRows == rows)
        {
            return (cells, weights);
        }

        var rowMap = new int[rows];
        var newWeights = new double[keptRows];
        var target = -1;
        for (var y = 0; y < rows; y++)
        {
            if (keep[y])
            {
                target++;
            }

            rowMap[y] = target;
            newWeights[target] += y < weights.Length && weights[y] > 0 ? weights[y] : 0;
        }

        var newCells = new int[keptRows, columns];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                newCells[rowMap[y], x] = cells[y, x];
            }
        }

        return (newCells, NormalizeWeightAverage(newWeights));
    }

    private void NormalizeZoneIdsFromVisualOrder()
    {
        if (!TryGetZoneRects(_zoneCells, out var rects))
        {
            return;
        }

        var selectedZoneIds = _selectedZoneIds.ToHashSet();
        var mappedSelection = new HashSet<int>();
        var zoneIdMap = rects
            .OrderBy(rect => rect.Y)
            .ThenBy(rect => rect.X)
            .Select((rect, index) => new { rect.ZoneId, NewZoneId = index + 1 })
            .ToDictionary(item => item.ZoneId, item => item.NewZoneId);

        for (var y = 0; y < _zoneCells.GetLength(0); y++)
        {
            for (var x = 0; x < _zoneCells.GetLength(1); x++)
            {
                var oldZoneId = _zoneCells[y, x];
                var newZoneId = zoneIdMap[oldZoneId];
                _zoneCells[y, x] = newZoneId;

                if (selectedZoneIds.Contains(oldZoneId))
                {
                    mappedSelection.Add(newZoneId);
                }
            }
        }

        _selectedZoneIds.Clear();
        foreach (var zoneId in mappedSelection)
        {
            _selectedZoneIds.Add(zoneId);
        }

        if (_selectedZoneIds.Count == 0 && zoneIdMap.Count > 0)
        {
            _selectedZoneIds.Add(1);
        }
    }

    private IReadOnlyList<ZoneRect> GetZoneRects()
    {
        return TryGetZoneRects(_zoneCells, out var rects) ? rects : [];
    }

    private static bool TryGetZoneRects(int[,] cells, out IReadOnlyList<ZoneRect> rects)
    {
        var rows = cells.GetLength(0);
        var columns = cells.GetLength(1);
        var zoneIds = new SortedSet<int>();

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                if (cells[y, x] <= 0)
                {
                    rects = [];
                    return false;
                }

                zoneIds.Add(cells[y, x]);
            }
        }

        var zoneRects = new List<ZoneRect>();
        foreach (var zoneId in zoneIds)
        {
            var zoneCells = new List<CellPoint>();
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    if (cells[y, x] == zoneId)
                    {
                        zoneCells.Add(new CellPoint(x, y));
                    }
                }
            }

            var minX = zoneCells.Min(cell => cell.X);
            var maxX = zoneCells.Max(cell => cell.X);
            var minY = zoneCells.Min(cell => cell.Y);
            var maxY = zoneCells.Max(cell => cell.Y);

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    if (cells[y, x] != zoneId)
                    {
                        rects = [];
                        return false;
                    }
                }
            }

            zoneRects.Add(new ZoneRect(zoneId, minX, minY, maxX - minX + 1, maxY - minY + 1));
        }

        rects = zoneRects;
        return true;
    }

    private IEnumerable<int> GetAdjacentZoneCandidates(ZoneRect selectedRect, int selectedZoneId)
    {
        var rows = _zoneCells.GetLength(0);
        var columns = _zoneCells.GetLength(1);
        var scores = new Dictionary<int, int>();

        void AddCandidate(int x, int y)
        {
            if (x < 0 || y < 0 || x >= columns || y >= rows)
            {
                return;
            }

            var zoneId = _zoneCells[y, x];
            if (zoneId == selectedZoneId)
            {
                return;
            }

            scores[zoneId] = scores.GetValueOrDefault(zoneId) + 1;
        }

        for (var y = selectedRect.Y; y < selectedRect.Y + selectedRect.H; y++)
        {
            AddCandidate(selectedRect.X - 1, y);
            AddCandidate(selectedRect.X + selectedRect.W, y);
        }

        for (var x = selectedRect.X; x < selectedRect.X + selectedRect.W; x++)
        {
            AddCandidate(x, selectedRect.Y - 1);
            AddCandidate(x, selectedRect.Y + selectedRect.H);
        }

        return scores
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key)
            .Select(item => item.Key)
            .ToArray();
    }

    private static int[,] CloneCells(int[,] cells)
    {
        var rows = cells.GetLength(0);
        var columns = cells.GetLength(1);
        var clonedCells = new int[rows, columns];

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                clonedCells[y, x] = cells[y, x];
            }
        }

        return clonedCells;
    }

    private static void ReplaceZone(int[,] cells, int sourceZoneId, int targetZoneId)
    {
        for (var y = 0; y < cells.GetLength(0); y++)
        {
            for (var x = 0; x < cells.GetLength(1); x++)
            {
                if (cells[y, x] == sourceZoneId)
                {
                    cells[y, x] = targetZoneId;
                }
            }
        }
    }

    private static double[] InsertSplitWeight(double[] sourceWeights, int insertIndex)
    {
        var sourceIndex = Math.Clamp(insertIndex - 1, 0, sourceWeights.Length - 1);
        var splitWeight = Math.Max(MinWeight, sourceWeights[sourceIndex] / 2);
        var nextWeights = new double[sourceWeights.Length + 1];

        for (var index = 0; index < nextWeights.Length; index++)
        {
            if (index < insertIndex)
            {
                nextWeights[index] = sourceWeights[index];
            }
            else if (index == insertIndex)
            {
                nextWeights[index] = splitWeight;
            }
            else
            {
                nextWeights[index] = sourceWeights[index - 1];
            }
        }

        nextWeights[sourceIndex] = splitWeight;
        return NormalizeWeightAverage(nextWeights);
    }

    private static double[] NormalizeWeights(IReadOnlyList<double>? weights, int count)
    {
        if (weights is null || weights.Count != count)
        {
            return CreateDefaultWeights(count);
        }

        var normalizedWeights = weights
            .Select(weight => double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0 ? 1 : weight)
            .ToArray();

        return NormalizeWeightAverage(normalizedWeights);
    }

    private static double[] CreateDefaultWeights(int count)
    {
        return Enumerable.Repeat(1d, Math.Max(1, count)).ToArray();
    }

    private static double[] NormalizeWeightAverage(double[] weights)
    {
        if (weights.Length == 0)
        {
            return [1];
        }

        var sum = weights.Sum();
        if (sum <= 0)
        {
            return CreateDefaultWeights(weights.Length);
        }

        var scale = weights.Length / sum;
        return weights
            .Select(weight => Math.Max(MinWeight, weight * scale))
            .ToArray();
    }

    private int GetNextZoneId()
    {
        return GetZoneRects().Max(rect => rect.ZoneId) + 1;
    }

    private static FrameworkElement BuildLayoutPreview(
        LayoutPreset layout,
        double width,
        double height,
        bool showSlotNumbers)
    {
        return LayoutPreviewBuilder.Build(layout, width, height, showSlotNumbers);
    }

    private enum SplitAxis
    {
        Vertical,
        Horizontal
    }

    private readonly record struct CellPoint(int X, int Y);

    private readonly record struct ZoneRect(int ZoneId, int X, int Y, int W, int H);
}
