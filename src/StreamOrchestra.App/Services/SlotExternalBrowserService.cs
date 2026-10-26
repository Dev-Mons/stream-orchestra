using System.Diagnostics;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

// 슬롯을 앱 창 밖에 드롭했을 때 그 방송을 외부 브라우저로 여는 실행 계획을 만든다.
public sealed class SlotExternalBrowserService
{
    public const string ChromeBrowserId = "chrome";

    private readonly StreamNavigationService _navigationService;

    public SlotExternalBrowserService(StreamNavigationService? navigationService = null)
    {
        _navigationService = navigationService ?? new StreamNavigationService();
    }

    // 받아준 드롭 대상이 없고, 취소(ESC 등)하지 않았고, 놓은 곳이 앱 창 위가 아닐 때만 외부로 연다.
    // 앱 안 드롭은 이 조건에 걸리지 않으므로 기존 교체 동작이 그대로 유지된다.
    public static bool ShouldOpenExternally(bool wasDropAccepted, bool wasCanceled, bool isPointerOverAppWindow)
    {
        return !wasDropAccepted && !wasCanceled && !isPointerOverAppWindow;
    }

    public SlotExternalBrowserPlan CreatePlan(SlotRuntimeState slot, IReadOnlyList<ExternalBrowserInfo>? browsers)
    {
        ArgumentNullException.ThrowIfNull(slot);

        var rawUrl = slot.StreamUrl?.Trim();
        if (string.IsNullOrEmpty(rawUrl) || rawUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return SlotExternalBrowserPlan.NotOpened($"Slot {slot.SlotId}: 빈 슬롯이라 브라우저로 열 방송이 없습니다.");
        }

        var url = _navigationService.NormalizeUrl(rawUrl);
        if (url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return SlotExternalBrowserPlan.NotOpened($"Slot {slot.SlotId}: 브라우저로 열 수 있는 방송 주소가 없습니다.");
        }

        var streamName = _navigationService.IsMeaningfulDisplayName(slot.StreamName)
            ? slot.StreamName.Trim()
            : _navigationService.CreateDisplayName(url);
        var chrome = (browsers ?? []).FirstOrDefault(browser =>
            browser is not null &&
            browser.IsInstalled &&
            !string.IsNullOrWhiteSpace(browser.ExecutablePath) &&
            string.Equals(browser.Id, ChromeBrowserId, StringComparison.OrdinalIgnoreCase));

        if (chrome is not null)
        {
            // 창 밖으로 끌어낸 느낌에 맞춰 기존 크롬 창의 탭이 아니라 새 창으로 띄운다.
            var chromeStartInfo = new ProcessStartInfo(chrome.ExecutablePath!.Trim())
            {
                UseShellExecute = false
            };
            chromeStartInfo.ArgumentList.Add("--new-window");
            chromeStartInfo.ArgumentList.Add(url);

            return new SlotExternalBrowserPlan(
                url,
                chromeStartInfo,
                UsesChrome: true,
                $"Slot {slot.SlotId}: {streamName}을(를) 크롬 새 창으로 열었습니다.");
        }

        // 크롬이 없으면 Windows 기본 브라우저로 연다. url은 위에서 http/https로 정규화된 값이다.
        return new SlotExternalBrowserPlan(
            url,
            new ProcessStartInfo(url) { UseShellExecute = true },
            UsesChrome: false,
            $"Slot {slot.SlotId}: 크롬을 찾지 못해 {streamName}을(를) 기본 브라우저로 열었습니다.");
    }
}
