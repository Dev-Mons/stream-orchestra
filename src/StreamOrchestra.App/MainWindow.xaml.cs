using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using System.Globalization;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;
using StreamOrchestra.App.Views;

namespace StreamOrchestra.App;

public partial class MainWindow : Window
{
    private readonly WebViewProfileService _profileService = new();
    private readonly WebViewRuntimeDiagnosticsService _diagnosticsService = new();
    private readonly PlaybackTestPlanService _playbackTestPlanService = new();
    private readonly LayoutPresetService _layoutPresetService = new();
    private readonly SlotSwapService _slotSwapService = new();
    private readonly PresetStorageService _presetStorageService = new();
    private readonly FavoriteStorageService _favoriteStorageService = new();
    private readonly FeasibilityResultStorageService _feasibilityResultStorageService = new();
    private readonly FeasibilityDecisionService _feasibilityDecisionService = new();
    private readonly FeasibilityResultValidationService _feasibilityResultValidationService = new();
    private readonly FeasibilityScenarioService _feasibilityScenarioService = new();
    private readonly FeasibilityAuditService _feasibilityAuditService = new();
    private readonly DiagnosticReportService _diagnosticReportService = new();
    private readonly ExternalBrowserFallbackExportService _externalBrowserFallbackExportService = new();
    private readonly StreamNavigationService _streamNavigationService = new();
    private readonly SlotSelectionService _slotSelectionService = new();
    private readonly AppWindowPlacementService _appWindowPlacementService = new();
    private readonly WorkspaceSlotVisibilityService _workspaceSlotVisibilityService = new();
    private readonly WorkspacePresetNormalizationService _workspacePresetNormalizationService;
    private readonly WorkspaceRestoreService _workspaceRestoreService;
    private readonly List<StreamSlotView> _slots = [];
    private IReadOnlyList<LayoutPreset> _layouts = [];
    private List<WorkspacePreset> _workspaces = [];
    private List<StreamEntry> _favorites = [];
    private AppState? _loadedAppState;
    private readonly DispatcherTimer _diagnosticsTimer;
    private WorkspacePreset? _activeWorkspace;
    private StreamSlotView? _selectedSlot;
    private ExplorerPanel? _explorerPanel;
    private bool _isExplorerPanelVisible = true;
    private bool _areSlotUrlEditorsVisible = true;
    private bool _areSlotControlBarsAlwaysVisible = true;
    private GridLength _lastExplorerColumnWidth = new(360);
    private int _currentPlaybackTestCount;
    private FeasibilityScenario _currentFeasibilityScenario = new("unspecified", "Unspecified");

    public MainWindow()
    {
        _workspacePresetNormalizationService = new WorkspacePresetNormalizationService(_streamNavigationService);
        _workspaceRestoreService = new WorkspaceRestoreService(
            _workspacePresetNormalizationService,
            _workspaceSlotVisibilityService);
        InitializeComponent();

        _loadedAppState = _presetStorageService.LoadAppState();
        RestoreWindowPlacement(_loadedAppState?.Window);

        CreateExplorerPanel();
        LoadFavorites();
        CreateSlots();
        LoadLayouts();
        LoadWorkspacePresets();
        ApplyViewState(_loadedAppState);
        _diagnosticsTimer = CreateDiagnosticsTimer();
        StatusTextBlock.Text = $"Profile data persists under: {_profileService.BaseProfileFolder}";
        UpdateDiagnostics();
        UpdateCurrentFeasibilityScenarioText();
        UpdateFeasibilityResultSummary();
        _diagnosticsTimer.Start();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void CreateExplorerPanel()
    {
        var explorerPanel = new ExplorerPanel(_profileService, _streamNavigationService);
        explorerPanel.UseCurrentUrlRequested += ExplorerPanel_UseCurrentUrlRequested;
        explorerPanel.AddFavoriteRequested += ExplorerPanel_AddFavoriteRequested;
        explorerPanel.UseFavoriteRequested += ExplorerPanel_UseFavoriteRequested;
        _explorerPanel = explorerPanel;
        ExplorerHost.Content = _explorerPanel;
    }

    private void LoadFavorites()
    {
        _favorites = _favoriteStorageService.LoadFavorites().ToList();
        RefreshExplorerFavorites();
    }

    private void CreateSlots()
    {
        for (var slotId = 1; slotId <= 16; slotId++)
        {
            var configuration = new SlotConfiguration(slotId, _profileService.GetGroupForSlot(slotId));
            var slotView = new StreamSlotView(configuration, _profileService, _streamNavigationService);
            slotView.SlotSelected += SelectSlot;
            slotView.SlotSwapRequested += SlotView_SlotSwapRequested;
            _slots.Add(slotView);
        }
    }

    private void LoadLayouts()
    {
        _layouts = _layoutPresetService.LoadFromDefaultLocation();
        LayoutComboBox.ItemsSource = _layouts;
        LayoutComboBox.SelectedItem = LayoutPresetService.SelectDefaultLayout(_layouts);
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
            SlotsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var columnIndex = 0; columnIndex < layout.GridColumns; columnIndex++)
        {
            SlotsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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

    private async void LayoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LayoutComboBox.SelectedItem is LayoutPreset layout)
        {
            ApplyLayout(layout);
            EnsureSelectedSlotVisible(layout);
            await ClearHiddenNonBlankSlotsAsync(layout);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
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

    private async void LoadAllButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadAllSlotsAsync();
    }

    private async void LoadScopeButton_Click(object sender, RoutedEventArgs e)
    {
        var groupId = GetSelectedGroupId();
        var targetSlots = groupId == "All"
            ? _slots.ToArray()
            : _slots.Where(slot => slot.ProfileGroupId.Equals(groupId, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (!TryEnsureSlotsVisible(targetSlots.Select(slot => slot.SlotId).ToArray()))
        {
            return;
        }

        SetCurrentFeasibilityScenario(
            targetSlots.Length,
            _feasibilityScenarioService.CreateScopeLoadScenario(groupId, targetSlots.Length));
        await LoadSlotsAsync(targetSlots);
    }

    private async void LoadIsolatedScopeButton_Click(object sender, RoutedEventArgs e)
    {
        var groupId = GetSelectedGroupId();
        if (groupId == "All")
        {
            if (!TryEnsureSlotsVisible(_slots.Select(slot => slot.SlotId).ToArray()))
            {
                return;
            }

            SetCurrentFeasibilityScenario(
                PlaybackTestPlanService.MaxSlotCount,
                _feasibilityScenarioService.CreateScopeLoadScenario("All", _slots.Count));
            await LoadSlotsAsync(_slots);
            return;
        }

        var targetSlots = _slots
            .Where(slot => slot.ProfileGroupId.Equals(groupId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (!TryEnsureSlotsVisible(targetSlots.Select(slot => slot.SlotId).ToArray()))
        {
            return;
        }

        var plan = _playbackTestPlanService.CreateIsolatedSlotPlan(targetSlots.Select(slot => slot.SlotId).ToArray());
        var activeSlotIds = plan.ActiveSlotIds.ToHashSet();
        var inactiveSlotIds = plan.InactiveSlotIds.ToHashSet();
        var activeSlots = _slots.Where(slot => activeSlotIds.Contains(slot.SlotId)).ToArray();
        var inactiveSlots = _slots.Where(slot => inactiveSlotIds.Contains(slot.SlotId)).ToArray();

        SetCurrentFeasibilityScenario(
            plan.TargetPlaybackCount,
            _feasibilityScenarioService.CreateIsolatedGroupScenario(groupId, plan.TargetPlaybackCount));
        await ClearSlotsAsync(inactiveSlots, "Clearing non-selected groups");
        await LoadSlotsAsync(activeSlots, statusPrefix: $"Loading isolated Group {groupId} test");
    }

    private async void BlankAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentFeasibilityScenario(0, new FeasibilityScenario("blank_all", "Blank all slots"));
        await ClearSlotsAsync(_slots, "Clearing all slots");
    }

    private async void LoadFirstCountButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string countText } || !int.TryParse(countText, out var count))
        {
            return;
        }

        var plan = _playbackTestPlanService.CreatePlan(count);
        if (!TryEnsureSlotsVisible(plan.ActiveSlotIds))
        {
            return;
        }

        SetCurrentFeasibilityScenario(
            plan.TargetPlaybackCount,
            _feasibilityScenarioService.CreateFirstSlotsScenario(plan));
        var activeSlotIds = plan.ActiveSlotIds.ToHashSet();
        var inactiveSlotIds = plan.InactiveSlotIds.ToHashSet();
        var activeSlots = _slots.Where(slot => activeSlotIds.Contains(slot.SlotId)).ToArray();
        var inactiveSlots = _slots.Where(slot => inactiveSlotIds.Contains(slot.SlotId)).ToArray();

        await ClearSlotsAsync(inactiveSlots, "Clearing inactive slots");
        await LoadSlotsAsync(activeSlots, statusPrefix: $"Loading {plan.TargetPlaybackCount}-slot playback test");
    }

    private bool TryEnsureSlotsVisible(IReadOnlyCollection<int> targetSlotIds)
    {
        var currentLayout = LayoutComboBox.SelectedItem as LayoutPreset;
        LayoutPreset playbackLayout;
        try
        {
            playbackLayout = LayoutPresetService.SelectLayoutContainingSlots(
                _layouts,
                currentLayout,
                targetSlotIds);
        }
        catch (InvalidOperationException ex)
        {
            StatusTextBlock.Text = ex.Message;
            return false;
        }

        if (!ReferenceEquals(LayoutComboBox.SelectedItem, playbackLayout))
        {
            LayoutComboBox.SelectedItem = playbackLayout;
        }

        return true;
    }

    private void RecordFeasibilityResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string outcome })
        {
            return;
        }

        if (_currentPlaybackTestCount <= 0)
        {
            StatusTextBlock.Text = "Run a playback test or load a group before recording a feasibility result.";
            return;
        }

        var playbackCount = _currentPlaybackTestCount;
        var sameAccountSession = SameAccountSessionCheckBox.IsChecked == true;
        var verifiedProfileGroups = GetVerifiedProfileGroups();
        var restartSession = RestartSessionCheckBox.IsChecked == true;
        var resourceUsageAcceptable = ResourceAcceptableCheckBox.IsChecked == true;
        if (!TryReadOptionalPercent(ObservedCpuTextBox, "CPU", out var observedCpuPercent) ||
            !TryReadOptionalPercent(ObservedGpuTextBox, "GPU", out var observedGpuPercent) ||
            !TryReadOptionalNonNegativeNumber(ObservedMemoryTextBox, "Memory MB", out var observedMemoryMegabytes))
        {
            return;
        }

        var scenarioPlaybackCountError = FeasibilityScenarioService.ValidatePlaybackCountConsistency(
            playbackCount,
            _currentFeasibilityScenario.Id);
        if (scenarioPlaybackCountError is not null)
        {
            StatusTextBlock.Text = scenarioPlaybackCountError;
            return;
        }

        var scenarioProfileGroupError = FeasibilityProfileGroupEvidenceService.ValidateScenarioConsistency(
            playbackCount,
            _currentFeasibilityScenario.Id,
            verifiedProfileGroups);
        if (scenarioProfileGroupError is not null)
        {
            StatusTextBlock.Text = scenarioProfileGroupError;
            return;
        }

        var validationError = _feasibilityResultValidationService.Validate(
            playbackCount,
            outcome,
            sameAccountSession,
            restartSession,
            resourceUsageAcceptable,
            observedCpuPercent,
            observedGpuPercent,
            observedMemoryMegabytes,
            verifiedProfileGroups);
        if (validationError is not null)
        {
            StatusTextBlock.Text = validationError;
            return;
        }

        var capturedAt = DateTimeOffset.Now;
        var diagnostics = _diagnosticsService.Capture();
        var result = new FeasibilityTestResult
        {
            Id = FeasibilityResultStorageService.CreateResultId(capturedAt, playbackCount, outcome),
            CapturedAt = capturedAt,
            PlaybackCount = playbackCount,
            ScenarioId = _currentFeasibilityScenario.Id,
            ScenarioName = _currentFeasibilityScenario.Name,
            Outcome = outcome,
            Diagnostics = diagnostics,
            IsSameAccountSessionMaintained = sameAccountSession,
            AccountLabel = AccountLabelTextBox.Text.Trim(),
            VerifiedProfileGroups = verifiedProfileGroups,
            IsRestartSessionMaintained = restartSession,
            IsResourceUsageAcceptable = resourceUsageAcceptable,
            ObservedCpuPercent = observedCpuPercent,
            ObservedGpuPercent = observedGpuPercent,
            ObservedMemoryMegabytes = observedMemoryMegabytes,
            Notes = FeasibilityNotesTextBox.Text.Trim()
        };

        var existingResults = _feasibilityResultStorageService.LoadResults();
        var decision = _feasibilityDecisionService.Decide(existingResults.Append(result).ToArray());
        FeasibilityResultStorageService.ApplyDecisionSnapshot(result, decision);
        _feasibilityResultStorageService.AppendResult(result);
        FeasibilityNotesTextBox.Clear();
        ObservedCpuTextBox.Clear();
        ObservedGpuTextBox.Clear();
        ObservedMemoryTextBox.Clear();
        UpdateFeasibilityResultSummary();
        StatusTextBlock.Text =
            $"Feasibility result recorded: {outcome}, {playbackCount} slot(s), {_currentFeasibilityScenario.Name}. Decision: {decision.Code}.";
    }

    private bool TryReadOptionalPercent(TextBox textBox, string label, out double? value)
    {
        if (!TryReadOptionalNumber(textBox.Text, label, out value))
        {
            return false;
        }

        if (value is < 0 or > 100)
        {
            StatusTextBlock.Text = $"{label} must be between 0 and 100.";
            return false;
        }

        return true;
    }

    private IReadOnlyList<string> GetVerifiedProfileGroups()
    {
        var groups = new List<string>();
        if (VerifiedGroupACheckBox.IsChecked == true)
        {
            groups.Add("A");
        }

        if (VerifiedGroupBCheckBox.IsChecked == true)
        {
            groups.Add("B");
        }

        if (VerifiedGroupCCheckBox.IsChecked == true)
        {
            groups.Add("C");
        }

        if (VerifiedGroupDCheckBox.IsChecked == true)
        {
            groups.Add("D");
        }

        return groups;
    }

    private bool TryReadOptionalNonNegativeNumber(TextBox textBox, string label, out double? value)
    {
        if (!TryReadOptionalNumber(textBox.Text, label, out value))
        {
            return false;
        }

        if (value < 0)
        {
            StatusTextBlock.Text = $"{label} must be 0 or higher.";
            return false;
        }

        return true;
    }

    private bool TryReadOptionalNumber(string text, string label, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantValue) ||
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out invariantValue))
        {
            value = invariantValue;
            return true;
        }

        StatusTextBlock.Text = $"{label} must be a number.";
        return false;
    }

    private void SetCurrentFeasibilityScenario(int playbackCount, FeasibilityScenario scenario)
    {
        _currentPlaybackTestCount = playbackCount;
        _currentFeasibilityScenario = scenario;
        UpdateCurrentFeasibilityScenarioText();
    }

    private void UpdateCurrentFeasibilityScenarioText()
    {
        CurrentFeasibilityScenarioTextBlock.Text = _currentPlaybackTestCount <= 0
            ? "현재 테스트: 미선택"
            : $"현재 테스트: {_currentPlaybackTestCount} slots / {_currentFeasibilityScenario.Name}";
        CurrentFeasibilityScenarioTextBlock.ToolTip = _currentPlaybackTestCount <= 0
            ? "재생 테스트 또는 그룹 로드 후 검증 결과를 기록할 수 있습니다."
            : $"{_currentFeasibilityScenario.Name} ({_currentFeasibilityScenario.Id})";
    }

    private void SaveDiagnosticReportButton_Click(object sender, RoutedEventArgs e)
    {
        var results = _feasibilityResultStorageService.LoadResults();
        var decision = _feasibilityDecisionService.Decide(results);
        var report = _diagnosticReportService.CreateReport(
            _profileService,
            _presetStorageService,
            _favoriteStorageService,
            _feasibilityResultStorageService,
            decision,
            CaptureWorkspace("diagnostic_current_session", "Diagnostic Current Session"),
            _layouts);
        var path = _diagnosticReportService.SaveReport(report, _presetStorageService.DataFolder);
        var fallbackScriptPath = _diagnosticReportService.SaveExternalBrowserFallbackScript(
            report,
            _presetStorageService.DataFolder);
        var auditSummary = _feasibilityAuditService.CreateSummary(report.FeasibilityAudit);
        var fallbackStatus = FormatExternalBrowserFallbackScriptStatus(report, fallbackScriptPath);

        StatusTextBlock.Text = $"Diagnostic report saved: {path} | {fallbackStatus} | audit {auditSummary.ToCompactText()}";
    }

    private void SaveExternalBrowserFallbackScriptButton_Click(object sender, RoutedEventArgs e)
    {
        var exportResult = _externalBrowserFallbackExportService.SaveScript(
            CaptureWorkspace("fallback_current_session", "Fallback Current Session"),
            _presetStorageService.DataFolder,
            DateTimeOffset.Now,
            "No current session is available.",
            _layouts);

        if (!exportResult.ScriptSaved)
        {
            StatusTextBlock.Text = $"External browser fallback unavailable: {exportResult.Reason}";
            return;
        }

        StatusTextBlock.Text =
            $"External browser fallback script saved: {exportResult.ScriptPath} | {exportResult.Plan?.PlannedSlotCount ?? 0} slot(s). Review before running.";
    }

    private static string FormatExternalBrowserFallbackScriptStatus(
        DiagnosticReport report,
        string? fallbackScriptPath)
    {
        if (fallbackScriptPath is not null)
        {
            return $"fallback script: {fallbackScriptPath}";
        }

        var reason = report.ExternalBrowserFallbackPlan?.Reason ?? "No last saved session is available.";
        return $"fallback unavailable: {reason}";
    }

    private void CopyAuditButton_Click(object sender, RoutedEventArgs e)
    {
        var results = _feasibilityResultStorageService.LoadResults();
        var decision = _feasibilityDecisionService.Decide(results);
        var auditText = _feasibilityAuditService.CreateAuditText(results, decision);

        Clipboard.SetText(auditText);
        StatusTextBlock.Text = "Plan audit copied to clipboard.";
    }

    private void ToggleExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        SetExplorerPanelVisible(!_isExplorerPanelVisible);
        StatusTextBlock.Text = _isExplorerPanelVisible ? "Explorer panel shown." : "Explorer panel hidden.";
    }

    private void ToggleSlotUrlEditorsButton_Click(object sender, RoutedEventArgs e)
    {
        SetSlotUrlEditorsVisible(!_areSlotUrlEditorsVisible);
        StatusTextBlock.Text = _areSlotUrlEditorsVisible ? "Slot URL editors shown." : "Slot URL editors hidden.";
    }

    private void ToggleSlotControlBarsButton_Click(object sender, RoutedEventArgs e)
    {
        SetSlotControlBarsAlwaysVisible(!_areSlotControlBarsAlwaysVisible);
        StatusTextBlock.Text = _areSlotControlBarsAlwaysVisible
            ? "Slot control bars pinned."
            : "Slot control bars show on hover.";
    }

    private async void SlotView_SlotSwapRequested(StreamSlotView targetSlot, int sourceSlotId)
    {
        var sourceSlot = _slots.SingleOrDefault(slot => slot.SlotId == sourceSlotId);
        if (sourceSlot is null || sourceSlot.SlotId == targetSlot.SlotId)
        {
            return;
        }

        var result = _slotSwapService.SwapStreams(sourceSlot.CreateRuntimeState(), targetSlot.CreateRuntimeState());

        await sourceSlot.NavigateAsync(result.SourceSlot.StreamUrl, result.SourceSlot.StreamName);
        await targetSlot.NavigateAsync(result.TargetSlot.StreamUrl, result.TargetSlot.StreamName);

        StatusTextBlock.Text =
            $"Swapped streams: Slot {sourceSlot.SlotId} <-> Slot {targetSlot.SlotId}. Mute and profile group stayed with each slot.";
    }

    private async void ExplorerPanel_UseCurrentUrlRequested(string url)
    {
        if (!TryGetSelectedVisibleSlot("explorer URL", out var selectedSlot) || selectedSlot is null)
        {
            return;
        }

        await selectedSlot.NavigateAsync(url);
        StatusTextBlock.Text = $"Loaded explorer URL into Slot {selectedSlot.SlotId}: {url}";
    }

    private void ExplorerPanel_AddFavoriteRequested(string name, string url)
    {
        var existingFavorite = _favorites.FirstOrDefault(
            favorite => favorite.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
        var favorite = new StreamEntry
        {
            Id = existingFavorite?.Id ?? FavoriteStorageService.CreateFavoriteId(name, _favorites),
            Name = name,
            Platform = "SOOP",
            Url = url,
            Memo = existingFavorite?.Memo ?? "",
            LastUsedAt = DateTimeOffset.Now
        };

        UpsertFavorite(favorite);
        _favoriteStorageService.SaveFavorites(_favorites);
        RefreshExplorerFavorites();
        StatusTextBlock.Text = $"Favorite saved: {favorite.Name}";
    }

    private async void ExplorerPanel_UseFavoriteRequested(StreamEntry favorite)
    {
        if (!TryGetSelectedVisibleSlot("favorite", out var selectedSlot) || selectedSlot is null)
        {
            return;
        }

        await selectedSlot.NavigateAsync(favorite.Url, favorite.Name);

        var updatedFavorite = new StreamEntry
        {
            Id = favorite.Id,
            Name = favorite.Name,
            Platform = favorite.Platform,
            Url = favorite.Url,
            Memo = favorite.Memo,
            LastUsedAt = DateTimeOffset.Now
        };

        UpsertFavorite(updatedFavorite);
        _favoriteStorageService.SaveFavorites(_favorites);
        RefreshExplorerFavorites();
        StatusTextBlock.Text = $"Loaded favorite into Slot {selectedSlot.SlotId}: {favorite.Name}";
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

    private async void RevertWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeWorkspace is null)
        {
            StatusTextBlock.Text = "No active preset to revert.";
            return;
        }

        var originalWorkspace = _workspaces.FirstOrDefault(workspace => workspace.Id == _activeWorkspace.Id);
        if (originalWorkspace is null)
        {
            StatusTextBlock.Text = "Active preset was not found.";
            return;
        }

        await ApplyWorkspaceAsync(originalWorkspace, setActiveWorkspace: true);
        StatusTextBlock.Text = $"Reverted to preset: {originalWorkspace.Name}";
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
        var selectedLayout = LayoutComboBox.SelectedItem as LayoutPreset;
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
                    Muted = slot.IsMuted,
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
            AreSlotUrlEditorsVisible = _areSlotUrlEditorsVisible,
            AreSlotControlBarsAlwaysVisible = _areSlotControlBarsAlwaysVisible,
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
        if (!ReferenceEquals(LayoutComboBox.SelectedItem, layout))
        {
            LayoutComboBox.SelectedItem = layout;
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

            slot.SetMuted(workspaceSlot.Muted);
            if (layout.Slots.Any(layoutSlot => layoutSlot.SlotId == slot.SlotId))
            {
                await slot.NavigateAsync(workspaceSlot.StreamUrl, workspaceSlot.StreamName);
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

    private void UpsertFavorite(StreamEntry favorite)
    {
        var existingIndex = _favorites.FindIndex(candidate => candidate.Id == favorite.Id);
        if (existingIndex >= 0)
        {
            _favorites[existingIndex] = favorite;
            return;
        }

        _favorites.Add(favorite);
    }

    private void RefreshExplorerFavorites()
    {
        _explorerPanel?.SetFavorites(_favorites);
    }

    private void SelectSlotFromState(int? selectedSlotId)
    {
        var targetSlotId = selectedSlotId;
        if (LayoutComboBox.SelectedItem is LayoutPreset layout)
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

    private bool TryGetSelectedVisibleSlot(string itemName, out StreamSlotView? selectedSlot)
    {
        selectedSlot = null;
        if (_selectedSlot is null)
        {
            StatusTextBlock.Text = $"Select a slot before inserting the {itemName}.";
            return false;
        }

        if (LayoutComboBox.SelectedItem is LayoutPreset layout &&
            !_slotSelectionService.IsSlotVisible(layout, _selectedSlot.SlotId))
        {
            SelectSlotFromState(_selectedSlot.SlotId);
            if (_selectedSlot is null || !_slotSelectionService.IsSlotVisible(layout, _selectedSlot.SlotId))
            {
                StatusTextBlock.Text = $"Select a visible slot before inserting the {itemName}.";
                return false;
            }
        }

        selectedSlot = _selectedSlot;
        return true;
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
        SetSlotUrlEditorsVisible(appState?.AreSlotUrlEditorsVisible ?? true);
        SetSlotControlBarsAlwaysVisible(appState?.AreSlotControlBarsAlwaysVisible ?? true);
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
        ToggleExplorerButton.Content = isVisible ? "탐색 숨김" : "탐색 표시";
    }

    private void SetSlotUrlEditorsVisible(bool isVisible)
    {
        _areSlotUrlEditorsVisible = isVisible;

        foreach (var slot in _slots)
        {
            slot.SetUrlEditorVisible(isVisible);
        }

        ToggleSlotUrlEditorsButton.Content = isVisible ? "URL 숨김" : "URL 표시";
    }

    private void SetSlotControlBarsAlwaysVisible(bool isAlwaysVisible)
    {
        _areSlotControlBarsAlwaysVisible = isAlwaysVisible;

        foreach (var slot in _slots)
        {
            slot.SetControlBarAlwaysVisible(isAlwaysVisible);
        }

        ToggleSlotControlBarsButton.Content = isAlwaysVisible ? "바 자동" : "바 고정";
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _presetStorageService.SaveAppState(CaptureAppState());
    }

    private void RefreshDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateDiagnostics();
    }

    private async void GlobalUrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await LoadAllSlotsAsync();
    }

    private async Task LoadAllSlotsAsync()
    {
        if (!TryEnsureSlotsVisible(_slots.Select(slot => slot.SlotId).ToArray()))
        {
            return;
        }

        SetCurrentFeasibilityScenario(
            PlaybackTestPlanService.MaxSlotCount,
            _feasibilityScenarioService.CreateScopeLoadScenario("All", _slots.Count));
        await LoadSlotsAsync(_slots);
    }

    private async Task LoadSlotsAsync(
        IEnumerable<StreamSlotView> slots,
        string? urlOverride = null,
        string? statusPrefix = null)
    {
        var url = urlOverride ?? GlobalUrlTextBox.Text;
        var slotList = slots.ToArray();
        var messagePrefix = statusPrefix ?? "Loading";

        StatusTextBlock.Text = $"{messagePrefix} {slotList.Length} slot(s): {url}";

        foreach (var slot in slotList)
        {
            await slot.NavigateAsync(url);
        }

        StatusTextBlock.Text = $"Loaded {slotList.Length} slot(s). Profile data persists under: {_profileService.BaseProfileFolder}";
        UpdateDiagnostics();
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

    private string GetSelectedGroupId()
    {
        if (LoadScopeComboBox.SelectedItem is ComboBoxItem { Tag: string groupId })
        {
            return groupId;
        }

        return "All";
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

    private void UpdateFeasibilityResultSummary()
    {
        var results = _feasibilityResultStorageService.LoadResults();
        var latest = results.OrderByDescending(result => result.CapturedAt).FirstOrDefault();
        var decision = _feasibilityDecisionService.Decide(results);
        var auditItems = _feasibilityAuditService.CreateAudit(results, decision);
        var auditSummary = _feasibilityAuditService.CreateSummary(auditItems);
        var auditText = $"Audit {auditSummary.ToCompactText()}";
        var successGate = auditItems.FirstOrDefault(item => item.Id == "phase0_success_gate");
        var gateText = successGate is null ? "Gate n/a" : $"Gate {successGate.Status}";
        var planVerificationText = $"Plan {GetPlanVerificationStatus(auditItems)}";
        FeasibilityResultTextBlock.Text = latest is null
            ? $"No feasibility result recorded | {decision.Title} | {planVerificationText} | {gateText} | {auditText}"
            : $"Last: {latest.Outcome} / {latest.PlaybackCount} slots / {latest.ScenarioName} / {latest.CapturedAt:yyyy-MM-dd HH:mm} | {decision.Title} | {planVerificationText} | {gateText} | {auditText}";
        FeasibilityResultTextBlock.ToolTip = string.IsNullOrWhiteSpace(decision.NextAction)
            ? decision.Detail
            : $"{decision.Detail}{Environment.NewLine}Next: {decision.NextAction}";
    }

    private static string GetPlanVerificationStatus(IReadOnlyList<FeasibilityAuditItem> auditItems)
    {
        if (auditItems.Count == 0)
        {
            return "n/a";
        }

        if (auditItems.All(item => item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase)))
        {
            return "pass";
        }

        return auditItems.Any(item => item.Status.Equals("fail", StringComparison.OrdinalIgnoreCase))
            ? "fail"
            : "pending";
    }
}
