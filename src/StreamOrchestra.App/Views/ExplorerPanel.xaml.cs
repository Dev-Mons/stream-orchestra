using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class ExplorerPanel : UserControl
{
    private readonly WebViewProfileService _profileService;
    private readonly StreamNavigationService _navigationService;
    private bool _isInitialized;

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

    public event Action<string>? UseCurrentUrlRequested;

    public event Action<string, string>? AddFavoriteRequested;

    public event Action<StreamEntry>? UseFavoriteRequested;

    public string CurrentUrl { get; private set; }

    public string CurrentTitle { get; private set; } = "";

    public void SetFavorites(IReadOnlyList<StreamEntry> favorites)
    {
        FavoriteComboBox.ItemsSource = null;
        FavoriteComboBox.ItemsSource = FavoriteStorageService.OrderForDisplay(favorites);
        FavoriteComboBox.SelectedIndex = FavoriteComboBox.Items.Count > 0 ? 0 : -1;
    }

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

        Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        Browser.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
        Browser.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
        _isInitialized = true;
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

    private void UseCurrentUrlButton_Click(object sender, RoutedEventArgs e)
    {
        UseCurrentUrlRequested?.Invoke(CurrentUrl);
    }

    private void AddFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        var favoriteName = FavoriteNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(favoriteName))
        {
            favoriteName = _navigationService.CreateDisplayName(CurrentUrl, CurrentTitle);
        }

        AddFavoriteRequested?.Invoke(favoriteName, CurrentUrl);
    }

    private void UseFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (FavoriteComboBox.SelectedItem is StreamEntry favorite)
        {
            UseFavoriteRequested?.Invoke(favorite);
        }
    }

    private void ShowInitializationError(Exception ex)
    {
        InitializationOverlay.Visibility = Visibility.Visible;
        InitializationTextBlock.Text = ex.Message;
    }

}
