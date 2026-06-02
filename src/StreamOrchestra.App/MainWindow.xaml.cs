using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.ComponentModel;
using System.Runtime.InteropServices;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;
using StreamOrchestra.App.Views;

namespace StreamOrchestra.App;

public partial class MainWindow : Window
{
    private const int KeyPressedMask = 0x8000;

    // 사이드탭 토글 버튼 아이콘: 열린 상태에선 닫기(close), 닫힌 상태에선 열기(open) 아이콘을 표시한다.
    private static readonly ImageSource SidebarCloseIcon =
        new BitmapImage(new Uri("pack://application:,,,/Assets/sidebar-close.png"));
    private static readonly ImageSource SidebarOpenIcon =
        new BitmapImage(new Uri("pack://application:,,,/Assets/sidebar-open.png"));

    private readonly WebViewProfileService _profileService = new();
    private readonly WebViewRuntimeDiagnosticsService _diagnosticsService = new();
    private readonly PresetStorageService _presetStorageService = new();
    private readonly LayoutPresetService _layoutPresetService;
    private readonly StreamNavigationService _streamNavigationService = new();
    private readonly SlotSelectionService _slotSelectionService = new();
    private readonly SlotSwapService _slotSwapService = new();
    private readonly SlotRemovalSelectionService _slotRemovalSelectionService = new();
    private readonly LayoutTemplateCandidateService _layoutTemplateCandidateService = new();
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
    private readonly DispatcherTimer _swapModeKeyboardPollTimer;
    private WorkspacePreset? _activeWorkspace;
    private StreamSlotView? _selectedSlot;
    private ExplorerPanel? _explorerPanel;
    private readonly LayoutCardPresenter _layoutCardPresenter = new();
    private LayoutCardMode _layoutCardMode = LayoutCardMode.Add;
    private bool _isExplorerPanelVisible = true;
    private GridLength _lastExplorerColumnWidth = new(360);
    private LayoutPreset? _selectedLayout;
    private ShortcutSettings _shortcutSettings = new();

    public MainWindow()
    {
        _layoutPresetService = new LayoutPresetService(_presetStorageService.DataFolder);
        _workspacePresetNormalizationService = new WorkspacePresetNormalizationService(_streamNavigationService);
        _workspaceRestoreService = new WorkspaceRestoreService(
            _workspacePresetNormalizationService,
            _workspaceSlotVisibilityService);
        InitializeComponent();
        _layoutCardPresenter.CardChosen += LayoutCardPresenter_CardChosen;

        _loadedAppState = _presetStorageService.LoadAppState();
        RestoreWindowPlacement(_loadedAppState?.Window);
        _shortcutSettings = _loadedAppState?.Shortcuts ?? new ShortcutSettings();

        _updateService = TryCreateUpdateService(_loadedAppState?.AutoUpdate ?? new AutoUpdateState());

        CreateExplorerPanel();
        CreateSlots();
        ApplyShortcutLabelsToSlots();
        LoadLayouts();
        LoadWorkspacePresets();
        ApplyViewState(_loadedAppState);
        RefreshQualityMenuChecks();
        _diagnosticsTimer = CreateDiagnosticsTimer();
        StatusTextBlock.Text = $"Profile data persists under: {_profileService.BaseProfileFolder}";
        UpdateDiagnostics();
        _diagnosticsTimer.Start();
        _swapModeKeyboardPollTimer = CreateSwapModeKeyboardPollTimer();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;
        Deactivated += (_, _) =>
        {
            SetRemoveModeActive(false);
            SetSwapModeActive(false);
        };
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        HandleShortcutKeyEvent(e, pressed: true, e.IsRepeat);
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        HandleShortcutKeyEvent(e, pressed: false, e.IsRepeat);
    }

    private void HandleShortcutKeyEvent(KeyEventArgs e, bool pressed, bool isRepeat)
    {
        // 텍스트 입력란(예: 탐색 URL 창)에 포커스가 있으면 임의 키 단축키가 입력을 가로채지 않도록 무시한다.
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        if (!ShortcutKeyResolver.TryResolveVirtualKey(e, out var virtualKey))
        {
            return;
        }

        var action = _shortcutSettings.GetAction(virtualKey);
        if (action is null)
        {
            return;
        }

        // Tab(포커스 이동)·Alt(메뉴 활성화)에 매핑된 단축키는 기본 동작을 막기 위해 이벤트를 소비한다.
        if (virtualKey is ShortcutKeyResolver.VkTab or ShortcutKeyResolver.VkMenu)
        {
            e.Handled = true;
        }

        ActivateAction(action.Value, pressed, isRepeat);
    }

    // 화면 조작 동작을 활성/비활성한다(창 키 이벤트·WebView2 메시지 공용).
    private void ActivateAction(ShortcutAction action, bool pressed, bool isRepeat)
    {
        switch (action)
        {
            case ShortcutAction.Remove:
                SetRemoveModeActive(pressed);
                break;
            case ShortcutAction.Swap:
                SetSwapModeActive(pressed);
                break;
            case ShortcutAction.Switch:
                if (pressed)
                {
                    if (!isRepeat)
                    {
                        ShowSwitchLayoutCards();
                    }
                }
                else if (_layoutCardMode == LayoutCardMode.Switch)
                {
                    HideSwitchLayoutCards();
                }

                break;
            case ShortcutAction.ToggleExplorer:
                // 홀드가 아니라 누를 때마다 토글한다(자동 반복은 무시).
                if (pressed && !isRepeat)
                {
                    SetExplorerPanelVisible(!_isExplorerPanelVisible);
                    StatusTextBlock.Text = _isExplorerPanelVisible
                        ? "탐색 패널을 표시했습니다."
                        : "탐색 패널을 숨겼습니다.";
                }

                break;
        }
    }

    private void OnSlotKeyStateChanged(int virtualKey, bool pressed)
    {
        // WebView2 JS는 키 상태 전이마다 한 번씩만 보고하므로 자동 반복(isRepeat) 개념이 없다.
        var action = _shortcutSettings.GetAction(virtualKey);
        if (action is not null)
        {
            ActivateAction(action.Value, pressed, isRepeat: false);
        }
    }

    // 현재 단축키 매핑을 슬롯 오버레이 안내 문구(제거 버튼 툴팁·교체 오버레이 문구)에 반영한다.
    private void ApplyShortcutLabelsToSlots()
    {
        var removeLabel = _shortcutSettings.RemoveKey.Name;
        var swapLabel = _shortcutSettings.SwapKey.Name;

        foreach (var slot in _slots)
        {
            slot.SetRemoveKeyLabel(removeLabel);
            slot.SetSwapKeyLabel(swapLabel);
        }
    }

    private void CreateExplorerPanel()
    {
        var explorerPanel = new ExplorerPanel(_profileService, _streamNavigationService);
        _explorerPanel = explorerPanel;
        _explorerPanel.HostDragStarted += ShowLayoutCards;
        _explorerPanel.HostDragCompleted += HideLayoutCards;
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
            slotView.SlotSwapRequested += SlotView_SlotSwapRequested;
            slotView.RemoveSlotRequested += SlotView_RemoveSlotRequested;
            slotView.KeyStateChanged += OnSlotKeyStateChanged;
            slotView.SwapDragCompleted += ReconcileSwapModeWithKeyboardState;
            _slots.Add(slotView);
        }
    }

    private void LoadLayouts(string? selectedLayoutId = null)
    {
        var currentLayoutId = selectedLayoutId ?? _selectedLayout?.Id;

        // 빌트인 템플릿은 더 이상 사용하지 않는다. 앱은 사용자 지정 레이아웃만 사용한다.
        _builtInLayouts = [];
        _customLayouts = _layoutPresetService.LoadCustomLayouts().ToList();
        _layouts = _customLayouts;

        _selectedLayout = ResolveInitialLayout(currentLayoutId);

        RefreshLayoutSelector();
        if (_selectedLayout is not null)
        {
            ApplyLayout(_selectedLayout);
        }
        else
        {
            ShowEmptyLayoutState();
        }
    }

    private LayoutPreset? ResolveInitialLayout(string? layoutId)
    {
        if (_layouts.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(layoutId) &&
            _layouts.FirstOrDefault(layout => layout.Id.Equals(layoutId, StringComparison.OrdinalIgnoreCase)) is { } match)
        {
            return match;
        }

        return _layouts[0];
    }

    private void ShowEmptyLayoutState()
    {
        _selectedLayout = null;
        SlotsGrid.Children.Clear();
        SlotsGrid.RowDefinitions.Clear();
        SlotsGrid.ColumnDefinitions.Clear();

        SlotsGrid.Children.Add(new TextBlock
        {
            Text = "표시할 레이아웃이 없습니다.\n설정 → 레이아웃에서 사용자 지정 레이아웃을 만든 뒤,\n채널을 드래그해 카드에서 선택하세요.",
            Foreground = new SolidColorBrush(Color.FromRgb(120, 132, 146)),
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        // 보이는 슬롯이 하나도 없으므로 모든 슬롯의 재생을 중지한다.
        StopPlaybackForHiddenSlots([]);

        StatusTextBlock.Text = "사용자 지정 레이아웃이 없습니다. 설정 → 레이아웃에서 레이아웃을 생성하세요.";
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
        var savedWorkspaces = _presetStorageService.LoadWorkspaces();
        _workspaces = _layouts.Count == 0
            ? savedWorkspaces.ToList()
            : savedWorkspaces
                .Select(workspace => _workspaceRestoreService.Prepare(workspace, _layouts).Workspace)
                .ToList();
        RefreshWorkspaceComboBox();
    }

    private void ApplyLayout(LayoutPreset layout)
    {
        _selectedLayout = layout;
        SlotsGrid.Children.Clear();
        SlotsGrid.RowDefinitions.Clear();
        SlotsGrid.ColumnDefinitions.Clear();

        var slotsById = _slots.ToDictionary(slot => slot.SlotId);
        var content = LayoutGridRenderer.Build(layout, slotsById);
        SlotsGrid.Children.Add(content);

        // 새 레이아웃에서 사라진 슬롯은 화면에서 빠지므로 재생을 즉시 중지한다.
        StopPlaybackForHiddenSlots(layout.Slots.Select(slot => slot.SlotId).ToHashSet());

        StatusTextBlock.Text = $"Layout applied: {layout.Name} ({layout.GridColumns}x{layout.GridRows}, {layout.EffectiveSlotCount} visible slots)";
    }

    // 보이지 않게 된 슬롯(레이아웃에 포함되지 않은 슬롯)의 재생을 about:blank로 중지한다.
    private void StopPlaybackForHiddenSlots(IReadOnlyCollection<int> visibleSlotIds)
    {
        foreach (var slot in _slots)
        {
            if (!visibleSlotIds.Contains(slot.SlotId) &&
                !slot.CurrentUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                _ = ClearSlotSafelyAsync(slot);
            }
        }
    }

    private static async Task ClearSlotSafelyAsync(StreamSlotView slot)
    {
        try
        {
            await slot.ClearAsync();
        }
        catch
        {
            // 슬롯 정리 실패는 사용자 흐름을 막지 않는다.
        }
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
            _customLayouts,
            currentLayout)
        {
            Owner = this
        };

        dialog.ShowDialog();

        if (!dialog.HasCustomLayoutChanges)
        {
            return;
        }

        // "저장 후 적용"으로 닫혔으면 해당 레이아웃을 바로 적용하고, 그 외에는 목록만 갱신한다.
        LoadLayouts(dialog.AppliedLayoutId ?? currentLayout?.Id);
        StatusTextBlock.Text = dialog.AppliedLayoutId is not null
            ? "사용자 지정 레이아웃을 적용했습니다."
            : "사용자 지정 레이아웃 목록을 갱신했습니다.";
    }

    private void ShortcutSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ShortcutSettingsDialog(_shortcutSettings)
        {
            Owner = this
        };

        // 키를 캡처할 때마다 즉시 적용·저장한다(다이얼로그는 변경만 통지).
        dialog.ShortcutsChanged += ApplyShortcutSettings;
        dialog.ShowDialog();
    }

    private void ApplyShortcutSettings(ShortcutSettings settings)
    {
        _shortcutSettings = settings;
        ApplyShortcutLabelsToSlots();
        UpdateExplorerToggleTooltip();

        // 진행 중이던 모드 오버레이는 새 매핑과 어긋날 수 있으니 모두 닫는다.
        SetRemoveModeActive(false);
        SetSwapModeActive(false);

        // 단축키 변경은 즉시 영속화해 크래시 등으로 손실되지 않게 한다.
        _presetStorageService.SaveAppState(CaptureAppState());

        StatusTextBlock.Text = "단축키 설정을 적용했습니다. " +
            $"제거 {_shortcutSettings.RemoveKey.Name} · " +
            $"교체 {_shortcutSettings.SwapKey.Name} · " +
            $"전환 {_shortcutSettings.SwitchKey.Name} · " +
            $"사이드바 {_shortcutSettings.ToggleExplorerKey.Name}.";
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

    private async void SlotView_StreamUrlDropRequested(StreamSlotView targetSlot, string url, string? streamName)
    {
        await LoadDroppedStreamIntoSlotAsync(targetSlot, url, streamName);
    }

    private void SlotView_SlotSwapRequested(StreamSlotView targetSlot, int sourceSlotId)
    {
        if (_slots.FirstOrDefault(slot => slot.SlotId == sourceSlotId) is not { } sourceSlot ||
            ReferenceEquals(sourceSlot, targetSlot))
        {
            return;
        }

        _ = SwapSlotStreamsAsync(sourceSlot, targetSlot);
    }

    // 슬롯 제거 버튼 클릭 → 삭제 예정 상태를 토글하고, 남은 슬롯 수에 맞는 레이아웃 카드를 갱신한다.
    private void SlotView_RemoveSlotRequested(StreamSlotView slot)
    {
        ToggleRemoveScreen(slot);
    }

    private void ToggleRemoveScreen(StreamSlotView slot)
    {
        if (_selectedLayout is not { } layout)
        {
            return;
        }

        var visibleSlotIds = layout.Slots
            .Select(visibleSlot => visibleSlot.SlotId)
            .OrderBy(slotId => slotId)
            .ToArray();
        if (!visibleSlotIds.Contains(slot.SlotId))
        {
            return;
        }

        if (!_slotRemovalSelectionService.ToggleSlot(slot.SlotId, visibleSlotIds.Length))
        {
            StatusTextBlock.Text = "마지막 한 화면은 제거할 수 없습니다.";
            return;
        }

        UpdateRemoveSlotButtons(isActive: true, visibleSlotIds.ToHashSet());
        UpdateRemovalLayoutCards(visibleSlotIds);
    }

    // 제거 모드(Ctrl 홀드): 보이는 모든 슬롯 위에 제거 버튼을 표시한다.
    // 마지막 한 화면이면 버튼을 띄우지 않고 안내만 한다.
    private void SetRemoveModeActive(bool isActive)
    {
        if (!isActive)
        {
            ClearRemoveSelection(hideCards: true);
            return;
        }

        var visibleSlotIds = _selectedLayout is { } layout
            ? layout.Slots.Select(s => s.SlotId).ToHashSet()
            : [];

        if (visibleSlotIds.Count <= 1)
        {
            StatusTextBlock.Text = "마지막 한 화면은 제거할 수 없습니다.";
            ClearRemoveSelection(hideCards: true);
            return;
        }

        UpdateRemoveSlotButtons(isActive: true, visibleSlotIds);
    }

    private void UpdateRemoveSlotButtons(bool isActive, HashSet<int> visibleSlotIds)
    {
        foreach (var slot in _slots)
        {
            slot.SetRemoveModeActive(
                isActive && visibleSlotIds.Contains(slot.SlotId),
                _slotRemovalSelectionService.IsSelected(slot.SlotId));
        }
    }

    private void ClearRemoveSelection(bool hideCards)
    {
        _slotRemovalSelectionService.Clear();

        foreach (var slot in _slots)
        {
            slot.SetRemoveModeActive(false);
        }

        if (hideCards && _layoutCardMode == LayoutCardMode.Remove)
        {
            _layoutCardPresenter.Hide();
            _layoutCardMode = LayoutCardMode.Add;
        }
    }

    private void UpdateRemovalLayoutCards(IReadOnlyCollection<int> visibleSlotIds)
    {
        var removalSlotIds = _slotRemovalSelectionService.GetSelectedSlotIds();
        if (removalSlotIds.Count == 0)
        {
            if (_layoutCardMode == LayoutCardMode.Remove)
            {
                _layoutCardPresenter.Hide();
                _layoutCardMode = LayoutCardMode.Add;
            }

            StatusTextBlock.Text = "제거할 화면을 선택하세요.";
            return;
        }

        var targetSlotCount = _slotRemovalSelectionService.GetTargetSlotCount(visibleSlotIds.Count);
        var candidates = _layoutTemplateCandidateService.GetTemplatesForSlotCount(_layouts, targetSlotCount);
        var removalLabel = string.Join(", ", removalSlotIds.Select(slotId => $"Slot {slotId}"));

        _layoutCardMode = LayoutCardMode.Remove;
        _layoutCardPresenter.Show(candidates, SlotsGrid, LayoutCardMode.Remove);
        StatusTextBlock.Text = candidates.Count > 0
            ? $"{removalLabel} 제거 예정. 슬롯 {targetSlotCount}개 레이아웃 카드를 선택하세요."
            : $"{removalLabel} 제거 예정이지만 슬롯 {targetSlotCount}개에 맞는 레이아웃 템플릿이 없습니다.";
    }

    // 교체 모드(왼쪽 Shift 홀드): 보이는 모든 슬롯 위에 드래그용 오버레이를 표시한다.
    private void SetSwapModeActive(bool isActive)
    {
        // 레이아웃 카드 오버레이가 떠 있는 동안에는 교체 모드를 켜지 않는다.
        if (isActive && _layoutCardPresenter.IsOpen)
        {
            return;
        }

        var visibleSlotIds = _selectedLayout is { } layout
            ? layout.Slots.Select(s => s.SlotId).ToHashSet()
            : [];

        foreach (var slot in _slots)
        {
            slot.SetSwapModeActive(isActive && visibleSlotIds.Contains(slot.SlotId));
        }

        if (isActive && visibleSlotIds.Count > 0)
        {
            _swapModeKeyboardPollTimer.Start();
        }
        else
        {
            _swapModeKeyboardPollTimer.Stop();
        }
    }

    private void ReconcileSwapModeWithKeyboardState()
    {
        // 교체 모드에 매핑된 키가 물리적으로 떼졌으면 오버레이를 닫는다(드래그 중 KeyUp이 들어오지 않는 경우 대비).
        if (!IsKeyPhysicallyDown(_shortcutSettings.SwapKey.VirtualKey))
        {
            SetSwapModeActive(false);
        }
    }

    private DispatcherTimer CreateSwapModeKeyboardPollTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        timer.Tick += (_, _) => ReconcileSwapModeWithKeyboardState();
        return timer;
    }

    private static bool IsKeyPhysicallyDown(int virtualKey)
    {
        return virtualKey != 0 && (GetAsyncKeyState(virtualKey) & KeyPressedMask) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    // 선택된 템플릿으로 전환하면서, 제거 대상을 뺀 생존 채널을 새 템플릿 슬롯에 순서대로 다시 채운다(compaction).
    private async Task ApplyRemovalAsync(IReadOnlyCollection<int> removalSlotIds, LayoutPreset template)
    {
        if (_selectedLayout is not { } currentLayout)
        {
            return;
        }

        var visibleSlotIds = currentLayout.Slots
            .Select(visibleSlot => visibleSlot.SlotId)
            .OrderBy(slotId => slotId)
            .ToArray();
        if (visibleSlotIds.Length <= 1 || removalSlotIds.Count >= visibleSlotIds.Length)
        {
            StatusTextBlock.Text = "마지막 한 화면은 제거할 수 없습니다.";
            return;
        }

        var survivors = visibleSlotIds
            .Where(slotId => !removalSlotIds.Contains(slotId))
            .Select(slotId => _slots.First(candidate => candidate.SlotId == slotId))
            .Select(candidate => (candidate.CurrentUrl, candidate.CurrentStreamName))
            .ToArray();

        await RefillTemplateSlotsAsync(template, survivors);

        StatusTextBlock.Text = $"화면 {removalSlotIds.Count}개를 제거하고 '{template.Name}' 레이아웃으로 전환했습니다.";
        UpdateDiagnostics();
    }

    // 왼쪽 Alt 카드 선택: 현재 화면 수(N)를 유지한 채 같은 슬롯 수의 다른 레이아웃으로 전환한다.
    // 현재 채널을 슬롯 순서대로 새 템플릿 슬롯에 다시 채운다.
    private async Task ApplySwitchAsync(LayoutPreset template)
    {
        if (_selectedLayout is not { } currentLayout)
        {
            return;
        }

        if (currentLayout.Id.Equals(template.Id, StringComparison.OrdinalIgnoreCase))
        {
            StatusTextBlock.Text = $"이미 '{template.Name}' 레이아웃입니다.";
            return;
        }

        var streams = currentLayout.Slots
            .Select(visibleSlot => visibleSlot.SlotId)
            .OrderBy(slotId => slotId)
            .Select(slotId => _slots.First(candidate => candidate.SlotId == slotId))
            .Select(candidate => (candidate.CurrentUrl, candidate.CurrentStreamName))
            .ToArray();

        await RefillTemplateSlotsAsync(template, streams);

        StatusTextBlock.Text = $"'{template.Name}' 레이아웃으로 전환했습니다.";
        UpdateDiagnostics();
    }

    // 새 템플릿을 적용하고, 보존된 채널을 템플릿 슬롯에 순서대로 다시 채운다.
    private async Task RefillTemplateSlotsAsync(
        LayoutPreset template,
        IReadOnlyList<(string CurrentUrl, string CurrentStreamName)> survivors)
    {
        var targetSlotIds = template.Slots
            .Select(templateSlot => templateSlot.SlotId)
            .OrderBy(slotId => slotId)
            .ToArray();

        await ApplySelectedLayoutAsync(template, clearHiddenSlots: true);

        for (var index = 0; index < targetSlotIds.Length; index++)
        {
            var targetSlot = _slots.First(candidate => candidate.SlotId == targetSlotIds[index]);
            var desiredUrl = index < survivors.Count ? survivors[index].CurrentUrl : "about:blank";
            var desiredName = index < survivors.Count ? survivors[index].CurrentStreamName : null;

            if (targetSlot.CurrentUrl.Equals(desiredUrl, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (desiredUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                await targetSlot.ClearAsync();
            }
            else
            {
                await NavigateSlotAsync(targetSlot, desiredUrl, desiredName);
            }
        }
    }

    private void ShowLayoutCards()
    {
        ClearRemoveSelection(hideCards: false);
        _layoutCardMode = LayoutCardMode.Add;
        var currentVisibleSlotCount = _selectedLayout?.EffectiveSlotCount ?? GetVisibleSlotViews().Count();
        var candidates = _layoutTemplateCandidateService.GetCandidates(_layouts, currentVisibleSlotCount);
        _layoutCardPresenter.Show(candidates, SlotsGrid, LayoutCardMode.Add);

        StatusTextBlock.Text = candidates.Count > 0
            ? $"레이아웃 카드 {candidates.Count}개 표시 중 (슬롯 {currentVisibleSlotCount + 1}개)."
            : $"슬롯 {currentVisibleSlotCount + 1}개에 맞는 레이아웃 템플릿이 없습니다.";
    }

    // 왼쪽 Alt를 누르고 있는 동안 현재 화면 수(N)와 같은 슬롯 수의 레이아웃 카드를 표시한다.
    private void ShowSwitchLayoutCards()
    {
        // 이미 카드가 떠 있으면(예: 제거 카드) 덮어쓰지 않는다.
        if (_layoutCardPresenter.IsOpen)
        {
            return;
        }

        ClearRemoveSelection(hideCards: false);
        SetRemoveModeActive(false);

        var currentCount = _selectedLayout?.EffectiveSlotCount ?? GetVisibleSlotViews().Count();
        if (currentCount <= 0)
        {
            StatusTextBlock.Text = "표시 중인 화면이 없어 레이아웃 카드를 띄울 수 없습니다.";
            return;
        }

        var candidates = _layoutTemplateCandidateService.GetTemplatesForSlotCount(_layouts, currentCount);
        _layoutCardMode = LayoutCardMode.Switch;
        _layoutCardPresenter.Show(candidates, SlotsGrid, LayoutCardMode.Switch);

        StatusTextBlock.Text = candidates.Count > 0
            ? $"슬롯 {currentCount}개 레이아웃 카드 {candidates.Count}개 표시 중. 전환할 카드를 선택하세요."
            : $"슬롯 {currentCount}개에 맞는 레이아웃 템플릿이 없습니다.";
    }

    private void HideSwitchLayoutCards()
    {
        _layoutCardPresenter.Hide();
        _layoutCardMode = LayoutCardMode.Add;
    }

    private void HideLayoutCards()
    {
        // 제거/전환 카드는 카드를 눌러야만 닫힌다. 드래그 종료(추가 모드)에서만 자동으로 닫는다.
        if (_layoutCardMode is LayoutCardMode.Remove or LayoutCardMode.Switch)
        {
            return;
        }

        _layoutCardPresenter.Hide();
    }

    private async void LayoutCardPresenter_CardChosen(LayoutPreset? template, IDataObject? data)
    {
        var mode = _layoutCardMode;
        var removalSlotIds = _slotRemovalSelectionService.GetSelectedSlotIds();

        _layoutCardPresenter.Hide();
        if (mode == LayoutCardMode.Remove)
        {
            ClearRemoveSelection(hideCards: false);
        }

        _layoutCardMode = LayoutCardMode.Add;

        // "아무것도 안 함"(취소) 카드.
        if (template is null)
        {
            StatusTextBlock.Text = "레이아웃을 변경하지 않았습니다.";
            return;
        }

        if (mode == LayoutCardMode.Remove)
        {
            if (removalSlotIds.Count > 0)
            {
                await ApplyRemovalAsync(removalSlotIds, template);
            }

            return;
        }

        if (mode == LayoutCardMode.Switch)
        {
            await ApplySwitchAsync(template);
            return;
        }

        string? url = null;
        string? streamName = null;
        if (data is not null)
        {
            StreamDropDataReader.TryGetDroppedStream(data, _streamNavigationService, out url, out streamName);
        }

        await ApplyTemplateFromCardAsync(template, url, streamName);
    }

    private async Task ApplyTemplateFromCardAsync(LayoutPreset template, string? url, string? streamName)
    {
        var previousSlotIds = _selectedLayout?.Slots.Select(slot => slot.SlotId).ToHashSet() ?? [];
        var targetSlotId = url is null ? null : ResolveDropTargetSlotId(template, previousSlotIds);

        await ApplySelectedLayoutAsync(template, clearHiddenSlots: true);

        if (url is not null && targetSlotId is int slotId &&
            _slots.FirstOrDefault(slot => slot.SlotId == slotId) is { } targetSlot)
        {
            await LoadDroppedStreamIntoSlotAsync(targetSlot, url, streamName);
            StatusTextBlock.Text =
                $"'{template.Name}' 레이아웃으로 전환하고 " +
                $"{(string.IsNullOrWhiteSpace(streamName) ? url : streamName)}을(를) Slot {slotId}에 배치했습니다.";
            return;
        }

        StatusTextBlock.Text = $"'{template.Name}' 레이아웃으로 전환했습니다.";
    }

    private int? ResolveDropTargetSlotId(LayoutPreset template, HashSet<int> previousSlotIds)
    {
        var templateSlotIds = template.Slots
            .Select(slot => slot.SlotId)
            .OrderBy(slotId => slotId)
            .ToArray();
        if (templateSlotIds.Length == 0)
        {
            return null;
        }

        // 1순위: 이전 레이아웃에 없던 새 슬롯(그로우 케이스에서 새로 추가된 화면)
        var newSlotId = templateSlotIds.FirstOrDefault(slotId => !previousSlotIds.Contains(slotId));
        if (newSlotId > 0)
        {
            return newSlotId;
        }

        // 2순위: 현재 비어 있는 슬롯
        var blankSlotId = templateSlotIds.FirstOrDefault(slotId =>
            _slots.FirstOrDefault(slot => slot.SlotId == slotId) is { } slot &&
            slot.CurrentUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase));
        if (blankSlotId > 0)
        {
            return blankSlotId;
        }

        // 3순위: 마지막 슬롯
        return templateSlotIds[^1];
    }

    private void SlotsGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsSupportedDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void SlotsGrid_Drop(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(SlotsGrid);
        if (!TryGetDropTargetSlot(position, out var targetSlot) || targetSlot is null)
        {
            StatusTextBlock.Text = "Drop the stream over a visible playback area.";
            e.Handled = true;
            return;
        }

        if (TryGetDroppedSlotId(e.Data, out var sourceSlotId) &&
            _slots.FirstOrDefault(slot => slot.SlotId == sourceSlotId) is { } sourceSlot &&
            !ReferenceEquals(sourceSlot, targetSlot))
        {
            await SwapSlotStreamsAsync(sourceSlot, targetSlot);
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
        if (_selectedLayout is null)
        {
            yield break;
        }

        var slotsById = _slots.ToDictionary(slot => slot.SlotId);
        foreach (var slotId in _selectedLayout.Slots.Select(slot => slot.SlotId))
        {
            if (slotsById.TryGetValue(slotId, out var slot))
            {
                yield return slot;
            }
        }
    }

    private bool IsSupportedDrop(IDataObject data)
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

    private void MuteAllButton_Click(object sender, RoutedEventArgs e)
    {
        // 단발성 동작: 모든 슬롯 볼륨을 0%로 내린다(복원 없음).
        foreach (var slot in _slots)
        {
            slot.SetVolumePercentSilently(0);
        }

        StatusTextBlock.Text = "전체 볼륨을 0%로 변경했습니다.";
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
        var layoutId = selectedLayout?.Id
            ?? _layouts.FirstOrDefault()?.Id
            ?? string.Empty;

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
                    VolumePercent = slot.VolumePercent,
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
            Shortcuts = _shortcutSettings,
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
        // 사용자 지정 레이아웃이 하나도 없으면 표시할 레이아웃이 없으므로 빈 상태로 둔다.
        if (_layouts.Count == 0)
        {
            ShowEmptyLayoutState();
            if (setActiveWorkspace)
            {
                _activeWorkspace = workspace;
                RefreshWorkspaceComboBox();
            }

            return;
        }

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
        var visibleSlotIds = layout.Slots.Select(slot => slot.SlotId).ToHashSet();

        foreach (var slot in _slots.OrderBy(slot => slot.SlotId))
        {
            if (!workspaceSlots.TryGetValue(slot.SlotId, out var workspaceSlot))
            {
                continue;
            }

            slot.SetMuted(false);
            slot.SetVolumePercentSilently(workspaceSlot.VolumePercent);
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
        ToggleExplorerIcon.Source = isVisible ? SidebarCloseIcon : SidebarOpenIcon;
        UpdateExplorerToggleTooltip();
    }

    // 사이드바 토글 버튼 툴팁에 현재 상태와 단축키를 함께 표시한다.
    private void UpdateExplorerToggleTooltip()
    {
        var keyLabel = _shortcutSettings.ToggleExplorerKey.Name;
        ToggleExplorerButton.ToolTip = _isExplorerPanelVisible
            ? $"탐색 숨김 ({keyLabel})"
            : $"탐색 표시 ({keyLabel})";
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
        HashSet<int>? visibleSlotIds = _selectedLayout is { } layout
            ? layout.Slots.Select(slot => slot.SlotId).ToHashSet()
            : null;

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
