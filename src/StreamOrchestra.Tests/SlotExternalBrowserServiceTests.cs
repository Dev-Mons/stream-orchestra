using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SlotExternalBrowserServiceTests
{
    private const string ChromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    public void ShouldOpenExternally_OnlyWhenUnacceptedUncanceledDropLandsOutsideApp(
        bool wasDropAccepted,
        bool wasCanceled,
        bool isPointerOverAppWindow,
        bool expected)
    {
        Assert.Equal(
            expected,
            SlotExternalBrowserService.ShouldOpenExternally(wasDropAccepted, wasCanceled, isPointerOverAppWindow));
    }

    [Fact]
    public void CreatePlan_OpensStreamInNewChromeWindowWhenChromeIsInstalled()
    {
        var service = new SlotExternalBrowserService();

        var plan = service.CreatePlan(
            CreateSlot(3, "테스트 방송", "https://play.sooplive.com/streamer"),
            [CreateBrowser("edge", "Microsoft Edge", @"C:\Edge\msedge.exe"), CreateBrowser("chrome", "Google Chrome", ChromePath)]);

        Assert.True(plan.CanOpen);
        Assert.True(plan.UsesChrome);
        Assert.Equal("https://play.sooplive.com/streamer", plan.Url);
        Assert.NotNull(plan.StartInfo);
        Assert.Equal(ChromePath, plan.StartInfo!.FileName);
        Assert.False(plan.StartInfo.UseShellExecute);
        Assert.Equal(new[] { "--new-window", "https://play.sooplive.com/streamer" }, plan.StartInfo.ArgumentList);
        Assert.Equal("Slot 3: 테스트 방송을(를) 크롬 새 창으로 열었습니다.", plan.StatusMessage);
    }

    [Fact]
    public void CreatePlan_PassesUrlAsSingleArgumentSoItCannotInjectChromeSwitches()
    {
        var service = new SlotExternalBrowserService();

        var plan = service.CreatePlan(
            CreateSlot(1, "Stream", "https://example.com/a b?x=1&y=--incognito"),
            [CreateBrowser("chrome", "Google Chrome", ChromePath)]);

        Assert.True(plan.CanOpen);
        Assert.Equal(2, plan.StartInfo!.ArgumentList.Count);
        Assert.Equal(plan.Url, plan.StartInfo.ArgumentList[1]);
        Assert.StartsWith("https://example.com/", plan.StartInfo.ArgumentList[1]);
    }

    [Fact]
    public void CreatePlan_FallsBackToDefaultBrowserWhenChromeIsMissing()
    {
        var service = new SlotExternalBrowserService();

        var plan = service.CreatePlan(
            CreateSlot(5, "Stream", "https://play.sooplive.com/streamer"),
            [
                CreateBrowser("chrome", "Google Chrome", null, isInstalled: false),
                CreateBrowser("edge", "Microsoft Edge", @"C:\Edge\msedge.exe")
            ]);

        Assert.True(plan.CanOpen);
        Assert.False(plan.UsesChrome);
        Assert.NotNull(plan.StartInfo);
        Assert.Equal("https://play.sooplive.com/streamer", plan.StartInfo!.FileName);
        Assert.True(plan.StartInfo.UseShellExecute);
        Assert.Empty(plan.StartInfo.ArgumentList);
        Assert.Equal("Slot 5: 크롬을 찾지 못해 Stream을(를) 기본 브라우저로 열었습니다.", plan.StatusMessage);
    }

    [Fact]
    public void CreatePlan_FallsBackToDefaultBrowserWhenBrowserListIsEmptyOrNull()
    {
        var service = new SlotExternalBrowserService();
        var slot = CreateSlot(2, "Stream", "https://play.sooplive.com/streamer");

        Assert.False(service.CreatePlan(slot, []).UsesChrome);
        Assert.True(service.CreatePlan(slot, null).CanOpen);
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("ABOUT:BLANK")]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePlan_DoesNotOpenEmptySlot(string streamUrl)
    {
        var service = new SlotExternalBrowserService();

        var plan = service.CreatePlan(
            CreateSlot(7, "Empty", streamUrl),
            [CreateBrowser("chrome", "Google Chrome", ChromePath)]);

        Assert.False(plan.CanOpen);
        Assert.Null(plan.StartInfo);
        Assert.Null(plan.Url);
        Assert.Equal("Slot 7: 빈 슬롯이라 브라우저로 열 방송이 없습니다.", plan.StatusMessage);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("edge://settings")]
    public void CreatePlan_DoesNotOpenNonWebAddress(string streamUrl)
    {
        var service = new SlotExternalBrowserService();

        var plan = service.CreatePlan(
            CreateSlot(4, "Stream", streamUrl),
            [CreateBrowser("chrome", "Google Chrome", ChromePath)]);

        Assert.False(plan.CanOpen);
        Assert.Null(plan.StartInfo);
        Assert.Equal("Slot 4: 브라우저로 열 수 있는 방송 주소가 없습니다.", plan.StatusMessage);
    }

    [Fact]
    public void CreatePlan_UsesUrlDisplayNameWhenStreamNameIsNotMeaningful()
    {
        var service = new SlotExternalBrowserService();

        var plan = service.CreatePlan(
            CreateSlot(6, "SOOP", "https://play.sooplive.com/streamer/123456"),
            [CreateBrowser("chrome", "Google Chrome", ChromePath)]);

        Assert.Equal("Slot 6: streamer을(를) 크롬 새 창으로 열었습니다.", plan.StatusMessage);
    }

    [Fact]
    public void CreatePlan_UsesCustomChromeCandidateFromDiscoveryService()
    {
        var rootFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootFolder);
        try
        {
            var executablePath = Path.Combine(rootFolder, "chrome.exe");
            File.WriteAllText(executablePath, "");
            var browsers = new ExternalBrowserDiscoveryService(
            [
                new ExternalBrowserCandidate("chrome", "Google Chrome", [executablePath])
            ]).Discover();

            var plan = new SlotExternalBrowserService().CreatePlan(
                CreateSlot(1, "Stream", "https://play.sooplive.com/streamer"),
                browsers);

            Assert.True(plan.UsesChrome);
            Assert.Equal(executablePath, plan.StartInfo!.FileName);
        }
        finally
        {
            Directory.Delete(rootFolder, recursive: true);
        }
    }

    private static SlotRuntimeState CreateSlot(int slotId, string streamName, string streamUrl)
    {
        return new SlotRuntimeState(slotId, streamName, streamUrl, IsMuted: false, ProfileGroupId: "A");
    }

    private static ExternalBrowserInfo CreateBrowser(string id, string name, string? executablePath, bool isInstalled = true)
    {
        return new ExternalBrowserInfo(id, name, isInstalled, executablePath, executablePath is null ? [] : [executablePath]);
    }
}
