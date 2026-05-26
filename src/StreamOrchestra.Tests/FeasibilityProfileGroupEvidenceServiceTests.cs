using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class FeasibilityProfileGroupEvidenceServiceTests
{
    [Fact]
    public void Normalize_IgnoresNullBlankDuplicatesAndSortsGroups()
    {
        var groups = FeasibilityProfileGroupEvidenceService.Normalize(["b", null!, " ", "A", "b"]);

        Assert.Equal(["A", "B"], groups);
    }

    [Theory]
    [InlineData(4, "isolated_group_a", "A")]
    [InlineData(4, "manual_group_d", "D")]
    [InlineData(8, "groups_a_b_8_slots", "A/B")]
    [InlineData(9, "groups_a_b_c_9_slot_threshold", "A/B/C")]
    [InlineData(16, "groups_a_b_c_d_16_slots", "A/B/C/D")]
    public void GetScenarioProfileGroups_MapsKnownScenariosToAllowedGroups(
        int playbackCount,
        string scenarioId,
        string expectedGroups)
    {
        var groups = FeasibilityProfileGroupEvidenceService.GetScenarioProfileGroups(playbackCount, scenarioId);

        Assert.Equal(expectedGroups, string.Join("/", groups));
    }

    [Fact]
    public void ValidateScenarioConsistency_RejectsProfileGroupsOutsideScenario()
    {
        var error = FeasibilityProfileGroupEvidenceService.ValidateScenarioConsistency(
            playbackCount: 4,
            scenarioId: "isolated_group_a",
            profileGroups: ["D"]);

        Assert.Equal("Profile groups must match scenario groups: A.", error);
    }

    [Fact]
    public void GetScenarioConsistentGroups_FiltersContradictorySavedEvidence()
    {
        var result = new FeasibilityTestResult
        {
            PlaybackCount = 4,
            ScenarioId = "isolated_group_a",
            VerifiedProfileGroups = ["A", "D", null!]
        };

        var groups = FeasibilityProfileGroupEvidenceService.GetScenarioConsistentGroups(result);

        Assert.Equal(["A"], groups);
    }

    [Fact]
    public void GetLatestSameAccountCoveredGroups_UsesLatestEvidencePerProfileGroup()
    {
        var olderGroupDPass = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            groups: ["D"],
            sameAccount: true);
        var newerGroupDFailure = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            groups: ["D"],
            sameAccount: false,
            outcome: "failure");
        var groupAPass = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 30, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_a",
            groups: ["A"],
            sameAccount: true);

        var groups = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountCoveredGroups(
            [olderGroupDPass, newerGroupDFailure, groupAPass]);

        Assert.Equal(["A"], groups);
    }

    [Fact]
    public void GetLatestSameAccountCoveredGroups_UsesScenarioGroupsForFailureEvidence()
    {
        var olderGroupDPass = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            groups: ["D"],
            sameAccount: true);
        var newerGroupDFailureWithoutCheckedGroups = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            groups: [],
            sameAccount: false,
            outcome: "failure");

        var groups = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountCoveredGroups(
            [olderGroupDPass, newerGroupDFailureWithoutCheckedGroups]);

        Assert.Empty(groups);
    }

    [Fact]
    public void GetLatestSameAccountCoveredGroups_IgnoresPartialWithoutSameAccountCheck()
    {
        var olderGroupDPass = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            groups: ["D"],
            sameAccount: true);
        var newerGroupDPlaybackOnlyPartial = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            groups: ["D"],
            sameAccount: false,
            outcome: "partial");

        var groups = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountCoveredGroups(
            [olderGroupDPass, newerGroupDPlaybackOnlyPartial]);

        Assert.Equal(["D"], groups);
    }

    [Fact]
    public void GetLatestSameAccountCoveredGroups_IgnoresInvalidOutcomeEvidence()
    {
        var olderGroupDPass = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            groups: ["D"],
            sameAccount: true);
        var newerMalformedGroupDFailure = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            groups: [],
            sameAccount: false,
            outcome: "");

        var groups = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountCoveredGroups(
            [olderGroupDPass, newerMalformedGroupDFailure]);

        Assert.Equal(["D"], groups);
    }

    [Fact]
    public void GetLatestSameAccountCoveredGroups_IgnoresCustomFailureWithoutCheckedGroups()
    {
        var olderGroupsPass = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_9_slot_threshold",
            playbackCount: 9,
            groups: ["A", "B", "C"],
            sameAccount: true);
        var customFailureWithoutCheckedGroups = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero),
            scenarioId: "custom_9_slot_note",
            playbackCount: 9,
            groups: [],
            sameAccount: false,
            outcome: "failure");

        var groups = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountCoveredGroups(
            [olderGroupsPass, customFailureWithoutCheckedGroups]);

        Assert.Equal(["A", "B", "C"], groups);
    }

    [Fact]
    public void GetLatestSameAccountCoveredGroups_UsesCheckedGroupsForCustomFailure()
    {
        var olderGroupsPass = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_9_slot_threshold",
            playbackCount: 9,
            groups: ["A", "B", "C"],
            sameAccount: true);
        var customFailureWithCheckedGroup = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero),
            scenarioId: "custom_9_slot_note",
            playbackCount: 9,
            groups: ["B"],
            sameAccount: false,
            outcome: "failure");

        var groups = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountCoveredGroups(
            [olderGroupsPass, customFailureWithCheckedGroup]);

        Assert.Equal(["A", "C"], groups);
    }

    [Fact]
    public void GetLatestSameAccountAccountLabels_ReturnsDistinctLabelsFromCoveredGroups()
    {
        var mainAccountGroups = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_9_slot_threshold",
            playbackCount: 9,
            groups: ["A", "B", "C"],
            sameAccount: true,
            accountLabel: " main_soop ");
        var alternateAccountGroup = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 15, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            groups: ["D"],
            sameAccount: true,
            accountLabel: "alt_soop");

        var labels = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountAccountLabels(
            [mainAccountGroups, alternateAccountGroup]);

        Assert.Equal(["alt_soop", "main_soop"], labels);
        Assert.True(FeasibilityProfileGroupEvidenceService.HasConflictingSameAccountLabels(
            [mainAccountGroups, alternateAccountGroup]));
    }

    [Fact]
    public void GetLatestSameAccountCoveredGroupsWithoutAccountLabels_UsesLatestCoveredEvidence()
    {
        var groupAWithLabel = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_a",
            groups: ["A"],
            sameAccount: true,
            accountLabel: "main_soop");
        var groupBWithoutLabel = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 15, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_b",
            groups: ["B"],
            sameAccount: true);
        var olderGroupCWithoutLabel = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_c",
            groups: ["C"],
            sameAccount: true);
        var newerGroupCWithLabel = CreateResult(
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 45, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_c",
            groups: ["C"],
            sameAccount: true,
            accountLabel: "main_soop");

        var groups = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountCoveredGroupsWithoutAccountLabels(
            [groupAWithLabel, groupBWithoutLabel, olderGroupCWithoutLabel, newerGroupCWithLabel]);

        Assert.Equal(["B"], groups);
    }

    private static FeasibilityTestResult CreateResult(
        DateTimeOffset capturedAt,
        string scenarioId,
        int playbackCount,
        IReadOnlyList<string> groups,
        bool sameAccount,
        string outcome = "partial",
        string accountLabel = "")
    {
        return new FeasibilityTestResult
        {
            CapturedAt = capturedAt,
            PlaybackCount = playbackCount,
            ScenarioId = scenarioId,
            Outcome = outcome,
            VerifiedProfileGroups = groups,
            IsSameAccountSessionMaintained = sameAccount,
            AccountLabel = accountLabel
        };
    }

    private static FeasibilityTestResult CreateResult(
        DateTimeOffset capturedAt,
        string scenarioId,
        IReadOnlyList<string> groups,
        bool sameAccount,
        string outcome = "partial",
        string accountLabel = "")
    {
        return CreateResult(capturedAt, scenarioId, 4, groups, sameAccount, outcome, accountLabel);
    }
}
