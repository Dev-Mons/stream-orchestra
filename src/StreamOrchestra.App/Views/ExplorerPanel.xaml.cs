using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class ExplorerPanel : UserControl
{
    private readonly WebViewProfileService _profileService;
    private readonly StreamNavigationService _navigationService;
    private bool _isInitialized;
    private Point? _dragStartPoint;
    private string? _linkDragScriptId;

    public ExplorerPanel(WebViewProfileService profileService, StreamNavigationService navigationService)
    {
        _profileService = profileService;
        _navigationService = navigationService;

        InitializeComponent();

        CurrentUrl = "https://www.sooplive.co.kr";
        ExplorerUrlTextBox.Text = CurrentUrl;
        ProfilePathTextBlock.Text = _profileService.ExplorerGroup.UserDataFolder;

        Loaded += ExplorerPanel_Loaded;
    }

    public string CurrentUrl { get; private set; }

    public string CurrentTitle { get; private set; } = "";

    private async void ExplorerPanel_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await NavigateAsync(CurrentUrl);
        }
        catch (Exception ex)
        {
            ShowInitializationError(ex);
        }
    }

    private async Task NavigateAsync(string url)
    {
        CurrentUrl = _navigationService.NormalizeUrl(url);
        CurrentTitle = "";
        ExplorerUrlTextBox.Text = CurrentUrl;

        await EnsureInitializedAsync();
        Browser.CoreWebView2.Navigate(CurrentUrl);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        InitializationOverlay.Visibility = Visibility.Visible;
        InitializationTextBlock.Text = "Initializing SOOP explorer...";

        var environment = await _profileService.GetEnvironmentAsync(_profileService.ExplorerGroup);
        await Browser.EnsureCoreWebView2Async(environment);
        await InstallLinkDragScriptAsync();

        Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        Browser.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
        Browser.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
        _isInitialized = true;
    }

    private async Task InstallLinkDragScriptAsync()
    {
        if (_linkDragScriptId is not null || Browser.CoreWebView2 is null)
        {
            return;
        }

        _linkDragScriptId = await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            CreateLinkDragScript());
    }

    private static string CreateLinkDragScript()
    {
        return """
(() => {
  if (window.__streamOrchestraLinkDragInstalled) {
    return;
  }

  window.__streamOrchestraLinkDragInstalled = true;

  document.addEventListener("dragstart", event => {
    const anchor = event.target?.closest?.("a[href]");
    if (!anchor || !event.dataTransfer) {
      return;
    }

    let url = "";
    try {
      url = new URL(anchor.getAttribute("href"), document.baseURI).href;
    } catch {
      return;
    }

    if (!/^https?:\/\//i.test(url)) {
      return;
    }

    const title = anchor.textContent?.trim() || anchor.getAttribute("title") || url;
    event.dataTransfer.effectAllowed = "copy";
    event.dataTransfer.setData("text/plain", url);
    event.dataTransfer.setData("text/uri-list", url);
    event.dataTransfer.setData("text/html", `<a href="${escapeAttribute(url)}">${escapeHtml(title)}</a>`);
  }, true);

  function escapeHtml(value) {
    return String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  function escapeAttribute(value) {
    return escapeHtml(value).replaceAll("'", "&#39;");
  }
})();
""";
    }

    private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        UpdateCurrentLocation(Browser.Source?.ToString());
    }

    private void CoreWebView2_DocumentTitleChanged(object? sender, object e)
    {
        CurrentTitle = Browser.CoreWebView2.DocumentTitle.Trim();
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            InitializationOverlay.Visibility = Visibility.Visible;
            InitializationTextBlock.Text = $"Navigation failed: {e.WebErrorStatus}";
            return;
        }

        UpdateCurrentLocation(Browser.Source?.ToString());
        InitializationOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateCurrentLocation(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        CurrentUrl = _navigationService.NormalizeUrl(url);
        ExplorerUrlTextBox.Text = CurrentUrl;
    }

    private async void GoButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateFromTextBoxAsync();
    }

    private async void ExplorerUrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await NavigateFromTextBoxAsync();
    }

    private async Task NavigateFromTextBoxAsync()
    {
        try
        {
            await NavigateAsync(ExplorerUrlTextBox.Text);
        }
        catch (Exception ex)
        {
            ShowInitializationError(ex);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoBack == true)
        {
            Browser.CoreWebView2.GoBack();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        Browser.CoreWebView2?.Reload();
    }

    private void ExplorerDragSource_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
    }

    private void ExplorerDragSource_MouseMove(object sender, MouseEventArgs e)
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

        var normalizedUrl = _navigationService.NormalizeUrl(CurrentUrl);
        if (normalizedUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var data = new DataObject();
        data.SetData(StreamDragDataFormats.StreamUrl, normalizedUrl);
        data.SetData(DataFormats.UnicodeText, normalizedUrl);
        data.SetData(DataFormats.Text, normalizedUrl);

        if (!string.IsNullOrWhiteSpace(CurrentTitle))
        {
            data.SetData(StreamDragDataFormats.StreamName, CurrentTitle.Trim());
        }

        DragDrop.DoDragDrop(ExplorerDragSource, data, DragDropEffects.Copy);
        _dragStartPoint = null;
    }

    private void ShowInitializationError(Exception ex)
    {
        InitializationOverlay.Visibility = Visibility.Visible;
        InitializationTextBlock.Text = ex.Message;
    }

}
