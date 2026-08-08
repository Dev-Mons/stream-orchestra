using System.Windows;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class WebViewPopupWindow : Window
{
    private SoopLoginSessionCookieService? _sessionCookieService;

    public WebViewPopupWindow()
    {
        InitializeComponent();
        Closed += (_, _) => Browser.Dispose();
    }

    public static async Task OpenRequestedAsync(
        CoreWebView2NewWindowRequestedEventArgs args,
        Task<CoreWebView2Environment> environmentTask,
        SoopLoginSessionCookieService sessionCookieService,
        Window? owner)
    {
        var deferral = args.GetDeferral();
        args.Handled = true;
        WebViewPopupWindow? popup = null;

        try
        {
            var environment = await environmentTask;
            popup = new WebViewPopupWindow();
            if (owner is { IsLoaded: true, IsVisible: true })
            {
                popup.Owner = owner;
            }

            popup.Show();
            args.NewWindow = await popup.InitializeAsync(environment, sessionCookieService);
        }
        catch (Exception ex)
        {
            popup?.Close();
            var message = $"로그인 팝업을 열지 못했습니다.\n\n{ex.Message}";
            if (owner is { IsLoaded: true })
            {
                MessageBox.Show(owner, message, "로그인 창 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(message, "로그인 창 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<CoreWebView2> InitializeAsync(
        CoreWebView2Environment environment,
        SoopLoginSessionCookieService sessionCookieService)
    {
        _sessionCookieService = sessionCookieService;
        await Browser.EnsureCoreWebView2Async(environment);
        Browser.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            var title = Browser.CoreWebView2.DocumentTitle?.Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                Title = title;
            }
        };
        Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        Browser.CoreWebView2.WindowCloseRequested += CoreWebView2_WindowCloseRequested;
        return Browser.CoreWebView2;
    }

    private async void CoreWebView2_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            InitializationOverlay.Visibility = Visibility.Collapsed;
            if (SoopLoginSessionCookiePolicy.IsLoginCallback(Browser.Source?.AbsoluteUri))
            {
                await TryPersistLoginSessionAsync();
            }
            return;
        }

        InitializationTextBlock.Text = $"로그인 페이지를 열지 못했습니다: {e.WebErrorStatus}";
    }

    private async void CoreWebView2_WindowCloseRequested(object? sender, object e)
    {
        await TryPersistLoginSessionAsync();
        _ = Dispatcher.BeginInvoke(Close);
    }

    private async Task TryPersistLoginSessionAsync()
    {
        if (_sessionCookieService is null || Browser.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await _sessionCookieService.PersistAsync(Browser.CoreWebView2);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SOOP login session cookie persistence failed: {ex}");
        }
    }
}
