using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly SlotSwapService _slotSwapService = new();
    private readonly DropZoneService _dropZoneService = new();
    private readonly LayoutTreeMutationService _layoutTreeMutationService = new();
    private readonly AppWindowPlacementService _appWindowPlacementService = new();
    private readonly WorkspaceSlotVisibilityService _workspaceSlotVisibilityService = new();
    private readonly WorkspacePresetNormalizationService _workspacePresetNormalizationService;
    private readonly WorkspaceRestoreService _workspaceRestoreService;
    private readonly UpdateService? _updateService;
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
    private DockingOverlayPresenter? _dockingOverlayPresenter;
    private Popup? _dockingInputOverlayPopup;
    private Canvas? _dockingInputOverlay;
    private Border? _dockingInputPreview;
    private StreamSlotView? _lastDockTargetSlot;
    private DockDirection _lastDockDirection = DockDirection.None;
    private bool _isExplorerPanelVisible = true;
    private GridLength _lastExplorerColumnWidth = new(360);
    private LayoutPreset? _selectedLayout;
    private LayoutTreeDocument? _currentLayoutTree;

    public MainWindow()
    {
        _layoutPresetService = new LayoutPresetService(_presetStorageService.DataFolder);
        _workspacePresetNormalizationService = new WorkspacePresetNormalizationService(_streamNavigationService);
        _workspaceRestoreService = new WorkspaceRestoreService(
            _workspacePresetNormalizationService,
            _workspaceSlotVisibilityService);
        InitializeComponent();
        _dockingOverlayPresenter = new DockingOverlayPresenter();

        _loadedAppState = _presetStorageService.LoadAppState();
        RestoreWindowPlacement(_loadedAppState?.Window);

        _updateService = TryCreateUpdateService(_loadedAppState?.AutoUpdate ?? new AutoUpdateState());

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
        _explorerPanel.HostDragStarted += ShowDockingInputOverlay;
        _explorerPanel.HostDragCompleted += HideDockingInputOverlay;
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
            slotView.HostDragStarted += ShowDockingInputOverlay;
            slotView.HostDragCompleted += HideDockingInputOverlay;
            slotView.DockPreviewRequested += SlotView_DockPreviewRequested;
            slotView.DockPreviewEnded += SlotView_DockPreviewEnded;
            slotView.StreamDockDropRequested += SlotView_StreamDockDropRequested;
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
        _currentLayoutTree = LayoutTreePresetConverter.Convert(layout);
        ApplyLayoutTree(_currentLayoutTree);
        StatusTextBlock.Text = $"Layout applied: {layout.Name} ({layout.GridColumns}x{layout.GridRows}, {layout.Slots.Count} visible slots)";
    }

    private void ApplyLayoutTree(LayoutTreeDocument layoutTree)
    {
        SlotsGrid.Children.Clear();
        SlotsGrid.RowDefinitions.Clear();
        SlotsGrid.ColumnDefinitions.Clear();

        if (layoutTree.Root is null)
        {
            return;
        }

        var slotsById = _slots.ToDictionary(slot => slot.SlotId);
        var content = LayoutTreeRenderer.Build(layoutTree, slotsById);
        SlotsGrid.Children.Add(content);
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
            _ = StartAutomaticUpdateCheckAsync();
        }
    }

    private static UpdateService? TryCreateUpdateService(AutoUpdateState state)
    {
        try
        {
            var checker = new VelopackUpdateChecker("https://github.com/Dev-Mons/stream-orchestra");
            return new UpdateService(checker, state);
        }
        catch
        {
            return null;
        }
    }

    private async Task StartAutomaticUpdateCheckAsync()
    {
        if (_updateService is null)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var result = await _updateService.RunAutomaticCheckAsync();
            if (result.Outcome == UpdateCheckOutcome.Available && result.Update is not null)
            {
                await PromptForUpdateAsync(result.Update);
            }
        }
        catch
        {
            // 자동 업데이트 실패는 사용자 흐름을 방해하지 않음
        }
    }

    private async Task PromptForUpdateAsync(AvailableUpdate update)
    {
        if (_updateService is null)
        {
            return;
        }

        var message =
            $"새 버전 {update.Version}이(가) 사용 가능합니다.\n\n" +
            "지금 업데이트하시겠습니까?\n" +
            "• 예: 다운로드 후 자동 재시작\n" +
            "• 아니오: 나중에 알림\n" +
            "• 취소: 이 버전 건너뛰기";

        var choice = MessageBox.Show(
            this,
            message,
            "StreamOrchestra 업데이트",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Information);

        if (choice == MessageBoxResult.Cancel)
        {
            _updateService.SkipVersion(update.Version);
            StatusTextBlock.Text = $"버전 {update.Version} 건너뜀.";
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            StatusTextBlock.Text = $"업데이트 {update.Version} 다운로드 중...";
            await _updateService.DownloadAndApplyAsync();
        }
        catch
        {
            StatusTextBlock.Text = "업데이트 다운로드에 실패했습니다.";
        }
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateService is null)
        {
            StatusTextBlock.Text = "업데이트 검사 기능을 사용할 수 없습니다.";
            return;
        }

        StatusTextBlock.Text = "업데이트 확인 중...";
        try
        {
            var result = await _updateService.RunManualCheckAsync();
            switch (result.Outcome)
            {
                case UpdateCheckOutcome.Available when result.Update is not null:
                    await PromptForUpdateAsync(result.Update);
                    break;
                case UpdateCheckOutcome.Skipped when result.Update is not null:
                    await PromptForUpdateAsync(result.Update);
                    break;
                case UpdateCheckOutcome.NoUpdate:
                    StatusTextBlock.Text = "이미 최신 버전입니다.";
                    break;
                case UpdateCheckOutcome.Failed:
                    StatusTextBlock.Text = "업데이트 확인에 실패했습니다.";
                    break;
                case UpdateCheckOutcome.Disabled:
                    StatusTextBlock.Text = "자동 업데이트가 비활성화되어 있습니다.";
                    break;
            }
        }
        catch
        {
            StatusTextBlock.Text = "업데이트 확인 중 오류가 발생했습니다.";
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

    private void SlotView_DockPreviewRequested(StreamSlotView targetSlot, DockDirection direction)
    {
        _dockingOverlayPresenter?.Show(targetSlot, direction);
    }

    private void SlotView_DockPreviewEnded(StreamSlotView targetSlot)
    {
        _dockingOverlayPresenter?.Hide();
    }

    private void ShowDockingInputOverlay()
    {
        if (_dockingInputOverlayPopup is null)
        {
            _dockingInputOverlay = new Canvas
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                AllowDrop = true
            };
            _dockingInputOverlay.DragOver += SlotsGrid_DragOver;
            _dockingInputOverlay.DragLeave += SlotsGrid_DragLeave;
            _dockingInputOverlay.Drop += SlotsGrid_Drop;

            _dockingInputPreview = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(105, 47, 128, 237)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(230, 243, 246, 250)),
                BorderThickness = new Thickness(2),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            _dockingInputOverlay.Children.Add(_dockingInputPreview);

            _dockingInputOverlayPopup = new Popup
            {
                AllowsTransparency = true,
                Focusable = false,
                Placement = PlacementMode.Relative,
                PlacementTarget = SlotsGrid,
                PopupAnimation = PopupAnimation.None,
                StaysOpen = true,
                Child = _dockingInputOverlay
            };
        }

        if (_dockingInputOverlay is not null)
        {
            _dockingInputOverlay.Width = Math.Max(1, SlotsGrid.ActualWidth);
            _dockingInputOverlay.Height = Math.Max(1, SlotsGrid.ActualHeight);
        }

        _dockingInputOverlayPopup.IsOpen = true;
    }

    private void HideDockingInputOverlay()
    {
        _dockingOverlayPresenter?.Hide();
        HideDockingInputPreview();
        _lastDockTargetSlot = null;
        _lastDockDirection = DockDirection.None;
        if (_dockingInputOverlayPopup is not null)
        {
            _dockingInputOverlayPopup.IsOpen = false;
        }
    }

    private async void SlotView_StreamDockDropRequested(
        StreamSlotView targetSlot,
        string url,
        string? streamName,
        DockDirection direction)
    {
        _dockingOverlayPresenter?.Hide();
        if (DropZoneService.IsEdge(direction))
        {
            await CreateDockedSlotFromDropAsync(targetSlot, direction, url, streamName);
            return;
        }

        await LoadDroppedStreamIntoSlotAsync(targetSlot, url, streamName);
    }

    private void SlotsGrid_DragOver(object sender, DragEventArgs e)
    {
        var position = GetDockPointerPosition(sender, e);
        var hasTargetSlot = TryGetDropTargetSlot(position, out var targetSlot);

        if (hasTargetSlot &&
            targetSlot is not null &&
            IsSupportedDockDrop(e.Data) &&
            TryGetDropDirection(targetSlot, position, out var direction))
        {
            var canApplyDirection = direction == DockDirection.Center || CanCreateDockedSlot();
            e.Effects = canApplyDirection ? DragDropEffects.Copy : DragDropEffects.None;
            if (canApplyDirection)
            {
                _lastDockTargetSlot = targetSlot;
                _lastDockDirection = direction;
                ShowDockingPreview(targetSlot, direction);
            }
            else
            {
                _lastDockTargetSlot = null;
                _lastDockDirection = DockDirection.None;
                HideDockingPreview();
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
            _lastDockTargetSlot = null;
            _lastDockDirection = DockDirection.None;
            HideDockingPreview();
        }

        e.Handled = true;
    }

    private void SlotsGrid_DragLeave(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(sender, _dockingInputOverlay) && _dockingInputOverlay is not null)
        {
            var position = e.GetPosition(_dockingInputOverlay);
            var bounds = new Rect(0, 0, _dockingInputOverlay.ActualWidth, _dockingInputOverlay.ActualHeight);
            if (bounds.Contains(position))
            {
                return;
            }
        }

        HideDockingPreview();
    }

    private async void SlotsGrid_Drop(object sender, DragEventArgs e)
    {
        _dockingOverlayPresenter?.Hide();
        var position = GetDockPointerPosition(sender, e);
        if (!TryGetDropTargetSlot(position, out var targetSlot) || targetSlot is null)
        {
            targetSlot = _lastDockTargetSlot;
        }

        if (targetSlot is null)
        {
            StatusTextBlock.Text = "Drop the stream over a visible playback area.";
            e.Handled = true;
            HideDockingInputOverlay();
            return;
        }

        if (!TryGetDropDirection(targetSlot, position, out var direction))
        {
            direction = ReferenceEquals(targetSlot, _lastDockTargetSlot)
                ? _lastDockDirection
                : DockDirection.None;
        }

        if (direction == DockDirection.None)
        {
            e.Handled = true;
            HideDockingInputOverlay();
            return;
        }

        if (DropZoneService.IsEdge(direction) &&
            StreamDropDataReader.TryGetDroppedStream(e.Data, _streamNavigationService, out var dockedUrl, out var dockedStreamName))
        {
            await CreateDockedSlotFromDropAsync(targetSlot, direction, dockedUrl, dockedStreamName);
            e.Handled = true;
            HideDockingInputOverlay();
            return;
        }

        if (TryGetDroppedSlotId(e.Data, out var sourceSlotId) &&
            _slots.FirstOrDefault(slot => slot.SlotId == sourceSlotId) is { } sourceSlot &&
            !ReferenceEquals(sourceSlot, targetSlot))
        {
            await SwapSlotStreamsAsync(sourceSlot, targetSlot);
            e.Handled = true;
            HideDockingInputOverlay();
            return;
        }

        if (StreamDropDataReader.TryGetDroppedStream(e.Data, _streamNavigationService, out var url, out var streamName))
        {
            await LoadDroppedStreamIntoSlotAsync(targetSlot, url, streamName);
            e.Handled = true;
        }

        HideDockingInputOverlay();
    }

    private Point GetDockPointerPosition(object sender, DragEventArgs e)
    {
        if (_dockingInputOverlay is not null && ReferenceEquals(sender, _dockingInputOverlay))
        {
            var overlayPosition = e.GetPosition(_dockingInputOverlay);
            return _dockingInputOverlay.TranslatePoint(overlayPosition, SlotsGrid);
        }

        return e.GetPosition(SlotsGrid);
    }

    private void ShowDockingPreview(StreamSlotView targetSlot, DockDirection direction)
    {
        if (_dockingInputOverlayPopup?.IsOpen == true)
        {
            ShowDockingInputPreview(targetSlot, direction);
            _dockingOverlayPresenter?.Hide();
            return;
        }

        _dockingOverlayPresenter?.Show(targetSlot, direction);
    }

    private void HideDockingPreview()
    {
        HideDockingInputPreview();
        _dockingOverlayPresenter?.Hide();
    }

    private void ShowDockingInputPreview(StreamSlotView targetSlot, DockDirection direction)
    {
        if (_dockingInputOverlay is null || _dockingInputPreview is null || direction == DockDirection.None)
        {
            return;
        }

        var targetTopLeft = targetSlot.TranslatePoint(new Point(0, 0), SlotsGrid);
        var targetBounds = new Rect(targetTopLeft, targetSlot.RenderSize);
        var previewBounds = GetDockingInputPreviewBounds(targetBounds, direction);

        Canvas.SetLeft(_dockingInputPreview, previewBounds.X);
        Canvas.SetTop(_dockingInputPreview, previewBounds.Y);
        _dockingInputPreview.Width = previewBounds.Width;
        _dockingInputPreview.Height = previewBounds.Height;
        _dockingInputPreview.Visibility = Visibility.Visible;
    }

    private void HideDockingInputPreview()
    {
        if (_dockingInputPreview is not null)
        {
            _dockingInputPreview.Visibility = Visibility.Collapsed;
        }
    }

    private static Rect GetDockingInputPreviewBounds(Rect targetBounds, DockDirection direction)
    {
        return direction switch
        {
            DockDirection.Left => new Rect(targetBounds.X, targetBounds.Y, targetBounds.Width / 2, targetBounds.Height),
            DockDirection.Right => new Rect(targetBounds.X + targetBounds.Width / 2, targetBounds.Y, targetBounds.Width / 2, targetBounds.Height),
            DockDirection.Top => new Rect(targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height / 2),
            DockDirection.Bottom => new Rect(targetBounds.X, targetBounds.Y + targetBounds.Height / 2, targetBounds.Width, targetBounds.Height / 2),
            DockDirection.Center => new Rect(
                targetBounds.X + targetBounds.Width * 0.25,
                targetBounds.Y + targetBounds.Height * 0.25,
                targetBounds.Width * 0.5,
                targetBounds.Height * 0.5),
            _ => Rect.Empty
        };
    }

    private bool TryGetDropTargetSlot(Point position, out StreamSlotView? targetSlot)
    {
        targetSlot = null;
        var visibleSlots = GetVisibleSlotViews().ToArray();

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

    private IEnumerable<StreamSlotView> GetVisibleSlotViews()
    {
        if (_currentLayoutTree?.Root is not null)
        {
            var slotsById = _slots.ToDictionary(slot => slot.SlotId);
            foreach (var slotId in LayoutTreePresetConverter.GetVisibleSlotIds(_currentLayoutTree))
            {
                if (slotsById.TryGetValue(slotId, out var slot))
                {
                    yield return slot;
                }
            }

            yield break;
        }

        foreach (var slot in SlotsGrid.Children.OfType<StreamSlotView>())
        {
            yield return slot;
        }
    }

    private bool TryGetDropDirection(StreamSlotView targetSlot, Point positionInSlotsGrid, out DockDirection direction)
    {
        var topLeft = targetSlot.TranslatePoint(new Point(0, 0), SlotsGrid);
        var bounds = new Rect(topLeft, targetSlot.RenderSize);
        direction = _dropZoneService.Calculate(bounds, positionInSlotsGrid);
        return direction != DockDirection.None;
    }

    private bool IsSupportedDockDrop(IDataObject data)
    {
        return TryGetDroppedSlotId(data, out _) ||
               StreamDropDataReader.TryGetDroppedStream(data, _streamNavigationService, out _, out _);
    }

    private static bool TryGetDroppedSlotId(IDataObject data, out int slotId)
    {
        slotId = 0;
        if (!data.GetDataPresent(StreamDragDataFormats.SlotId))
        {
            return false;
        }

        var value = data.GetData(StreamDragDataFormats.SlotId)?.ToString();
        return int.TryParse(value, out slotId) &&
               slotId is >= 1 and <= PlaybackTestPlanService.MaxSlotCount;
    }

    private bool CanCreateDockedSlot()
    {
        return GetNextAvailableSlotId() is not null;
    }

    private int? GetNextAvailableSlotId()
    {
        var visibleSlotIds = _currentLayoutTree is null
            ? GetVisibleSlotViews().Select(slot => slot.SlotId).ToHashSet()
            : LayoutTreePresetConverter.GetVisibleSlotIds(_currentLayoutTree).ToHashSet();

        return Enumerable.Range(1, PlaybackTestPlanService.MaxSlotCount)
            .FirstOrDefault(slotId => !visibleSlotIds.Contains(slotId)) is var slotId && slotId > 0
            ? slotId
            : null;
    }

    private async Task CreateDockedSlotFromDropAsync(
        StreamSlotView targetSlot,
        DockDirection direction,
        string url,
        string? streamName)
    {
        if (_currentLayoutTree?.Root is null || GetNextAvailableSlotId() is not { } newSlotId)
        {
            StatusTextBlock.Text = "No empty slot is available for a new split.";
            return;
        }

        var targetLeaf = LayoutTreePresetConverter
            .GetLeaves(_currentLayoutTree.Root)
            .FirstOrDefault(leaf => leaf.SlotId == targetSlot.SlotId);
        if (targetLeaf is null)
        {
            StatusTextBlock.Text = "Could not find the target layout leaf.";
            return;
        }

        var newLeaf = LayoutTreePresetConverter.CreateLeaf(newSlotId);
        var nextRoot = _layoutTreeMutationService.InsertSplit(_currentLayoutTree.Root, targetLeaf.Id, newLeaf, direction);
        _currentLayoutTree = new LayoutTreeDocument
        {
            SourceLayoutId = "dynamic",
            Root = nextRoot,
            ActiveLeafId = newLeaf.Id
        };
        ApplyLayoutTree(_currentLayoutTree);

        var newSlot = _slots.First(slot => slot.SlotId == newSlotId);
        await LoadDroppedStreamIntoSlotAsync(newSlot, url, streamName);
        StatusTextBlock.Text = $"Docked {(string.IsNullOrWhiteSpace(streamName) ? url : streamName)} into Slot {newSlotId}.";
    }

    private async Task SwapSlotStreamsAsync(StreamSlotView sourceSlot, StreamSlotView targetSlot)
    {
        var result = _slotSwapService.SwapStreams(sourceSlot.CreateRuntimeState(), targetSlot.CreateRuntimeState());
        await NavigateSlotAsync(sourceSlot, result.SourceSlot.StreamUrl, result.SourceSlot.StreamName);
        await NavigateSlotAsync(targetSlot, result.TargetSlot.StreamUrl, result.TargetSlot.StreamName);
        SelectSlot(targetSlot);
        StatusTextBlock.Text = $"Swapped Slot {sourceSlot.SlotId} with Slot {targetSlot.SlotId}.";
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
            LayoutTree = _currentLayoutTree,
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

        if (_currentLayoutTree?.Root is not null)
        {
            return _workspaceSlotVisibilityService.BlankHiddenSlots(workspace, _currentLayoutTree);
        }

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
            },
            AutoUpdate = _updateService?.CurrentState ?? _loadedAppState?.AutoUpdate ?? new AutoUpdateState()
        };
    }

    private async Task ApplyWorkspaceAsync(WorkspacePreset workspace, bool setActiveWorkspace)
    {
        var preparedWorkspace = _workspaceRestoreService.Prepare(workspace, _layouts);
        workspace = preparedWorkspace.Workspace;
        var layout = preparedWorkspace.Layout;
        if (preparedWorkspace.LayoutTree?.Root is not null)
        {
            _selectedLayout = layout;
            _currentLayoutTree = preparedWorkspace.LayoutTree;
            RefreshLayoutSelector();
            ApplyLayoutTree(_currentLayoutTree);
        }
        else if (_selectedLayout?.Id.Equals(layout.Id, StringComparison.OrdinalIgnoreCase) != true)
        {
            await ApplySelectedLayoutAsync(layout, clearHiddenSlots: false);
        }
        else
        {
            ApplyLayout(layout);
        }

        var workspaceSlots = workspace.Slots.ToDictionary(slot => slot.SlotId);
        var visibleSlotIds = GetVisibleSlotIds(layout, preparedWorkspace.LayoutTree);

        foreach (var slot in _slots.OrderBy(slot => slot.SlotId))
        {
            if (!workspaceSlots.TryGetValue(slot.SlotId, out var workspaceSlot))
            {
                continue;
            }

            slot.SetMuted(false);
            if (visibleSlotIds.Contains(slot.SlotId))
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
        if (_currentLayoutTree?.Root is not null)
        {
            targetSlotId = ResolveVisibleSlotId(_currentLayoutTree, selectedSlotId);
        }
        else if (_selectedLayout is LayoutPreset layout)
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
        if (_currentLayoutTree?.Root is not null)
        {
            if (_selectedSlot is null || LayoutTreePresetConverter.GetVisibleSlotIds(_currentLayoutTree).Contains(_selectedSlot.SlotId))
            {
                return;
            }

            SelectSlotFromState(_selectedSlot.SlotId);
            return;
        }

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
        if (_currentLayoutTree?.Root is not null)
        {
            visibleSlotIds = LayoutTreePresetConverter.GetVisibleSlotIds(_currentLayoutTree).ToHashSet();
        }
        else if (_selectedLayout is LayoutPreset layout)
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

    private static HashSet<int> GetVisibleSlotIds(LayoutPreset layout, LayoutTreeDocument? layoutTree)
    {
        if (layoutTree?.Root is not null)
        {
            return LayoutTreePresetConverter.GetVisibleSlotIds(layoutTree).ToHashSet();
        }

        return layout.Slots
            .Select(slot => slot.SlotId)
            .ToHashSet();
    }

    private static int? ResolveVisibleSlotId(LayoutTreeDocument layoutTree, int? requestedSlotId)
    {
        if (requestedSlotId is null)
        {
            return null;
        }

        var visibleSlotIds = LayoutTreePresetConverter.GetVisibleSlotIds(layoutTree)
            .Where(slotId => slotId is >= 1 and <= PlaybackTestPlanService.MaxSlotCount)
            .Order()
            .ToArray();
        if (visibleSlotIds.Length == 0)
        {
            return null;
        }

        return visibleSlotIds.Contains(requestedSlotId.Value)
            ? requestedSlotId.Value
            : visibleSlotIds[0];
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
