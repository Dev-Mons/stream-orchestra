using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class StreamSlotView : UserControl
{
    private const string SlotDragDataFormat = "StreamOrchestraSlotId";
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
        _isMuted = isMuted;

        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.IsMuted = _isMuted;
        }

        UpdateMuteButton();
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

        var data = new DataObject(SlotDragDataFormat, SlotId);
        DragDrop.DoDragDrop(DragHandleTextBlock, data, DragDropEffects.Move);
        _dragStartPoint = null;
    }

    private void SlotBorder_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(SlotDragDataFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void SlotBorder_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(SlotDragDataFormat))
        {
            return;
        }

        var sourceSlotId = (int)e.Data.GetData(SlotDragDataFormat);
        SlotSwapRequested?.Invoke(this, sourceSlotId);
        e.Handled = true;
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
