using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class LayoutEditorDialog : Window
{
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
    private SplitNode _editorRoot = SplitNode.CreateLeaf(1);
    private SplitNode? _selectedLeaf;
    private bool _isRefreshingEditor;
    private LayoutPreset? _selectedTemplate;

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
        RefreshTemplateList();
        RefreshCustomLayoutList();
        ResetSplitEditor("Custom Layout");

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
        LoadCustomLayoutIntoSplitEditor(selectedLayout);
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
            LoadCustomLayoutIntoSplitEditor(layout);
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
        if (TryCreateSplitTreeFromLayout(_selectedTemplate, out var root))
        {
            LoadSplitTree($"{_selectedTemplate.Name} Custom", root);
            DialogStatusTextBlock.Text = "템플릿을 사용자 지정 분할 편집기로 복사했습니다.";
            return;
        }

        ResetSplitEditor($"{_selectedTemplate.Name} Custom");
        DialogStatusTextBlock.Text = "이 템플릿은 균등 분할 구조가 아니어서 새 레이아웃으로 시작합니다.";
    }

    private void NewCustomLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _isRefreshingEditor = true;
        CustomLayoutListBox.SelectedItem = null;
        _isRefreshingEditor = false;
        ResetSplitEditor("Custom Layout");
        DialogStatusTextBlock.Text = "새 사용자 지정 레이아웃을 시작했습니다.";
    }

    private void VerticalSplitButton_Click(object sender, RoutedEventArgs e)
    {
        SplitSelectedLeaf(SplitAxis.Vertical);
    }

    private void HorizontalSplitButton_Click(object sender, RoutedEventArgs e)
    {
        SplitSelectedLeaf(SplitAxis.Horizontal);
    }

    private void RemoveSelectedSlotButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedLeaf();
    }

    private void SplitSelectedLeaf(SplitAxis axis)
    {
        _selectedLeaf ??= GetLeaves(_editorRoot).FirstOrDefault();
        if (_selectedLeaf is null)
        {
            DialogStatusTextBlock.Text = "분할할 슬롯을 선택하세요.";
            return;
        }

        if (GetLeaves(_editorRoot).Count >= PlaybackTestPlanService.MaxSlotCount)
        {
            DialogStatusTextBlock.Text = $"최대 {PlaybackTestPlanService.MaxSlotCount}개 슬롯까지 만들 수 있습니다.";
            return;
        }

        var originalSlotId = _selectedLeaf.SlotId;
        _selectedLeaf.Axis = axis;
        _selectedLeaf.First = SplitNode.CreateLeaf(originalSlotId);
        _selectedLeaf.Second = SplitNode.CreateLeaf(0);
        _selectedLeaf.SlotId = 0;
        _selectedLeaf = _selectedLeaf.Second;

        NormalizeSlotIdsFromVisualOrder();
        RefreshSplitEditorSurface();
        DialogStatusTextBlock.Text = axis == SplitAxis.Vertical
            ? "선택 슬롯을 좌/우로 세로분할했습니다."
            : "선택 슬롯을 상/하로 가로분할했습니다.";
    }

    private void RemoveSelectedLeaf()
    {
        _selectedLeaf ??= GetLeaves(_editorRoot).FirstOrDefault();
        if (_selectedLeaf is null)
        {
            DialogStatusTextBlock.Text = "제거할 슬롯을 선택하세요.";
            return;
        }

        if (ReferenceEquals(_editorRoot, _selectedLeaf))
        {
            DialogStatusTextBlock.Text = "마지막 슬롯은 제거할 수 없습니다.";
            return;
        }

        if (!TryCollapseParentToSibling(_editorRoot, _selectedLeaf, out var collapsedNode))
        {
            DialogStatusTextBlock.Text = "선택 슬롯을 제거할 수 없습니다.";
            return;
        }

        _selectedLeaf = GetLeaves(collapsedNode).FirstOrDefault() ?? GetLeaves(_editorRoot).FirstOrDefault();
        NormalizeSlotIdsFromVisualOrder();
        RefreshSplitEditorSurface();
        DialogStatusTextBlock.Text = "선택 슬롯을 제거하고 형제 영역을 확장했습니다.";
    }

    private void LoadCustomLayoutIntoSplitEditor(LayoutPreset layout)
    {
        if (!TryCreateSplitTreeFromLayout(layout, out var root))
        {
            ResetSplitEditor(layout.Name);
            DialogStatusTextBlock.Text = "이 레이아웃은 분할 편집 구조로 변환할 수 없어 새 구조로 시작합니다.";
            return;
        }

        LoadSplitTree(layout.Name, root);
        SelectedLayout = layout;
        DialogStatusTextBlock.Text = $"사용자 지정 선택: {layout.Name}";
    }

    private void ResetSplitEditor(string layoutName)
    {
        LoadSplitTree(layoutName, SplitNode.CreateLeaf(1));
    }

    private void LoadSplitTree(string layoutName, SplitNode root)
    {
        _editorRoot = root;
        NormalizeSlotIdsFromVisualOrder();
        _selectedLeaf = GetLeaves(_editorRoot).FirstOrDefault();
        LayoutNameTextBox.Text = layoutName;
        RefreshSplitEditorSurface();
    }

    private void RefreshSplitEditorSurface()
    {
        NormalizeSlotIdsFromVisualOrder();

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

        SplitEditorHost.Content = BuildSplitEditorGrid(layout);
        EditorPreviewHost.Content = BuildLayoutPreview(layout, 260, 420, showSlotNumbers: true);
    }

    private FrameworkElement BuildSplitEditorGrid(LayoutPreset layout)
    {
        var nodeBySlotId = CreateRects(_editorRoot)
            .ToDictionary(rect => rect.Node.SlotId, rect => rect.Node);
        var grid = new Grid
        {
            Width = Math.Max(320, layout.GridColumns * 128),
            Height = Math.Max(220, layout.GridRows * 96),
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 10))
        };

        for (var rowIndex = 0; rowIndex < layout.GridRows; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var columnIndex = 0; columnIndex < layout.GridColumns; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        foreach (var slot in layout.Slots.OrderBy(slot => slot.SlotId))
        {
            var node = nodeBySlotId[slot.SlotId];
            var isSelected = ReferenceEquals(node, _selectedLeaf);
            var button = new Button
            {
                Tag = node,
                Content = slot.SlotId.ToString(),
                Margin = new Thickness(3),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                Background = GetSlotBrush(slot.SlotId),
                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(243, 246, 250))
                    : new SolidColorBrush(Color.FromRgb(45, 54, 66)),
                BorderThickness = isSelected ? new Thickness(4) : new Thickness(1)
            };
            button.Click += SplitEditorSlotButton_Click;

            Grid.SetColumn(button, slot.X);
            Grid.SetRow(button, slot.Y);
            Grid.SetColumnSpan(button, slot.W);
            Grid.SetRowSpan(button, slot.H);
            grid.Children.Add(button);
        }

        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = grid
        };
    }

    private void SplitEditorSlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SplitNode node })
        {
            return;
        }

        _selectedLeaf = node;
        RefreshSplitEditorSurface();
        DialogStatusTextBlock.Text = $"선택 슬롯: {node.SlotId}";
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
        ResetSplitEditor("Custom Layout");

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

        var size = Measure(_editorRoot);
        var rects = CreateRects(_editorRoot)
            .OrderBy(rect => rect.Node.SlotId)
            .ToArray();

        if (rects.Length == 0)
        {
            error = "최소 한 개 이상의 슬롯이 필요합니다.";
            return false;
        }

        layout = new LayoutPreset
        {
            Id = layoutId,
            Name = name,
            GridColumns = size.Columns,
            GridRows = size.Rows,
            Slots = rects
                .Select(rect => new LayoutSlot
                {
                    SlotId = rect.Node.SlotId,
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

    private void NormalizeSlotIdsFromVisualOrder()
    {
        var orderedRects = CreateRects(_editorRoot)
            .OrderBy(rect => rect.Y)
            .ThenBy(rect => rect.X)
            .ThenBy(rect => rect.H)
            .ThenBy(rect => rect.W)
            .ToArray();

        for (var index = 0; index < orderedRects.Length; index++)
        {
            orderedRects[index].Node.SlotId = index + 1;
        }
    }

    private static IReadOnlyList<SplitLayoutRect> CreateRects(SplitNode root)
    {
        var size = Measure(root);
        var rects = new List<SplitLayoutRect>();
        AddRects(root, 0, 0, size.Columns, size.Rows, rects);
        return rects;
    }

    private static void AddRects(
        SplitNode node,
        int x,
        int y,
        int width,
        int height,
        List<SplitLayoutRect> rects)
    {
        if (node.IsLeaf)
        {
            rects.Add(new SplitLayoutRect(node, x, y, width, height));
            return;
        }

        if (node.First is null || node.Second is null || node.Axis is null)
        {
            return;
        }

        if (node.Axis == SplitAxis.Vertical)
        {
            var childWidth = width / 2;
            AddRects(node.First, x, y, childWidth, height, rects);
            AddRects(node.Second, x + childWidth, y, childWidth, height, rects);
            return;
        }

        var childHeight = height / 2;
        AddRects(node.First, x, y, width, childHeight, rects);
        AddRects(node.Second, x, y + childHeight, width, childHeight, rects);
    }

    private static GridSize Measure(SplitNode node)
    {
        if (node.IsLeaf)
        {
            return new GridSize(1, 1);
        }

        if (node.First is null || node.Second is null || node.Axis is null)
        {
            return new GridSize(1, 1);
        }

        var first = Measure(node.First);
        var second = Measure(node.Second);

        if (node.Axis == SplitAxis.Vertical)
        {
            var childColumns = Lcm(first.Columns, second.Columns);
            return new GridSize(childColumns * 2, Lcm(first.Rows, second.Rows));
        }

        var childRows = Lcm(first.Rows, second.Rows);
        return new GridSize(Lcm(first.Columns, second.Columns), childRows * 2);
    }

    private static int Lcm(int first, int second)
    {
        return first / Gcd(first, second) * second;
    }

    private static int Gcd(int first, int second)
    {
        while (second != 0)
        {
            var next = first % second;
            first = second;
            second = next;
        }

        return Math.Abs(first);
    }

    private static IReadOnlyList<SplitNode> GetLeaves(SplitNode root)
    {
        var leaves = new List<SplitNode>();
        AddLeaves(root, leaves);
        return leaves;
    }

    private static void AddLeaves(SplitNode node, List<SplitNode> leaves)
    {
        if (node.IsLeaf)
        {
            leaves.Add(node);
            return;
        }

        if (node.First is not null)
        {
            AddLeaves(node.First, leaves);
        }

        if (node.Second is not null)
        {
            AddLeaves(node.Second, leaves);
        }
    }

    private static bool TryCollapseParentToSibling(
        SplitNode current,
        SplitNode target,
        out SplitNode collapsedNode)
    {
        collapsedNode = null!;

        if (current.IsLeaf || current.First is null || current.Second is null)
        {
            return false;
        }

        if (ReferenceEquals(current.First, target))
        {
            CopyNode(current, current.Second);
            collapsedNode = current;
            return true;
        }

        if (ReferenceEquals(current.Second, target))
        {
            CopyNode(current, current.First);
            collapsedNode = current;
            return true;
        }

        return TryCollapseParentToSibling(current.First, target, out collapsedNode) ||
               TryCollapseParentToSibling(current.Second, target, out collapsedNode);
    }

    private static void CopyNode(SplitNode target, SplitNode source)
    {
        target.SlotId = source.SlotId;
        target.Axis = source.Axis;
        target.First = source.First;
        target.Second = source.Second;
    }

    private static bool TryCreateSplitTreeFromLayout(LayoutPreset layout, out SplitNode root)
    {
        var slots = layout.Slots
            .Select(slot => new LayoutRect(slot.SlotId, slot.X, slot.Y, slot.W, slot.H))
            .ToArray();
        var region = new LayoutRect(0, 0, 0, layout.GridColumns, layout.GridRows);

        return TryCreateSplitTree(region, slots, out root);
    }

    private static bool TryCreateSplitTree(
        LayoutRect region,
        IReadOnlyList<LayoutRect> slots,
        out SplitNode root)
    {
        root = null!;

        if (slots.Count == 1 &&
            slots[0].X == region.X &&
            slots[0].Y == region.Y &&
            slots[0].W == region.W &&
            slots[0].H == region.H)
        {
            root = SplitNode.CreateLeaf(slots[0].SlotId);
            return true;
        }

        if (region.W % 2 == 0 &&
            TryPartitionVertically(region, slots, out var leftSlots, out var rightSlots) &&
            TryCreateSplitTree(new LayoutRect(0, region.X, region.Y, region.W / 2, region.H), leftSlots, out var leftNode) &&
            TryCreateSplitTree(new LayoutRect(0, region.X + region.W / 2, region.Y, region.W / 2, region.H), rightSlots, out var rightNode))
        {
            root = SplitNode.CreateSplit(SplitAxis.Vertical, leftNode, rightNode);
            return true;
        }

        if (region.H % 2 == 0 &&
            TryPartitionHorizontally(region, slots, out var topSlots, out var bottomSlots) &&
            TryCreateSplitTree(new LayoutRect(0, region.X, region.Y, region.W, region.H / 2), topSlots, out var topNode) &&
            TryCreateSplitTree(new LayoutRect(0, region.X, region.Y + region.H / 2, region.W, region.H / 2), bottomSlots, out var bottomNode))
        {
            root = SplitNode.CreateSplit(SplitAxis.Horizontal, topNode, bottomNode);
            return true;
        }

        return false;
    }

    private static bool TryPartitionVertically(
        LayoutRect region,
        IReadOnlyList<LayoutRect> slots,
        out IReadOnlyList<LayoutRect> leftSlots,
        out IReadOnlyList<LayoutRect> rightSlots)
    {
        var splitX = region.X + region.W / 2;
        var left = new List<LayoutRect>();
        var right = new List<LayoutRect>();

        foreach (var slot in slots)
        {
            if (slot.X < splitX && slot.X + slot.W > splitX)
            {
                leftSlots = [];
                rightSlots = [];
                return false;
            }

            if (slot.X + slot.W <= splitX)
            {
                left.Add(slot);
            }
            else if (slot.X >= splitX)
            {
                right.Add(slot);
            }
            else
            {
                leftSlots = [];
                rightSlots = [];
                return false;
            }
        }

        leftSlots = left;
        rightSlots = right;
        return left.Count > 0 && right.Count > 0;
    }

    private static bool TryPartitionHorizontally(
        LayoutRect region,
        IReadOnlyList<LayoutRect> slots,
        out IReadOnlyList<LayoutRect> topSlots,
        out IReadOnlyList<LayoutRect> bottomSlots)
    {
        var splitY = region.Y + region.H / 2;
        var top = new List<LayoutRect>();
        var bottom = new List<LayoutRect>();

        foreach (var slot in slots)
        {
            if (slot.Y < splitY && slot.Y + slot.H > splitY)
            {
                topSlots = [];
                bottomSlots = [];
                return false;
            }

            if (slot.Y + slot.H <= splitY)
            {
                top.Add(slot);
            }
            else if (slot.Y >= splitY)
            {
                bottom.Add(slot);
            }
            else
            {
                topSlots = [];
                bottomSlots = [];
                return false;
            }
        }

        topSlots = top;
        bottomSlots = bottom;
        return top.Count > 0 && bottom.Count > 0;
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
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var columnIndex = 0; columnIndex < layout.GridColumns; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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

    private sealed class SplitNode
    {
        private SplitNode(int slotId)
        {
            SlotId = slotId;
        }

        public int SlotId { get; set; }

        public SplitAxis? Axis { get; set; }

        public SplitNode? First { get; set; }

        public SplitNode? Second { get; set; }

        public bool IsLeaf => Axis is null;

        public static SplitNode CreateLeaf(int slotId)
        {
            return new SplitNode(slotId);
        }

        public static SplitNode CreateSplit(SplitAxis axis, SplitNode first, SplitNode second)
        {
            return new SplitNode(0)
            {
                Axis = axis,
                First = first,
                Second = second
            };
        }
    }

    private readonly record struct GridSize(int Columns, int Rows);

    private readonly record struct SplitLayoutRect(SplitNode Node, int X, int Y, int W, int H);

    private readonly record struct LayoutRect(int SlotId, int X, int Y, int W, int H);
}
