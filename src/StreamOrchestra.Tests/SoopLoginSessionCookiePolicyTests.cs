using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SoopLoginSessionCookiePolicyTests
{
    [Theory]
    [InlineData(".sooplive.com", true, true)]
    [InlineData("sooplive.com", true, true)]
    [InlineData(".SOOPLIVE.CO.KR", true, true)]
    [InlineData("login.sooplive.com", true, false)]
    [InlineData(".naver.com", true, false)]
    [InlineData(".sooplive.com", false, false)]
    [InlineData("", true, false)]
    public void ShouldPersist_AllowsOnlySharedSoopSessionCookies(
        string domain,
        bool isSession,
        bool expected)
    {
        Assert.Equal(expected, SoopLoginSessionCookiePolicy.ShouldPersist(domain, isSession));
    }

    [Theory]
    [InlineData("https://login.sooplive.com/app/callback_naver.php?code=value", true)]
    [InlineData("https://login.sooplive.co.kr/app/callback_google.php?code=value", true)]
    [InlineData("https://login.sooplive.com/afreeca/connect.php?sns_code=21", false)]
    [InlineData("https://www.sooplive.com/app/callback_naver.php", false)]
    [InlineData("https://login.sooplive.com.evil.example/app/callback_naver.php", false)]
    [InlineData("javascript:callback_naver()", false)]
    [InlineData("", false)]
    public void IsLoginCallback_AllowsOnlySoopLoginCallbackUrls(string uri, bool expected)
    {
        Assert.Equal(expected, SoopLoginSessionCookiePolicy.IsLoginCallback(uri));
    }

    [Fact]
    public void CreateExpiration_UsesBoundedThirtyDayLifetime()
    {
        var now = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

        var expiration = SoopLoginSessionCookiePolicy.CreateExpiration(now);

        Assert.Equal(now.AddDays(30), expiration);
        Assert.Equal(DateTimeKind.Utc, expiration.Kind);
    }
}
