using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Text.Json;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class StreamSlotView : UserControl, IStreamSyncTarget
{
    private const int MinVolumePercent = 0;
    private const int MaxVolumePercent = 100;
    private const int InitialVolumePercent = 100;
    private const int VolumeStepPercent = 10;
    private const int FineVolumeStepPercent = 5;
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PlaybackReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly Brush RemoveButtonBackground = new SolidColorBrush(Color.FromArgb(224, 31, 41, 55));
    private static readonly Brush RemoveButtonBorder = new SolidColorBrush(Color.FromRgb(243, 246, 250));
    private static readonly Brush SelectedRemoveButtonBackground = new SolidColorBrush(Color.FromArgb(224, 185, 28, 28));
    private static readonly Brush SelectedRemoveButtonBorder = new SolidColorBrush(Color.FromRgb(252, 165, 165));
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoMove = 0x0002;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;
    private static readonly IntPtr HwndNotTopmost = new(-2);

    // 웹페이지마다 휠 한 칸에 wheel 이벤트를 1~3개씩 발생시켜, 한 번 스크롤에 볼륨이
    // 20~30%씩 바뀌는 버그가 있었다. 한 번의 물리적 스크롤에서 연달아 들어오는 이벤트
    // 묶음을 이 시간 창 안에서 한 번의 스텝으로 합쳐 설정된 증감 단위만큼만 변경되도록 한다.
    private const long WheelStepThrottleMilliseconds = 50;

    private static readonly Brush SwapBorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x15, 0xA3, 0xFF));
    private static readonly Brush SwapHighlightBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFA));

    private readonly WebViewProfileService _profileService;
    private readonly StreamNavigationService _navigationService;
    private readonly DispatcherTimer _volumeOverlayTimer;
    private readonly DispatcherTimer _playbackViewportResizeTimer;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private readonly SemaphoreSlim _processRecoveryGate = new(1, 1);
    private CoreWebView2Environment? _environment;
    private uint? _lastRecoveredBrowserProcessId;
    private bool _isInitialized;
    private bool _isMuted;
    private int _volumePercent = InitialVolumePercent;
    private bool _hasExplicitStreamName;
    private string _preferredQualityKey = "master";
    private string? _playbackViewportScriptId;
    private string? _qualityObserverScriptId;
    private CoreWebView2ContextMenuItem? _copyAddressContextMenuItem;
    private string? _contextMenuAddress;
    private Point? _slotDragStartPoint;
    private Point? _swapDragStartPoint;
    private long _lastWheelStepTimestamp;
    private Point? _lastRemovePopupAnchor;
    private Point? _lastSwapPopupAnchor;
    private Point? _lastVolumePopupAnchor;

    public StreamSlotView(
        SlotConfiguration configuration,
        WebViewProfileService profileService,
        StreamNavigationService navigationService)
    {
        Configuration = configuration;
        _profileService = profileService;
        _navigationService = navigationService;
        InitializeComponent();
        _volumeOverlayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _volumeOverlayTimer.Tick += (_, _) =>
        {
            VolumeIndicatorPopup.IsOpen = false;
            _lastVolumePopupAnchor = null;
            _volumeOverlayTimer.Stop();
        };
        _playbackViewportResizeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackViewportResizeTimer.Tick += async (_, _) =>
        {
            _playbackViewportResizeTimer.Stop();
            await NotifyPlaybackViewportSizeChangedAsync();
        };

        ProfilePathTextBlock.Text = Configuration.ProfileGroup.UserDataFolder;

        Loaded += StreamSlotView_Loaded;
        SlotBorder.SizeChanged += SlotBorder_SizeChanged;
        SlotBorder.LayoutUpdated += (_, _) => RefreshOpenOverlayPlacement(force: false);
    }

    public event Action<StreamSlotView>? SlotSelected;

    public event Action<StreamSlotView>? PlaybackStateChanged;

    public event Action<StreamSlotView>? VolumeChanged;

    public event Action<StreamSlotView, string, string?>? StreamUrlDropRequested;

    public event Action? HostDragStarted;

    public event Action? HostDragCompleted;

    public event Action? SwapDragStarted;

    public event Action? SwapDragCompleted;

    public event Action<StreamSlotView, int>? SlotSwapRequested;

    /// <summary>슬롯 위의 제거 버튼이 클릭됨(화면 제거 요청).</summary>
    public event Action<StreamSlotView>? RemoveSlotRequested;

    /// <summary>SOOP이 같은 프로필의 동시 재생 개수 초과 경고를 표시함.</summary>
    public event Action<StreamSlotView>? SoopPlaybackLimitDetected;

    /// <summary>WebView2 장애 감지 및 자동 복구 상태가 변경됨.</summary>
    public event Action<StreamSlotView, string>? RecoveryStatusChanged;

    /// <summary>WebView2 콘텐츠에서 키 상태가 바뀜(가상 키 코드, 눌림 여부). 어떤 동작에 매핑됐는지는 호스트가 결정한다.</summary>
    public event Action<int, bool>? KeyStateChanged;

    public SlotConfiguration Configuration { get; }

    public int SlotId => Configuration.SlotId;

    public string ProfileGroupId => Configuration.ProfileGroup.Id;

    public string CurrentUrl { get; private set; } = "about:blank";

    public string CurrentStreamName { get; private set; } = "Empty";

    public bool IsMuted => _isMuted;

    public int VolumePercent => _volumePercent;

    public bool IsBrowserInitialized => _isInitialized;

    public async Task NavigateAsync(string url, string? streamName = null)
    {
        var normalizedUrl = _navigationService.NormalizeUrl(url);
        await _navigationGate.WaitAsync();
        try
        {
            _hasExplicitStreamName = _navigationService.IsMeaningfulDisplayName(streamName);
            UpdateCurrentLocation(
                normalizedUrl,
                _hasExplicitStreamName ? streamName!.Trim() : _navigationService.CreateDisplayName(normalizedUrl));

            await EnsureInitializedAsync();
            await RunNavigationAndWaitAsync(
                () => Browser.CoreWebView2.Navigate(normalizedUrl),
                NavigationTimeout);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    public async Task ReloadAsync()
    {
        if (CurrentUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _navigationGate.WaitAsync();
        try
        {
            await EnsureInitializedAsync();
            await RunNavigationAndWaitAsync(
                Browser.CoreWebView2.Reload,
                NavigationTimeout);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    public async Task WaitForPlaybackReadyAsync()
    {
        if (!IsSoopUrl(CurrentUrl))
        {
            return;
        }

        await _navigationGate.WaitAsync();
        try
        {
            await WaitForPlaybackReadyCoreAsync(PlaybackReadyTimeout);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    /// <summary>세션 복원·전체 볼륨 변경 등 외부 요청으로 볼륨을 오버레이 없이 적용한다.</summary>
    public void SetVolumePercentSilently(int volumePercent)
    {
        var clamped = Math.Clamp(volumePercent, MinVolumePercent, MaxVolumePercent);
        if (_volumePercent == clamped)
        {
            return;
        }

        _volumePercent = clamped;
        _ = ApplyVolumeToWebPageAsync();
        VolumeChanged?.Invoke(this);
    }

    public async Task ClearAsync()
    {
        await _navigationGate.WaitAsync();
        try
        {
            _hasExplicitStreamName = false;
            UpdateCurrentLocation("about:blank", "Empty");
            if (!_isInitialized)
            {
                return;
            }

            await EnsureInitializedAsync();
            await RunNavigationAndWaitAsync(
                () => Browser.CoreWebView2.Navigate("about:blank"),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    public async Task StopPlaybackForReplacementAsync()
    {
        await ClearAsync();
    }

    public async Task<StreamQualityApplyResult> ApplyQualityAsync(string qualityKey)
    {
        try
        {
            _preferredQualityKey = NormalizeQualityKey(qualityKey);
            await EnsureInitializedAsync();
            await RefreshQualityObserverScriptAsync();

            return await ClickCurrentPlayerQualityAsync(_preferredQualityKey);
        }
        catch (Exception ex)
        {
            return new StreamQualityApplyResult(false, ex.Message);
        }
    }

    public SlotRuntimeState CreateRuntimeState()
    {
        return new SlotRuntimeState(SlotId, CurrentStreamName, CurrentUrl, false, ProfileGroupId);
    }

    public void SetSelected(bool isSelected)
    {
    }

    public void SetMuted(bool isMuted, bool suppressQualityUpdate = false)
    {
        _isMuted = false;

        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.IsMuted = false;
        }
    }

    // 제거 버튼을 슬롯 오른쪽 하단에 붙일 때 두는 여백(px).
    private const double RemoveButtonMargin = 12;

    /// <summary>제거 모드(Ctrl 홀드)일 때 이 슬롯 위에 제거 버튼을 표시/숨김.</summary>
    public void SetRemoveModeActive(bool isActive, bool isSelectedForRemoval = false)
    {
        RemoveSlotPopup.IsOpen = isActive;
        if (isActive)
        {
            PositionRemoveButtonAtBottomRight(forceRefresh: true);
            QueueRefreshOpenOverlayPlacement();
        }

        if (!isActive)
        {
            _lastRemovePopupAnchor = null;
        }

        SetRemoveButtonSelected(isSelectedForRemoval);
    }

    // 제거 버튼 Popup을 슬롯 영역의 오른쪽 하단 모서리에 배치한다(Relative 배치 기준 오프셋).
    private void PositionRemoveButtonAtBottomRight(bool forceRefresh = false)
    {
        RemoveSlotPopup.HorizontalOffset =
            Math.Max(0, SlotBorder.ActualWidth - RemoveSlotButton.Width - RemoveButtonMargin);
        RemoveSlotPopup.VerticalOffset =
            Math.Max(0, SlotBorder.ActualHeight - RemoveSlotButton.Height - RemoveButtonMargin);

        var anchor = GetSlotScreenPoint(RemoveSlotPopup.HorizontalOffset, RemoveSlotPopup.VerticalOffset);
        if (forceRefresh || HasPointChanged(_lastRemovePopupAnchor, anchor))
        {
            _lastRemovePopupAnchor = anchor;
            NudgePopupPlacement(RemoveSlotPopup);
            SetPopupScreenPosition(RemoveSlotPopup, anchor);
        }
    }

    private void RemoveSlotButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSlotRequested?.Invoke(this);
        e.Handled = true;
    }

    private void SetRemoveButtonSelected(bool isSelected)
    {
        RemoveSlotButton.Background = isSelected ? SelectedRemoveButtonBackground : RemoveButtonBackground;
        RemoveSlotButton.BorderBrush = isSelected ? SelectedRemoveButtonBorder : RemoveButtonBorder;
    }

    /// <summary>제거 모드 키가 바뀌면 제거 버튼 툴팁의 키 안내를 갱신한다.</summary>
    public void SetRemoveKeyLabel(string keyLabel)
    {
        RemoveSlotButton.ToolTip = $"이 화면 제거 ({keyLabel})";
    }

    /// <summary>교체 모드 키가 바뀌면 교체 오버레이 안내 문구의 키 안내를 갱신한다.</summary>
    public void SetSwapKeyLabel(string keyLabel)
    {
        SwapModeLabel.Text = $"드래그하여 위치 교체 ({keyLabel})";
    }

    /// <summary>교체 모드(Shift 홀드)일 때 이 슬롯 위에 드래그용 오버레이를 표시/숨김.</summary>
    public void SetSwapModeActive(bool isActive)
    {
        if (!isActive)
        {
            ResetSwapOverlayHighlight();
            _lastSwapPopupAnchor = null;
        }

        SwapModePopup.IsOpen = isActive;
        if (isActive)
        {
            RefreshCenteredPopupPlacement(SwapModePopup, ref _lastSwapPopupAnchor, forceRefresh: true);
            QueueRefreshOpenOverlayPlacement();
        }
    }

    // 교체 오버레이에서 드래그 시작 준비(빈 슬롯은 이후 MouseMove에서 드래그를 시작하지 않는다).
    private void SwapOverlay_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _swapDragStartPoint = e.GetPosition((IInputElement)sender);
        SlotSelected?.Invoke(this);
    }

    private void SwapOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (_swapDragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition((IInputElement)sender);
        var movedEnough =
            Math.Abs(currentPoint.X - _swapDragStartPoint.Value.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(currentPoint.Y - _swapDragStartPoint.Value.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!movedEnough)
        {
            return;
        }

        _swapDragStartPoint = null;

        // 빈 슬롯은 교체할 영상이 없으므로 드래그를 시작하지 않는다.
        if (CurrentUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var data = new DataObject();
        data.SetData(StreamDragDataFormats.SlotId, SlotId.ToString());
        try
        {
            SwapDragStarted?.Invoke();
            DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        }
        finally
        {
            ResetSwapOverlayHighlight();
            SwapDragCompleted?.Invoke();
        }
    }

    private void SwapOverlay_DragOver(object sender, DragEventArgs e)
    {
        var canSwap = TryGetDroppedSlotId(e.Data, out var sourceSlotId) && sourceSlotId != SlotId;
        e.Effects = canSwap ? DragDropEffects.Move : DragDropEffects.None;
        SwapOverlayBorder.BorderBrush = canSwap ? SwapHighlightBrush : SwapBorderBrush;
        e.Handled = true;
    }

    private void SwapOverlay_DragLeave(object sender, DragEventArgs e)
    {
        ResetSwapOverlayHighlight();
    }

    private void SwapOverlay_Drop(object sender, DragEventArgs e)
    {
        ResetSwapOverlayHighlight();

        if (TryGetDroppedSlotId(e.Data, out var sourceSlotId) && sourceSlotId != SlotId)
        {
            SlotSelected?.Invoke(this);
            SlotSwapRequested?.Invoke(this, sourceSlotId);
        }

        e.Handled = true;
    }

    private void ResetSwapOverlayHighlight()
    {
        SwapOverlayBorder.BorderBrush = SwapBorderBrush;
    }

    private async void StreamSlotView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureInitializedAsync();
        }
        catch (Exception ex)
        {
            ShowInitializationError(ex);
        }
    }

    private async Task EnsureInitializedAsync()
    {
        await _initializationGate.WaitAsync();
        try
        {
            if (_isInitialized)
            {
                return;
            }

            InitializationOverlay.Visibility = Visibility.Visible;
            InitializationTextBlock.Text = $"Initializing Group {Configuration.ProfileGroup.Id}...";

            var environment = await _profileService.GetEnvironmentAsync(Configuration.ProfileGroup);
            if (!ReferenceEquals(_environment, environment))
            {
                if (_environment is not null)
                {
                    _environment.BrowserProcessExited -= Environment_BrowserProcessExited;
                }

                _environment = environment;
                _environment.BrowserProcessExited += Environment_BrowserProcessExited;
            }

            await Browser.EnsureCoreWebView2Async(environment);
            await InstallPlaybackViewportScriptAsync();
            await InitializeStreamSyncAsync();
            AttachCoreWebViewEvents(Browser.CoreWebView2);

            _isMuted = false;
            Browser.CoreWebView2.IsMuted = false;
            _isInitialized = true;

            InitializationOverlay.Visibility = Visibility.Collapsed;
            _ = ApplyVolumeToWebPageAsync();
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task RunNavigationAndWaitAsync(Action startNavigation, TimeSpan timeout)
    {
        if (Browser.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 is not initialized.");
        }

        ulong? expectedNavigationId = null;
        var navigationCompleted = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void StartingHandler(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            expectedNavigationId ??= e.NavigationId;
        }

        void CompletedHandler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (expectedNavigationId is null || e.NavigationId != expectedNavigationId.Value)
            {
                return;
            }

            navigationCompleted.TrySetResult(e);
        }

        Browser.CoreWebView2.NavigationStarting += StartingHandler;
        Browser.CoreWebView2.NavigationCompleted += CompletedHandler;
        try
        {
            startNavigation();
            var result = await navigationCompleted.Task.WaitAsync(timeout);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"WebView2 navigation failed: {result.WebErrorStatus}.");
            }
        }
        finally
        {
            Browser.CoreWebView2.NavigationStarting -= StartingHandler;
            Browser.CoreWebView2.NavigationCompleted -= CompletedHandler;
        }
    }

    private void AttachCoreWebViewEvents(CoreWebView2 coreWebView)
    {
        coreWebView.NavigationCompleted += CoreWebView2_NavigationCompleted;
        coreWebView.SourceChanged += CoreWebView2_SourceChanged;
        coreWebView.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
        coreWebView.WebMessageReceived += CoreWebView2_WebMessageReceived;
        coreWebView.NewWindowRequested += CoreWebView2_NewWindowRequested;
        coreWebView.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
        coreWebView.ProcessFailed += CoreWebView2_ProcessFailed;
    }

    private void DetachCoreWebViewEvents(CoreWebView2 coreWebView)
    {
        coreWebView.NavigationCompleted -= CoreWebView2_NavigationCompleted;
        coreWebView.SourceChanged -= CoreWebView2_SourceChanged;
        coreWebView.DocumentTitleChanged -= CoreWebView2_DocumentTitleChanged;
        coreWebView.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        coreWebView.NewWindowRequested -= CoreWebView2_NewWindowRequested;
        coreWebView.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;
        coreWebView.ProcessFailed -= CoreWebView2_ProcessFailed;
    }

    private void CoreWebView2_ContextMenuRequested(
        object? sender,
        CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (sender is not CoreWebView2 coreWebView)
        {
            return;
        }

        _copyAddressContextMenuItem ??= CreateCopyAddressContextMenuItem(coreWebView.Environment);
        _contextMenuAddress = CurrentUrl;
        _copyAddressContextMenuItem.IsEnabled = IsCopyableAddress(_contextMenuAddress);
        e.MenuItems.Insert(0, _copyAddressContextMenuItem);
    }

    private CoreWebView2ContextMenuItem CreateCopyAddressContextMenuItem(CoreWebView2Environment environment)
    {
        var menuItem = environment.CreateContextMenuItem(
            "주소 복사하기",
            iconStream: null,
            CoreWebView2ContextMenuItemKind.Command);
        menuItem.CustomItemSelected += (_, _) =>
        {
            if (_contextMenuAddress is not { } address || !IsCopyableAddress(address))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => CopyAddressToClipboard(address)));
        };
        return menuItem;
    }

    private static bool IsCopyableAddress(string? address)
    {
        return Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static void CopyAddressToClipboard(string address)
    {
        try
        {
            Clipboard.SetText(address);
        }
        catch (ExternalException ex)
        {
            Trace.WriteLine($"[{DateTimeOffset.Now:O}] Failed to copy stream address: {ex.Message}");
        }
    }

    private async void CoreWebView2_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        var action = WebViewRecoveryPolicy.GetAction(e.ProcessFailedKind);
        var details =
            $"WebView2 process failure: Slot {SlotId}, Group {ProfileGroupId}, " +
            $"Kind={e.ProcessFailedKind}, Reason={e.Reason}, ExitCode={e.ExitCode}, Url={CurrentUrl}";
        ReportRecoveryStatus(details);

        if (action == WebViewRecoveryAction.Recreate)
        {
            InitializationOverlay.Visibility = Visibility.Visible;
            InitializationTextBlock.Text = "WebView2 browser process stopped. Recreating...";
            return;
        }

        if (action == WebViewRecoveryAction.Reload)
        {
            await RecoverFromProcessFailureAsync(action, details);
        }
    }

    private void Environment_BrowserProcessExited(
        object? sender,
        CoreWebView2BrowserProcessExitedEventArgs e)
    {
        var details =
            $"WebView2 browser process exited: Slot {SlotId}, Group {ProfileGroupId}, " +
            $"ProcessId={e.BrowserProcessId}, ExitKind={e.BrowserProcessExitKind}, Url={CurrentUrl}";
        ReportRecoveryStatus(details);

        if (e.BrowserProcessExitKind != CoreWebView2BrowserProcessExitKind.Failed ||
            _lastRecoveredBrowserProcessId == e.BrowserProcessId)
        {
            return;
        }

        _lastRecoveredBrowserProcessId = e.BrowserProcessId;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _ = RecoverFromProcessFailureAsync(WebViewRecoveryAction.Recreate, details);
        }));
    }

    private async Task RecoverFromProcessFailureAsync(WebViewRecoveryAction action, string details)
    {
        if (!await _processRecoveryGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (action == WebViewRecoveryAction.Recreate)
            {
                await RecreateBrowserAsync();
            }
            else if (action == WebViewRecoveryAction.Reload)
            {
                await ReloadAsync();
                await WaitForPlaybackReadyAsync();
            }

            ReportRecoveryStatus($"WebView2 recovery completed: Slot {SlotId}, Action={action}, Url={CurrentUrl}");
        }
        catch (Exception ex)
        {
            InitializationOverlay.Visibility = Visibility.Visible;
            InitializationTextBlock.Text = $"WebView2 recovery failed: {ex.Message}";
            ReportRecoveryStatus($"{details}; RecoveryError={ex}");
        }
        finally
        {
            _processRecoveryGate.Release();
        }
    }

    private async Task RecreateBrowserAsync()
    {
        await _navigationGate.WaitAsync();
        try
        {
            var restoreUrl = CurrentUrl;
            var oldBrowser = Browser;
            var oldIndex = BrowserHost.Children.IndexOf(oldBrowser);
            try
            {
                if (oldBrowser.CoreWebView2 is not null)
                {
                    DetachCoreWebViewEvents(oldBrowser.CoreWebView2);
                }
            }
            catch (ObjectDisposedException)
            {
                // The failed control is already closed; replacement is still required.
            }
            catch (InvalidOperationException)
            {
                // A crashed browser process can leave CoreWebView2 inaccessible.
            }

            BrowserHost.Children.Remove(oldBrowser);
            oldBrowser.Dispose();

            Browser = CreateBrowserControl();
            BrowserHost.Children.Insert(Math.Max(0, oldIndex), Browser);
            _isInitialized = false;
            _playbackViewportScriptId = null;
            _qualityObserverScriptId = null;
            _syncBridgeScriptId = null;
            ResetStreamSyncObservations();

            await EnsureInitializedAsync();
            if (!restoreUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                await RunNavigationAndWaitAsync(
                    () => Browser.CoreWebView2.Navigate(restoreUrl),
                    NavigationTimeout);
                await WaitForPlaybackReadyCoreAsync(PlaybackReadyTimeout);
            }
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    private Microsoft.Web.WebView2.Wpf.WebView2 CreateBrowserControl()
    {
        var browser = new Microsoft.Web.WebView2.Wpf.WebView2
        {
            AllowExternalDrop = true
        };
        browser.PreviewMouseLeftButtonDown += SlotBorder_PreviewMouseLeftButtonDown;
        browser.MouseMove += SlotBorder_MouseMove;
        browser.PreviewMouseWheel += SlotBorder_PreviewMouseWheel;
        return browser;
    }

    private async Task WaitForPlaybackReadyCoreAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        PlaybackHealthSnapshot? lastSnapshot = null;
        double? previousTime = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var json = await Browser.CoreWebView2.ExecuteScriptAsync(CreatePlaybackHealthScript());
            var snapshot = JsonSerializer.Deserialize<PlaybackHealthSnapshot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (snapshot is not null)
            {
                lastSnapshot = snapshot;
                if (snapshot.ErrorCode is not null)
                {
                    throw new InvalidOperationException(
                        $"SOOP video reported media error {snapshot.ErrorCode} (networkState={snapshot.NetworkState}).");
                }

                if (snapshot.Found &&
                    !snapshot.Paused &&
                    snapshot.ReadyState >= 2 &&
                    snapshot.VideoWidth > 0 &&
                    snapshot.VideoHeight > 0 &&
                    previousTime is not null &&
                    snapshot.CurrentTime > previousTime.Value + 0.05)
                {
                    await EnsurePlaybackViewportAsync();
                    return;
                }

                if (snapshot.Found)
                {
                    previousTime = snapshot.CurrentTime;
                }
            }

            await Task.Delay(500);
        }

        var state = lastSnapshot is null
            ? "no video state returned"
            : $"found={lastSnapshot.Found}, paused={lastSnapshot.Paused}, readyState={lastSnapshot.ReadyState}, " +
              $"networkState={lastSnapshot.NetworkState}, size={lastSnapshot.VideoWidth}x{lastSnapshot.VideoHeight}, " +
              $"currentTime={lastSnapshot.CurrentTime:F2}";
        throw new TimeoutException($"SOOP playback did not become healthy within {timeout.TotalSeconds:F0}s ({state}).");
    }

    private static string CreatePlaybackHealthScript()
    {
        return """
(() => {
  const videos = Array.from(document.querySelectorAll("video"));
  const video = videos.find(candidate => !candidate.paused) || videos[0];
  if (!video) {
    return { found: false, paused: true, readyState: 0, networkState: 0, errorCode: null, videoWidth: 0, videoHeight: 0, currentTime: 0 };
  }

  return {
    found: true,
    paused: video.paused,
    readyState: video.readyState,
    networkState: video.networkState,
    errorCode: video.error?.code ?? null,
    videoWidth: video.videoWidth,
    videoHeight: video.videoHeight,
    currentTime: Number.isFinite(video.currentTime) ? video.currentTime : 0
  };
})()
""";
    }

    private static bool IsSoopUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Host.Equals("sooplive.co.kr", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".sooplive.co.kr", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("sooplive.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".sooplive.com", StringComparison.OrdinalIgnoreCase);
    }

    private void ReportRecoveryStatus(string message)
    {
        Trace.WriteLine($"[{DateTimeOffset.Now:O}] {message}");
        if (Dispatcher.CheckAccess())
        {
            RecoveryStatusChanged?.Invoke(this, message);
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => RecoveryStatusChanged?.Invoke(this, message)));
    }

    private sealed class PlaybackHealthSnapshot
    {
        public bool Found { get; init; }

        public bool Paused { get; init; }

        public int ReadyState { get; init; }

        public int NetworkState { get; init; }

        public int? ErrorCode { get; init; }

        public int VideoWidth { get; init; }

        public int VideoHeight { get; init; }

        public double CurrentTime { get; init; }
    }

    private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var currentSource = Browser.Source?.ToString();
        if (string.IsNullOrWhiteSpace(currentSource))
        {
            return;
        }

        var normalizedUrl = _navigationService.NormalizeUrl(currentSource);
        if (normalizedUrl.Equals(CurrentUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var displayName = _hasExplicitStreamName
            ? CurrentStreamName
            : _navigationService.CreateDisplayName(normalizedUrl);
        UpdateCurrentLocation(normalizedUrl, displayName);
    }

    private void CoreWebView2_DocumentTitleChanged(object? sender, object e)
    {
        if (_hasExplicitStreamName)
        {
            return;
        }

        var displayName = _navigationService.CreateDisplayName(CurrentUrl, Browser.CoreWebView2.DocumentTitle);
        if (displayName.Equals(CurrentStreamName, StringComparison.Ordinal))
        {
            return;
        }

        UpdateCurrentLocation(CurrentUrl, displayName);
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            InitializationOverlay.Visibility = Visibility.Visible;
            InitializationTextBlock.Text = $"Navigation failed: {e.WebErrorStatus}";
            return;
        }

        InitializationOverlay.Visibility = Visibility.Collapsed;
        Browser.CoreWebView2.IsMuted = false;
        _isMuted = false;
        _ = ApplyVolumeToWebPageAsync();
        _ = EnsurePlaybackViewportAsync();
    }

    private async void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (WebViewPopupPolicy.ShouldPreservePopupContext(e.Uri))
        {
            await WebViewPopupWindow.OpenRequestedAsync(
                e,
                _profileService.GetEnvironmentAsync(Configuration.ProfileGroup),
                _profileService.SoopLoginSessionCookies,
                Window.GetWindow(this));
            return;
        }

        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            Browser.CoreWebView2.Navigate(e.Uri);
        }
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        PlaybackDropMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<PlaybackDropMessage>(e.WebMessageAsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return;
        }

        if (message is null)
        {
            return;
        }

        if (message.Type.Equals("slot-wheel", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWheelVolume(message.DeltaY, message.CtrlKey);
            return;
        }

        if (message.Type.Equals("shortcut-key", StringComparison.OrdinalIgnoreCase))
        {
            if (message.KeyCode != 0)
            {
                KeyStateChanged?.Invoke(message.KeyCode, message.Pressed);
            }

            return;
        }

        if (message.Type.Equals("soop-playback-limit", StringComparison.OrdinalIgnoreCase))
        {
            SoopPlaybackLimitDetected?.Invoke(this);
            return;
        }

        if (!message.Type.Equals("stream-drop", StringComparison.OrdinalIgnoreCase) ||
            !StreamDropDataReader.TryNormalizeDroppedText(message.Url, _navigationService, out var url))
        {
            return;
        }

        SlotSelected?.Invoke(this);
        StreamUrlDropRequested?.Invoke(this, url, message.StreamName);
    }

    private void SlotBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _slotDragStartPoint = e.GetPosition(this);
        SlotSelected?.Invoke(this);
    }

    private void SlotBorder_MouseMove(object sender, MouseEventArgs e)
    {
        if (_slotDragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        var movedEnough =
            Math.Abs(currentPoint.X - _slotDragStartPoint.Value.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(currentPoint.Y - _slotDragStartPoint.Value.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!movedEnough)
        {
            return;
        }

        _slotDragStartPoint = null;
        var data = new DataObject();
        data.SetData(StreamDragDataFormats.SlotId, SlotId.ToString());
        data.SetData(StreamDragDataFormats.StreamUrl, CurrentUrl);
        data.SetData(DataFormats.UnicodeText, CurrentUrl);
        data.SetData(DataFormats.Text, CurrentUrl);
        if (!string.IsNullOrWhiteSpace(CurrentStreamName))
        {
            data.SetData(StreamDragDataFormats.StreamName, CurrentStreamName);
        }

        try
        {
            HostDragStarted?.Invoke();
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        finally
        {
            HostDragCompleted?.Invoke();
        }
    }

    private void SlotBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0)
        {
            return;
        }

        ApplyWheelVolume(-e.Delta, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        e.Handled = true;
    }

    private void ApplyWheelVolume(double deltaY, bool ctrlKey)
    {
        if (deltaY == 0)
        {
            return;
        }

        SlotSelected?.Invoke(this);

        // 한 번의 물리적 스크롤이 발생시킨 연속된 휠 이벤트 묶음은 첫 이벤트만 반영하고
        // 나머지는 무시해 한 번에 하나의 증감 단위만 적용되도록 한다.
        var now = Environment.TickCount64;
        if (now - _lastWheelStepTimestamp < WheelStepThrottleMilliseconds)
        {
            return;
        }

        _lastWheelStepTimestamp = now;
        SetVolumePercent(CalculateWheelVolumePercent(_volumePercent, deltaY, ctrlKey));
    }

    private static int CalculateWheelVolumePercent(int currentVolumePercent, double deltaY, bool ctrlKey)
    {
        var direction = Math.Sign(deltaY);
        if (direction == 0)
        {
            return Math.Clamp(currentVolumePercent, MinVolumePercent, MaxVolumePercent);
        }

        var stepPercent = ctrlKey ? FineVolumeStepPercent : VolumeStepPercent;
        var nextVolumePercent = currentVolumePercent + (direction < 0 ? stepPercent : -stepPercent);
        return Math.Clamp(nextVolumePercent, MinVolumePercent, MaxVolumePercent);
    }

    private void SetVolumePercent(int volumePercent)
    {
        var clamped = Math.Clamp(volumePercent, MinVolumePercent, MaxVolumePercent);
        if (_volumePercent == clamped)
        {
            ShowVolumeIndicator(_volumePercent);
            return;
        }

        _volumePercent = clamped;
        ShowVolumeIndicator(_volumePercent);
        _ = ApplyVolumeToWebPageAsync();
        VolumeChanged?.Invoke(this);
    }

    private void ShowVolumeIndicator(int volumePercent)
    {
        VolumeIndicatorTextBlock.Text = $"볼륨 {volumePercent}%";
        VolumeIndicatorPopup.IsOpen = true;
        RefreshCenteredPopupPlacement(VolumeIndicatorPopup, ref _lastVolumePopupAnchor, forceRefresh: true);
        QueueRefreshOpenOverlayPlacement();
        _volumeOverlayTimer.Stop();
        _volumeOverlayTimer.Start();
    }

    public void RefreshOverlayPlacement()
    {
        RefreshOpenOverlayPlacement(force: true);
    }

    private void RefreshOpenOverlayPlacement(bool force)
    {
        if (RemoveSlotPopup.IsOpen)
        {
            PositionRemoveButtonAtBottomRight(force);
        }

        if (SwapModePopup.IsOpen)
        {
            RefreshCenteredPopupPlacement(SwapModePopup, ref _lastSwapPopupAnchor, force);
        }

        if (VolumeIndicatorPopup.IsOpen)
        {
            RefreshCenteredPopupPlacement(VolumeIndicatorPopup, ref _lastVolumePopupAnchor, force);
        }

        if (SyncStatusPopup.IsOpen)
        {
            RefreshSyncBadgePlacement(force);
        }
    }

    private void QueueRefreshOpenOverlayPlacement()
    {
        Dispatcher.BeginInvoke(
            (Action)(() => RefreshOpenOverlayPlacement(force: true)),
            DispatcherPriority.Render);
    }

    private void RefreshCenteredPopupPlacement(Popup popup, ref Point? lastAnchor, bool forceRefresh)
    {
        var anchor = GetSlotScreenPoint(SlotBorder.ActualWidth / 2, SlotBorder.ActualHeight / 2);
        if (!forceRefresh && !HasPointChanged(lastAnchor, anchor))
        {
            return;
        }

        lastAnchor = anchor;
        NudgePopupPlacement(popup);
        SetPopupScreenPosition(popup, GetCenteredPopupTopLeft(popup, anchor));
    }

    private Point GetSlotScreenPoint(double x, double y)
    {
        return SlotBorder.PointToScreen(new Point(x, y));
    }

    private static bool HasPointChanged(Point? previous, Point current)
    {
        return previous is null ||
               Math.Abs(previous.Value.X - current.X) > 0.5 ||
               Math.Abs(previous.Value.Y - current.Y) > 0.5;
    }

    private static void NudgePopupPlacement(Popup popup)
    {
        var offset = popup.HorizontalOffset;
        popup.HorizontalOffset = offset + 0.01;
        popup.HorizontalOffset = offset;
    }

    private Point GetCenteredPopupTopLeft(Popup popup, Point centerScreenPoint)
    {
        if (popup.Child is not FrameworkElement child ||
            child.ActualWidth <= 0 ||
            child.ActualHeight <= 0)
        {
            QueueRefreshOpenOverlayPlacement();
            return centerScreenPoint;
        }

        var pixelSize = GetElementPixelSize(child);
        return new Point(
            centerScreenPoint.X - pixelSize.Width / 2,
            centerScreenPoint.Y - pixelSize.Height / 2);
    }

    private static Size GetElementPixelSize(FrameworkElement element)
    {
        var size = new Size(element.ActualWidth, element.ActualHeight);
        if (PresentationSource.FromVisual(element) is not HwndSource source ||
            source.CompositionTarget is null)
        {
            return size;
        }

        var transform = source.CompositionTarget.TransformToDevice;
        return new Size(size.Width * transform.M11, size.Height * transform.M22);
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

    private static void SetPopupNotTopmost(Popup popup)
    {
        if (popup.Child is not { } child ||
            PresentationSource.FromVisual(child) is not HwndSource source ||
            source.Handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            source.Handle,
            HwndNotTopmost,
            0,
            0,
            0,
            0,
            SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
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

    private async Task ApplyVolumeToWebPageAsync()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await Browser.CoreWebView2.ExecuteScriptAsync(CreateSetVolumeScript(_volumePercent));
        }
        catch
        {
            // Ignore transient script execution failures.
        }
    }

    private void SlotBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshOpenOverlayPlacement(force: false);
        if (!_isInitialized || !IsSoopUrl(CurrentUrl))
        {
            return;
        }

        _playbackViewportResizeTimer.Stop();
        _playbackViewportResizeTimer.Start();
    }

    private async Task EnsurePlaybackViewportAsync()
    {
        try
        {
            if (!IsSoopUrl(CurrentUrl))
            {
                return;
            }

            var coreWebView = Browser.CoreWebView2;
            if (coreWebView is null)
            {
                return;
            }

            await coreWebView.ExecuteScriptAsync(
                """
(() => {
  if (typeof window.__streamOrchestraEnsurePlaybackViewport !== "function") {
    return false;
  }

  window.__streamOrchestraEnsurePlaybackViewport();
  return true;
})()
""");
        }
        catch
        {
            // Ignore transient script execution failures during navigation or process recovery.
        }
    }

    private async Task NotifyPlaybackViewportSizeChangedAsync()
    {
        try
        {
            if (!_isInitialized || !IsSoopUrl(CurrentUrl))
            {
                return;
            }

            var coreWebView = Browser.CoreWebView2;
            if (coreWebView is null)
            {
                return;
            }

            await coreWebView.ExecuteScriptAsync(
                """
(() => {
  if (typeof window.__streamOrchestraHandleHostResize !== "function") {
    return false;
  }

  window.__streamOrchestraHandleHostResize();
  return true;
})()
""");
        }
        catch
        {
            // Ignore transient script execution failures while WebView2 is resizing or navigating.
        }
    }

    private void SlotBorder_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsAcceptableDrop(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void SlotBorder_Drop(object sender, DragEventArgs e)
    {
        // 슬롯 간 드래그(레이아웃 내 영상 위치 교환)
        if (TryGetDroppedSlotId(e.Data, out var sourceSlotId) && sourceSlotId != SlotId)
        {
            SlotSelected?.Invoke(this);
            SlotSwapRequested?.Invoke(this, sourceSlotId);
            e.Handled = true;
            return;
        }

        // 영상 영역에 채널 드롭 = 현재 슬롯의 영상을 교체
        if (StreamDropDataReader.TryGetDroppedStream(e.Data, _navigationService, out var url, out var streamName))
        {
            SlotSelected?.Invoke(this);
            StreamUrlDropRequested?.Invoke(this, url, streamName);
            e.Handled = true;
        }
    }

    private bool IsAcceptableDrop(IDataObject data)
    {
        return TryGetDroppedSlotId(data, out _) ||
               StreamDropDataReader.TryGetDroppedStream(data, _navigationService, out _, out _);
    }

    private static bool TryGetDroppedSlotId(IDataObject data, out int slotId)
    {
        slotId = 0;
        if (!data.GetDataPresent(StreamDragDataFormats.SlotId))
        {
            return false;
        }

        var value = data.GetData(StreamDragDataFormats.SlotId)?.ToString();
        return int.TryParse(value, out slotId);
    }

    private static string NormalizeQualityKey(string qualityKey)
    {
        return qualityKey.Trim().ToLowerInvariant() switch
        {
            "auto" or "adaptive" or "master" => "master",
            "1440" or "1440p" or "q1440" => "q1440",
            "source" or "best" or "max" or "maximum" or "1080" or "1080p" or "original" => "original",
            "720" or "720p" or "hd4k" => "hd4k",
            "540" or "540p" or "hd" => "hd",
            "360" or "360p" or "sd" => "sd",
            _ => "master"
        };
    }

    private static string FormatQualityLabel(string qualityKey)
    {
        return NormalizeQualityKey(qualityKey) switch
        {
            "master" => "auto",
            "q1440" => "1440p",
            "original" => "1080p",
            "hd4k" => "720p",
            "hd" => "540p",
            "sd" => "360p",
            _ => qualityKey
        };
    }

    private async Task InstallPlaybackViewportScriptAsync()
    {
        if (_playbackViewportScriptId is not null || Browser.CoreWebView2 is null)
        {
            return;
        }

        _playbackViewportScriptId = await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            CreatePlaybackViewportScript());
    }

    private static string CreatePlaybackViewportScript()
    {
        return """
(() => {
  if (window.__streamOrchestraPlaybackViewportInstalled) {
    return;
  }

  window.__streamOrchestraPlaybackViewportInstalled = true;

  const styleText = `
    html, body {
      width: 100% !important;
      height: 100% !important;
      margin: 0 !important;
      overflow: hidden !important;
      background: #000 !important;
    }

    video {
      max-width: 100vw !important;
      max-height: 100vh !important;
      object-fit: contain !important;
      object-position: center center !important;
    }

    .stream-orchestra-hidden {
      display: none !important;
      visibility: hidden !important;
      opacity: 0 !important;
      pointer-events: none !important;
    }

    .embeded_mode #webplayer.chat_open #chatting_area {
      display: none !important;
    }

    .embeded_mode #webplayer #player div.quality_box {
      display: block !important;
    }

    #webplayer #player_info {
      display: none !important;
      visibility: hidden !important;
      opacity: 0 !important;
      pointer-events: none !important;
    }

    .popout_chat #chatting_area {
      min-width: auto !important;
    }

    body.stream-orchestra-immersive-mode .stream-orchestra-viewport-root {
      position: fixed !important;
      inset: 0 !important;
      width: 100vw !important;
      height: 100vh !important;
      min-width: 0 !important;
      min-height: 0 !important;
      max-width: none !important;
      max-height: none !important;
      margin: 0 !important;
      padding: 0 !important;
      border: 0 !important;
      display: flex !important;
      flex-direction: row !important;
      overflow: hidden !important;
      box-sizing: border-box !important;
      z-index: 2147483000 !important;
      background: #000 !important;
    }

    body.stream-orchestra-immersive-mode .stream-orchestra-viewport-root .stream-orchestra-player-layer {
      width: 100% !important;
      height: 100% !important;
      min-width: 0 !important;
      min-height: 0 !important;
      max-width: none !important;
      max-height: none !important;
      margin: 0 !important;
      padding: 0 !important;
      border: 0 !important;
      box-sizing: border-box !important;
    }

    body.stream-orchestra-immersive-mode .stream-orchestra-viewport-root .stream-orchestra-playback-video {
      display: block !important;
      width: 100% !important;
      height: 100% !important;
      min-width: 0 !important;
      min-height: 0 !important;
      max-width: none !important;
      max-height: none !important;
      margin: auto !important;
      object-fit: contain !important;
      object-position: center center !important;
    }

    body.stream-orchestra-immersive-mode .stream-orchestra-viewport-root #webplayer_contents,
    body.stream-orchestra-immersive-mode #webplayer.stream-orchestra-viewport-root #webplayer_contents,
    body.screen_mode #webplayer #webplayer_contents,
    body.fullScreen_mode #webplayer #webplayer_contents {
      position: fixed !important;
      inset: 0 !important;
      top: 0 !important;
      left: 0 !important;
      width: 100vw !important;
      height: 100vh !important;
      margin: 0 !important;
      display: flex !important;
      flex-direction: row !important;
      overflow: hidden !important;
      box-sizing: border-box !important;
      background: #000 !important;
    }

    body.stream-orchestra-immersive-mode .stream-orchestra-viewport-root #player_area,
    body.stream-orchestra-immersive-mode #webplayer.stream-orchestra-viewport-root #player_area,
    body.screen_mode #webplayer #webplayer_contents #player_area,
    body.fullScreen_mode #webplayer #webplayer_contents #player_area {
      flex: 1 1 auto !important;
      min-width: 0 !important;
      min-height: 0 !important;
      width: auto !important;
      height: 100vh !important;
      max-width: none !important;
      max-height: none !important;
      overflow: hidden !important;
      box-sizing: border-box !important;
      background: #000 !important;
    }

    body.screen_mode #webplayer #player_area .htmlplayer_wrap,
    body.screen_mode #webplayer #player_area .htmlplayer_content,
    body.screen_mode #webplayer #player,
    body.fullScreen_mode #webplayer #player_area .htmlplayer_wrap,
    body.fullScreen_mode #webplayer #player_area .htmlplayer_content,
    body.fullScreen_mode #webplayer #player {
      width: 100% !important;
      height: 100% !important;
      min-width: 0 !important;
      min-height: 0 !important;
      max-width: none !important;
      max-height: none !important;
      box-sizing: border-box !important;
    }

    /* SOOP also keeps a hidden #pipMedia dummy video next to #livePlayer. Never promote every
       video here: forcing that dummy to display makes it cover the live stream with black. */
    body.screen_mode #webplayer video#livePlayer,
    body.fullScreen_mode #webplayer video#livePlayer {
      display: block !important;
      width: 100% !important;
      height: 100% !important;
      min-width: 0 !important;
      min-height: 0 !important;
      max-width: none !important;
      max-height: none !important;
      margin: auto !important;
      object-fit: contain !important;
      object-position: center center !important;
    }

    body.stream-orchestra-immersive-mode .stream-orchestra-viewport-root .wrapping.side,
    body.stream-orchestra-immersive-mode #webplayer.stream-orchestra-viewport-root #webplayer_contents .wrapping.side,
    body.screen_mode #webplayer #webplayer_contents .wrapping.side,
    body.fullScreen_mode #webplayer #webplayer_contents .wrapping.side {
      display: none !important;
      width: 0 !important;
      min-width: 0 !important;
      max-width: 0 !important;
      overflow: hidden !important;
      padding: 0 !important;
      flex-shrink: 0 !important;
    }

  `;

  function installStyle() {
    const root = document.head || document.documentElement;
    if (!root || document.getElementById("stream-orchestra-playback-viewport")) {
      return;
    }

    const style = document.createElement("style");
    style.id = "stream-orchestra-playback-viewport";
    style.textContent = styleText;
    root.appendChild(style);
  }

  installStyle();
  window.addEventListener("DOMContentLoaded", installStyle, { once: true });
  applySoopImmersiveMode();
  installSoopPlaybackLimitDetector();

  document.addEventListener("dragover", event => {
    if (!hasStreamUrlData(event.dataTransfer)) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    event.dataTransfer.dropEffect = "copy";
  }, true);

  document.addEventListener("drop", event => {
    const payload = readStreamDropPayload(event.dataTransfer);
    if (!payload?.url) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    window.chrome?.webview?.postMessage({
      type: "stream-drop",
      url: payload.url,
      streamName: payload.streamName || ""
    });
  }, true);

  document.addEventListener("wheel", event => {
    if (event.deltaY === 0) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    window.chrome?.webview?.postMessage({
      type: "slot-wheel",
      deltaY: event.deltaY,
      ctrlKey: event.ctrlKey
    });
  }, { capture: true, passive: false });

  // 단축키로 쓰는 키(ESC 제외 임의 키) 홀드 → 호스트에 키 상태 전달(WebView2가 키 포커스를 가져도 동작).
  // 어떤 키가 어떤 동작(제거/교체/전환)에 매핑되는지는 호스트가 설정으로 결정한다.
  // keyCode는 Chromium 기준 Win32 가상 키 코드와 동일하므로 호스트의 가상 키 코드와 그대로 맞는다.
  const pressedKeyCodes = new Set();

  const reportKey = (keyCode, pressed) => {
    if (!keyCode) {
      return;
    }

    if (pressed) {
      if (pressedKeyCodes.has(keyCode)) {
        return;
      }

      pressedKeyCodes.add(keyCode);
    } else {
      if (!pressedKeyCodes.has(keyCode)) {
        return;
      }

      pressedKeyCodes.delete(keyCode);
    }

    window.chrome?.webview?.postMessage({ type: "shortcut-key", keyCode, pressed });
  };

  // 채팅 입력 등 편집 가능한 요소에 포커스가 있을 때는, 임의 키 단축키가 타이핑을 가로채지 않도록 보고하지 않는다.
  const isEditableTarget = () => {
    const element = document.activeElement;
    if (!element) {
      return false;
    }

    const tag = element.tagName;
    return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || element.isContentEditable === true;
  };

  document.addEventListener("keydown", event => {
    if (isEditableTarget()) {
      return;
    }

    reportKey(event.keyCode, true);
  }, true);
  document.addEventListener("keyup", event => reportKey(event.keyCode, false), true);
  window.addEventListener("blur", () => {
    for (const keyCode of Array.from(pressedKeyCodes)) {
      pressedKeyCodes.delete(keyCode);
      window.chrome?.webview?.postMessage({ type: "shortcut-key", keyCode, pressed: false });
    }
  }, true);

  function hasStreamUrlData(dataTransfer) {
    if (!dataTransfer) {
      return false;
    }

    return Array.from(dataTransfer.types || []).some(type =>
      ["text/plain", "text/uri-list", "text/html", "Text"].includes(type));
  }

  function readStreamDropPayload(dataTransfer) {
    if (!dataTransfer) {
      return null;
    }

    const uriList = dataTransfer.getData("text/uri-list");
    const plainText = dataTransfer.getData("text/plain") || dataTransfer.getData("Text");
    const html = dataTransfer.getData("text/html");
    const htmlPayload = readHtmlPayload(html);
    const url = firstWebUrl(uriList) || htmlPayload.url || firstWebUrl(plainText);

    return url
      ? { url, streamName: htmlPayload.streamName || "" }
      : null;
  }

  function readHtmlPayload(html) {
    if (!html) {
      return { url: "", streamName: "" };
    }

    const template = document.createElement("template");
    template.innerHTML = html;
    const anchor = template.content.querySelector("a[href]");
    if (!anchor) {
      return { url: firstWebUrl(html), streamName: "" };
    }

    let url = "";
    try {
      url = new URL(anchor.getAttribute("href"), document.baseURI).href;
    } catch {
      url = firstWebUrl(html);
    }

    return {
      url,
      streamName: anchor.textContent?.trim() || anchor.getAttribute("title") || ""
    };
  }

  function firstWebUrl(value) {
    const match = String(value || "").match(/https?:\/\/[^\s"'<>]+/i);
    return match?.[0] || "";
  }

  window.__streamOrchestraVolumePercent = 100;

  function clampVolumePercent(value) {
    const normalized = Number(value);
    if (!Number.isFinite(normalized)) {
      return 1;
    }

    return Math.max(0, Math.min(1, normalized / 100));
  }

  function collectMediaElements(root, mediaElements) {
    if (!root) {
      return;
    }

    const candidates = Array.from(root.querySelectorAll ? root.querySelectorAll("audio, video") : []);
    for (const candidate of candidates) {
      if (candidate) {
        mediaElements.push(candidate);
      }
    }

    const elements = Array.from(root.querySelectorAll ? root.querySelectorAll("*") : []);
    for (const element of elements) {
      if (element?.shadowRoot) {
        collectMediaElements(element.shadowRoot, mediaElements);
      }
    }
  }

  window.__streamOrchestraApplyVolumeToMediaElements = function (volumePercent) {
    const volume = clampVolumePercent(volumePercent);
    window.__streamOrchestraVolumePercent = volumePercent;
    const mediaElements = [];
    collectMediaElements(document, mediaElements);

    for (const mediaElement of mediaElements) {
      try {
        if (!mediaElement) {
          continue;
        }

        mediaElement.volume = volume;
      } catch {}
    }
  };

  window.__streamOrchestraSetVolumePercent = function (volumePercent) {
    window.__streamOrchestraApplyVolumeToMediaElements(volumePercent);
  };

  const volumeObserver = new MutationObserver(() => {
    window.__streamOrchestraApplyVolumeToMediaElements(window.__streamOrchestraVolumePercent);
  });

  const volumeTarget = document.body || document.documentElement || document;
  if (volumeTarget) {
    volumeObserver.observe(volumeTarget, { childList: true, subtree: true });
    window.__streamOrchestraApplyVolumeToMediaElements(window.__streamOrchestraVolumePercent);
  } else {
    window.addEventListener("DOMContentLoaded", () => {
      const target = document.body || document.documentElement || document;
      if (!target) {
        return;
      }

      volumeObserver.observe(target, { childList: true, subtree: true });
      window.__streamOrchestraApplyVolumeToMediaElements(window.__streamOrchestraVolumePercent);
    }, { once: true });
  }

  function applySoopImmersiveMode() {
    const host = location.hostname.toLowerCase();
    if (!isSoopHost(host)) {
      return;
    }

    const immersiveModeClass = "stream-orchestra-immersive-mode";
    const hideSelectors = [
      "#serviceHeader",
      "#serviceLnb",
      "#header",
      ".header",
      ".top_area",
      ".topbar",
      ".global_header",
      ".live_header",
      ".player_header",
      "#player_info",
      ".title_wrap",
      ".title_area"
    ];

    const fullscreenButtonSelectors = [
      "#player .btn_fullScreen_mode",
      "#webplayer .btn_fullScreen_mode",
      ".btn_fullScreen_mode"
    ];

    const screenModeButtonSelectors = [
      "#player .btn_screen_mode",
      "#webplayer .btn_screen_mode",
      ".btn_screen_mode"
    ];

    const relevantDomSelector = [
      "#webplayer",
      "#webplayer_contents",
      "#player_area",
      "#player",
      "#livePlayer",
      "#stream-orchestra-playback-viewport",
      "video",
      ...hideSelectors,
      ...screenModeButtonSelectors,
      ...fullscreenButtonSelectors
    ].join(", ");
    let soopFullscreenRetryCount = 0;
    let soopFullscreenRetryTimer = 0;
    let soopDomRefreshTimer = 0;
    let soopViewportSatisfied = false;
    let lastScreenModeClickAt = 0;
    let nativeScreenModeRequestedAt = 0;
    let nativeScreenModeTarget = null;
    let lastFullscreenClickAt = 0;
    let observedDocumentRoot = null;
    let observedBody = null;
    let observedPlayerRoot = null;
    let viewportResizeObserver = null;
    let observedViewportResizeRoot = null;
    const observedChromeElements = new WeakSet();

    const hideElements = () => {
      for (const selector of hideSelectors) {
        for (const element of document.querySelectorAll(selector)) {
          element.classList.add("stream-orchestra-hidden");
        }
      }
    };

    const isUsableElement = element =>
      element &&
      element !== document.documentElement &&
      element !== document.body;

    const isVisibleElement = element => {
      if (!isUsableElement(element) || element.disabled) {
        return false;
      }

      const style = window.getComputedStyle(element);
      if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity) === 0) {
        return false;
      }

      const rect = element.getBoundingClientRect();
      const viewportWidth = window.innerWidth || document.documentElement?.clientWidth || 0;
      const viewportHeight = window.innerHeight || document.documentElement?.clientHeight || 0;
      return rect.width > 0 &&
        rect.height > 0 &&
        rect.right > 0 &&
        rect.bottom > 0 &&
        rect.left < viewportWidth &&
        rect.top < viewportHeight;
    };

    const findFirstButton = selectors => {
      for (const selector of selectors) {
        for (const button of document.querySelectorAll(selector)) {
          if (isVisibleElement(button)) {
            return button;
          }
        }
      }

      return null;
    };

    const hasVisiblePageChrome = () => {
      for (const selector of hideSelectors) {
        for (const element of document.querySelectorAll(selector)) {
          if (isVisibleElement(element)) {
            return true;
          }
        }
      }

      return false;
    };

    const findSoopPlaybackVideo = () => {
      let videos = Array.from(document.querySelectorAll(
        "video#livePlayer, #livePlayer video, #webplayer video, #player_area video, .htmlplayer_wrap video"));
      if (videos.length === 0) {
        videos = Array.from(document.querySelectorAll("video"));
      }

      return videos.find(video => isVisibleElement(video) && !video.paused) ||
        videos.find(isVisibleElement) ||
        videos[0] ||
        null;
    };

    // Keep the native hover controls inside the promoted stacking context by preferring
    // the outer player shell over the narrower video-only containers.
    const getSoopViewportRoot = (video = findSoopPlaybackVideo()) =>
      video?.closest("#webplayer") ||
      video?.closest("#webplayer_contents") ||
      video?.closest("#player_area") ||
      video?.closest(".htmlplayer_wrap") ||
      video?.closest(".htmlplayer_content") ||
      video?.closest("#player") ||
      Array.from(document.querySelectorAll("#webplayer")).find(element => element.querySelector("video")) ||
      Array.from(document.querySelectorAll("#webplayer_contents")).find(element => element.querySelector("video")) ||
      video?.parentElement ||
      null;

    const isSoopViewportSatisfied = () => {
      const video = findSoopPlaybackVideo();
      const playerRoot = getSoopViewportRoot(video);
      if (!video || !playerRoot) {
        return false;
      }

      const viewportWidth = window.innerWidth || document.documentElement?.clientWidth || 0;
      const viewportHeight = window.innerHeight || document.documentElement?.clientHeight || 0;
      if (viewportWidth <= 0 || viewportHeight <= 0) {
        return false;
      }

      const style = window.getComputedStyle(playerRoot);
      if (style.display === "none" || style.visibility === "hidden" || hasVisiblePageChrome()) {
        return false;
      }

      const rect = playerRoot.getBoundingClientRect();
      const videoRect = video.getBoundingClientRect();
      const videoStyle = window.getComputedStyle(video);
      const tolerance = Math.max(2, Math.min(8, Math.min(viewportWidth, viewportHeight) * 0.01));
      // A one-axis match accepts a top-aligned 16:9 element in a taller slot. Require the
      // element box to match all four viewport edges; object-fit then centers the pixels.
      const fillsViewport = candidateRect =>
        Math.abs(candidateRect.left) <= tolerance &&
        Math.abs(candidateRect.top) <= tolerance &&
        Math.abs(candidateRect.right - viewportWidth) <= tolerance &&
        Math.abs(candidateRect.bottom - viewportHeight) <= tolerance &&
        Math.abs(candidateRect.width - viewportWidth) <= tolerance * 2 &&
        Math.abs(candidateRect.height - viewportHeight) <= tolerance * 2;
      const videoFillsViewport = fillsViewport(videoRect);
      const objectPosition = String(videoStyle.objectPosition || "").toLowerCase();
      const videoUsesCenteredContain =
        videoStyle.objectFit === "contain" &&
        (objectPosition.includes("center") || objectPosition === "50% 50%");
      return videoFillsViewport &&
        videoUsesCenteredContain &&
        fillsViewport(rect);
    };

    const isSoopPlaybackModeActive = () => isSoopViewportSatisfied();

    const applyKnownSoopViewportFallback = () => {
      const video = findSoopPlaybackVideo();
      const playerRoot = getSoopViewportRoot(video);
      if (!document.body || !video || !playerRoot) {
        return false;
      }

      for (const previousRoot of document.querySelectorAll(".stream-orchestra-viewport-root")) {
        if (previousRoot !== playerRoot) {
          previousRoot.classList.remove("stream-orchestra-viewport-root");
        }
      }

      for (const previousVideo of document.querySelectorAll(".stream-orchestra-playback-video")) {
        if (previousVideo !== video) {
          previousVideo.classList.remove("stream-orchestra-playback-video");
        }
      }

      for (const previousLayer of playerRoot.querySelectorAll(".stream-orchestra-player-layer")) {
        previousLayer.classList.remove("stream-orchestra-player-layer");
      }

      document.body.classList.add(immersiveModeClass);
      playerRoot.classList.add("stream-orchestra-viewport-root");
      video.classList.add("stream-orchestra-playback-video");

      let playerLayer = video.parentElement;
      while (playerLayer && playerLayer !== playerRoot &&
             playerLayer !== document.body && playerLayer !== document.documentElement) {
        playerLayer.classList.add("stream-orchestra-player-layer");
        playerLayer = playerLayer.parentElement;
      }

      return isSoopViewportSatisfied();
    };

    const isNativePlaybackModeActive = () =>
      Boolean(document.body?.classList.contains("screen_mode") ||
        document.body?.classList.contains("fullScreen_mode") ||
        document.fullscreenElement);

    const clickScreenModeButton = video => {
      if (isNativePlaybackModeActive()) {
        return "active";
      }

      const now = Date.now();
      if (nativeScreenModeTarget === video && nativeScreenModeRequestedAt > 0) {
        return now - nativeScreenModeRequestedAt < 1500 ? "pending" : "timed-out";
      }

      const button = findFirstButton(screenModeButtonSelectors);
      if (!button) {
        return "unavailable";
      }

      if (now - lastScreenModeClickAt < 1000) {
        return "pending";
      }

      lastScreenModeClickAt = now;
      nativeScreenModeRequestedAt = now;
      nativeScreenModeTarget = video;
      try {
        button.click();
        return "requested";
      } catch {
        nativeScreenModeRequestedAt = 0;
        nativeScreenModeTarget = null;
        return "failed";
      }
    };

    const clickFullscreenButton = () => {
      if (document.body?.classList.contains("fullScreen_mode") || document.fullscreenElement) {
        return true;
      }

      const button = findFirstButton(fullscreenButtonSelectors);
      if (!button) {
        return false;
      }

      const now = Date.now();
      if (now - lastFullscreenClickAt < 1000) {
        return false;
      }

      lastFullscreenClickAt = now;
      try {
        button.click();
        return true;
      } catch {
        return false;
      }
    };

    const clearSoopFullscreenRetry = () => {
      if (soopFullscreenRetryTimer === 0) {
        return;
      }

      window.clearTimeout(soopFullscreenRetryTimer);
      soopFullscreenRetryTimer = 0;
    };

    const requestSoopFullscreenViewport = () => {
      const video = findSoopPlaybackVideo();
      if (!video) {
        soopViewportSatisfied = false;
        return false;
      }

      hideElements();
      if (isSoopViewportSatisfied()) {
        soopViewportSatisfied = true;
        clearSoopFullscreenRetry();
        return true;
      }

      const screenModeRequest = clickScreenModeButton(video);
      // SOOP applies the body mode class and rebuilds its controls asynchronously. Give that
      // transition time to settle before installing the deterministic CSS fallback.
      const waitingForNativeMode =
        (screenModeRequest === "requested" || screenModeRequest === "pending") &&
        !isNativePlaybackModeActive() &&
        Date.now() - nativeScreenModeRequestedAt < 1500;
      if (waitingForNativeMode) {
        soopViewportSatisfied = false;
        return false;
      }

      const fallbackApplied = applyKnownSoopViewportFallback();
      if ((screenModeRequest === "unavailable" || screenModeRequest === "timed-out" ||
           screenModeRequest === "failed") &&
          !fallbackApplied) {
        clickFullscreenButton();
      }

      soopViewportSatisfied = isSoopViewportSatisfied();
      if (soopViewportSatisfied) {
        clearSoopFullscreenRetry();
      }

      return soopViewportSatisfied;
    };

    const scheduleSoopFullscreenRetry = () => {
      if (!findSoopPlaybackVideo() || soopViewportSatisfied || soopFullscreenRetryTimer !== 0) {
        return;
      }

      const retryDelay = soopFullscreenRetryCount < 120 ? 250 : 2000;
      soopFullscreenRetryTimer = window.setTimeout(() => {
        soopFullscreenRetryTimer = 0;
        soopFullscreenRetryCount = Math.min(120, soopFullscreenRetryCount + 1);
        requestSoopFullscreenViewport();
        scheduleSoopFullscreenRetry();
      }, retryDelay);
    };

    const wireMediaPlayback = () => {
      for (const video of document.querySelectorAll("video")) {
        if (video.__streamOrchestraSoopPlaybackWired) {
          continue;
        }

        video.__streamOrchestraSoopPlaybackWired = true;
        video.addEventListener("play", () => {
          soopFullscreenRetryCount = 0;
          requestSoopFullscreenViewport();
          scheduleSoopFullscreenRetry();
        }, { passive: true });
        if (!video.paused) {
          requestSoopFullscreenViewport();
          scheduleSoopFullscreenRetry();
        }
      }
    };

    let observer = null;

    const attachSoopObserver = () => {
      if (!observer) {
        return;
      }

      const documentRoot = document.documentElement || document.body;
      if (documentRoot && documentRoot !== observedDocumentRoot) {
        observer.observe(documentRoot, { childList: true, subtree: true });
        observedDocumentRoot = documentRoot;
      }

      if (document.body && document.body !== observedBody) {
        observer.observe(document.body, { attributes: true, attributeFilter: ["class", "hidden"] });
        observedBody = document.body;
      }

      const playerRoot = getSoopViewportRoot();
      if (playerRoot && playerRoot !== observedPlayerRoot) {
        observer.observe(playerRoot, { attributes: true, attributeFilter: ["class", "hidden"] });
        observedPlayerRoot = playerRoot;
      }

      if (viewportResizeObserver && playerRoot !== observedViewportResizeRoot) {
        viewportResizeObserver.disconnect();
        if (playerRoot) {
          viewportResizeObserver.observe(playerRoot);
        }

        observedViewportResizeRoot = playerRoot;
      }

      for (const selector of hideSelectors) {
        for (const element of document.querySelectorAll(selector)) {
          if (observedChromeElements.has(element)) {
            continue;
          }

          observer.observe(element, { attributes: true, attributeFilter: ["class", "hidden"] });
          observedChromeElements.add(element);
        }
      }
    };

    const nodeTouchesSoopPlayback = node => {
      if (node?.nodeType !== 1) {
        return false;
      }

      return Boolean(node.matches?.(relevantDomSelector) || node.querySelector?.(relevantDomSelector));
    };

    const mutationTouchesSoopPlayback = mutation => {
      if (mutation.type === "attributes") {
        return true;
      }

      return Array.from(mutation.addedNodes || []).some(nodeTouchesSoopPlayback) ||
        Array.from(mutation.removedNodes || []).some(nodeTouchesSoopPlayback);
    };

    const refreshSoopViewport = () => {
      soopDomRefreshTimer = 0;
      installStyle();
      attachSoopObserver();
      hideElements();
      wireMediaPlayback();
      soopViewportSatisfied = isSoopPlaybackModeActive();
      if (soopViewportSatisfied) {
        clearSoopFullscreenRetry();
      }

      if (!soopViewportSatisfied && findSoopPlaybackVideo()) {
        requestSoopFullscreenViewport();
        scheduleSoopFullscreenRetry();
      }
    };

    const scheduleSoopDomRefresh = () => {
      if (soopDomRefreshTimer !== 0) {
        return;
      }

      soopDomRefreshTimer = window.setTimeout(refreshSoopViewport, 100);
    };

    const invalidatePlaybackViewport = () => {
      // A WebView host resize does not necessarily mutate the DOM, so invalidate the cached
      // geometry explicitly and re-measure after the new viewport has settled.
      soopViewportSatisfied = false;
      scheduleSoopDomRefresh();
      scheduleSoopFullscreenRetry();
    };

    observer = new MutationObserver(mutations => {
      if (Array.from(mutations || []).some(mutationTouchesSoopPlayback)) {
        scheduleSoopDomRefresh();
      }
    });
    viewportResizeObserver = typeof ResizeObserver === "function"
      ? new ResizeObserver(invalidatePlaybackViewport)
      : null;

    window.__streamOrchestraEnsurePlaybackViewport = () => {
      soopFullscreenRetryCount = 0;
      installStyle();
      attachSoopObserver();
      hideElements();
      wireMediaPlayback();
      requestSoopFullscreenViewport();
      scheduleSoopFullscreenRetry();
    };

    window.__streamOrchestraHandleHostResize = () => {
      invalidatePlaybackViewport();
      window.requestAnimationFrame(() => {
        window.requestAnimationFrame(window.__streamOrchestraEnsurePlaybackViewport);
      });
    };

    attachSoopObserver();
    hideElements();
    wireMediaPlayback();
    window.__streamOrchestraEnsurePlaybackViewport();
    window.addEventListener("DOMContentLoaded", () => {
      attachSoopObserver();
      window.__streamOrchestraEnsurePlaybackViewport();
    }, { once: true });
    document.addEventListener("fullscreenchange", window.__streamOrchestraEnsurePlaybackViewport);
    window.addEventListener("resize", invalidatePlaybackViewport, { passive: true });
    window.visualViewport?.addEventListener("resize", invalidatePlaybackViewport, { passive: true });
  }

  function installSoopPlaybackLimitDetector() {
    const host = location.hostname.toLowerCase();
    if (!isSoopHost(host)) {
      return;
    }

    let reported = false;
    let checkTimer = 0;

    const matchesPlaybackLimitWarning = text => {
      const compactText = String(text || "").replace(/\s+/g, "");
      return compactText.includes("초과") && (
        compactText.includes("방송개수") ||
        compactText.includes("방송갯수") ||
        compactText.includes("시청개수") ||
        compactText.includes("시청갯수") ||
        compactText.includes("동시시청") ||
        compactText.includes("동시재생") ||
        /방송.{0,30}(개수|갯수).{0,30}초과/.test(compactText) ||
        /초과.{0,30}(방송|시청)/.test(compactText)
      );
    };

    const reportIfDetected = () => {
      checkTimer = 0;
      if (reported || !document.body) {
        return;
      }

      if (!matchesPlaybackLimitWarning(document.body.innerText || "")) {
        return;
      }

      reported = true;
      window.chrome?.webview?.postMessage({ type: "soop-playback-limit" });
    };

    const scheduleCheck = () => {
      if (reported || checkTimer !== 0) {
        return;
      }

      checkTimer = window.setTimeout(reportIfDetected, 750);
    };

    scheduleCheck();
    window.addEventListener("DOMContentLoaded", scheduleCheck, { once: true });

    const target = document.documentElement || document.body;
    if (target) {
      const observer = new MutationObserver(scheduleCheck);
      observer.observe(target, { childList: true, subtree: true, characterData: true });
    }
  }

  function isSoopHost(host) {
    return host === "sooplive.co.kr" ||
      host.endsWith(".sooplive.co.kr") ||
      host === "sooplive.com" ||
      host.endsWith(".sooplive.com");
  }
})();
""";
    }

    private static string CreateSetVolumeScript(int volumePercent)
    {
        var clamped = Math.Clamp(volumePercent, MinVolumePercent, MaxVolumePercent);
        var template = """
(() => {
  if (typeof window.__streamOrchestraSetVolumePercent === "function") {
    window.__streamOrchestraSetVolumePercent({0});
    return;
  }

  const volume = {0} / 100;
  const mediaElements = document.querySelectorAll("audio, video");
  for (const mediaElement of mediaElements) {
    try {
      mediaElement.volume = volume;
    } catch {}
  }
})();
""";

        return template.Replace("{0}", clamped.ToString());
    }

    private async Task RefreshQualityObserverScriptAsync()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        if (_qualityObserverScriptId is not null)
        {
            Browser.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_qualityObserverScriptId);
        }

        _qualityObserverScriptId = await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            CreateQualityObserverScript(_preferredQualityKey));
    }

    private async Task<StreamQualityApplyResult> ClickCurrentPlayerQualityAsync(string qualityKey)
    {
        var json = await Browser.CoreWebView2.ExecuteScriptAsync(CreateClickCurrentPlayerQualityScript(qualityKey));
        return JsonSerializer.Deserialize<StreamQualityApplyResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new StreamQualityApplyResult(false, "SOOP player returned no quality result.");
    }

    private static string CreateQualityObserverScript(string qualityKey)
    {
        var qualityJson = JsonSerializer.Serialize(NormalizeQualityKey(qualityKey));

        return $$"""
(() => {
  window.__streamOrchestraPreferredQuality = {{qualityJson}};
  window.__streamOrchestraQualityApplied = false;
  window.__streamOrchestraClickQuality = clickQuality;

  if (window.__streamOrchestraQualityObserverInstalled) {
    window.__streamOrchestraApplyQuality?.();
    return;
  }

  window.__streamOrchestraQualityObserverInstalled = true;
  window.__streamOrchestraApplyQuality = () => {
    if (window.__streamOrchestraQualityApplied) {
      return;
    }

    const result = window.__streamOrchestraClickQuality?.(window.__streamOrchestraPreferredQuality);
    if (result?.isSuccess) {
      window.__streamOrchestraQualityApplied = true;
    }
  };

  const observer = new MutationObserver(() => window.__streamOrchestraApplyQuality());
  if (document.body) {
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["class", "style"] });
  } else {
    window.addEventListener("DOMContentLoaded", () => {
      observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["class", "style"] });
      window.__streamOrchestraApplyQuality();
    });
  }

  window.__streamOrchestraApplyQuality();
{{CreateQualityClickFunctionScript()}}
})();
""";
    }

    private static string CreateClickCurrentPlayerQualityScript(string qualityKey)
    {
        var qualityJson = JsonSerializer.Serialize(NormalizeQualityKey(qualityKey));

        return $$"""
(() => {
  const targetQuality = {{qualityJson}};
  window.__streamOrchestraPreferredQuality = targetQuality;
  window.__streamOrchestraQualityApplied = false;
  window.__streamOrchestraClickQuality = clickQuality;

  return clickQuality(targetQuality);
{{CreateQualityClickFunctionScript()}}
})();
""";
    }

    private static string CreateQualityClickFunctionScript()
    {
        return """
  function clickQuality(qualityKey) {
  const fixedTargets = {
    master: ["자동"],
    q1440: ["1440p"],
    original: ["1080p"],
    hd4k: ["720p"],
    hd: ["540p"],
    sd: ["360p"]
  };

  const qualityBoxes = Array.from(document.querySelectorAll(".quality_box"));
  if (qualityBoxes.length === 0) {
    return { isSuccess: false, message: "SOOP quality box was not found." };
  }

  for (const qualityBox of qualityBoxes) {
    const button = findQualityButton(qualityBox, qualityKey);
    if (!button) {
      continue;
    }

    if (!button.classList.contains("on")) {
      button.click();
    }

    return { isSuccess: true, message: "SOOP quality set to " + getQualityText(button) + "." };
  }

  return { isSuccess: false, message: "Requested SOOP quality was not available." };

  function findQualityButton(qualityBox, qualityKey) {
    const availableButtons = Array.from(qualityBox.querySelectorAll("ul button"))
      .filter(isAvailable);
    if (availableButtons.length === 0) {
      return null;
    }

    const targets = fixedTargets[qualityKey] || [];
    return targets.map(text => findButtonByText(availableButtons, text)).find(Boolean) || null;
  }

  function findButtonByText(buttons, text) {
    return buttons.find(button => getQualityText(button) === text) || null;
  }

  function isAvailable(button) {
    const li = button.closest("li");
    return Boolean(button) &&
      (!li || li.style.display !== "none") &&
      button.offsetParent !== null;
  }

  function getQualityText(button) {
    return button?.querySelector("span")?.textContent?.trim() ||
      button?.textContent?.trim() ||
      "";
  }
  }
""";
    }

    private void UpdateCurrentLocation(string url, string streamName)
    {
        var normalizedName = string.IsNullOrWhiteSpace(streamName)
            ? _navigationService.CreateDisplayName(url)
            : streamName.Trim();
        if (CurrentUrl.Equals(url, StringComparison.OrdinalIgnoreCase) &&
            CurrentStreamName.Equals(normalizedName, StringComparison.Ordinal))
        {
            return;
        }

        CurrentUrl = url;
        CurrentStreamName = normalizedName;
        PlaybackStateChanged?.Invoke(this);
    }

    private void ShowInitializationError(Exception ex)
    {
        InitializationOverlay.Visibility = Visibility.Visible;
        InitializationTextBlock.Text = ex.Message;
    }

    private sealed class PlaybackDropMessage
    {
        public string Type { get; init; } = "";

        public string Url { get; init; } = "";

        public string? StreamName { get; init; }

        public string? Direction { get; init; }

        public double DeltaY { get; init; }

        public bool CtrlKey { get; init; }

        public bool Pressed { get; init; }

        public int KeyCode { get; init; }
    }

}
