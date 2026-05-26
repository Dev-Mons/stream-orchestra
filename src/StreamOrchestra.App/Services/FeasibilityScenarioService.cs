using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class FeasibilityScenarioService
{
    private static readonly string[] GroupIds = ["a", "b", "c", "d"];

    public FeasibilityScenario CreateFirstSlotsScenario(PlaybackTestPlan plan)
    {
        return plan.TargetPlaybackCount switch
        {
            <= 4 => new FeasibilityScenario("group_a_first_slots", $"Group A only ({plan.TargetPlaybackCount} slot(s))"),
            8 => new FeasibilityScenario("groups_a_b_8_slots", "Groups A/B split, 8 slots"),
            9 => new FeasibilityScenario("groups_a_b_c_9_slot_threshold", "Groups A/B/C, 9-slot success threshold"),
            12 => new FeasibilityScenario("groups_a_b_c_12_slots", "Groups A/B/C, 12 slots"),
            16 => new FeasibilityScenario("groups_a_b_c_d_16_slots", "Groups A/B/C/D, 16 slots"),
            _ => new FeasibilityScenario("first_slots_custom", $"First {plan.TargetPlaybackCount} slots")
        };
    }

    public FeasibilityScenario CreateScopeLoadScenario(string groupId, int targetSlotCount)
    {
        if (groupId.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return new FeasibilityScenario("manual_all_groups", $"Manual all-groups load ({targetSlotCount} slot(s))");
        }

        var normalizedGroupId = groupId.Trim().ToUpperInvariant();
        return new FeasibilityScenario(
            $"manual_group_{normalizedGroupId.ToLowerInvariant()}",
            $"Manual Group {normalizedGroupId} load ({targetSlotCount} slot(s))");
    }

    public FeasibilityScenario CreateIsolatedGroupScenario(string groupId, int targetSlotCount)
    {
        var normalizedGroupId = groupId.Trim().ToUpperInvariant();
        return new FeasibilityScenario(
            $"isolated_group_{normalizedGroupId.ToLowerInvariant()}",
            $"Isolated Group {normalizedGroupId} test ({targetSlotCount} slot(s))");
    }

    public static bool IsPlaybackCountConsistent(FeasibilityTestResult result)
    {
        return ValidatePlaybackCountConsistency(result.PlaybackCount, result.ScenarioId) is null;
    }

    public static bool IsPlanNinePlusPlaybackScenario(FeasibilityTestResult result)
    {
        if (!IsPlaybackCountConsistent(result))
        {
            return false;
        }

        var normalizedScenarioId = result.ScenarioId?.Trim().ToLowerInvariant();
        return result.PlaybackCount switch
        {
            9 => normalizedScenarioId == "groups_a_b_c_9_slot_threshold",
            12 => normalizedScenarioId == "groups_a_b_c_12_slots",
            16 => normalizedScenarioId == "groups_a_b_c_d_16_slots",
            _ => false
        };
    }

    public static bool IsPlanSuccessPlaybackScenario(FeasibilityTestResult result)
    {
        return IsPlanNinePlusPlaybackScenario(result);
    }

    public static string? ValidatePlaybackCountConsistency(int playbackCount, string? scenarioId)
    {
        if (!TryGetScenarioPlaybackCountRange(scenarioId, out var minimumCount, out var maximumCount))
        {
            return null;
        }

        if (playbackCount >= minimumCount && playbackCount <= maximumCount)
        {
            return null;
        }

        var normalizedScenarioId = scenarioId!.Trim();
        return minimumCount == maximumCount
            ? $"Scenario {normalizedScenarioId} requires {minimumCount} slot(s)."
            : $"Scenario {normalizedScenarioId} requires {minimumCount}-{maximumCount} slot(s).";
    }

    private static bool TryGetScenarioPlaybackCountRange(
        string? scenarioId,
        out int minimumCount,
        out int maximumCount)
    {
        minimumCount = 0;
        maximumCount = 0;
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            return false;
        }

        var normalizedScenarioId = scenarioId.Trim().ToLowerInvariant();
        if (normalizedScenarioId is "group_a_first_slots" ||
            IsSingleProfileGroupScenario(normalizedScenarioId))
        {
            minimumCount = 1;
            maximumCount = 4;
            return true;
        }

        (minimumCount, maximumCount) = normalizedScenarioId switch
        {
            "groups_a_b_8_slots" => (8, 8),
            "groups_a_b_c_9_slot_threshold" => (9, 9),
            "groups_a_b_c_12_slots" => (12, 12),
            "groups_a_b_c_d_16_slots" => (16, 16),
            _ => (0, 0)
        };

        return minimumCount > 0;
    }

    private static bool IsSingleProfileGroupScenario(string normalizedScenarioId)
    {
        return HasKnownGroupSuffix(normalizedScenarioId, "isolated_group_") ||
            HasKnownGroupSuffix(normalizedScenarioId, "manual_group_");
    }

    private static bool HasKnownGroupSuffix(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var groupId = value[prefix.Length..];
        return GroupIds.Contains(groupId);
    }
}
