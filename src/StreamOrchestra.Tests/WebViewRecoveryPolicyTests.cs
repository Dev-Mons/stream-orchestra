using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class WebViewRecoveryPolicyTests
{
    [Theory]
    [InlineData(CoreWebView2ProcessFailedKind.BrowserProcessExited, WebViewRecoveryAction.Recreate)]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessExited, WebViewRecoveryAction.Reload)]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive, WebViewRecoveryAction.Reload)]
    [InlineData(CoreWebView2ProcessFailedKind.FrameRenderProcessExited, WebViewRecoveryAction.None)]
    [InlineData(CoreWebView2ProcessFailedKind.GpuProcessExited, WebViewRecoveryAction.None)]
    [InlineData(CoreWebView2ProcessFailedKind.UtilityProcessExited, WebViewRecoveryAction.None)]
    [InlineData(CoreWebView2ProcessFailedKind.UnknownProcessExited, WebViewRecoveryAction.None)]
    public void GetAction_ReturnsExpectedRecovery(
        CoreWebView2ProcessFailedKind failureKind,
        WebViewRecoveryAction expected)
    {
        Assert.Equal(expected, WebViewRecoveryPolicy.GetAction(failureKind));
    }
}
