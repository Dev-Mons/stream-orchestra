using Microsoft.Web.WebView2.Core;

namespace StreamOrchestra.App.Services;

public enum WebViewRecoveryAction
{
    None,
    Reload,
    Recreate
}

public static class WebViewRecoveryPolicy
{
    public static WebViewRecoveryAction GetAction(CoreWebView2ProcessFailedKind failureKind)
    {
        return failureKind switch
        {
            CoreWebView2ProcessFailedKind.BrowserProcessExited => WebViewRecoveryAction.Recreate,
            CoreWebView2ProcessFailedKind.RenderProcessExited => WebViewRecoveryAction.Reload,
            CoreWebView2ProcessFailedKind.RenderProcessUnresponsive => WebViewRecoveryAction.Reload,
            _ => WebViewRecoveryAction.None
        };
    }
}
