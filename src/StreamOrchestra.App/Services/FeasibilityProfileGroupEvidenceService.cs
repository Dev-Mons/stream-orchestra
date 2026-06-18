using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class FeasibilityProfileGroupEvidenceService
{
    private static readonly string[] GroupOrder = SlotProfileGroupMapping.GroupIds.ToArray();

    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? profileGroups)
    {
        if (profileGroups is null || profileGroups.Count == 0)
        {
            return [];
        }

        return profileGroups
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => group!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetGroupSortIndex)
            .ThenBy(group => group, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? ValidateValues(IReadOnlyList<string>? profileGroups)
    {
        var normalizedGroups = Normalize(profileGroups);
        var invalidGroup = normalizedGroups.FirstOrDefault(group => !GroupOrder.Contains(group));

        return invalidGroup is null
            ? null
            : $"Profile groups must be {FormatAllowedGroupsForValidation()}.";
    }

    public static IReadOnlyList<string> GetRequiredGroupsForPlaybackCount(int playbackCount)
    {
        if (playbackCount <= 0)
        {
            return [];
        }

        return Enumerable
            .Range(1, Math.Min(playbackCount, SlotProfileGroupMapping.MaxSlotCount))
            .Select(SlotProfileGroupMapping.GetGroupIdForSlot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool HasRequiredGroups(int playbackCount, IReadOnlyList<string>? profileGroups)
    {
        var normalizedGroups = Normalize(profileGroups);
        var groupSet = normalizedGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetRequiredGroupsForPlaybackCount(playbackCount).All(groupSet.Contains);
    }

    public static IReadOnlyList<string> GetScenarioProfileGroups(int playbackCount, string? scenarioId)
    {
        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            var normalizedScenarioId = scenarioId.Trim().ToLowerInvariant();
            if (TryGetSingleGroupScenario(normalizedScenarioId, "isolated_group_", out var scenarioGroup))
            {
                return [scenarioGroup];
            }

            if (TryGetSingleGroupScenario(normalizedScenarioId, "manual_group_", out scenarioGroup))
            {
                return [scenarioGroup];
            }

            return normalizedScenarioId switch
            {
                "group_a_first_slots" => ["A"],
                "groups_a_b_8_slots" => GetRequiredGroupsForPlaybackCount(8),
                "groups_a_b_c_9_slot_threshold" => ["A", "B", "C"],
                "groups_a_b_c_12_slots" => GetRequiredGroupsForPlaybackCount(12),
                "groups_a_b_c_d_16_slots" => GetRequiredGroupsForPlaybackCount(16),
                _ => GetRequiredGroupsForPlaybackCount(playbackCount)
            };
        }

        return GetRequiredGroupsForPlaybackCount(playbackCount);
    }

    public static IReadOnlyList<string> GetScenarioConsistentGroups(FeasibilityTestResult result)
    {
        return GetScenarioConsistentGroups(result.PlaybackCount, result.ScenarioId, result.VerifiedProfileGroups);
    }

    public static IReadOnlyList<string> GetLatestSameAccountCoveredGroups(IReadOnlyList<FeasibilityTestResult> results)
    {
        var latestResultByGroup = GetLatestSameAccountResultByGroup(results);

        return Normalize(
            latestResultByGroup
                .Where(item => item.Value.IsSameAccountSessionMaintained)
                .Select(item => item.Key)
                .ToArray());
    }

    public static IReadOnlyList<string> GetLatestSameAccountAccountLabels(IReadOnlyList<FeasibilityTestResult> results)
    {
        return GetLatestSameAccountResultByGroup(results)
            .Values
            .Where(result => result.IsSameAccountSessionMaintained)
            .Select(result => result.AccountLabel?.Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> GetLatestSameAccountCoveredGroupsWithoutAccountLabels(
        IReadOnlyList<FeasibilityTestResult> results)
    {
        return Normalize(
            GetLatestSameAccountResultByGroup(results)
                .Where(item => item.Value.IsSameAccountSessionMaintained &&
                    string.IsNullOrWhiteSpace(item.Value.AccountLabel))
                .Select(item => item.Key)
                .ToArray());
    }

    public static bool HasConflictingSameAccountLabels(IReadOnlyList<FeasibilityTestResult> results)
    {
        return GetLatestSameAccountAccountLabels(results).Count > 1;
    }

    private static Dictionary<string, FeasibilityTestResult> GetLatestSameAccountResultByGroup(
        IReadOnlyList<FeasibilityTestResult> results)
    {
        var latestResultByGroup = new Dictionary<string, FeasibilityTestResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in FeasibilityResultOrderingService.OrderOldestFirst(
                     results.Where(result => FeasibilityScenarioService.IsPlaybackCountConsistent(result) &&
                         FeasibilityOutcomeService.IsKnown(result))))
        {
            foreach (var group in GetSameAccountEvidenceGroups(result))
            {
                latestResultByGroup[group] = result;
            }
        }

        return latestResultByGroup;
    }

    private static IReadOnlyList<string> GetSameAccountEvidenceGroups(FeasibilityTestResult result)
    {
        if (result.IsSameAccountSessionMaintained)
        {
            return GetScenarioConsistentGroups(result);
        }

        if (!FeasibilityOutcomeService.IsFailure(result))
        {
            return [];
        }

        var checkedGroups = GetScenarioConsistentGroups(result);
        return checkedGroups.Count > 0
            ? checkedGroups
            : GetKnownScenarioProfileGroups(result.ScenarioId);
    }

    public static IReadOnlyList<string> GetScenarioConsistentGroups(
        int playbackCount,
        string? scenarioId,
        IReadOnlyList<string>? profileGroups)
    {
        var normalizedGroups = Normalize(profileGroups);
        if (normalizedGroups.Count == 0)
        {
            return [];
        }

        var allowedGroups = GetScenarioProfileGroups(playbackCount, scenarioId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return normalizedGroups
            .Where(allowedGroups.Contains)
            .ToArray();
    }

    public static string? ValidateScenarioConsistency(
        int playbackCount,
        string? scenarioId,
        IReadOnlyList<string>? profileGroups)
    {
        var normalizedGroups = Normalize(profileGroups);
        if (normalizedGroups.Count == 0)
        {
            return null;
        }

        var allowedGroups = GetScenarioProfileGroups(playbackCount, scenarioId);
        var allowedGroupSet = allowedGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalidGroup = normalizedGroups.FirstOrDefault(group => !allowedGroupSet.Contains(group));

        return invalidGroup is null
            ? null
            : $"Profile groups must match scenario groups: {string.Join(", ", allowedGroups)}.";
    }

    public static string FormatGroups(IReadOnlyList<string>? profileGroups)
    {
        var normalizedGroups = Normalize(profileGroups);

        return normalizedGroups.Count == 0
            ? "n/a"
            : string.Join("/", normalizedGroups);
    }

    public static string FormatRequiredGroups(int playbackCount)
    {
        return string.Join(", ", GetRequiredGroupsForPlaybackCount(playbackCount));
    }

    private static int GetGroupSortIndex(string group)
    {
        var index = Array.IndexOf(GroupOrder, group);
        return index >= 0 ? index : int.MaxValue;
    }

    private static bool TryGetSingleGroupScenario(string scenarioId, string prefix, out string group)
    {
        group = "";
        if (!scenarioId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = scenarioId[prefix.Length..].ToUpperInvariant();
        if (!GroupOrder.Contains(candidate))
        {
            return false;
        }

        group = candidate;
        return true;
    }

    private static IReadOnlyList<string> GetKnownScenarioProfileGroups(string? scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            return [];
        }

        var normalizedScenarioId = scenarioId.Trim().ToLowerInvariant();
        if (TryGetSingleGroupScenario(normalizedScenarioId, "isolated_group_", out var scenarioGroup) ||
            TryGetSingleGroupScenario(normalizedScenarioId, "manual_group_", out scenarioGroup))
        {
            return [scenarioGroup];
        }

        return normalizedScenarioId switch
        {
            "group_a_first_slots" => ["A"],
            "groups_a_b_8_slots" => GetRequiredGroupsForPlaybackCount(8),
            "groups_a_b_c_9_slot_threshold" => ["A", "B", "C"],
            "groups_a_b_c_12_slots" => GetRequiredGroupsForPlaybackCount(12),
            "groups_a_b_c_d_16_slots" => GetRequiredGroupsForPlaybackCount(16),
            _ => []
        };
    }

    private static string FormatAllowedGroupsForValidation()
    {
        return GroupOrder.Length switch
        {
            0 => "",
            1 => GroupOrder[0],
            2 => $"{GroupOrder[0]} and/or {GroupOrder[1]}",
            _ => $"{string.Join(", ", GroupOrder.Take(GroupOrder.Length - 1))}, and/or {GroupOrder[^1]}"
        };
    }
}
