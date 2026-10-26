using System.Diagnostics;

namespace StreamOrchestra.App.Models;

public sealed record SlotExternalBrowserPlan(
    string? Url,
    ProcessStartInfo? StartInfo,
    bool UsesChrome,
    string StatusMessage)
{
    public bool CanOpen => StartInfo is not null;

    public static SlotExternalBrowserPlan NotOpened(string statusMessage)
    {
        return new SlotExternalBrowserPlan(null, null, UsesChrome: false, statusMessage);
    }
}
