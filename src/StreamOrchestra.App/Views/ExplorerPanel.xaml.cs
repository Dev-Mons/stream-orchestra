using System.Text.Json;
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
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _processRecoveryGate = new(1, 1);
    private bool _isInitialized;
    private Point? _dragStartPoint;
    private string? _linkDragScriptId;
    private string? _soopSidebarSortScriptId;

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

    public event Action? HostDragStarted;

    public event Action? HostDragCompleted;

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
        await _initializationGate.WaitAsync();
        try
        {
            if (_isInitialized)
            {
                return;
            }

            InitializationOverlay.Visibility = Visibility.Visible;
            InitializationTextBlock.Text = "Initializing SOOP explorer...";

            var environment = await _profileService.GetEnvironmentAsync(_profileService.ExplorerGroup);
            await Browser.EnsureCoreWebView2Async(environment);
            var coreWebView = Browser.CoreWebView2;
            await InstallLinkDragScriptAsync(coreWebView);
            await InstallSoopSidebarSortScriptAsync(coreWebView);

            AttachCoreWebViewEvents(coreWebView);
            _isInitialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task InstallLinkDragScriptAsync(CoreWebView2 coreWebView)
    {
        if (_linkDragScriptId is not null)
        {
            return;
        }

        _linkDragScriptId = await coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(
            CreateLinkDragScript());
    }

    private async Task InstallSoopSidebarSortScriptAsync(CoreWebView2 coreWebView)
    {
        if (_soopSidebarSortScriptId is not null)
        {
            return;
        }

        _soopSidebarSortScriptId = await coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(
            SoopSidebarSortScriptService.CreateScript());
    }

    private void AttachCoreWebViewEvents(CoreWebView2 coreWebView)
    {
        coreWebView.NavigationCompleted += CoreWebView2_NavigationCompleted;
        coreWebView.SourceChanged += CoreWebView2_SourceChanged;
        coreWebView.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
        coreWebView.WebMessageReceived += CoreWebView2_WebMessageReceived;
        coreWebView.NewWindowRequested += CoreWebView2_NewWindowRequested;
        coreWebView.ProcessFailed += CoreWebView2_ProcessFailed;
    }

    private void DetachCoreWebViewEvents(CoreWebView2 coreWebView)
    {
        coreWebView.NavigationCompleted -= CoreWebView2_NavigationCompleted;
        coreWebView.SourceChanged -= CoreWebView2_SourceChanged;
        coreWebView.DocumentTitleChanged -= CoreWebView2_DocumentTitleChanged;
        coreWebView.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        coreWebView.NewWindowRequested -= CoreWebView2_NewWindowRequested;
        coreWebView.ProcessFailed -= CoreWebView2_ProcessFailed;
    }

    private void CoreWebView2_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        var action = WebViewRecoveryPolicy.GetAction(e.ProcessFailedKind);
        if (action == WebViewRecoveryAction.None)
        {
            return;
        }

        var details =
            $"Explorer WebView2 process failure: Kind={e.ProcessFailedKind}, " +
            $"Reason={e.Reason}, ExitCode={e.ExitCode}, Url={CurrentUrl}";
        InitializationOverlay.Visibility = Visibility.Visible;
        InitializationTextBlock.Text = action == WebViewRecoveryAction.Recreate
            ? "Explorer browser process stopped. Recreating..."
            : "Explorer renderer stopped. Reloading...";

        // Let WebView2 finish raising ProcessFailed before replacing or reloading the control.
        Dispatcher.BeginInvoke(new Action(() => _ = RecoverFromProcessFailureAsync(action, details)));
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
            else
            {
                try
                {
                    Browser.CoreWebView2.Reload();
                }
                catch (InvalidOperationException)
                {
                    await RecreateBrowserAsync();
                }
            }
        }
        catch (Exception ex)
        {
            ShowInitializationError(ex);
            System.Diagnostics.Trace.WriteLine($"[{DateTimeOffset.Now:O}] {details}; RecoveryError={ex}");
        }
        finally
        {
            _processRecoveryGate.Release();
        }
    }

    private async Task RecreateBrowserAsync()
    {
        var restoreUrl = CurrentUrl;
        var oldBrowser = Browser;
        var oldIndex = BrowserHost.Children.IndexOf(oldBrowser);

        try
        {
            if (oldBrowser.CoreWebView2 is { } oldCoreWebView)
            {
                DetachCoreWebViewEvents(oldCoreWebView);
            }
        }
        catch (InvalidOperationException)
        {
            // A crashed browser process can leave CoreWebView2 inaccessible.
        }

        BrowserHost.Children.Remove(oldBrowser);
        oldBrowser.Dispose();

        Browser = new Microsoft.Web.WebView2.Wpf.WebView2();
        BrowserHost.Children.Insert(Math.Max(0, oldIndex), Browser);
        _isInitialized = false;
        _linkDragScriptId = null;
        _soopSidebarSortScriptId = null;

        await EnsureInitializedAsync();
        Browser.CoreWebView2.Navigate(restoreUrl);
    }

    private async Task RecoverFromInteractionFailureAsync(Exception ex)
    {
        InitializationOverlay.Visibility = Visibility.Visible;
        InitializationTextBlock.Text = "Explorer browser is unavailable. Recreating...";
        await RecoverFromProcessFailureAsync(
            WebViewRecoveryAction.Recreate,
            $"Explorer browser interaction failed at {CurrentUrl}: {ex}");
    }

    private static string CreateLinkDragScript()
    {
        return """
(() => {
  if (window.__streamOrchestraLinkDragInstalled) {
    return;
  }

  window.__streamOrchestraLinkDragInstalled = true;

  // Warm the host IPC channel so the first user drag does not race with cold-start latency.
  window.chrome?.webview?.postMessage({ type: "drag-warmup" });

  function resolveAnchorUrl(anchor) {
    try {
      const url = new URL(anchor.getAttribute("href"), document.baseURI).href;
      return /^https?:\/\//i.test(url) ? url : "";
    } catch {
      return "";
    }
  }

  // Use the browser's own drag-threshold detection (dragstart) as the trigger.
  // preventDefault cancels the WebView2-initiated OLE drag (which has stuck-state bugs),
  // and we ask the WPF host to start its own DragDrop instead. dragstart fires exactly
  // once at threshold crossing, eliminating mousemove tracking races.
  document.addEventListener("dragstart", event => {
    const anchor = event.target?.closest?.("a[href]");
    if (!anchor) {
      return;
    }

    const url = resolveAnchorUrl(anchor);
    if (!url) {
      return;
    }

    event.preventDefault();

    const title = anchor.textContent?.trim() || anchor.getAttribute("title") || url;
    window.chrome?.webview?.postMessage({
      type: "begin-host-drag",
      url,
      streamName: title
    });
  }, true);
})();
""";
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        LinkDragMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<LinkDragMessage>(e.WebMessageAsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return;
        }

        if (message is null ||
            !message.Type.Equals("begin-host-drag", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(message.Url))
        {
            return;
        }

        BeginHostDrag(message.Url, message.StreamName);
    }

    private void BeginHostDrag(string url, string? streamName)
    {
        var normalizedUrl = _navigationService.NormalizeUrl(url);
        if (normalizedUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var data = new DataObject();
        data.SetData(StreamDragDataFormats.StreamUrl, normalizedUrl);
        data.SetData(DataFormats.UnicodeText, normalizedUrl);
        data.SetData(DataFormats.Text, normalizedUrl);
        if (!string.IsNullOrWhiteSpace(streamName))
        {
            data.SetData(StreamDragDataFormats.StreamName, streamName.Trim());
        }

        try
        {
            HostDragStarted?.Invoke();
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        catch
        {
            // Drag may fail if the mouse button was released before the host took over.
        }
        finally
        {
            HostDragCompleted?.Invoke();
        }
    }

    private async void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (WebViewPopupPolicy.ShouldPreservePopupContext(e.Uri))
        {
            await WebViewPopupWindow.OpenRequestedAsync(
                e,
                _profileService.GetEnvironmentAsync(_profileService.ExplorerGroup),
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

    private sealed class LinkDragMessage
    {
        public string Type { get; init; } = "";

        public string Url { get; init; } = "";

        public string? StreamName { get; init; }
    }

    private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (sender is CoreWebView2 coreWebView)
        {
            UpdateCurrentLocation(coreWebView.Source);
        }
    }

    private void CoreWebView2_DocumentTitleChanged(object? sender, object e)
    {
        if (sender is CoreWebView2 coreWebView)
        {
            CurrentTitle = coreWebView.DocumentTitle.Trim();
        }
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
        catch (InvalidOperationException ex)
        {
            await RecoverFromInteractionFailureAsync(ex);
        }
        catch (Exception ex)
        {
            ShowInitializationError(ex);
        }
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Browser.CoreWebView2?.CanGoBack == true)
            {
                Browser.CoreWebView2.GoBack();
            }
        }
        catch (InvalidOperationException ex)
        {
            await RecoverFromInteractionFailureAsync(ex);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Browser.CoreWebView2 is { } coreWebView)
            {
                coreWebView.Reload();
            }
            else
            {
                await NavigateAsync(CurrentUrl);
            }
        }
        catch (InvalidOperationException ex)
        {
            await RecoverFromInteractionFailureAsync(ex);
        }
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

        try
        {
            HostDragStarted?.Invoke();
            DragDrop.DoDragDrop(ExplorerDragSource, data, DragDropEffects.Copy);
        }
        finally
        {
            _dragStartPoint = null;
            HostDragCompleted?.Invoke();
        }
    }

    private void ShowInitializationError(Exception ex)
    {
        InitializationOverlay.Visibility = Visibility.Visible;
        InitializationTextBlock.Text = ex.Message;
    }

}
