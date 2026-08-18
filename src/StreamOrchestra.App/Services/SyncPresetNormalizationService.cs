using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class SyncPresetNormalizationService
{
    public const int DefaultMinimumSafetyDelayMs = 3000;
    public const int MinimumSafetyDelayMs = 1500;
    public const int MaximumSafetyDelayMs = 15000;
    public const int MaximumManualDelayMs = 60000;
    public const int MaximumUserResidualMs = MaximumManualDelayMs * 2;

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
            .Select(member =>
            {
                var components = NormalizeDelayComponents(member);
                return new SyncMemberPreset
                {
                    SlotId = member.SlotId,
                    ManualDelayMs = components.FinalDelayMilliseconds,
                    DelayModelVersion = SyncManualDelaySchema.CurrentVersion,
                    AlgorithmPriorMs = components.AlgorithmPriorMilliseconds,
                    UserResidualMs = components.UserResidualMilliseconds,
                    CalibratedStreamUrl = CreateStreamKey(member.CalibratedStreamUrl)
                };
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

    public static SyncManualDelayComponents NormalizeDelayComponents(SyncMemberPreset member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member.DelayModelVersion >= SyncManualDelaySchema.CurrentVersion &&
            member.AlgorithmPriorMs is { } algorithmPrior &&
            member.UserResidualMs is { } userResidual)
        {
            algorithmPrior = Math.Clamp(
                algorithmPrior,
                -MaximumManualDelayMs,
                MaximumManualDelayMs);
            userResidual = Math.Clamp(
                userResidual,
                -MaximumUserResidualMs,
                MaximumUserResidualMs);
            return new SyncManualDelayComponents(
                algorithmPrior,
                userResidual,
                Math.Clamp(
                    algorithmPrior + userResidual,
                    -MaximumManualDelayMs,
                    MaximumManualDelayMs));
        }

        var legacyFinal = Math.Clamp(
            member.ManualDelayMs,
            -MaximumManualDelayMs,
            MaximumManualDelayMs);
        return new SyncManualDelayComponents(0, legacyFinal, legacyFinal);
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
