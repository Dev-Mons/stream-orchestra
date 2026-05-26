using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class PlaybackTestPlanService
{
    public const int MaxSlotCount = 16;

    public PlaybackTestPlan CreatePlan(int targetPlaybackCount)
    {
        if (targetPlaybackCount is < 1 or > MaxSlotCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetPlaybackCount),
                targetPlaybackCount,
                $"Playback test count must be between 1 and {MaxSlotCount}.");
        }

        var allSlotIds = Enumerable.Range(1, MaxSlotCount).ToArray();
        var activeSlotIds = allSlotIds.Take(targetPlaybackCount).ToArray();
        var inactiveSlotIds = allSlotIds.Skip(targetPlaybackCount).ToArray();

        return new PlaybackTestPlan(targetPlaybackCount, activeSlotIds, inactiveSlotIds);
    }

    public PlaybackTestPlan CreateIsolatedSlotPlan(IReadOnlyList<int> activeSlotIds)
    {
        if (activeSlotIds.Count == 0)
        {
            throw new ArgumentException("At least one active slot is required.", nameof(activeSlotIds));
        }

        var normalizedActiveSlotIds = activeSlotIds
            .Distinct()
            .OrderBy(slotId => slotId)
            .ToArray();

        if (normalizedActiveSlotIds.Any(slotId => slotId is < 1 or > MaxSlotCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeSlotIds),
                $"Playback test slots must be between 1 and {MaxSlotCount}.");
        }

        var activeSet = normalizedActiveSlotIds.ToHashSet();
        var inactiveSlotIds = Enumerable.Range(1, MaxSlotCount)
            .Where(slotId => !activeSet.Contains(slotId))
            .ToArray();

        return new PlaybackTestPlan(normalizedActiveSlotIds.Length, normalizedActiveSlotIds, inactiveSlotIds);
    }
}
