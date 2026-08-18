using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;
using StreamOrchestra.App.Views;

namespace StreamOrchestra.App;

public partial class MainWindow
{
    private StreamSyncCoordinator? _syncCoordinator;
    private ISyncBiasPriorService _syncBiasPriorService = DisabledSyncBiasPriorService.Instance;
    private bool _isRefreshingSyncUi;
    private DateTimeOffset _lastSyncListRefreshAtUtc;

    private void InitializeSyncFeature()
    {
        try
        {
            _syncBiasPriorService = SyncBiasPriorService.CreateDefault(_presetStorageService.DataFolder);
        }
        catch
        {
            _syncBiasPriorService = DisabledSyncBiasPriorService.Instance;
        }
        _syncCoordinator = new StreamSyncCoordinator(
            _slots,
            biasPriorService: _syncBiasPriorService);
        _syncCoordinator.StateChanged += OnSyncStateChanged;
        _syncCoordinator.LoadPreset(new SyncGroupPreset());

        foreach (var slot in _slots)
        {
            slot.PlaybackStateChanged += SyncSlot_PlaybackStateChanged;
        }

        Closing += (_, _) =>
        {
            if (_syncCoordinator.IsEnabled)
            {
                _ = _syncCoordinator.StopAsync();
            }
        };
        RefreshSyncUi(rebuildLists: true);
    }

    private void SyncSlot_PlaybackStateChanged(StreamSlotView slot)
    {
        if (_syncCoordinator?.ReconcileMemberStreamIdentity(slot.SlotId) == true)
        {
            QueueAppStateSave();
        }

        if (SyncPopup.IsOpen && !IsSyncPopupEditing())
        {
            RefreshSyncUi(rebuildLists: true);
        }
    }

    private void OnSyncStateChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke((Action)OnSyncStateChanged);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var rebuildLists = SyncPopup.IsOpen &&
                           !IsSyncPopupEditing() &&
                           now - _lastSyncListRefreshAtUtc >= TimeSpan.FromSeconds(2);
        RefreshSyncUi(rebuildLists);
    }

    private void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPopup.IsOpen = !SyncPopup.IsOpen;
        if (SyncPopup.IsOpen)
        {
            RefreshSyncUi(rebuildLists: true);
        }
    }

    private async void SyncStartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_syncCoordinator is null)
        {
            return;
        }

        if (_syncCoordinator.IsEnabled)
        {
            await _syncCoordinator.StopAsync();
            StatusTextBlock.Text = "SOOP 재생 동기화를 정지했습니다.";
        }
        else if (!await _syncCoordinator.StartAsync())
        {
            StatusTextBlock.Text = "동기화할 방송을 2개 이상 추가해 주세요.";
        }
        else
        {
            StatusTextBlock.Text = "SOOP 재생 동기화를 시작했습니다.";
        }

        RefreshSyncUi(rebuildLists: true);
    }

    private void SyncMinimumSafetySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshingSyncUi || SyncMinimumSafetyText is null)
        {
            return;
        }

        var milliseconds = (int)Math.Round(e.NewValue / 100) * 100;
        SyncMinimumSafetyText.Text = FormatSeconds(milliseconds);
        if (_syncCoordinator?.SetMinimumSafetyDelay(milliseconds) == true)
        {
            QueueAppStateSave();
        }
    }

    private SyncGroupPreset CaptureSyncPreset()
    {
        return _syncCoordinator?.CapturePreset() ?? new SyncGroupPreset();
    }

    private async Task ApplySyncPresetAsync(SyncGroupPreset? preset)
    {
        if (_syncCoordinator is null)
        {
            return;
        }

        await _syncCoordinator.StopAsync();
        _syncCoordinator.LoadPreset(preset);
        RefreshSyncUi(rebuildLists: true);
    }

    private void RefreshSyncUi(bool rebuildLists)
    {
        if (_syncCoordinator is null || SyncStatusDot is null)
        {
            return;
        }

        var state = _syncCoordinator.CreateViewState();
        _isRefreshingSyncUi = true;
        try
        {
            SyncStatusDot.Fill = CreateSyncStateBrush(state.RuntimeState);
            SyncStartStopButton.Content = state.IsEnabled ? "정지" : "시작";
            SyncStartStopButton.IsEnabled = state.IsEnabled || state.Members.Count >= 2;
            SyncConfirmAlignmentButton.IsEnabled = state.Members.Count >= 2 &&
                                                   _syncBiasPriorService.IsEnabled;
            SyncExportBiasButton.IsEnabled = _syncBiasPriorService.IsEnabled;
            SyncDeleteBiasButton.IsEnabled = _syncBiasPriorService.IsEnabled;
            SyncGroupStateText.Text = FormatRuntimeState(state.RuntimeState);
            SyncReadyText.Text = $"준비 {state.ReadyMemberCount}/{state.Members.Count} · 그룹 최대 16개";
            SyncEffectiveDelayText.Text = $"현재 유효 안전 딜레이 {FormatSeconds(state.EffectiveSafetyDelayMs)}";
            SyncMinimumSafetySlider.Value = state.MinimumSafetyDelayMs;
            SyncMinimumSafetyText.Text = FormatSeconds(state.MinimumSafetyDelayMs);
            SyncNoticeText.Text = state.Notice;
            SyncButton.ToolTip = $"SOOP 방송 재생 동기화 · {FormatRuntimeState(state.RuntimeState)}";

            if (rebuildLists)
            {
                _lastSyncListRefreshAtUtc = DateTimeOffset.UtcNow;
                RebuildSyncMemberRows(state);
                RebuildSyncAvailableRows(state);
            }
        }
        finally
        {
            _isRefreshingSyncUi = false;
        }
    }

    private void RebuildSyncMemberRows(SyncGroupViewState state)
    {
        SyncMembersPanel.Children.Clear();
        if (state.Members.Count == 0)
        {
            SyncMembersPanel.Children.Add(CreateEmptySyncText("아래 목록에서 동기화할 방송을 추가해 주세요."));
            return;
        }

        foreach (var member in state.Members)
        {
            var container = new Border
            {
                Margin = new Thickness(0, 0, 0, 7),
                Padding = new Thickness(9),
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 60, 73)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7)
            };
            var content = new StackPanel();
            container.Child = content;

            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock
            {
                Text = member.StreamName,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var status = new TextBlock
            {
                Text = member.StatusText,
                Foreground = member.IsReady ? new SolidColorBrush(Color.FromRgb(111, 211, 158)) :
                    new SolidColorBrush(Color.FromRgb(244, 197, 106)),
                FontSize = 11
            };
            Grid.SetColumn(status, 1);
            heading.Children.Add(name);
            heading.Children.Add(status);
            content.Children.Add(heading);

            content.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 5, 0, 0),
                Text = $"{FormatTimelineSource(member.TimelineSource)} · 버퍼 {FormatNullableSeconds(member.BufferSec)} · 오차 {FormatError(member.ErrorMs)}",
                Foreground = new SolidColorBrush(Color.FromRgb(166, 178, 191)),
                FontSize = 10
            });

            content.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 3, 0, 0),
                Text = $"알고리즘 prior {FormatSeconds(member.AlgorithmPriorMs)} · 사용자 residual {FormatSeconds(member.UserResidualMs)} · 최종 {FormatSeconds(member.ManualDelayMs)}",
                Foreground = new SolidColorBrush(Color.FromRgb(142, 157, 174)),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            });

            var controls = new StackPanel
            {
                Margin = new Thickness(0, 7, 0, 0),
                Orientation = Orientation.Horizontal
            };
            var advanceButton = CreateSyncSmallButton("0.1초 앞당김", "이 방송을 0.1초 앞당깁니다.");
            advanceButton.Click += (_, _) => ChangeManualDelay(member.SlotId, member.ManualDelayMs - 100);
            controls.Children.Add(advanceButton);

            var delayInput = new TextBox
            {
                Width = 58,
                Height = 25,
                Margin = new Thickness(5, 0, 5, 0),
                Padding = new Thickness(4, 2, 4, 2),
                Text = (member.ManualDelayMs / 1000d).ToString("0.0", CultureInfo.InvariantCulture),
                TextAlignment = TextAlignment.Center,
                ToolTip = "추가 지연(초), -60~60",
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(delayInput, $"{member.StreamName} 추가 지연 초");
            delayInput.LostKeyboardFocus += (_, _) => CommitManualDelay(member.SlotId, delayInput.Text);
            delayInput.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    CommitManualDelay(member.SlotId, delayInput.Text);
                    Keyboard.ClearFocus();
                }
            };
            controls.Children.Add(delayInput);

            var delayButton = CreateSyncSmallButton("0.1초 늦춤", "이 방송을 0.1초 늦춥니다.");
            delayButton.Click += (_, _) => ChangeManualDelay(member.SlotId, member.ManualDelayMs + 100);
            controls.Children.Add(delayButton);

            var resetButton = CreateSyncSmallButton("초기화", "이 방송의 수동 보정을 0초로 초기화합니다.");
            resetButton.Margin = new Thickness(5, 0, 0, 0);
            resetButton.Click += (_, _) => ChangeManualDelay(member.SlotId, 0);
            controls.Children.Add(resetButton);

            var removeButton = CreateSyncSmallButton("제거", "이 방송을 동기화 그룹에서 제거합니다.");
            removeButton.Margin = new Thickness(5, 0, 0, 0);
            removeButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 166, 176));
            removeButton.Click += async (_, _) =>
            {
                if (_syncCoordinator is not null && await _syncCoordinator.RemoveMemberAsync(member.SlotId))
                {
                    QueueAppStateSave();
                    RefreshSyncUi(rebuildLists: true);
                }
            };
            controls.Children.Add(removeButton);
            content.Children.Add(controls);

            if (member.SuggestedDelayMs is { } suggestedDelay)
            {
                _syncCoordinator?.MarkBiasSuggestionShown(member.SlotId);
                var suggestionPanel = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
                suggestionPanel.Children.Add(new TextBlock
                {
                    Text = $"로컬 제안 {FormatSeconds(suggestedDelay)} · {FormatBiasHierarchy(member.SuggestionHierarchy)} · 독립 세션 {member.SuggestionSupport}개 (자동 적용 안 됨)",
                    Foreground = new SolidColorBrush(Color.FromRgb(129, 190, 255)),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                });
                var suggestionButtons = new StackPanel
                {
                    Margin = new Thickness(0, 4, 0, 0),
                    Orientation = Orientation.Horizontal
                };
                var accept = CreateSyncSmallButton("제안 수락", "로컬 지연 제안을 이 방송에 적용합니다.");
                accept.Click += (_, _) => ApplyBiasSuggestionAction(
                    member.SlotId,
                    _syncCoordinator?.AcceptBiasSuggestion(member.SlotId) == true,
                    "로컬 지연 제안을 적용했습니다.");
                suggestionButtons.Children.Add(accept);
                var reject = CreateSyncSmallButton("거절", "이 방송 세션의 로컬 지연 제안을 숨깁니다.");
                reject.Margin = new Thickness(5, 0, 0, 0);
                reject.Click += (_, _) => ApplyBiasSuggestionAction(
                    member.SlotId,
                    _syncCoordinator?.RejectBiasSuggestion(member.SlotId) == true,
                    "이 세션의 로컬 지연 제안을 거절했습니다.");
                suggestionButtons.Children.Add(reject);
                suggestionPanel.Children.Add(suggestionButtons);
                content.Children.Add(suggestionPanel);
            }

            if (member.CanRevertSuggestion)
            {
                var revert = CreateSyncSmallButton("제안 되돌리기", "수락 전 지연 값으로 되돌립니다.");
                revert.Margin = new Thickness(0, 7, 0, 0);
                revert.HorizontalAlignment = HorizontalAlignment.Left;
                revert.Click += (_, _) => ApplyBiasSuggestionAction(
                    member.SlotId,
                    _syncCoordinator?.RevertBiasSuggestion(member.SlotId) == true,
                    "수락 전 지연 값으로 되돌렸습니다.");
                content.Children.Add(revert);
            }
            SyncMembersPanel.Children.Add(container);
        }
    }

    private void SyncConfirmAlignmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_syncCoordinator?.ConfirmCurrentManualAlignment() == true)
        {
            StatusTextBlock.Text = "현재 정렬을 암호화된 온디바이스 학습값으로 저장했습니다.";
        }
        else
        {
            StatusTextBlock.Text = "방송 2개가 필요하거나 이 방송 세션은 이미 확인되었습니다.";
        }
        RefreshSyncUi(rebuildLists: true);
    }

    private void ApplyBiasSuggestionAction(int slotId, bool changed, string status)
    {
        if (!changed)
        {
            return;
        }

        QueueAppStateSave();
        StatusTextBlock.Text = status;
        RefreshSyncUi(rebuildLists: true);
    }

    private void SyncExportBiasButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "동기화 로컬 학습 데이터 내보내기",
            Filter = "JSON 파일 (*.json)|*.json",
            FileName = $"stream-orchestra-sync-bias-{DateTime.Now:yyyyMMdd}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _syncBiasPriorService.ExportPrivacySafe(dialog.FileName);
            StatusTextBlock.Text = "해시 처리된 로컬 학습 데이터를 내보냈습니다.";
        }
        catch
        {
            StatusTextBlock.Text = "로컬 학습 데이터를 내보내지 못했습니다.";
        }
    }

    private void SyncDeleteBiasButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "이 PC에 저장된 동기화 학습 기록을 모두 삭제할까요? 복구할 수 없습니다.",
                "동기화 학습 데이터 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _syncBiasPriorService.DeleteAll();
            StatusTextBlock.Text = "로컬 동기화 학습 데이터를 삭제했습니다.";
            RefreshSyncUi(rebuildLists: true);
        }
        catch
        {
            StatusTextBlock.Text = "로컬 학습 데이터를 삭제하지 못했습니다.";
        }
    }

    private void RebuildSyncAvailableRows(SyncGroupViewState state)
    {
        SyncAvailablePanel.Children.Clear();
        var memberIds = state.Members.Select(member => member.SlotId).ToHashSet();
        var available = _slots
            .Where(slot => !memberIds.Contains(slot.SlotId) && IsSoopStreamUrl(slot.CurrentUrl))
            .OrderBy(slot => slot.SlotId)
            .ToArray();
        if (available.Length == 0)
        {
            SyncAvailablePanel.Children.Add(CreateEmptySyncText("추가 가능한 SOOP 방송이 없습니다."));
            return;
        }

        foreach (var slot in available)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = slot.SyncDisplayName,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 215, 225)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var addButton = CreateSyncSmallButton("추가", $"{slot.SyncDisplayName} 방송을 동기화 그룹에 추가합니다.");
            addButton.IsEnabled = state.Members.Count < SlotProfileGroupMapping.MaxSlotCount;
            addButton.Click += (_, _) =>
            {
                if (_syncCoordinator?.AddMember(slot.SlotId) == true)
                {
                    QueueAppStateSave();
                    RefreshSyncUi(rebuildLists: true);
                }
            };
            Grid.SetColumn(addButton, 1);
            row.Children.Add(addButton);
            SyncAvailablePanel.Children.Add(row);
        }
    }

    private void ChangeManualDelay(int slotId, int manualDelayMs)
    {
        if (_syncCoordinator?.SetManualDelay(slotId, manualDelayMs) == true)
        {
            QueueAppStateSave();
            RefreshSyncUi(rebuildLists: true);
        }
    }

    private void CommitManualDelay(int slotId, string value)
    {
        var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var currentCultureValue)
            ? currentCultureValue
            : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantValue)
                ? invariantValue
                : double.NaN;
        if (!double.IsFinite(parsed))
        {
            RefreshSyncUi(rebuildLists: true);
            return;
        }

        parsed = Math.Clamp(parsed, -60, 60);
        ChangeManualDelay(slotId, (int)Math.Round(parsed * 10) * 100);
    }

    private static Button CreateSyncSmallButton(string text, string automationName)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 25,
            Padding = new Thickness(7, 2, 7, 2),
            Background = new SolidColorBrush(Color.FromRgb(38, 50, 64)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(59, 73, 89)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            FontSize = 10,
            ToolTip = automationName
        };
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private static TextBlock CreateEmptySyncText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Padding = new Thickness(6),
            Foreground = new SolidColorBrush(Color.FromRgb(130, 143, 157)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static Brush CreateSyncStateBrush(SyncRuntimeState state)
    {
        return state switch
        {
            SyncRuntimeState.Running => new SolidColorBrush(Color.FromRgb(55, 190, 120)),
            SyncRuntimeState.Recovering => new SolidColorBrush(Color.FromRgb(225, 80, 80)),
            SyncRuntimeState.Preparing or SyncRuntimeState.Waiting or SyncRuntimeState.Degraded =>
                new SolidColorBrush(Color.FromRgb(234, 179, 72)),
            _ => new SolidColorBrush(Color.FromRgb(105, 117, 134))
        };
    }

    private static string FormatRuntimeState(SyncRuntimeState state)
    {
        return state switch
        {
            SyncRuntimeState.Stopped => "정지",
            SyncRuntimeState.Preparing => "시간축 준비 중",
            SyncRuntimeState.Running => "정상 동기화 중",
            SyncRuntimeState.Recovering => "버퍼 복구 중",
            SyncRuntimeState.Waiting => "방송 신호 대기",
            SyncRuntimeState.Degraded => "추정 동기화 중",
            _ => state.ToString()
        };
    }

    private static string FormatTimelineSource(SyncTimelineSource source)
    {
        return source switch
        {
            SyncTimelineSource.ProgramDateTime => "플랫폼 HLS 시각",
            SyncTimelineSource.CdnDate => "CDN 응답 기반 추정",
            SyncTimelineSource.LiveEdgeEstimate => "라이브 엣지 추정",
            _ => "시간축 대기"
        };
    }

    private static string FormatSeconds(int milliseconds) => $"{milliseconds / 1000d:0.0}초";

    private static string FormatNullableSeconds(double? seconds) => seconds is null ? "--" : $"{seconds:0.00}초";

    private static string FormatError(double? errorMs) =>
        errorMs is null ? "--" : $"{errorMs.Value / 1000:+0.00;-0.00;0.00}초";

    private static string FormatBiasHierarchy(SyncBiasHierarchyLevel level) => level switch
    {
        SyncBiasHierarchyLevel.ChannelQualityCdn => "채널×화질×CDN",
        SyncBiasHierarchyLevel.ChannelQuality => "채널×화질",
        SyncBiasHierarchyLevel.Channel => "채널",
        _ => "제안"
    };

    private static bool IsSoopStreamUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("sooplive.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sooplive.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("sooplive.co.kr", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sooplive.co.kr", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSyncPopupEditing()
    {
        return SyncPopup.Child is UIElement child && child.IsKeyboardFocusWithin;
    }
}
