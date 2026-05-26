using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class SlotSwapService
{
    private readonly StreamNavigationService _navigationService;

    public SlotSwapService(StreamNavigationService? navigationService = null)
    {
        _navigationService = navigationService ?? new StreamNavigationService();
    }

    public SlotSwapResult SwapStreams(SlotRuntimeState sourceSlot, SlotRuntimeState targetSlot)
    {
        ArgumentNullException.ThrowIfNull(sourceSlot);
        ArgumentNullException.ThrowIfNull(targetSlot);

        if (sourceSlot.SlotId == targetSlot.SlotId)
        {
            return new SlotSwapResult(sourceSlot, targetSlot);
        }

        var sourceStream = NormalizeStream(sourceSlot.StreamName, sourceSlot.StreamUrl);
        var targetStream = NormalizeStream(targetSlot.StreamName, targetSlot.StreamUrl);

        return new SlotSwapResult(
            sourceSlot with
            {
                StreamName = targetStream.Name,
                StreamUrl = targetStream.Url
            },
            targetSlot with
            {
                StreamName = sourceStream.Name,
                StreamUrl = sourceStream.Url
            });
    }

    private (string Name, string Url) NormalizeStream(string? streamName, string? streamUrl)
    {
        var normalizedUrl = _navigationService.NormalizeUrl(streamUrl ?? "about:blank");
        var normalizedName = string.IsNullOrWhiteSpace(streamName)
            ? _navigationService.CreateDisplayName(normalizedUrl)
            : streamName.Trim();

        if (normalizedUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            normalizedName = "Empty";
        }

        return (normalizedName, normalizedUrl);
    }
}
