using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class SyncPresetNormalizationService
{
    public const int DefaultMinimumSafetyDelayMs = 3000;
    public const int MinimumSafetyDelayMs = 1500;
    public const int MaximumSafetyDelayMs = 15000;
    public const int MaximumManualDelayMs = 60000;
    public static SyncGroupPreset Normalize(SyncGroupPreset? preset)
    {
        if (preset is null)
        {
            return new SyncGroupPreset();
        }

        IEnumerable<SyncMemberPreset?> sourceMembers = preset.Members ?? [];
        var members = sourceMembers
            .OfType<SyncMemberPreset>()
            .Where(member => member.SlotId is >= 1 and <= SlotProfileGroupMapping.MaxSlotCount)
            .GroupBy(member => member.SlotId)
            .Select(group => group.Last())
            .OrderBy(member => member.SlotId)
            .Select(member => new SyncMemberPreset
            {
                SlotId = member.SlotId,
                ManualDelayMs = Math.Clamp(
                    member.ManualDelayMs,
                    -MaximumManualDelayMs,
                    MaximumManualDelayMs),
                CalibratedStreamUrl = CreateStreamKey(member.CalibratedStreamUrl)
            })
            .ToArray();

        return new SyncGroupPreset
        {
            MinimumSafetyDelayMs = Math.Clamp(
                preset.MinimumSafetyDelayMs,
                MinimumSafetyDelayMs,
                MaximumSafetyDelayMs),
            Members = members
        };
    }

    public static string CreateStreamKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "";
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{path}";
    }
}
