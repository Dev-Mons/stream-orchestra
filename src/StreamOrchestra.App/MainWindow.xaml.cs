using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.ComponentModel;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;
using StreamOrchestra.App.Views;

namespace StreamOrchestra.App;

public partial class MainWindow : Window
{
    private readonly WebViewProfileService _profileService = new();
    private readonly WebViewRuntimeDiagnosticsService _diagnosticsService = new();
    private readonly PresetStorageService _presetStorageService = new();
    private readonly LayoutPresetService _layoutPresetService;
    private readonly StreamNavigationService _streamNavigationService = new();
    private readonly SlotSelectionService _slotSelectionService = new();
    private readonly AppWindowPlacementService _appWindowPlacementService = new();
    private readonly WorkspaceSlotVisibilityService _workspaceSlotVisibilityService = new();
    private readonly WorkspacePresetNormalizationService _workspacePresetNormalizationService;
    private readonly WorkspaceRestoreService _workspaceRestoreService;
    private readonly List<StreamSlotView> _slots = [];
    private IReadOnlyList<LayoutPreset> _builtInLayouts = [];
    private List<LayoutPreset> _customLayouts = [];
    private IReadOnlyList<LayoutPreset> _layouts = [];
    private List<WorkspacePreset> _workspaces = [];
    private AppState? _loadedAppState;
    private readonly DispatcherTimer _diagnosticsTimer;
    private WorkspacePreset? _activeWorkspace;
    private StreamSlotView? _selectedSlot;
    private ExplorerPanel? _explorerPanel;
    private bool _isExplorerPanelVisible = true;
    private GridLength _lastExplorerColumnWidth = new(360);
    private LayoutPreset? _selectedLayout;

    public MainWindow()
    {
        _layoutPresetService = new LayoutPresetService(_presetStorageService.DataFolder);
        _workspacePresetNormalizationService = new WorkspacePresetNormalizationService(_streamNavigationService);
        _workspaceRestoreService = new WorkspaceRestoreService(
            _workspacePresetNormalizationService,
            _workspaceSlotVisibilityService);
        InitializeComponent();

        _loadedAppState = _presetStorageService.LoadAppState();
        RestoreWindowPlacement(_loadedAppState?.Window);

        CreateExplorerPanel();
        CreateSlots();
        LoadLayouts();
        LoadWorkspacePresets();
        ApplyViewState(_loadedAppState);
        RefreshQualityMenuChecks();
        _diagnosticsTimer = CreateDiagnosticsTimer();
        StatusTextBlock.Text = $"Profile data persists under: {_profileService.BaseProfileFolder}";
        UpdateDiagnostics();
        _diagnosticsTimer.Start();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void CreateExplorerPanel()
    {
        var explorerPanel = new ExplorerPanel(_profileService, _streamNavigationService);
        _explorerPanel = explorerPanel;
        ExplorerHost.Content = _explorerPanel;
    }


    private void CreateSlots()
    {
        for (var slotId = 1; slotId <= 16; slotId++)
        {
            var configuration = new SlotConfiguration(slotId, _profileService.GetGroupForSlot(slotId));
            var slotView = new StreamSlotView(configuration, _profileService, _streamNavigationService);
            slotView.SlotSelected += SelectSlot;
            slotView.StreamUrlDropRequested += SlotView_StreamUrlDropRequested;
            _slots.Add(slotView);
        }
    }

    private void LoadLayouts(string? selectedLayoutId = null)
    {
        var currentLayoutId = selectedLayoutId ?? _selectedLayout?.Id;
        _builtInLayouts = _layoutPresetService.LoadBuiltInLayouts();
        _customLayouts = _layoutPresetService.LoadCustomLayouts().ToList();
        _layouts = LayoutPresetService.CombineLayouts(_builtInLayouts, _customLayouts);

        _selectedLayout = string.IsNullOrWhiteSpace(currentLayoutId)
            ? LayoutPresetService.SelectDefaultLayout(_layouts)
            : _layouts.FirstOrDefault(layout => layout.Id.Equals(currentLayoutId, StringComparison.OrdinalIgnoreCase))
              ?? LayoutPresetService.SelectDefaultLayout(_layouts);

        RefreshLayoutSelector();
        ApplyLayout(_selectedLayout);
    }

    private void RefreshLayoutSelector()
    {
    }

    private static Button CreateLayoutSelectorButton(LayoutPreset layout, bool isSelected)
    {
        var button = new Button
        {
            Tag = layout,
            Width = 150,
            Height = 72,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)),
            BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(243, 246, 250))
                : new SolidColorBrush(Color.FromRgb(45, 54, 66)),
            BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
            Foreground = Brushes.White,
            ToolTip = layout.Name
        };

        var panel = new DockPanel { LastChildFill = true };
        var title = new TextBlock
        {
            Text = layout.Name,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);
        panel.Children.Add(LayoutPreviewBuilder.Build(layout, 132, 44, showSlotNumbers: false));
        button.Content = panel;

        return button;
    }

    private void LoadWorkspacePresets()
    {
        _workspaces = _presetStorageService.LoadWorkspaces()
            .Select(workspace => _workspaceRestoreService.Prepare(workspace, _layouts).Workspace)
            .ToList();
        RefreshWorkspaceComboBox();
    }

    private void ApplyLayout(LayoutPreset layout)
    {
        SlotsGrid.Children.Clear();
        SlotsGrid.RowDefinitions.Clear();
        SlotsGrid.ColumnDefinitions.Clear();

        for (var rowIndex = 0; rowIndex < layout.GridRows; rowIndex++)
        {
            SlotsGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(GetGridWeight(layout.RowWeights, rowIndex), GridUnitType.Star)
            });
        }

        for (var columnIndex = 0; columnIndex < layout.GridColumns; columnIndex++)
        {
            SlotsGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(GetGridWeight(layout.ColumnWeights, columnIndex), GridUnitType.Star)
            });
        }

        var slotsById = _slots.ToDictionary(slot => slot.SlotId);

        foreach (var layoutSlot in layout.Slots.OrderBy(slot => slot.SlotId))
        {
            var slotView = slotsById[layoutSlot.SlotId];

            Grid.SetRow(slotView, layoutSlot.Y);
            Grid.SetColumn(slotView, layoutSlot.X);
            Grid.SetRowSpan(slotView, layoutSlot.H);
            Grid.SetColumnSpan(slotView, layoutSlot.W);

            SlotsGrid.Children.Add(slotView);
        }

        StatusTextBlock.Text = $"Layout applied: {layout.Name} ({layout.GridColumns}x{layout.GridRows}, {layout.Slots.Count} visible slots)";
    }

    private static double GetGridWeight(IReadOnlyList<double>? weights, int index)
    {
        return weights is not null && index >= 0 && index < weights.Count && weights[index] > 0
            ? weights[index]
            : 1;
    }

    private async Task ApplySelectedLayoutAsync(LayoutPreset layout, bool clearHiddenSlots)
    {
        _selectedLayout = layout;
        RefreshLayoutSelector();
        ApplyLayout(layout);
        EnsureSelectedSlotVisible(layout);

        if (clearHiddenSlots)
        {
            await ClearHiddenNonBlankSlotsAsync(layout);
        }
    }

    private void EditLayoutsButton_Click(object sender, RoutedEventArgs e)
    {
        var currentLayout = _selectedLayout;
        var dialog = new LayoutEditorDialog(
            _layoutPresetService,
            _builtInLayouts,
            _customLayouts,
            _layouts,
            currentLayout)
        {
            Owner = this
        };

        var shouldApplyLayout = dialog.ShowDialog() == true && dialog.SelectedLayout is not null;
        if (!shouldApplyLayout && !dialog.HasCustomLayoutChanges)
        {
            return;
        }

        var selectedLayoutId = shouldApplyLayout
            ? dialog.SelectedLayout?.Id
            : currentLayout?.Id;
        LoadLayouts(selectedLayoutId);

        if (!shouldApplyLayout)
        {
            StatusTextBlock.Text = "사용자 지정 레이아웃 목록을 갱신했습니다.";
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_loadedAppState?.LastSession is not null)
            {
                _activeWorkspace = _workspaces.FirstOrDefault(workspace => workspace.Id == _loadedAppState.LastWorkspaceId);
                RefreshWorkspaceComboBox();
                await ApplyWorkspaceAsync(_loadedAppState.LastSession, setActiveWorkspace: false);
                SelectSlotFromState(_loadedAppState.SelectedSlotId);
                StatusTextBlock.Text = $"Last session restored. Presets are stored under: {_presetStorageService.DataFolder}";
                return;
            }

            var lastWorkspace = _workspaces.FirstOrDefault(workspace => workspace.Id == _loadedAppState?.LastWorkspaceId);
            if (lastWorkspace is not null)
            {
                await ApplyWorkspaceAsync(lastWorkspace, setActiveWorkspace: true);
                SelectSlotFromState(_loadedAppState?.SelectedSlotId);
            }
        }
        finally
        {
            RefreshAutoShowExplorerEdgeVisibility();
        }
    }

    private void ToggleExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        SetExplorerPanelVisible(!_isExplorerPanelVisible);
        StatusTextBlock.Text = _isExplorerPanelVisible ? "Explorer panel shown." : "Explorer panel hidden.";
    }

    private void AutoShowExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        SetExplorerPanelVisible(true);
        StatusTextBlock.Text = "Explorer panel shown.";
    }

    private void AutoShowExplorerButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_isExplorerPanelVisible)
        {
            AutoShowExplorerPopup.IsOpen = true;
        }
    }

    private void AutoShowExplorerButton_MouseLeave(object sender, MouseEventArgs e)
    {
        AutoShowExplorerPopup.IsOpen = false;
    }

    private void AutoShowExplorerHitTarget_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_isExplorerPanelVisible)
        {
            AutoShowExplorerPopup.IsOpen = true;
        }
    }

    private void AutoShowExplorerHitTarget_MouseLeave(object sender, MouseEventArgs e)
    {
    }

    private async void SlotView_StreamUrlDropRequested(StreamSlotView targetSlot, string url, string? streamName)
    {
        await LoadDroppedStreamIntoSlotAsync(targetSlot, url, streamName);
    }

    private void SlotsGrid_DragOver(object sender, DragEventArgs e)
    {
        var hasTargetSlot = TryGetDropTargetSlot(e.GetPosition(SlotsGrid), out _);

        if (hasTargetSlot && StreamDropDataReader.TryGetDroppedStream(e.Data, _streamNavigationService, out _, out _))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void SlotsGrid_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDropTargetSlot(e.GetPosition(SlotsGrid), out var targetSlot) || targetSlot is null)
        {
            StatusTextBlock.Text = "Drop the stream over a visible playback area.";
            e.Handled = true;
            return;
        }

        if (StreamDropDataReader.TryGetDroppedStream(e.Data, _streamNavigationService, out var url, out var streamName))
        {
            await LoadDroppedStreamIntoSlotAsync(targetSlot, url, streamName);
            e.Handled = true;
        }
    }

    private bool TryGetDropTargetSlot(Point position, out StreamSlotView? targetSlot)
    {
        targetSlot = null;
        var visibleSlots = SlotsGrid.Children.OfType<StreamSlotView>().ToArray();

        foreach (var slot in visibleSlots.Reverse())
        {
            var topLeft = slot.TranslatePoint(new Point(0, 0), SlotsGrid);
            var bounds = new Rect(topLeft, slot.RenderSize);
            if (bounds.Contains(position))
            {
                targetSlot = slot;
                return true;
            }
        }

        if (visibleSlots.Length == 1)
        {
            targetSlot = visibleSlots[0];
            return true;
        }

        return false;
    }

    private async Task LoadDroppedStreamIntoSlotAsync(StreamSlotView targetSlot, string url, string? streamName)
    {
        SelectSlot(targetSlot);
        await NavigateSlotAsync(targetSlot, url, streamName);

        StatusTextBlock.Text = string.IsNullOrWhiteSpace(streamName)
            ? $"Dropped URL into Slot {targetSlot.SlotId}: {url}"
            : $"Dropped {streamName} into Slot {targetSlot.SlotId}: {url}";
    }




    private async void LoadWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceComboBox.SelectedItem is not WorkspacePreset workspace)
        {
            StatusTextBlock.Text = "No preset is selected.";
            return;
        }

        await ApplyWorkspaceAsync(workspace, setActiveWorkspace: true);
        StatusTextBlock.Text = $"Preset loaded: {workspace.Name}";
    }

    private void SaveCurrentWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeWorkspace is null)
        {
            SaveWorkspaceAs();
            return;
        }

        var updatedWorkspace = CaptureWorkspace(_activeWorkspace.Id, _activeWorkspace.Name);
        UpsertWorkspace(updatedWorkspace);
        _activeWorkspace = updatedWorkspace;
        _presetStorageService.SaveWorkspaces(_workspaces);
        RefreshWorkspaceComboBox();
        StatusTextBlock.Text = $"Preset saved: {updatedWorkspace.Name}";
    }

    private void SaveWorkspaceAsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveWorkspaceAs();
    }

    private void SelectSlot(StreamSlotView slot)
    {
        if (_selectedSlot is not null)
        {
            _selectedSlot.SetSelected(false);
        }

        _selectedSlot = slot;
        _selectedSlot.SetSelected(true);
        StatusTextBlock.Text = $"Selected Slot {slot.SlotId} / Group {slot.ProfileGroupId}";
    }

    private void SaveWorkspaceAs()
    {
        var dialog = new SavePresetDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var workspaceId = PresetStorageService.CreateWorkspaceId(dialog.PresetName, _workspaces);
        var workspace = CaptureWorkspace(workspaceId, dialog.PresetName);

        UpsertWorkspace(workspace);
        _activeWorkspace = workspace;
        _presetStorageService.SaveWorkspaces(_workspaces);
        RefreshWorkspaceComboBox();
        StatusTextBlock.Text = $"Preset saved as: {workspace.Name}";
    }

    private WorkspacePreset CaptureWorkspace(string id, string name)
    {
        var selectedLayout = _selectedLayout;
        var layoutId = selectedLayout is not null
            ? selectedLayout.Id
            : LayoutPresetIds.Default;

        var workspace = new WorkspacePreset
        {
            Id = id,
            Name = name,
            LayoutId = layoutId,
            Slots = _slots
                .OrderBy(slot => slot.SlotId)
                .Select(slot => new WorkspaceSlot
                {
                    SlotId = slot.SlotId,
                    StreamName = slot.CurrentStreamName,
                    StreamUrl = slot.CurrentUrl,
                    Muted = false,
                    ProfileGroupId = slot.ProfileGroupId
                })
                .ToArray()
        };

        if (selectedLayout is not null)
        {
            return _workspaceSlotVisibilityService.BlankHiddenSlots(workspace, selectedLayout);
        }

        return workspace;
    }

    private AppState CaptureAppState()
    {
        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);

        return new AppState
        {
            LastWorkspaceId = _activeWorkspace?.Id,
            SelectedSlotId = _selectedSlot?.SlotId,
            LastSession = CaptureWorkspace("last_session", "Last Session"),
            IsExplorerPanelVisible = _isExplorerPanelVisible,
            AudibleQualityKey = GetQualityKey(AudibleQualityComboBox),
            Window = new AppWindowState
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                IsMaximized = WindowState == WindowState.Maximized
            }
        };
    }

    private async Task ApplyWorkspaceAsync(WorkspacePreset workspace, bool setActiveWorkspace)
    {
        var preparedWorkspace = _workspaceRestoreService.Prepare(workspace, _layouts);
        workspace = preparedWorkspace.Workspace;
        var layout = preparedWorkspace.Layout;
        if (_selectedLayout?.Id.Equals(layout.Id, StringComparison.OrdinalIgnoreCase) != true)
        {
            await ApplySelectedLayoutAsync(layout, clearHiddenSlots: false);
        }
        else
        {
            ApplyLayout(layout);
        }

        var workspaceSlots = workspace.Slots.ToDictionary(slot => slot.SlotId);

        foreach (var slot in _slots.OrderBy(slot => slot.SlotId))
        {
            if (!workspaceSlots.TryGetValue(slot.SlotId, out var workspaceSlot))
            {
                continue;
            }

            slot.SetMuted(false);
            if (layout.Slots.Any(layoutSlot => layoutSlot.SlotId == slot.SlotId))
            {
                await NavigateSlotAsync(slot, workspaceSlot.StreamUrl, workspaceSlot.StreamName);
            }
            else
            {
                await slot.ClearAsync();
            }
        }

        if (setActiveWorkspace)
        {
            _activeWorkspace = workspace;
            RefreshWorkspaceComboBox();
        }
    }

    private void UpsertWorkspace(WorkspacePreset workspace)
    {
        var existingIndex = _workspaces.FindIndex(candidate => candidate.Id == workspace.Id);
        if (existingIndex >= 0)
        {
            _workspaces[existingIndex] = workspace;
            return;
        }

        _workspaces.Add(workspace);
    }

    private void RefreshWorkspaceComboBox()
    {
        WorkspaceComboBox.ItemsSource = null;
        WorkspaceComboBox.ItemsSource = _workspaces.OrderBy(workspace => workspace.Name).ToArray();

        if (_activeWorkspace is not null)
        {
            WorkspaceComboBox.SelectedItem = WorkspaceComboBox.Items
                .OfType<WorkspacePreset>()
                .FirstOrDefault(workspace => workspace.Id == _activeWorkspace.Id);
        }
        else
        {
            WorkspaceComboBox.SelectedIndex = _workspaces.Count > 0 ? 0 : -1;
        }
    }



    private void SelectSlotFromState(int? selectedSlotId)
    {
        var targetSlotId = selectedSlotId;
        if (_selectedLayout is LayoutPreset layout)
        {
            targetSlotId = _slotSelectionService.ResolveVisibleSlotId(layout, selectedSlotId);
        }

        if (targetSlotId is null)
        {
            return;
        }

        var slot = _slots.FirstOrDefault(candidate => candidate.SlotId == targetSlotId.Value);
        if (slot is not null)
        {
            SelectSlot(slot);
        }
    }

    private void EnsureSelectedSlotVisible(LayoutPreset layout)
    {
        if (_selectedSlot is null || _slotSelectionService.IsSlotVisible(layout, _selectedSlot.SlotId))
        {
            return;
        }

        SelectSlotFromState(_selectedSlot.SlotId);
    }


    private void RestoreWindowPlacement(AppWindowState? windowState)
    {
        var normalizedWindowState = _appWindowPlacementService.NormalizeForRestore(
            windowState,
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        if (normalizedWindowState is null)
        {
            return;
        }

        Left = normalizedWindowState.X;
        Top = normalizedWindowState.Y;
        Width = normalizedWindowState.Width;
        Height = normalizedWindowState.Height;
        WindowState = normalizedWindowState.IsMaximized ? WindowState.Maximized : WindowState.Normal;
    }

    private void ApplyViewState(AppState? appState)
    {
        SetExplorerPanelVisible(appState?.IsExplorerPanelVisible ?? true);

        if (appState is null)
        {
            return;
        }

        SetQualityComboBoxSelection(
            AudibleQualityComboBox,
            NormalizeQualityKey(appState.AudibleQualityKey) ?? "original");
    }

    private void SetExplorerPanelVisible(bool isVisible)
    {
        _isExplorerPanelVisible = isVisible;

        if (!isVisible && ExplorerColumn.Width.Value > 0)
        {
            _lastExplorerColumnWidth = ExplorerColumn.Width;
        }

        ExplorerBorder.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ExplorerGridSplitter.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ExplorerColumn.Width = isVisible ? _lastExplorerColumnWidth : new GridLength(0);
        ExplorerSplitterColumn.Width = isVisible ? new GridLength(6) : new GridLength(0);
        ToggleExplorerButton.ToolTip = isVisible ? "탐색 숨김" : "탐색 표시";
        RefreshAutoShowExplorerEdgeVisibility();
    }

    private void RefreshAutoShowExplorerEdgeVisibility()
    {
        AutoShowExplorerHitTarget.Visibility = _isExplorerPanelVisible ? Visibility.Collapsed : Visibility.Visible;
        AutoShowExplorerPopup.IsOpen = false;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _presetStorageService.SaveAppState(CaptureAppState());
    }

    private async void ApplyQualityPolicyButton_Click(object sender, RoutedEventArgs e)
    {
        var targetSlots = GetVisibleNonBlankSlots();
        if (targetSlots.Length == 0)
        {
            StatusTextBlock.Text = "화질을 적용할 재생 중인 슬롯이 없습니다.";
            return;
        }

        await ApplyQualityPolicyToSlotsAsync(targetSlots);
    }

    private async void AudibleQualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || AudibleQualityComboBox is null)
        {
            return;
        }

        RefreshQualityMenuChecks();
        await ApplyQualityPolicyToSlotsAsync(GetVisibleNonBlankSlots());
    }

    private async void QualityMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string qualityKey } menuItem)
        {
            return;
        }

        var previousItem = AudibleQualityComboBox.SelectedItem;
        SetQualityComboBoxSelection(AudibleQualityComboBox, qualityKey, menuItem.Header?.ToString());
        RefreshQualityMenuChecks();

        if (ReferenceEquals(previousItem, AudibleQualityComboBox.SelectedItem))
        {
            await ApplyQualityPolicyToSlotsAsync(GetVisibleNonBlankSlots());
        }
    }

    private async Task NavigateSlotAsync(StreamSlotView slot, string url, string? streamName = null)
    {
        await ApplyQualityPolicyToSlotAsync(slot);

        await slot.NavigateAsync(url, streamName);
    }

    private async Task ClearSlotsAsync(IEnumerable<StreamSlotView> slots, string statusPrefix)
    {
        var slotList = slots.ToArray();
        if (slotList.Length == 0)
        {
            return;
        }

        StatusTextBlock.Text = $"{statusPrefix} {slotList.Length} slot(s).";

        foreach (var slot in slotList)
        {
            await slot.ClearAsync();
        }

        StatusTextBlock.Text = $"Cleared {slotList.Length} slot(s).";
        UpdateDiagnostics();
    }

    private async Task ClearHiddenNonBlankSlotsAsync(LayoutPreset layout)
    {
        var visibleSlotIds = layout.Slots
            .Select(slot => slot.SlotId)
            .ToHashSet();
        var hiddenNonBlankSlots = _slots
            .Where(slot =>
                !visibleSlotIds.Contains(slot.SlotId) &&
                !slot.CurrentUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await ClearSlotsAsync(hiddenNonBlankSlots, "Clearing hidden slots");
    }

    private async Task ApplyQualityPolicyToSlotsAsync(IReadOnlyList<StreamSlotView> targetSlots)
    {
        if (targetSlots.Count == 0)
        {
            return;
        }

        var policyText = GetQualityLabel(AudibleQualityComboBox);
        StatusTextBlock.Text = $"화질 정책 적용 중: {policyText} / {targetSlots.Count} slot(s)";

        var results = new List<(StreamSlotView Slot, StreamQualityApplyResult Result)>();
        foreach (var slot in targetSlots)
        {
            results.Add((slot, await ApplyQualityPolicyToSlotAsync(slot)));
        }

        var successCount = results.Count(item => item.Result.IsSuccess);
        if (successCount == results.Count)
        {
            StatusTextBlock.Text = $"화질 정책 적용 완료: {policyText} / {successCount}/{results.Count} slot(s)";
            return;
        }

        var firstFailure = results.FirstOrDefault(item => !item.Result.IsSuccess);
        StatusTextBlock.Text =
            $"화질 정책 적용 일부 실패: {successCount}/{results.Count} slot(s). " +
            $"Slot {firstFailure.Slot.SlotId}: {firstFailure.Result.Message}";
    }

    private Task<StreamQualityApplyResult> ApplyQualityPolicyToSlotAsync(StreamSlotView slot)
    {
        var qualityKey = GetQualityKey(AudibleQualityComboBox);
        return slot.ApplyQualityAsync(qualityKey);
    }

    private StreamSlotView[] GetVisibleNonBlankSlots()
    {
        HashSet<int>? visibleSlotIds = null;
        if (_selectedLayout is LayoutPreset layout)
        {
            visibleSlotIds = layout.Slots
                .Select(slot => slot.SlotId)
                .ToHashSet();
        }

        return _slots
            .Where(slot => visibleSlotIds is null || visibleSlotIds.Contains(slot.SlotId))
            .Where(slot => !slot.CurrentUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string? NormalizeQualityKey(string? qualityKey)
    {
        return qualityKey?.Trim().ToLowerInvariant() switch
        {
            "original" => "original",
            "q1440" => "q1440",
            "hd4k" => "hd4k",
            "hd" => "hd",
            "sd" => "sd",
            _ => null
        };
    }

    private static void SetQualityComboBoxSelection(ComboBox comboBox, string qualityKey, string? qualityLabel = null)
    {
        var normalizedKey = string.IsNullOrWhiteSpace(qualityKey) ? "original" : qualityKey.Trim().ToLowerInvariant();
        var candidates = comboBox.Items
            .OfType<ComboBoxItem>()
            .Where(item => item.Tag is string tag && tag.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var selectedItem = string.IsNullOrWhiteSpace(qualityLabel)
            ? candidates.FirstOrDefault()
            : candidates.FirstOrDefault(item =>
                item.Content?.ToString()?.Equals(qualityLabel, StringComparison.OrdinalIgnoreCase) == true)
              ?? candidates.FirstOrDefault();

        if (selectedItem is not null)
        {
            comboBox.SelectedItem = selectedItem;
        }
    }

    private void RefreshQualityMenuChecks()
    {
        var selectedQualityKey = GetQualityKey(AudibleQualityComboBox);
        var selectedQualityLabel = GetQualityLabel(AudibleQualityComboBox);

        foreach (var item in QualityMenuItem.Items.OfType<MenuItem>())
        {
            item.IsChecked =
                item.Tag is string qualityKey &&
                qualityKey.Equals(selectedQualityKey, StringComparison.OrdinalIgnoreCase) &&
                item.Header?.ToString()?.Equals(selectedQualityLabel, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    private static string GetQualityKey(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string qualityKey }
            ? qualityKey
            : "original";
    }

    private static string GetQualityLabel(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem { Content: object content }
            ? content.ToString() ?? GetQualityKey(comboBox)
            : GetQualityKey(comboBox);
    }

    private DispatcherTimer CreateDiagnosticsTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        timer.Tick += (_, _) => UpdateDiagnostics();

        return timer;
    }

    private void UpdateDiagnostics()
    {
        var snapshot = _diagnosticsService.Capture();
        var cpuText = snapshot.WebViewCpuPercent is null
            ? "CPU sampling..."
            : $"CPU {snapshot.WebViewCpuPercent.Value:F1}%";

        DiagnosticsTextBlock.Text =
            $"WebView2 {snapshot.WebViewProcessCount} proc | {cpuText} | WS {snapshot.WebViewWorkingSetMegabytes:F0} MB | Private {snapshot.WebViewPrivateMemoryMegabytes:F0} MB";
    }

}
