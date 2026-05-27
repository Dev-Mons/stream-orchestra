using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private bool _areControlBarsAlwaysVisible = true;
    private bool _isPointerOverChrome;
    private bool _hasExplicitStreamName;
    private Point? _dragStartPoint;
    private string _preferredQualityKey = "master";
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

        SlotTitleTextBlock.Text = $"Slot {Configuration.SlotId}";
        GroupTextBlock.Text = $"Group {Configuration.ProfileGroup.Id}";
        ProfilePathTextBlock.Text = Configuration.ProfileGroup.UserDataFolder;
        SlotUrlTextBox.Text = "https://www.sooplive.co.kr";
        UpdateMuteButton();
        UpdateControlBarVisibility();

        Loaded += StreamSlotView_Loaded;
    }

    public event Action<StreamSlotView>? SlotSelected;

    public event Action<StreamSlotView, int>? SlotSwapRequested;

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

    public void SetMuted(bool isMuted)
    {
        var changed = _isMuted != isMuted;
        _isMuted = isMuted;

        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.IsMuted = _isMuted;
        }

        UpdateMuteButton();

        if (changed)
        {
            MuteChanged?.Invoke(this);
        }
    }

    public void SetUrlEditorVisible(bool isVisible)
    {
        SlotUrlEditor.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetControlBarAlwaysVisible(bool isAlwaysVisible)
    {
        _areControlBarsAlwaysVisible = isAlwaysVisible;
        UpdateControlBarVisibility();
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

        Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        Browser.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
        Browser.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
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

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateFromSlotTextAsync();
    }

    private async void SlotUrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await NavigateFromSlotTextAsync();
    }

    private async Task NavigateFromSlotTextAsync()
    {
        try
        {
            await NavigateAsync(SlotUrlTextBox.Text);
        }
        catch (Exception ex)
        {
            ShowInitializationError(ex);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        Browser.CoreWebView2?.Reload();
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (MenuButton.ContextMenu is null)
        {
            return;
        }

        MenuButton.ContextMenu.PlacementTarget = MenuButton;
        MenuButton.ContextMenu.IsOpen = true;
    }

    private void CopyUrlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CurrentUrl))
        {
            Clipboard.SetText(CurrentUrl);
        }
    }

    private async void ClearSlotMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await NavigateAsync("about:blank");
    }

    private async void LoadSoopHomeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await NavigateAsync("https://www.sooplive.co.kr");
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        SetMuted(!_isMuted);
    }

    private void ControlBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SlotSelected?.Invoke(this);
    }

    private void SlotChrome_MouseEnter(object sender, MouseEventArgs e)
    {
        _isPointerOverChrome = true;
        UpdateControlBarVisibility();
    }

    private void SlotChrome_MouseLeave(object sender, MouseEventArgs e)
    {
        _isPointerOverChrome = false;
        UpdateControlBarVisibility();
    }

    private void DragHandleTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        SlotSelected?.Invoke(this);
    }

    private void DragHandleTextBlock_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        var movedEnough =
            Math.Abs(currentPoint.X - _dragStartPoint.Value.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(currentPoint.Y - _dragStartPoint.Value.Y) >= SystemParameters.MinimumVerticalDragDistance;

        if (!movedEnough)
        {
            return;
        }

        var data = new DataObject(StreamDragDataFormats.SlotId, SlotId);
        DragDrop.DoDragDrop(DragHandleTextBlock, data, DragDropEffects.Move);
        _dragStartPoint = null;
    }

    private void SlotBorder_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(StreamDragDataFormats.SlotId))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (TryGetDroppedStream(e.Data, out _, out _))
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
        if (e.Data.GetDataPresent(StreamDragDataFormats.SlotId))
        {
            var sourceSlotId = (int)e.Data.GetData(StreamDragDataFormats.SlotId);
            SlotSwapRequested?.Invoke(this, sourceSlotId);
            e.Handled = true;
            return;
        }

        if (TryGetDroppedStream(e.Data, out var url, out var streamName))
        {
            SlotSelected?.Invoke(this);
            StreamUrlDropRequested?.Invoke(this, url, streamName);
            e.Handled = true;
        }
    }

    private bool TryGetDroppedStream(IDataObject data, out string url, out string? streamName)
    {
        url = "";
        streamName = null;

        if (TryGetStringData(data, StreamDragDataFormats.StreamName, out var droppedStreamName))
        {
            streamName = droppedStreamName.Trim();
        }

        if (TryGetStringData(data, StreamDragDataFormats.StreamUrl, out var customUrl) &&
            TryNormalizeDroppedUrl(customUrl, out url))
        {
            return true;
        }

        if (TryGetStringData(data, DataFormats.Html, out var html) &&
            TryExtractUrlFromHtml(html, out var htmlUrl) &&
            TryNormalizeDroppedUrl(htmlUrl, out url))
        {
            return true;
        }

        foreach (var format in new[] { DataFormats.UnicodeText, DataFormats.Text, "UniformResourceLocatorW", "UniformResourceLocator" })
        {
            if (TryGetStringData(data, format, out var text) &&
                TryNormalizeDroppedUrl(text, out url))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryNormalizeDroppedUrl(string candidate, out string url)
    {
        url = _navigationService.NormalizeUrl(candidate);
        return !url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractUrlFromHtml(string html, out string url)
    {
        url = "";
        var match = Regex.Match(
            html,
            "href\\s*=\\s*(?:\"(?<url>[^\"]+)\"|'(?<url>[^']+)'|(?<url>[^\\s>]+))",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        url = WebUtility.HtmlDecode(match.Groups["url"].Value);
        return !string.IsNullOrWhiteSpace(url);
    }

    private static bool TryGetStringData(IDataObject data, string format, out string value)
    {
        value = "";
        if (!data.GetDataPresent(format))
        {
            return false;
        }

        value = data.GetData(format) switch
        {
            string text => text,
            MemoryStream stream => ReadStreamText(stream),
            byte[] bytes => System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0'),
            _ => ""
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ReadStreamText(MemoryStream stream)
    {
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, System.Text.Encoding.Unicode, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return reader.ReadToEnd().TrimEnd('\0');
        }
        finally
        {
            stream.Position = position;
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

    private void UpdateMuteButton()
    {
        MuteButton.Content = _isMuted ? "🔇" : "🔊";
        MuteButton.ToolTip = _isMuted ? "Unmute slot" : "Mute slot";
    }

    private void UpdateCurrentLocation(string url, string streamName)
    {
        CurrentUrl = url;
        CurrentStreamName = string.IsNullOrWhiteSpace(streamName)
            ? _navigationService.CreateDisplayName(url)
            : streamName.Trim();
        SlotUrlTextBox.Text = url;
        SlotTitleTextBlock.Text = $"Slot {Configuration.SlotId} / {CurrentStreamName}";
        SlotTitleTextBlock.ToolTip = url;
    }

    private void UpdateControlBarVisibility()
    {
        var shouldShowControlBar = _areControlBarsAlwaysVisible || _isPointerOverChrome;

        ControlBar.Visibility = shouldShowControlBar ? Visibility.Visible : Visibility.Collapsed;
        ControlBarHoverTarget.Visibility = _areControlBarsAlwaysVisible || shouldShowControlBar
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ShowInitializationError(Exception ex)
    {
        InitializationOverlay.Visibility = Visibility.Visible;
        InitializationTextBlock.Text = ex.Message;
    }

}
