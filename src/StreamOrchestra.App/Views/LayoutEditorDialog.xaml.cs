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
    private const double WeightStep = 0.2;
    private const double MinWeight = 0.2;
    private const double ZoneGap = 3;
    private const double SplitterThickness = 8;
    private const double SplitDragThreshold = 8;

    private static readonly string[] SlotColorValues =
    [
        "#2F80ED",
        "#EB5757",
        "#27AE60",
        "#F2C94C",
        "#9B51E0",
        "#56CCF2",
        "#F2994A",
        "#6FCF97",
        "#BB6BD9",
        "#219653",
        "#2D9CDB",
        "#F24E1E",
        "#BDBDBD",
        "#00A896",
        "#FF6B6B",
        "#7B61FF"
    ];

    private readonly LayoutPresetService _layoutPresetService;
    private readonly List<LayoutPreset> _templateLayouts;
    private readonly List<LayoutPreset> _customLayouts;
    private readonly List<LayoutPreset> _allLayouts;
    private int[,] _zoneCells = new int[1, 1] { { 1 } };
    private double[] _columnWeights = [1];
    private double[] _rowWeights = [1];
    private readonly HashSet<int> _selectedZoneIds = new();
    private bool _isRefreshingEditor;
    private LayoutPreset? _selectedTemplate;

    private Canvas? _editorCanvas;
    private readonly List<(Button Button, int SlotId)> _editorZoneButtons = new();
    private readonly List<(Thumb Thumb, int Boundary)> _editorColumnSplitters = new();
    private readonly List<(Thumb Thumb, int Boundary)> _editorRowSplitters = new();
    private Size _editorSurfaceSize = new(760, 440);
    private bool _zonePointerDown;
    private bool _zoneDragSplitting;
    private Point _zoneDragStart;

    public LayoutEditorDialog(
        LayoutPresetService layoutPresetService,
        IReadOnlyList<LayoutPreset> templateLayouts,
        IReadOnlyList<LayoutPreset> customLayouts,
        IReadOnlyList<LayoutPreset> allLayouts,
        LayoutPreset? currentLayout)
    {
        _layoutPresetService = layoutPresetService;
        _templateLayouts = templateLayouts.ToList();
        _customLayouts = customLayouts.ToList();
        _allLayouts = allLayouts.ToList();

        InitializeComponent();
        SplitEditorHost.SizeChanged += SplitEditorHost_SizeChanged;
        RefreshTemplateList();
        RefreshCustomLayoutList();
        ResetZoneEditor("Custom Layout");

        var selectedLayout = currentLayout is null
            ? _templateLayouts.FirstOrDefault()
            : _allLayouts.FirstOrDefault(layout => layout.Id.Equals(currentLayout.Id, StringComparison.OrdinalIgnoreCase))
              ?? _templateLayouts.FirstOrDefault();

        if (selectedLayout is null)
        {
            return;
        }

        if (_templateLayouts.Any(layout => layout.Id.Equals(selectedLayout.Id, StringComparison.OrdinalIgnoreCase)))
        {
            SelectTemplate(selectedLayout);
            return;
        }

        EditorTabControl.SelectedIndex = 1;
        RefreshCustomLayoutList(selectedLayout);
        LoadCustomLayoutIntoZoneEditor(selectedLayout);
    }

    public bool HasCustomLayoutChanges { get; private set; }

    public LayoutPreset? SelectedLayout { get; private set; }

    private void RefreshTemplateList()
    {
        TemplateListPanel.Children.Clear();

        foreach (var layout in _templateLayouts)
        {
            var button = new Button
            {
                Tag = layout,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 54, 66)),
                Foreground = Brushes.White
            };
            button.Click += TemplateButton_Click;

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
            panel.Children.Add(BuildLayoutPreview(layout, 240, 104, showSlotNumbers: false));

            button.Content = panel;
            TemplateListPanel.Children.Add(button);
        }
    }

    private void RefreshCustomLayoutList(LayoutPreset? selectedLayout = null)
    {
        var orderedLayouts = _customLayouts
            .OrderBy(layout => layout.Name)
            .ToArray();

        _isRefreshingEditor = true;
        CustomLayoutListBox.ItemsSource = null;
        CustomLayoutListBox.ItemsSource = orderedLayouts;
        _isRefreshingEditor = false;

        if (selectedLayout is not null)
        {
            CustomLayoutListBox.SelectedItem = orderedLayouts.FirstOrDefault(layout =>
                layout.Id.Equals(selectedLayout.Id, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void TemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LayoutPreset layout })
        {
            SelectTemplate(layout);
        }
    }

    private void SelectTemplate(LayoutPreset layout)
    {
        _selectedTemplate = layout;
        SelectedLayout = layout;
        TemplatePreviewTitleTextBlock.Text =
            $"{layout.Name} ({layout.GridColumns}x{layout.GridRows}, {layout.Slots.Count} slots)";
        TemplatePreviewHost.Content = BuildLayoutPreview(layout, 620, 520, showSlotNumbers: true);
        DialogStatusTextBlock.Text = $"선택: {layout.Name}";
    }

    private void CustomLayoutListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingEditor)
        {
            return;
        }

        if (CustomLayoutListBox.SelectedItem is LayoutPreset layout)
        {
            LoadCustomLayoutIntoZoneEditor(layout);
        }
    }

    private void CopyTemplateToCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTemplate is null)
        {
            DialogStatusTextBlock.Text = "복사할 템플릿을 선택하세요.";
            return;
        }

        _isRefreshingEditor = true;
        CustomLayoutListBox.SelectedItem = null;
        _isRefreshingEditor = false;

        EditorTabControl.SelectedIndex = 1;
        LoadZoneEditorFromLayout($"{_selectedTemplate.Name} Custom", _selectedTemplate);
        DialogStatusTextBlock.Text = "템플릿을 사용자 지정 편집기로 복사했습니다.";
    }

    private void NewCustomLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _isRefreshingEditor = true;
        CustomLayoutListBox.SelectedItem = null;
        _isRefreshingEditor = false;
        ResetZoneEditor("Custom Layout");
        DialogStatusTextBlock.Text = "새 사용자 지정 레이아웃을 시작했습니다.";
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

    private void DecreaseWidthButton_Click(object sender, RoutedEventArgs e)
    {
        AdjustSelectedZoneWidth(-WeightStep);
    }

    private void IncreaseWidthButton_Click(object sender, RoutedEventArgs e)
    {
        AdjustSelectedZoneWidth(WeightStep);
    }

    private void DecreaseHeightButton_Click(object sender, RoutedEventArgs e)
    {
        AdjustSelectedZoneHeight(-WeightStep);
    }

    private void IncreaseHeightButton_Click(object sender, RoutedEventArgs e)
    {
        AdjustSelectedZoneHeight(WeightStep);
    }

    private void ResetZoneSizeButton_Click(object sender, RoutedEventArgs e)
    {
        _columnWeights = CreateDefaultWeights(_zoneCells.GetLength(1));
        _rowWeights = CreateDefaultWeights(_zoneCells.GetLength(0));
        RefreshZoneEditorSurface();
        DialogStatusTextBlock.Text = "열/행 비율을 균등하게 초기화했습니다.";
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
        RefreshZoneEditorSurface();
        DialogStatusTextBlock.Text = "선택한 슬롯을 병합했습니다.";
    }

    private void AdjustSelectedZoneWidth(double delta)
    {
        if (!TryGetSingleSelectedZone("크기를 조절할 슬롯을 선택하세요.", out _, out var selectedRect))
        {
            return;
        }

        if (selectedRect.W >= _columnWeights.Length)
        {
            DialogStatusTextBlock.Text = "선택 슬롯이 전체 폭을 차지하고 있어 폭 비율을 조절할 기준 열이 없습니다.";
            return;
        }

        _columnWeights = AdjustWeights(_columnWeights, selectedRect.X, selectedRect.W, delta);
        RefreshZoneEditorSurface();
        DialogStatusTextBlock.Text = delta > 0
            ? "선택 슬롯의 폭 비율을 키웠습니다."
            : "선택 슬롯의 폭 비율을 줄였습니다.";
    }

    private void AdjustSelectedZoneHeight(double delta)
    {
        if (!TryGetSingleSelectedZone("크기를 조절할 슬롯을 선택하세요.", out _, out var selectedRect))
        {
            return;
        }

        if (selectedRect.H >= _rowWeights.Length)
        {
            DialogStatusTextBlock.Text = "선택 슬롯이 전체 높이를 차지하고 있어 높이 비율을 조절할 기준 행이 없습니다.";
            return;
        }

        _rowWeights = AdjustWeights(_rowWeights, selectedRect.Y, selectedRect.H, delta);
        RefreshZoneEditorSurface();
        DialogStatusTextBlock.Text = delta > 0
            ? "선택 슬롯의 높이 비율을 키웠습니다."
            : "선택 슬롯의 높이 비율을 줄였습니다.";
    }

    private static double[] AdjustWeights(double[] sourceWeights, int start, int count, double delta)
    {
        var weights = sourceWeights.ToArray();
        for (var index = start; index < start + count && index < weights.Length; index++)
        {
            weights[index] = Math.Max(MinWeight, weights[index] + delta);
        }

        return NormalizeWeightAverage(weights);
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
        SelectedLayout = layout;
        DialogStatusTextBlock.Text = $"사용자 지정 선택: {layout.Name}";
    }

    private void ResetZoneEditor(string layoutName)
    {
        _zoneCells = new int[1, 1] { { 1 } };
        _columnWeights = [1];
        _rowWeights = [1];
        _selectedZoneIds.Clear();
        _selectedZoneIds.Add(1);
        LayoutNameTextBox.Text = layoutName;
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
        LayoutNameTextBox.Text = layoutName;
        RefreshZoneEditorSurface();
    }

    private void RefreshZoneEditorSurface()
    {
        NormalizeZoneIdsFromVisualOrder();

        if (!TryCreateLayoutFromEditor("preview", out var layout, out var error))
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
            EditorPreviewHost.Content = null;
            return;
        }

        SplitEditorHost.Content = BuildZoneEditorGrid(layout);
        EditorPreviewHost.Content = BuildLayoutPreview(layout, 260, 420, showSlotNumbers: true);
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
            var isSelected = _selectedZoneIds.Contains(slot.SlotId);
            var button = new Button
            {
                Tag = slot.SlotId,
                Content = slot.SlotId.ToString(),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                Background = GetSlotBrush(slot.SlotId),
                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(243, 246, 250))
                    : new SolidColorBrush(Color.FromRgb(45, 54, 66)),
                BorderThickness = isSelected ? new Thickness(4) : new Thickness(1),
                Cursor = Cursors.Cross,
                ToolTip = "클릭: 선택 · 내부 드래그: 드래그 방향으로 분할"
            };
            button.PreviewMouseLeftButtonDown += Zone_PreviewMouseLeftButtonDown;
            button.PreviewMouseMove += Zone_PreviewMouseMove;
            button.PreviewMouseLeftButtonUp += Zone_PreviewMouseLeftButtonUp;
            canvas.Children.Add(button);
            _editorZoneButtons.Add((button, slot.SlotId));
        }

        for (var boundary = 1; boundary < layout.GridColumns; boundary++)
        {
            var thumb = CreateSplitter(isVertical: true);
            thumb.Tag = boundary;
            thumb.DragDelta += ColumnSplitter_DragDelta;
            thumb.DragCompleted += Splitter_DragCompleted;
            canvas.Children.Add(thumb);
            _editorColumnSplitters.Add((thumb, boundary));
        }

        for (var boundary = 1; boundary < layout.GridRows; boundary++)
        {
            var thumb = CreateSplitter(isVertical: false);
            thumb.Tag = boundary;
            thumb.DragDelta += RowSplitter_DragDelta;
            thumb.DragCompleted += Splitter_DragCompleted;
            canvas.Children.Add(thumb);
            _editorRowSplitters.Add((thumb, boundary));
        }

        RepositionSurface();
        return canvas;
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

        foreach (var (button, slotId) in _editorZoneButtons)
        {
            if (!rects.TryGetValue(slotId, out var rect))
            {
                continue;
            }

            var left = columnOffsets[rect.X];
            var right = columnOffsets[rect.X + rect.W];
            var top = rowOffsets[rect.Y];
            var bottom = rowOffsets[rect.Y + rect.H];

            Canvas.SetLeft(button, left + ZoneGap);
            Canvas.SetTop(button, top + ZoneGap);
            button.Width = Math.Max(0, right - left - 2 * ZoneGap);
            button.Height = Math.Max(0, bottom - top - 2 * ZoneGap);
        }

        foreach (var (thumb, boundary) in _editorColumnSplitters)
        {
            Canvas.SetLeft(thumb, columnOffsets[boundary] - SplitterThickness / 2);
            Canvas.SetTop(thumb, 0);
            thumb.Width = SplitterThickness;
            thumb.Height = height;
        }

        foreach (var (thumb, boundary) in _editorRowSplitters)
        {
            Canvas.SetLeft(thumb, 0);
            Canvas.SetTop(thumb, rowOffsets[boundary] - SplitterThickness / 2);
            thumb.Width = width;
            thumb.Height = SplitterThickness;
        }
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
        if (sender is not Thumb { Tag: int boundary })
        {
            return;
        }

        var total = _columnWeights.Sum(weight => weight > 0 ? weight : 0);
        if (total <= 0)
        {
            return;
        }

        var deltaWeight = e.HorizontalChange / Math.Max(1, _editorSurfaceSize.Width) * total;
        var left = _columnWeights[boundary - 1] + deltaWeight;
        var right = _columnWeights[boundary] - deltaWeight;
        if (left < MinWeight || right < MinWeight)
        {
            return;
        }

        _columnWeights[boundary - 1] = left;
        _columnWeights[boundary] = right;
        RepositionSurface();
    }

    private void RowSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: int boundary })
        {
            return;
        }

        var total = _rowWeights.Sum(weight => weight > 0 ? weight : 0);
        if (total <= 0)
        {
            return;
        }

        var deltaWeight = e.VerticalChange / Math.Max(1, _editorSurfaceSize.Height) * total;
        var top = _rowWeights[boundary - 1] + deltaWeight;
        var bottom = _rowWeights[boundary] - deltaWeight;
        if (top < MinWeight || bottom < MinWeight)
        {
            return;
        }

        _rowWeights[boundary - 1] = top;
        _rowWeights[boundary] = bottom;
        RepositionSurface();
    }

    private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _columnWeights = NormalizeWeightAverage(_columnWeights);
        _rowWeights = NormalizeWeightAverage(_rowWeights);
        RefreshZoneEditorSurface();
        DialogStatusTextBlock.Text = "분할선을 드래그해 열/행 비율을 조정했습니다.";
    }

    private void Zone_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || _editorCanvas is null)
        {
            return;
        }

        _zonePointerDown = true;
        _zoneDragSplitting = false;
        _zoneDragStart = e.GetPosition(_editorCanvas);
        button.CaptureMouse();
    }

    private void Zone_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_zonePointerDown || _editorCanvas is null)
        {
            return;
        }

        var current = e.GetPosition(_editorCanvas);
        if (Math.Abs(current.X - _zoneDragStart.X) > SplitDragThreshold ||
            Math.Abs(current.Y - _zoneDragStart.Y) > SplitDragThreshold)
        {
            _zoneDragSplitting = true;
        }
    }

    private void Zone_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: int slotId } button || _editorCanvas is null)
        {
            return;
        }

        button.ReleaseMouseCapture();
        if (!_zonePointerDown)
        {
            return;
        }

        _zonePointerDown = false;
        e.Handled = true;

        if (_zoneDragSplitting)
        {
            PerformDragSplit(slotId, _zoneDragStart, e.GetPosition(_editorCanvas));
        }
        else
        {
            SelectZone(slotId);
        }
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

    private void PerformDragSplit(int slotId, Point start, Point end)
    {
        var rect = GetZoneRects().FirstOrDefault(candidate => candidate.ZoneId == slotId);
        if (rect.ZoneId == 0)
        {
            return;
        }

        if (GetZoneRects().Count >= PlaybackTestPlanService.MaxSlotCount)
        {
            DialogStatusTextBlock.Text = $"최대 {PlaybackTestPlanService.MaxSlotCount}개 슬롯까지 만들 수 있습니다.";
            return;
        }

        var width = Math.Max(1, _editorSurfaceSize.Width);
        var height = Math.Max(1, _editorSurfaceSize.Height);
        var columnOffsets = CumulativeOffsets(_columnWeights, width);
        var rowOffsets = CumulativeOffsets(_rowWeights, height);
        var left = columnOffsets[rect.X];
        var right = columnOffsets[rect.X + rect.W];
        var top = rowOffsets[rect.Y];
        var bottom = rowOffsets[rect.Y + rect.H];

        var newZoneId = GetNextZoneId();
        if (Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y))
        {
            var fraction = (end.X - left) / Math.Max(1, right - left);
            InsertVerticalSplit(rect, fraction, newZoneId);
            DialogStatusTextBlock.Text = "드래그 위치를 기준으로 세로 분할했습니다.";
        }
        else
        {
            var fraction = (end.Y - top) / Math.Max(1, bottom - top);
            InsertHorizontalSplit(rect, fraction, newZoneId);
            DialogStatusTextBlock.Text = "드래그 위치를 기준으로 가로 분할했습니다.";
        }

        _selectedZoneIds.Clear();
        _selectedZoneIds.Add(newZoneId);
        RefreshZoneEditorSurface();
    }

    private void InsertVerticalSplit(ZoneRect rect, double fraction, int newZoneId)
    {
        fraction = Math.Clamp(fraction, 0.05, 0.95);
        var columns = _zoneCells.GetLength(1);
        var rows = _zoneCells.GetLength(0);

        var zoneWeight = 0.0;
        for (var x = rect.X; x < rect.X + rect.W; x++)
        {
            zoneWeight += _columnWeights[x] > 0 ? _columnWeights[x] : 1;
        }

        var target = fraction * zoneWeight;
        var accumulated = 0.0;
        var splitColumn = rect.X + rect.W - 1;
        var within = 0.5;
        for (var x = rect.X; x < rect.X + rect.W; x++)
        {
            var weight = _columnWeights[x] > 0 ? _columnWeights[x] : 1;
            if (target <= accumulated + weight)
            {
                splitColumn = x;
                within = (target - accumulated) / weight;
                break;
            }

            accumulated += weight;
        }

        within = Math.Clamp(within, 0.05, 0.95);

        var nextCells = new int[rows, columns + 1];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x <= columns; x++)
            {
                if (x <= splitColumn)
                {
                    nextCells[y, x] = _zoneCells[y, x];
                }
                else if (x == splitColumn + 1)
                {
                    nextCells[y, x] = _zoneCells[y, splitColumn];
                }
                else
                {
                    nextCells[y, x] = _zoneCells[y, x - 1];
                }
            }
        }

        for (var y = rect.Y; y < rect.Y + rect.H; y++)
        {
            for (var x = splitColumn + 1; x <= columns; x++)
            {
                if (nextCells[y, x] == rect.ZoneId)
                {
                    nextCells[y, x] = newZoneId;
                }
            }
        }

        var sourceWeight = _columnWeights[splitColumn] > 0 ? _columnWeights[splitColumn] : 1;
        var nextWeights = new double[columns + 1];
        for (var x = 0; x <= columns; x++)
        {
            if (x < splitColumn)
            {
                nextWeights[x] = _columnWeights[x];
            }
            else if (x == splitColumn)
            {
                nextWeights[x] = sourceWeight * within;
            }
            else if (x == splitColumn + 1)
            {
                nextWeights[x] = sourceWeight * (1 - within);
            }
            else
            {
                nextWeights[x] = _columnWeights[x - 1];
            }
        }

        _zoneCells = nextCells;
        _columnWeights = NormalizeWeightAverage(nextWeights);
    }

    private void InsertHorizontalSplit(ZoneRect rect, double fraction, int newZoneId)
    {
        fraction = Math.Clamp(fraction, 0.05, 0.95);
        var columns = _zoneCells.GetLength(1);
        var rows = _zoneCells.GetLength(0);

        var zoneWeight = 0.0;
        for (var y = rect.Y; y < rect.Y + rect.H; y++)
        {
            zoneWeight += _rowWeights[y] > 0 ? _rowWeights[y] : 1;
        }

        var target = fraction * zoneWeight;
        var accumulated = 0.0;
        var splitRow = rect.Y + rect.H - 1;
        var within = 0.5;
        for (var y = rect.Y; y < rect.Y + rect.H; y++)
        {
            var weight = _rowWeights[y] > 0 ? _rowWeights[y] : 1;
            if (target <= accumulated + weight)
            {
                splitRow = y;
                within = (target - accumulated) / weight;
                break;
            }

            accumulated += weight;
        }

        within = Math.Clamp(within, 0.05, 0.95);

        var nextCells = new int[rows + 1, columns];
        for (var y = 0; y <= rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                if (y <= splitRow)
                {
                    nextCells[y, x] = _zoneCells[y, x];
                }
                else if (y == splitRow + 1)
                {
                    nextCells[y, x] = _zoneCells[splitRow, x];
                }
                else
                {
                    nextCells[y, x] = _zoneCells[y - 1, x];
                }
            }
        }

        for (var y = splitRow + 1; y <= rows; y++)
        {
            for (var x = rect.X; x < rect.X + rect.W; x++)
            {
                if (nextCells[y, x] == rect.ZoneId)
                {
                    nextCells[y, x] = newZoneId;
                }
            }
        }

        var sourceWeight = _rowWeights[splitRow] > 0 ? _rowWeights[splitRow] : 1;
        var nextWeights = new double[rows + 1];
        for (var y = 0; y <= rows; y++)
        {
            if (y < splitRow)
            {
                nextWeights[y] = _rowWeights[y];
            }
            else if (y == splitRow)
            {
                nextWeights[y] = sourceWeight * within;
            }
            else if (y == splitRow + 1)
            {
                nextWeights[y] = sourceWeight * (1 - within);
            }
            else
            {
                nextWeights[y] = _rowWeights[y - 1];
            }
        }

        _zoneCells = nextCells;
        _rowWeights = NormalizeWeightAverage(nextWeights);
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
        var selectedCustomLayout = CustomLayoutListBox.SelectedItem as LayoutPreset;
        var layoutId = selectedCustomLayout?.Id
                       ?? LayoutPresetService.CreateCustomLayoutId(
                           LayoutNameTextBox.Text,
                           _templateLayouts.Concat(_customLayouts).ToArray());

        if (!TryCreateLayoutFromEditor(layoutId, out var layout, out var error))
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
            LayoutPresetService.CombineLayouts(_templateLayouts, nextCustomLayouts);
            _layoutPresetService.SaveCustomLayouts(nextCustomLayouts);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            DialogStatusTextBlock.Text = ex.Message;
            return false;
        }

        _customLayouts.Clear();
        _customLayouts.AddRange(nextCustomLayouts);
        _allLayouts.Clear();
        _allLayouts.AddRange(LayoutPresetService.CombineLayouts(_templateLayouts, _customLayouts));
        HasCustomLayoutChanges = true;
        SelectedLayout = layout;
        savedLayout = layout;
        RefreshCustomLayoutList(layout);

        return true;
    }

    private void DeleteCustomLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (CustomLayoutListBox.SelectedItem is not LayoutPreset selectedLayout)
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
        _allLayouts.Clear();
        _allLayouts.AddRange(LayoutPresetService.CombineLayouts(_templateLayouts, _customLayouts));
        HasCustomLayoutChanges = true;
        SelectedLayout = _templateLayouts.FirstOrDefault();
        RefreshCustomLayoutList();
        ResetZoneEditor("Custom Layout");

        if (SelectedLayout is not null)
        {
            SelectTemplate(SelectedLayout);
        }

        DialogStatusTextBlock.Text = $"사용자 지정 레이아웃 삭제됨: {selectedLayout.Name}";
    }

    private bool TryCreateLayoutFromEditor(string layoutId, out LayoutPreset layout, out string error)
    {
        layout = null!;
        error = "";
        var name = LayoutNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "레이아웃 이름을 입력하세요.";
            return false;
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

    private void ApplySelectedLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (EditorTabControl.SelectedIndex == 1)
        {
            if (!SaveCustomLayoutFromEditor(out var savedLayout))
            {
                return;
            }

            SelectedLayout = savedLayout;
            DialogResult = true;
            return;
        }

        if (_selectedTemplate is null)
        {
            DialogStatusTextBlock.Text = "적용할 템플릿을 선택하세요.";
            return;
        }

        SelectedLayout = _selectedTemplate;
        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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

    private static double GetWeight(IReadOnlyList<double>? weights, int index)
    {
        return weights is not null && index >= 0 && index < weights.Count && weights[index] > 0
            ? weights[index]
            : 1;
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
        var grid = new Grid
        {
            Width = Math.Max(120, layout.GridColumns * 72),
            Height = Math.Max(90, layout.GridRows * 54),
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 10))
        };

        for (var rowIndex = 0; rowIndex < layout.GridRows; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(GetWeight(layout.RowWeights, rowIndex), GridUnitType.Star)
            });
        }

        for (var columnIndex = 0; columnIndex < layout.GridColumns; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(GetWeight(layout.ColumnWeights, columnIndex), GridUnitType.Star)
            });
        }

        foreach (var slot in layout.Slots.OrderBy(slot => slot.SlotId))
        {
            var border = new Border
            {
                Margin = new Thickness(3),
                Background = GetSlotBrush(slot.SlotId),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 243, 246, 250)),
                BorderThickness = new Thickness(1)
            };

            if (showSlotNumbers)
            {
                border.Child = new TextBlock
                {
                    Text = slot.SlotId.ToString(),
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            Grid.SetColumn(border, slot.X);
            Grid.SetRow(border, slot.Y);
            Grid.SetColumnSpan(border, slot.W);
            Grid.SetRowSpan(border, slot.H);
            grid.Children.Add(border);
        }

        return new Viewbox
        {
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            Child = grid
        };
    }

    private static Brush GetSlotBrush(int slotId)
    {
        var converter = new BrushConverter();
        return (Brush)(converter.ConvertFromString(SlotColorValues[(slotId - 1) % SlotColorValues.Length])
                       ?? Brushes.SteelBlue);
    }

    private enum SplitAxis
    {
        Vertical,
        Horizontal
    }

    private readonly record struct CellPoint(int X, int Y);

    private readonly record struct ZoneRect(int ZoneId, int X, int Y, int W, int H);
}
