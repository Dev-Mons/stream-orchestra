using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private static readonly Brush DefaultBorderBrush = new SolidColorBrush(Color.FromRgb(45, 54, 66));
    private static readonly Brush SelectedBorderBrush = new SolidColorBrush(Color.FromRgb(77, 163, 255));

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
        SlotBorder.BorderBrush = isSelected ? SelectedBorderBrush : DefaultBorderBrush;
        SlotBorder.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
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
        SlotSelected?.Invoke(this);
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
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void SlotBorder_Drop(object sender, DragEventArgs e)
    {
        if (StreamDropDataReader.TryGetDroppedStream(e.Data, _navigationService, out var url, out var streamName))
        {
            SlotSelected?.Invoke(this);
            StreamUrlDropRequested?.Invoke(this, url, streamName);
            e.Handled = true;
        }
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
    if (!host.includes("sooplive.co.kr")) {
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

    const hideElements = () => {
      for (const selector of hideSelectors) {
        for (const element of document.querySelectorAll(selector)) {
          element.classList.add("stream-orchestra-hidden");
        }
      }
    };

    hideElements();
    const observer = new MutationObserver(hideElements);
    const target = document.documentElement || document.body;
    if (target) {
      observer.observe(target, { childList: true, subtree: true });
    }
    window.addEventListener("DOMContentLoaded", hideElements, { once: true });
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

        public double DeltaY { get; init; }
    }

}
