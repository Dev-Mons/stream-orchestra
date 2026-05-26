using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class FeasibilityScenarioServiceTests
{
    [Theory]
    [InlineData(4, "group_a_first_slots")]
    [InlineData(8, "groups_a_b_8_slots")]
    [InlineData(9, "groups_a_b_c_9_slot_threshold")]
    [InlineData(12, "groups_a_b_c_12_slots")]
    [InlineData(16, "groups_a_b_c_d_16_slots")]
    public void CreateFirstSlotsScenario_MapsPlanCountsToNamedScenarios(int playbackCount, string expectedScenarioId)
    {
        var scenarioService = new FeasibilityScenarioService();
        var plan = new PlaybackTestPlanService().CreatePlan(playbackCount);

        var scenario = scenarioService.CreateFirstSlotsScenario(plan);

        Assert.Equal(expectedScenarioId, scenario.Id);
    }

    [Theory]
    [InlineData("All", 16, "manual_all_groups")]
    [InlineData("A", 4, "manual_group_a")]
    public void CreateScopeLoadScenario_MapsGroupLoadsToNamedScenarios(
        string groupId,
        int targetSlotCount,
        string expectedScenarioId)
    {
        var scenarioService = new FeasibilityScenarioService();

        var scenario = scenarioService.CreateScopeLoadScenario(groupId, targetSlotCount);

        Assert.Equal(expectedScenarioId, scenario.Id);
    }

    [Fact]
    public void CreateIsolatedGroupScenario_RecordsIsolatedGroupEvidence()
    {
        var scenarioService = new FeasibilityScenarioService();

        var scenario = scenarioService.CreateIsolatedGroupScenario("A", 4);

        Assert.Equal("isolated_group_a", scenario.Id);
        Assert.Equal("Isolated Group A test (4 slot(s))", scenario.Name);
    }

    [Theory]
    [InlineData(4, "isolated_group_a", null)]
    [InlineData(16, "isolated_group_a", "Scenario isolated_group_a requires 1-4 slot(s).")]
    [InlineData(8, "groups_a_b_8_slots", null)]
    [InlineData(12, "groups_a_b_8_slots", "Scenario groups_a_b_8_slots requires 8 slot(s).")]
    [InlineData(9, "groups_a_b_c_9_slot_threshold", null)]
    [InlineData(12, "groups_a_b_c_12_slots", null)]
    [InlineData(16, "groups_a_b_c_d_16_slots", null)]
    [InlineData(16, "custom_manual_test", null)]
    public void ValidatePlaybackCountConsistency_RejectsKnownScenarioCountMismatches(
        int playbackCount,
        string scenarioId,
        string? expectedError)
    {
        var error = FeasibilityScenarioService.ValidatePlaybackCountConsistency(playbackCount, scenarioId);

        Assert.Equal(expectedError, error);
    }

    [Theory]
    [InlineData(9, "groups_a_b_c_9_slot_threshold", true)]
    [InlineData(12, "groups_a_b_c_12_slots", true)]
    [InlineData(16, "groups_a_b_c_d_16_slots", true)]
    [InlineData(8, "groups_a_b_8_slots", false)]
    [InlineData(9, "custom_9_slot_note", false)]
    [InlineData(12, "groups_a_b_c_9_slot_threshold", false)]
    public void IsPlanNinePlusPlaybackScenario_RequiresKnownNinePlusPlanScenario(
        int playbackCount,
        string scenarioId,
        bool expected)
    {
        var result = new FeasibilityTestResult
        {
            PlaybackCount = playbackCount,
            ScenarioId = scenarioId
        };

        var isPlanSuccessScenario = FeasibilityScenarioService.IsPlanNinePlusPlaybackScenario(result);

        Assert.Equal(expected, isPlanSuccessScenario);
    }
}
