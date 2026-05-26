namespace StreamOrchestra.App.Models;

public sealed record ExternalBrowserSlotLaunchPlan(
    int SlotId,
    string StreamName,
    string StreamUrl,
    string BrowserId,
    string BrowserName,
    string ExecutablePath,
    string UserDataFolder,
    IReadOnlyList<string> Arguments,
    ExternalBrowserWindowLayout? WindowLayout = null,
    bool IsMuted = false);
