using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class StreamSlotView : UserControl
{
    private const int MinVolumePercent = 0;
    private const int MaxVolumePercent = 100;
    private const int InitialVolumePercent = 100;
    private const int VolumeStepPercent = 10;

    private readonly WebViewProfileService _profileService;
    private readonly StreamNavigationService _navigationService;
    private readonly DispatcherTimer _volumeOverlayTimer;
    private bool _isInitialized;
    private bool _isMuted;
    private int _volumePercent = InitialVolumePercent;
    private bool _hasExplicitStreamName;
    private string _preferredQualityKey = "master";
    private string? _playbackViewportScriptId;
    private string? _qualityObserverScriptId;
    private Point? _slotDragStartPoint;

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
            _volumeOverlayTimer.Stop();
        };

        ProfilePathTextBlock.Text = Configuration.ProfileGroup.UserDataFolder;

        Loaded += StreamSlotView_Loaded;
    }

    public event Action<StreamSlotView>? SlotSelected;

    public event Action<StreamSlotView, string, string?>? StreamUrlDropRequested;

    public event Action? HostDragStarted;

    public event Action? HostDragCompleted;

    public event Action<StreamSlotView, DockDirection>? DockPreviewRequested;

    public event Action<StreamSlotView>? DockPreviewEnded;

    public event Action<StreamSlotView, string, string?, DockDirection>? StreamDockDropRequested;

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
        _hasExplicitStreamName = !string.IsNullOrWhiteSpace(streamName);
        UpdateCurrentLocation(
            normalizedUrl,
            _hasExplicitStreamName ? streamName!.Trim() : _navigationService.CreateDisplayName(normalizedUrl));

        await EnsureInitializedAsync();
        Browser.CoreWebView2.Navigate(normalizedUrl);
    }

    public async Task ClearAsync()
    {
        _hasExplicitStreamName = false;

        if (_isInitialized)
        {
            await NavigateAsync("about:blank");
            return;
        }

        UpdateCurrentLocation("about:blank", "Empty");
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
        if (_isInitialized)
        {
            return;
        }

        InitializationOverlay.Visibility = Visibility.Visible;
        InitializationTextBlock.Text = $"Initializing Group {Configuration.ProfileGroup.Id}...";

        var environment = await _profileService.GetEnvironmentAsync(Configuration.ProfileGroup);
        await Browser.EnsureCoreWebView2Async(environment);
        await InstallPlaybackViewportScriptAsync();

        Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        Browser.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
        Browser.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
        Browser.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        Browser.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        _isMuted = false;
        Browser.CoreWebView2.IsMuted = false;
        _isInitialized = true;

        InitializationOverlay.Visibility = Visibility.Collapsed;
        _ = ApplyVolumeToWebPageAsync();
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
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
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
            ApplyWheelVolume(message.DeltaY);
            return;
        }

        if (message.Type.Equals("stream-dock-preview", StringComparison.OrdinalIgnoreCase))
        {
            DockPreviewRequested?.Invoke(this, ParseDockDirection(message.Direction));
            return;
        }

        if (message.Type.Equals("stream-dock-leave", StringComparison.OrdinalIgnoreCase))
        {
            DockPreviewEnded?.Invoke(this);
            return;
        }

        if (message.Type.Equals("stream-dock-drop", StringComparison.OrdinalIgnoreCase) &&
            StreamDropDataReader.TryNormalizeDroppedText(message.Url, _navigationService, out var dockUrl))
        {
            SlotSelected?.Invoke(this);
            StreamDockDropRequested?.Invoke(this, dockUrl, message.StreamName, ParseDockDirection(message.Direction));
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

        ApplyWheelVolume(-e.Delta);
        e.Handled = true;
    }

    private void ApplyWheelVolume(double deltaY)
    {
        if (deltaY == 0)
        {
            return;
        }

        SlotSelected?.Invoke(this);
        SetVolumePercent(CalculateWheelVolumePercent(_volumePercent, deltaY));
    }

    private static int CalculateWheelVolumePercent(int currentVolumePercent, double deltaY)
    {
        var direction = Math.Sign(deltaY);
        if (direction == 0)
        {
            return Math.Clamp(currentVolumePercent, MinVolumePercent, MaxVolumePercent);
        }

        var nextVolumePercent = currentVolumePercent + (direction < 0 ? VolumeStepPercent : -VolumeStepPercent);
        return Math.Clamp(nextVolumePercent, MinVolumePercent, MaxVolumePercent);
    }

    private void SetVolumePercent(int volumePercent)
    {
        _volumePercent = Math.Clamp(volumePercent, MinVolumePercent, MaxVolumePercent);
        ShowVolumeIndicator(_volumePercent);
        _ = ApplyVolumeToWebPageAsync();
    }

    private void ShowVolumeIndicator(int volumePercent)
    {
        VolumeIndicatorTextBlock.Text = $"볼륨 {volumePercent}%";
        VolumeIndicatorPopup.IsOpen = true;
        _volumeOverlayTimer.Stop();
        _volumeOverlayTimer.Start();
    }

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

    private void SlotBorder_DragOver(object sender, DragEventArgs e)
    {
        if (StreamDropDataReader.TryGetDroppedStream(e.Data, _navigationService, out _, out _))
        {
            e.Effects = DragDropEffects.Copy;
            DockPreviewRequested?.Invoke(this, CalculateDockDirection(sender, e));
        }
        else
        {
            e.Effects = DragDropEffects.None;
            DockPreviewEnded?.Invoke(this);
        }

        e.Handled = true;
    }

    private void SlotBorder_Drop(object sender, DragEventArgs e)
    {
        if (StreamDropDataReader.TryGetDroppedStream(e.Data, _navigationService, out var url, out var streamName))
        {
            var direction = CalculateDockDirection(sender, e);
            SlotSelected?.Invoke(this);
            if (direction is DockDirection.Left or DockDirection.Right or DockDirection.Top or DockDirection.Bottom)
            {
                StreamDockDropRequested?.Invoke(this, url, streamName, direction);
            }
            else
            {
                StreamUrlDropRequested?.Invoke(this, url, streamName);
            }

            e.Handled = true;
        }
    }

    private static DockDirection CalculateDockDirection(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return DockDirection.Center;
        }

        var position = e.GetPosition(element);
        return CalculateDockDirection(position.X, position.Y, element.ActualWidth, element.ActualHeight);
    }

    private static DockDirection CalculateDockDirection(double x, double y, double width, double height)
    {
        var edgeWidth = width * 0.25;
        var edgeHeight = height * 0.25;

        if (x <= edgeWidth)
        {
            return DockDirection.Left;
        }

        if (x >= width - edgeWidth)
        {
            return DockDirection.Right;
        }

        if (y <= edgeHeight)
        {
            return DockDirection.Top;
        }

        if (y >= height - edgeHeight)
        {
            return DockDirection.Bottom;
        }

        return DockDirection.Center;
    }

    private static DockDirection ParseDockDirection(string? direction)
    {
        return direction?.Trim().ToLowerInvariant() switch
        {
            "left" => DockDirection.Left,
            "right" => DockDirection.Right,
            "top" => DockDirection.Top,
            "bottom" => DockDirection.Bottom,
            "center" => DockDirection.Center,
            _ => DockDirection.Center
        };
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

    .popout_chat #chatting_area {
      min-width: auto !important;
    }

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
      background: #000 !important;
    }

    body.screen_mode #webplayer #webplayer_contents #player_area,
    body.fullScreen_mode #webplayer #webplayer_contents #player_area {
      flex: 1 1 auto !important;
      min-width: 0 !important;
      width: auto !important;
      height: 100vh !important;
      background: #000 !important;
    }

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

  document.addEventListener("dragover", event => {
    if (!hasStreamUrlData(event.dataTransfer)) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    event.dataTransfer.dropEffect = "copy";
    window.chrome?.webview?.postMessage({
      type: "stream-dock-preview",
      direction: calculateDockDirection(event.clientX, event.clientY)
    });
  }, true);

  document.addEventListener("dragleave", event => {
    if (event.relatedTarget) {
      return;
    }

    window.chrome?.webview?.postMessage({ type: "stream-dock-leave" });
  }, true);

  document.addEventListener("drop", event => {
    const payload = readStreamDropPayload(event.dataTransfer);
    if (!payload?.url) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    window.chrome?.webview?.postMessage({
      type: "stream-dock-drop",
      url: payload.url,
      streamName: payload.streamName || "",
      direction: calculateDockDirection(event.clientX, event.clientY)
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
      deltaY: event.deltaY
    });
  }, { capture: true, passive: false });

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

  function calculateDockDirection(clientX, clientY) {
    const width = Math.max(1, window.innerWidth || document.documentElement.clientWidth || 1);
    const height = Math.max(1, window.innerHeight || document.documentElement.clientHeight || 1);
    const edgeWidth = width * 0.25;
    const edgeHeight = height * 0.25;

    if (clientX <= edgeWidth) {
      return "left";
    }

    if (clientX >= width - edgeWidth) {
      return "right";
    }

    if (clientY <= edgeHeight) {
      return "top";
    }

    if (clientY >= height - edgeHeight) {
      return "bottom";
    }

    return "center";
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

    const hideSelectors = [
      "#header",
      ".header",
      ".top_area",
      ".topbar",
      ".global_header",
      ".live_header",
      ".player_header",
      ".title_wrap",
      ".title_area"
    ];

    const fullscreenButtonSelectors = [
      ".btn_fullScreen_mode"
    ];

    const screenModeButtonSelectors = [
      ".btn_screen_mode"
    ];

    let soopFullscreenRetryCount = 0;
    let soopFullscreenRetryTimer = 0;
    let lastScreenModeClickAt = 0;
    let lastFullscreenClickAt = 0;

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

    const findFirstButton = selectors => {
      for (const selector of selectors) {
        const button = document.querySelector(selector);
        if (isUsableElement(button)) {
          return button;
        }
      }

      return null;
    };

    const isSoopPlaybackModeActive = () =>
      Boolean(document.body?.classList.contains("screen_mode") ||
        document.body?.classList.contains("fullScreen_mode") ||
        document.fullscreenElement);

    const clickScreenModeButton = () => {
      if (document.body?.classList.contains("screen_mode")) {
        return true;
      }

      const button = findFirstButton(screenModeButtonSelectors);
      if (!button) {
        return false;
      }

      const now = Date.now();
      if (now - lastScreenModeClickAt < 1000) {
        return false;
      }

      lastScreenModeClickAt = now;
      button.click();
      return true;
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
      button.click();
      return true;
    };

    const requestSoopFullscreenViewport = () => {
      if (!document.querySelector("video")) {
        return;
      }

      clickScreenModeButton();
      clickFullscreenButton();
    };

    const scheduleSoopFullscreenRetry = () => {
      if (isSoopPlaybackModeActive() || soopFullscreenRetryCount >= 120 || soopFullscreenRetryTimer !== 0) {
        return;
      }

      soopFullscreenRetryTimer = window.setTimeout(() => {
        soopFullscreenRetryTimer = 0;
        soopFullscreenRetryCount += 1;
        requestSoopFullscreenViewport();
        scheduleSoopFullscreenRetry();
      }, 250);
    };

    const wireMediaPlayback = () => {
      for (const video of document.querySelectorAll("video")) {
        if (video.__streamOrchestraSoopPlaybackWired) {
          continue;
        }

        video.__streamOrchestraSoopPlaybackWired = true;
        video.addEventListener("play", () => {
          requestSoopFullscreenViewport();
          scheduleSoopFullscreenRetry();
        }, { passive: true });
        if (!video.paused) {
          requestSoopFullscreenViewport();
          scheduleSoopFullscreenRetry();
        }
      }
    };

    hideElements();
    wireMediaPlayback();
    const observer = new MutationObserver(() => {
      hideElements();
      wireMediaPlayback();
      if (document.querySelector("video")) {
        scheduleSoopFullscreenRetry();
      }
    });
    const target = document.documentElement || document.body;
    if (target) {
      observer.observe(target, { childList: true, subtree: true });
    }
    window.addEventListener("DOMContentLoaded", () => {
      hideElements();
      wireMediaPlayback();
      requestSoopFullscreenViewport();
      scheduleSoopFullscreenRetry();
    }, { once: true });
    document.addEventListener("fullscreenchange", requestSoopFullscreenViewport);
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
        CurrentUrl = url;
        CurrentStreamName = string.IsNullOrWhiteSpace(streamName)
            ? _navigationService.CreateDisplayName(url)
            : streamName.Trim();
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
    }

}
