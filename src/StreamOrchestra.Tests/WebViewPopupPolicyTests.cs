using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class WebViewPopupPolicyTests
{
    [Theory]
    [InlineData("https://login.sooplive.com/afreeca/connect.php?sns_code=21&view=login", true)]
    [InlineData("https://login.sooplive.com/afreeca/connect.php?sns_code=23&view=login", true)]
    [InlineData("https://nid.naver.com/oauth2.0/authorize?response_type=code", true)]
    [InlineData("https://login.sooplive.com/app/callback_naver.php?code=value", true)]
    [InlineData("https://login.sooplive.co.kr/app/login.php?provider=naver", true)]
    [InlineData("https://login.sooplive.com/afreeca/connect.php?view=login", false)]
    [InlineData("https://www.sooplive.com/", false)]
    [InlineData("https://nid.naver.com.evil.example/oauth2.0/authorize", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void ShouldPreservePopupContext_AllowsOnlyNaverAndSoopNaverAuthenticationUrls(
        string uri,
        bool expected)
    {
        Assert.Equal(expected, WebViewPopupPolicy.ShouldPreservePopupContext(uri));
    }
}
