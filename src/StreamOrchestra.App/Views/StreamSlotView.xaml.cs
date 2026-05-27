using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class StreamSlotView : UserControl
{
    private static readonly Brush DefaultBorderBrush = new SolidColorBrush(Color.FromRgb(45, 54, 66));
    private static readonly Brush SelectedBorderBrush = new SolidColorBrush(Color.FromRgb(77, 163, 255));

    private readonly WebViewProfileService _profileService;
    private readonly StreamNavigationService _navigationService;
    private bool _isInitialized;
    private bool _isMuted;
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

        ProfilePathTextBlock.Text = Configuration.ProfileGroup.UserDataFolder;

        Loaded += StreamSlotView_Loaded;
    }

    public event Action<StreamSlotView>? SlotSelected;

    public event Action<StreamSlotView, string, string?>? StreamUrlDropRequested;

    public event Action<StreamSlotView>? MuteChanged;

    public SlotConfiguration Configuration { get; }

    public int SlotId => Configuration.SlotId;

    public string ProfileGroupId => Configuration.ProfileGroup.Id;

    public string CurrentUrl { get; private set; } = "about:blank";

    public string CurrentStreamName { get; private set; } = "Empty";

    public bool IsMuted => _isMuted;

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
        return new SlotRuntimeState(SlotId, CurrentStreamName, CurrentUrl, IsMuted, ProfileGroupId);
    }

    public void SetSelected(bool isSelected)
    {
        SlotBorder.BorderBrush = isSelected ? SelectedBorderBrush : DefaultBorderBrush;
        SlotBorder.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
    }

    public void SetMuted(bool isMuted, bool suppressQualityUpdate = false)
    {
        var changed = _isMuted != isMuted;
        _isMuted = isMuted;

        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.IsMuted = _isMuted;
        }

        if (changed && !suppressQualityUpdate)
        {
            MuteChanged?.Invoke(this);
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
        Browser.CoreWebView2.IsMuted = _isMuted;
        _isInitialized = true;

        InitializationOverlay.Visibility = Visibility.Collapsed;
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
            ApplyWheelMute(message.DeltaY);
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

        ApplyWheelMute(-e.Delta);
        e.Handled = true;
    }

    private void ApplyWheelMute(double deltaY)
    {
        if (deltaY == 0)
        {
            return;
        }

        SlotSelected?.Invoke(this);
        SetMuted(deltaY > 0, suppressQualityUpdate: true);
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
            "original" => "maximum",
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

    if (qualityKey === "original") {
      const priority = ["1440p", "1080p", "720p", "540p", "360p", "최대화질", "원본"];
      return priority.map(text => findButtonByText(availableButtons, text)).find(Boolean) || null;
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
