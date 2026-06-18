using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class FeasibilityAuditServiceTests
{
    [Fact]
    public void CreateAudit_WithSuccessfulNineSlotResultMissingGroupD_KeepsSuccessGatePending()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        Assert.Equal("pending", Find(auditItems, "group_a_playback").Status);
        Assert.Equal("pending", Find(auditItems, "eight_plus_playback").Status);
        Assert.Equal("pass", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("pending", Find(auditItems, "twelve_slot_playback").Status);
        Assert.Equal("pending", Find(auditItems, "sixteen_slot_playback").Status);
        Assert.Equal("pending", Find(auditItems, "same_account_session").Status);
        Assert.Equal("pass", Find(auditItems, "restart_session").Status);
        Assert.Equal("pass", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("pass", Find(auditItems, "resource_observations").Status);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_WithSuccessfulResultAndAllProfileGroups_MarksSuccessGatePassed()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var groupD = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"]);
        var groupE = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_e",
            verifiedProfileGroups: ["E"]);
        var decision = new FeasibilityDecisionService().Decide([result, groupD, groupE]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result, groupD, groupE], decision);

        Assert.Equal("pass", Find(auditItems, "same_account_session").Status);
        Assert.Equal("pass", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_WithFailedNineSlotResult_MarksGateFailed()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "failure",
            account: false,
            restart: false,
            resources: false,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        Assert.Equal("pending", Find(auditItems, "eight_plus_playback").Status);
        Assert.Equal("fail", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("fail", Find(auditItems, "same_account_session").Status);
        Assert.Equal("fail", Find(auditItems, "restart_session").Status);
        Assert.Equal("fail", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("fail", Find(auditItems, "resource_observations").Status);
        Assert.Equal("fail", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_WithFailedNineSlotResultHavingCriteria_DoesNotPassCriteriaGates()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "failure",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        Assert.Equal("fail", Find(auditItems, "restart_session").Status);
        Assert.Equal("fail", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("fail", Find(auditItems, "resource_observations").Status);
        Assert.Contains("Latest 9+ slot attempt failed", Find(auditItems, "restart_session").Evidence);
        Assert.Contains("Latest 9+ slot attempt failed", Find(auditItems, "resource_acceptability").Evidence);
        Assert.Contains("Latest 9+ slot attempt failed", Find(auditItems, "resource_observations").Evidence);
    }

    [Fact]
    public void CreateAudit_WithSuccessMissingResourceObservations_KeepsSuccessGatePending()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            includeStructuredResourceObservations: false,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        Assert.Equal("pending", Find(auditItems, "eight_plus_playback").Status);
        Assert.Equal("pass", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("pending", Find(auditItems, "same_account_session").Status);
        Assert.Equal("pass", Find(auditItems, "restart_session").Status);
        Assert.Equal("pending", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("pending", Find(auditItems, "resource_observations").Status);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_WithMalformedNineSlotOutcome_KeepsSuccessGatePending()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        Assert.Equal("pending", decision.Code);
        Assert.Equal("pending", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("pending", Find(auditItems, "same_account_session").Status);
        Assert.Equal("pending", Find(auditItems, "restart_session").Status);
        Assert.Equal("pending", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("pending", Find(auditItems, "resource_observations").Status);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Theory]
    [MemberData(nameof(InvalidResourceObservationResults))]
    public void CreateAudit_WithSuccessHavingInvalidResourceObservations_KeepsSuccessGatePending(FeasibilityTestResult result)
    {
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        Assert.Equal("pending", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("pending", Find(auditItems, "resource_observations").Status);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_WithOnlyLowSlotCriteria_DoesNotPassNinePlusPlanGates()
    {
        var result = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: true,
            resources: true);
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        Assert.Equal("pending", Find(auditItems, "group_a_playback").Status);
        Assert.Equal("pending", Find(auditItems, "eight_plus_playback").Status);
        Assert.Equal("pending", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("pending", Find(auditItems, "twelve_slot_playback").Status);
        Assert.Equal("pending", Find(auditItems, "sixteen_slot_playback").Status);
        Assert.Equal("pending", Find(auditItems, "same_account_session").Status);
        Assert.Equal("pending", Find(auditItems, "restart_session").Status);
        Assert.Equal("pending", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("pending", Find(auditItems, "resource_observations").Status);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_DoesNotPassRestartGateWhenRestartEvidenceHasNoAccountEvidence()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "partial",
            account: false,
            restart: true,
            resources: false,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        var restartGate = Find(auditItems, "restart_session");
        Assert.Equal("pending", restartGate.Status);
        Assert.Equal("No 9+ slot plan-scenario restart result recorded.", restartGate.Evidence);
    }

    [Fact]
    public void CreateAudit_DoesNotPassRestartGateWhenRestartEvidenceHasNoAccountLabel()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "partial",
            account: true,
            restart: true,
            resources: false,
            scenarioId: "groups_a_b_c_9_slot_threshold",
            accountLabel: "");
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        var restartGate = Find(auditItems, "restart_session");
        Assert.Equal("pending", restartGate.Status);
        Assert.Equal("No 9+ slot plan-scenario restart result recorded.", restartGate.Evidence);
    }

    [Fact]
    public void CreateAudit_DoesNotPassRestartGateWhenRestartEvidenceLacksRequiredGroups()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "partial",
            account: true,
            restart: true,
            resources: false,
            scenarioId: "groups_a_b_c_9_slot_threshold",
            verifiedProfileGroups: ["A", "B"]);
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        var restartGate = Find(auditItems, "restart_session");
        Assert.Equal("pending", restartGate.Status);
        Assert.Equal("No 9+ slot plan-scenario restart result recorded.", restartGate.Evidence);
    }

    [Fact]
    public void CreateAudit_RequiresFourSlotGroupAPlaybackEvidence()
    {
        var groupAOneSlot = CreateResult(
            playbackCount: 1,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_a",
            verifiedProfileGroups: ["A"]);
        var decision = new FeasibilityDecisionService().Decide([groupAOneSlot]);

        var auditItems = new FeasibilityAuditService().CreateAudit([groupAOneSlot], decision);

        var groupAGate = Find(auditItems, "group_a_playback");
        Assert.Equal("pending", groupAGate.Status);
        Assert.Equal("No 3-slot Group A only or isolated Group A result is recorded.", groupAGate.Evidence);
    }

    [Fact]
    public void CreateAudit_WithNullScenarioId_DoesNotCrashGroupAPlaybackCheck()
    {
        var result = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: null!,
            verifiedProfileGroups: ["A"]);
        var decision = new FeasibilityDecisionService().Decide([result]);

        var auditItems = new FeasibilityAuditService().CreateAudit([result], decision);

        var groupAGate = Find(auditItems, "group_a_playback");
        Assert.Equal("pending", groupAGate.Status);
        Assert.Equal("No 3-slot Group A only or isolated Group A result is recorded.", groupAGate.Evidence);
    }

    [Fact]
    public void CreateAudit_WithOnlyHigherSlotResults_DoesNotPassNineSlotThresholdGate()
    {
        var twelve = CreateResult(
            playbackCount: 12,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_12_slots");
        var sixteen = CreateResult(
            playbackCount: 16,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_d_16_slots");
        var decision = new FeasibilityDecisionService().Decide([twelve, sixteen]);

        var auditItems = new FeasibilityAuditService().CreateAudit([twelve, sixteen], decision);

        Assert.Equal("pending", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("pass", Find(auditItems, "twelve_slot_playback").Status);
        Assert.Equal("pass", Find(auditItems, "sixteen_slot_playback").Status);
        Assert.Equal("pass", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_RequiresSameAccountEvidenceAcrossAllProfileGroups()
    {
        var nine = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold",
            verifiedProfileGroups: ["A", "B", "C"]);
        var sixteen = CreateResult(
            playbackCount: 16,
            outcome: "partial",
            account: true,
            restart: true,
            resources: true,
            verifiedProfileGroups: ["A", "B", "C"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_d_16_slots");
        var decision = new FeasibilityDecisionService().Decide([nine, sixteen]);

        var auditItems = new FeasibilityAuditService().CreateAudit([nine, sixteen], decision);

        var sameAccountGate = Find(auditItems, "same_account_session");
        Assert.Equal("pending", sameAccountGate.Status);
        Assert.Contains("Missing same-account profile-group evidence for group(s): D", sameAccountGate.Evidence);
    }

    [Fact]
    public void CreateAudit_IgnoresProfileGroupEvidenceThatContradictsScenario()
    {
        var groupAWithContradictoryGroup = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_a",
            verifiedProfileGroups: ["D"]);
        var decision = new FeasibilityDecisionService().Decide([groupAWithContradictoryGroup]);

        var auditItems = new FeasibilityAuditService().CreateAudit([groupAWithContradictoryGroup], decision);

        var sameAccountGate = Find(auditItems, "same_account_session");
        Assert.Equal("pending", sameAccountGate.Status);
        Assert.Contains("Missing same-account profile-group evidence for group(s): A, B, C, D, E", sameAccountGate.Evidence);
        Assert.Contains("Covered: n/a", sameAccountGate.Evidence);
    }

    [Fact]
    public void CreateAudit_FailsPlaybackGateWhenScenarioContradictsSlotCount()
    {
        var mismatchedSixteenSlotResult = CreateResult(
            playbackCount: 16,
            outcome: "partial",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "manual_group_a",
            verifiedProfileGroups: ["A"]);
        var decision = new FeasibilityDecisionService().Decide([mismatchedSixteenSlotResult]);

        var auditItems = new FeasibilityAuditService().CreateAudit([mismatchedSixteenSlotResult], decision);

        var sixteenSlotGate = Find(auditItems, "sixteen_slot_playback");
        Assert.Equal("fail", sixteenSlotGate.Status);
        Assert.Equal("Scenario manual_group_a requires 1-3 slot(s).", sixteenSlotGate.Evidence);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_FailsExactPlaybackGateWhenScenarioDoesNotMatchPlanGate()
    {
        var customNineSlotResult = CreateResult(
            playbackCount: 9,
            outcome: "partial",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "custom_9_slot_note");
        var decision = new FeasibilityDecisionService().Decide([customNineSlotResult]);

        var auditItems = new FeasibilityAuditService().CreateAudit([customNineSlotResult], decision);

        var nineSlotGate = Find(auditItems, "nine_plus_playback");
        Assert.Equal("fail", nineSlotGate.Status);
        Assert.Equal("Plan gate requires scenario groups_a_b_c_9_slot_threshold.", nineSlotGate.Evidence);
    }

    [Fact]
    public void CreateAudit_KeepsSuccessGatePendingWhenSuccessfulNinePlusScenarioIsAmbiguous()
    {
        var customNineSlotSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "custom_9_slot_note");
        var groupD = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"]);
        var groupE = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_e",
            verifiedProfileGroups: ["E"]);
        var decision = new FeasibilityDecisionService().Decide([customNineSlotSuccess, groupD, groupE]);

        var auditItems = new FeasibilityAuditService().CreateAudit([customNineSlotSuccess, groupD, groupE], decision);

        Assert.Equal("continue_webview2_experiments", decision.Code);
        Assert.Equal("fail", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("pass", Find(auditItems, "same_account_session").Status);
        Assert.Equal("pending", Find(auditItems, "restart_session").Status);
        Assert.Equal("pending", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("pending", Find(auditItems, "resource_observations").Status);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_KeepsSuccessGatePendingWhenFailedNinePlusScenarioIsAmbiguous()
    {
        var customNineSlotFailure = CreateResult(
            playbackCount: 9,
            outcome: "failure",
            account: false,
            restart: false,
            resources: false,
            scenarioId: "custom_9_slot_note");
        var decision = new FeasibilityDecisionService().Decide([customNineSlotFailure]);

        var auditItems = new FeasibilityAuditService().CreateAudit([customNineSlotFailure], decision);

        Assert.Equal("continue_webview2_experiments", decision.Code);
        Assert.Equal("fail", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("pending", Find(auditItems, "same_account_session").Status);
        Assert.Equal("pending", Find(auditItems, "restart_session").Status);
        Assert.Equal("pending", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("pending", Find(auditItems, "resource_observations").Status);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_AggregatesSameAccountEvidenceAcrossProfileGroupResults()
    {
        var groupA = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_a",
            verifiedProfileGroups: ["A"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var groupB = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_b",
            verifiedProfileGroups: ["B"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 15, 0, TimeSpan.Zero));
        var groupC = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_c",
            verifiedProfileGroups: ["C"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero));
        var groupD = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 45, 0, TimeSpan.Zero));
        var groupE = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_e",
            verifiedProfileGroups: ["E"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero));
        var decision = new FeasibilityDecisionService().Decide([groupA, groupB, groupC, groupD, groupE]);

        var auditItems = new FeasibilityAuditService().CreateAudit([groupA, groupB, groupC, groupD, groupE], decision);

        var sameAccountGate = Find(auditItems, "same_account_session");
        Assert.Equal("pass", sameAccountGate.Status);
        Assert.Contains("groups A/B/C/D/E", sameAccountGate.Evidence);
    }

    [Fact]
    public void CreateAudit_KeepsSameAccountGatePassedWhenLatestNinePlusPartialHasNoAccountEvidence()
    {
        var groupA = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_a",
            verifiedProfileGroups: ["A"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var groupB = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_b",
            verifiedProfileGroups: ["B"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 15, 0, TimeSpan.Zero));
        var groupC = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_c",
            verifiedProfileGroups: ["C"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero));
        var groupD = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 45, 0, TimeSpan.Zero));
        var groupE = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_e",
            verifiedProfileGroups: ["E"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero));
        var playbackOnlyPartial = CreateResult(
            playbackCount: 16,
            outcome: "partial",
            account: false,
            restart: false,
            resources: false,
            scenarioId: "groups_a_b_c_d_16_slots",
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 15, 0, TimeSpan.Zero));
        var results = new[] { groupA, groupB, groupC, groupD, groupE, playbackOnlyPartial };
        var decision = new FeasibilityDecisionService().Decide(results);

        var auditItems = new FeasibilityAuditService().CreateAudit(results, decision);

        var sameAccountGate = Find(auditItems, "same_account_session");
        Assert.Equal("pass", sameAccountGate.Status);
        Assert.Contains("groups A/B/C/D/E", sameAccountGate.Evidence);
    }

    [Fact]
    public void CreateAudit_KeepsRestartAndResourceGatesPassedWhenLatestNinePlusPartialHasNoEvidence()
    {
        var thresholdSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold",
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var groupD = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 15, 0, TimeSpan.Zero));
        var groupE = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_e",
            verifiedProfileGroups: ["E"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 20, 0, TimeSpan.Zero));
        var playbackOnlyPartial = CreateResult(
            playbackCount: 16,
            outcome: "partial",
            account: false,
            restart: false,
            resources: false,
            scenarioId: "groups_a_b_c_d_16_slots",
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero));
        var decision = new FeasibilityDecisionService().Decide(
            [thresholdSuccess, groupD, groupE, playbackOnlyPartial]);

        var auditItems = new FeasibilityAuditService().CreateAudit(
            [thresholdSuccess, groupD, groupE, playbackOnlyPartial],
            decision);

        Assert.Equal("pass", Find(auditItems, "same_account_session").Status);
        Assert.Equal("pass", Find(auditItems, "restart_session").Status);
        Assert.Equal("pass", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("pass", Find(auditItems, "resource_observations").Status);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_FailsSameAccountGateWhenAccountLabelsConflict()
    {
        var thresholdSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold",
            verifiedProfileGroups: ["A", "B", "C"],
            accountLabel: "main_soop");
        var groupD = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"],
            accountLabel: "alt_soop");
        var decision = new FeasibilityDecisionService().Decide([thresholdSuccess, groupD]);

        var auditItems = new FeasibilityAuditService().CreateAudit([thresholdSuccess, groupD], decision);

        var sameAccountGate = Find(auditItems, "same_account_session");
        Assert.Equal("continue_webview2_experiments", decision.Code);
        Assert.Equal("fail", sameAccountGate.Status);
        Assert.Contains("conflicting account labels: alt_soop, main_soop", sameAccountGate.Evidence);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_KeepsSameAccountGatePendingWhenAccountLabelsAreMissing()
    {
        var thresholdSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold",
            verifiedProfileGroups: ["A", "B", "C"]);
        var groupD = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"],
            accountLabel: "");
        var groupE = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_e",
            verifiedProfileGroups: ["E"]);
        var decision = new FeasibilityDecisionService().Decide([thresholdSuccess, groupD, groupE]);

        var auditItems = new FeasibilityAuditService().CreateAudit([thresholdSuccess, groupD, groupE], decision);

        var sameAccountGate = Find(auditItems, "same_account_session");
        Assert.Equal("continue_webview2_experiments", decision.Code);
        Assert.Equal("pending", sameAccountGate.Status);
        Assert.Contains("missing account label evidence for group(s): D", sameAccountGate.Evidence);
        Assert.Equal("pending", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_UsesLatestSameAccountEvidencePerProfileGroup()
    {
        var groupA = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_a",
            verifiedProfileGroups: ["A"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var groupB = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_b",
            verifiedProfileGroups: ["B"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 15, 0, TimeSpan.Zero));
        var groupC = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_c",
            verifiedProfileGroups: ["C"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero));
        var olderGroupDPass = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 45, 0, TimeSpan.Zero));
        var newerGroupDFailure = CreateResult(
            playbackCount: 3,
            outcome: "failure",
            account: false,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero));
        var decision = new FeasibilityDecisionService().Decide(
            [groupA, groupB, groupC, olderGroupDPass, newerGroupDFailure]);

        var auditItems = new FeasibilityAuditService().CreateAudit(
            [groupA, groupB, groupC, olderGroupDPass, newerGroupDFailure],
            decision);

        var sameAccountGate = Find(auditItems, "same_account_session");
        Assert.Equal("pending", sameAccountGate.Status);
        Assert.Contains("Missing same-account profile-group evidence for group(s): D, E", sameAccountGate.Evidence);
        Assert.Contains("Covered: A/B/C", sameAccountGate.Evidence);
    }

    [Fact]
    public void CreateAudit_UsesScenarioGroupForLatestSameAccountFailureWithoutCheckedGroups()
    {
        var olderGroupDPass = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"],
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var newerGroupDFailureWithoutCheckedGroups = CreateResult(
            playbackCount: 3,
            outcome: "failure",
            account: false,
            restart: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: [],
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero));
        var decision = new FeasibilityDecisionService().Decide(
            [olderGroupDPass, newerGroupDFailureWithoutCheckedGroups]);

        var auditItems = new FeasibilityAuditService().CreateAudit(
            [olderGroupDPass, newerGroupDFailureWithoutCheckedGroups],
            decision);

        var sameAccountGate = Find(auditItems, "same_account_session");
        Assert.Equal("pending", sameAccountGate.Status);
        Assert.Contains("Missing same-account profile-group evidence for group(s): A, B, C, D, E", sameAccountGate.Evidence);
        Assert.Contains("Covered: n/a", sameAccountGate.Evidence);
    }

    [Fact]
    public void CreateAudit_UsesLatestNinePlusResultInsteadOfOlderSuccess()
    {
        var olderSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var newerFailure = CreateResult(
            playbackCount: 9,
            outcome: "failure",
            account: false,
            restart: false,
            resources: false,
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([olderSuccess, newerFailure]);

        var auditItems = new FeasibilityAuditService().CreateAudit([olderSuccess, newerFailure], decision);

        Assert.Equal("pending", Find(auditItems, "eight_plus_playback").Status);
        Assert.Equal("fail", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("fail", Find(auditItems, "same_account_session").Status);
        Assert.Equal("fail", Find(auditItems, "restart_session").Status);
        Assert.Equal("fail", Find(auditItems, "resource_acceptability").Status);
        Assert.Equal("fail", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_UsesLaterRecordedNinePlusResultWhenTimestampsMatch()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        var success = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            capturedAt: capturedAt,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var failure = CreateResult(
            playbackCount: 9,
            outcome: "failure",
            account: false,
            restart: false,
            resources: false,
            capturedAt: capturedAt,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([success, failure]);

        var auditItems = new FeasibilityAuditService().CreateAudit([success, failure], decision);

        Assert.Equal("fail", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("fail", Find(auditItems, "phase0_success_gate").Status);
    }

    [Fact]
    public void CreateAudit_WithCompletePlanCoverage_MarksAllPlanGatesPassed()
    {
        var groupA = CreateResult(
            playbackCount: 3,
            outcome: "partial",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "group_a_first_slots",
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var eight = CreateResult(
            playbackCount: 8,
            outcome: "partial",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_8_slots",
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero));
        var nine = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold",
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 45, 0, TimeSpan.Zero));
        var twelve = CreateResult(
            playbackCount: 12,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_12_slots",
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero));
        var sixteen = CreateResult(
            playbackCount: 16,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_d_16_slots",
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 30, 0, TimeSpan.Zero));
        var results = new[] { groupA, eight, nine, twelve, sixteen };
        var decision = new FeasibilityDecisionService().Decide(results);

        var auditItems = new FeasibilityAuditService().CreateAudit(results, decision);
        var summary = new FeasibilityAuditService().CreateSummary(auditItems);

        Assert.All(auditItems, item => Assert.Equal("pass", item.Status));
        Assert.Equal("pass=11, pending=0, fail=0", summary.ToCompactText());
    }

    [Fact]
    public void CreateAudit_EvaluatesPlaybackGatesFromTheirOwnLatestSlotCounts()
    {
        var eight = CreateResult(
            playbackCount: 8,
            outcome: "partial",
            account: true,
            restart: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_8_slots");
        var twelve = CreateResult(
            playbackCount: 12,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_12_slots");
        var nine = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 45, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var sixteenFailure = CreateResult(
            playbackCount: 16,
            outcome: "failure",
            account: false,
            restart: false,
            resources: false,
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero),
            scenarioId: "groups_a_b_c_d_16_slots");
        var decision = new FeasibilityDecisionService().Decide([eight, twelve, nine, sixteenFailure]);

        var auditItems = new FeasibilityAuditService().CreateAudit([eight, twelve, nine, sixteenFailure], decision);

        Assert.Equal("pass", Find(auditItems, "eight_plus_playback").Status);
        Assert.Equal("pass", Find(auditItems, "nine_plus_playback").Status);
        Assert.Equal("pass", Find(auditItems, "twelve_slot_playback").Status);
        Assert.Equal("fail", Find(auditItems, "sixteen_slot_playback").Status);
    }

    [Fact]
    public void CreateSummary_CountsAuditStatuses()
    {
        var service = new FeasibilityAuditService();
        FeasibilityAuditItem[] auditItems =
        [
            new("one", "One", "pass", ""),
            new("two", "Two", "pending", ""),
            new("three", "Three", "fail", ""),
            new("four", "Four", "PASS", "")
        ];

        var summary = service.CreateSummary(auditItems);

        Assert.Equal(2, summary.PassCount);
        Assert.Equal(1, summary.PendingCount);
        Assert.Equal(1, summary.FailCount);
        Assert.Equal("pass=2, pending=1, fail=1", summary.ToCompactText());
    }

    [Fact]
    public void CreateAuditText_IncludesDecisionSummaryAndGateDetails()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "success",
            account: true,
            restart: true,
            resources: true,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([result]);

        var text = new FeasibilityAuditService().CreateAuditText([result], decision);

        Assert.Contains("Stream Orchestra Plan Audit", text);
        Assert.Contains("Decision: WebView2 추가 실험 (continue_webview2_experiments)", text);
        Assert.Contains("Results recorded: 1", text);
        Assert.Contains("Plan audit: pass=5, pending=6, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Success gate: [pending]", text);
        Assert.Contains("[pending] SOOP 8-slot split-profile playback", text);
        Assert.Contains("[pass] SOOP 9-slot threshold playback", text);
        Assert.Contains("[pending] SOOP 12-slot playback", text);
        Assert.Contains("[pending] Phase 0 WebView2 success gate", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Contains("record --count 8 --outcome partial --account --profile-groups A,B", text);
        Assert.Contains("record --count 12 --outcome partial --account --profile-groups A,B,C", text);
        Assert.Contains("--account-label <label>", text);
        Assert.DoesNotContain("record --count 12 --outcome <success|partial|failure>", text);
    }

    [Fact]
    public void CreateSuggestedRecordShapes_DeduplicatesSharedNineSlotSuggestions()
    {
        var result = CreateResult(
            playbackCount: 9,
            outcome: "partial",
            account: true,
            restart: false,
            resources: true,
            includeStructuredResourceObservations: false,
            scenarioId: "groups_a_b_c_9_slot_threshold");
        var decision = new FeasibilityDecisionService().Decide([result]);
        var service = new FeasibilityAuditService();
        var auditItems = service.CreateAudit([result], decision);

        var suggestions = service.CreateSuggestedRecordShapes(auditItems);

        Assert.Single(suggestions, suggestion => suggestion.Contains("record --count 9 --outcome success"));
        Assert.Contains(suggestions, suggestion => suggestion.Contains("record --group D --outcome partial"));
    }

    [Fact]
    public void CreateSuggestedRecordShapes_OrdersNineSlotSuccessAfterHigherSlotPlaybackEvidence()
    {
        var decision = new FeasibilityDecisionService().Decide([]);
        var service = new FeasibilityAuditService();
        var auditItems = service.CreateAudit([], decision);

        var suggestions = service.CreateSuggestedRecordShapes(auditItems);

        var nineSuccessIndex = Array.FindIndex(
            suggestions.ToArray(),
            suggestion => suggestion.Contains("record --count 9 --outcome success", StringComparison.OrdinalIgnoreCase));
        var sixteenPlaybackIndex = Array.FindIndex(
            suggestions.ToArray(),
            suggestion => suggestion.Contains("record --count 16 --outcome partial", StringComparison.OrdinalIgnoreCase));
        var groupDAccountIndex = Array.FindIndex(
            suggestions.ToArray(),
            suggestion => suggestion.Contains("record --group D --outcome partial", StringComparison.OrdinalIgnoreCase));
        Assert.True(nineSuccessIndex > sixteenPlaybackIndex);
        Assert.True(nineSuccessIndex > groupDAccountIndex);
    }

    [Fact]
    public void CreateSuggestedRecordShapes_IncludesNineSlotPartialThresholdBeforeFinalSuccess()
    {
        var decision = new FeasibilityDecisionService().Decide([]);
        var service = new FeasibilityAuditService();
        var auditItems = service.CreateAudit([], decision);

        var suggestions = service.CreateSuggestedRecordShapes(auditItems).ToArray();

        var ninePartialIndex = Array.FindIndex(
            suggestions,
            suggestion => suggestion.Contains("record --count 9 --outcome partial", StringComparison.OrdinalIgnoreCase));
        var nineSuccessIndex = Array.FindIndex(
            suggestions,
            suggestion => suggestion.Contains("record --count 9 --outcome success", StringComparison.OrdinalIgnoreCase));
        Assert.True(ninePartialIndex >= 0);
        Assert.True(nineSuccessIndex >= 0);
        Assert.True(ninePartialIndex < nineSuccessIndex);
        Assert.Contains("--account-label <label>", suggestions[ninePartialIndex]);
    }

    public static IEnumerable<object[]> InvalidResourceObservationResults()
    {
        yield return
        [
            CreateResult(
                playbackCount: 9,
                outcome: "success",
                account: true,
                restart: true,
                resources: true,
                scenarioId: "groups_a_b_c_9_slot_threshold",
                observedCpuPercent: double.NaN)
        ];
        yield return
        [
            CreateResult(
                playbackCount: 9,
                outcome: "success",
                account: true,
                restart: true,
                resources: true,
                scenarioId: "groups_a_b_c_9_slot_threshold",
                observedGpuPercent: double.NegativeInfinity)
        ];
        yield return
        [
            CreateResult(
                playbackCount: 9,
                outcome: "success",
                account: true,
                restart: true,
                resources: true,
                scenarioId: "groups_a_b_c_9_slot_threshold",
                observedMemoryMegabytes: -1)
        ];
    }

    private static FeasibilityAuditItem Find(IReadOnlyList<FeasibilityAuditItem> auditItems, string id)
    {
        return auditItems.Single(item => item.Id == id);
    }

    private static FeasibilityTestResult CreateResult(
        int playbackCount,
        string outcome,
        bool account,
        bool restart,
        bool resources,
        bool includeStructuredResourceObservations = true,
        DateTimeOffset? capturedAt = null,
        double? observedCpuPercent = null,
        double? observedGpuPercent = null,
        double? observedMemoryMegabytes = null,
        string scenarioId = "test_scenario",
        IReadOnlyList<string>? verifiedProfileGroups = null,
        string accountLabel = "main_soop")
    {
        capturedAt ??= new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        return new FeasibilityTestResult
        {
            Id = "result_1",
            CapturedAt = capturedAt.Value,
            PlaybackCount = playbackCount,
            ScenarioId = scenarioId,
            ScenarioName = "Test Scenario",
            Outcome = outcome,
            Diagnostics = new RuntimeDiagnosticsSnapshot(
                capturedAt.Value,
                WebViewProcessCount: playbackCount,
                WebViewWorkingSetMegabytes: 1024,
                WebViewPrivateMemoryMegabytes: 800,
                WebViewCpuPercent: 30),
            IsSameAccountSessionMaintained = account,
            AccountLabel = account ? accountLabel : "",
            VerifiedProfileGroups = verifiedProfileGroups ??
                FeasibilityProfileGroupEvidenceService.GetRequiredGroupsForPlaybackCount(playbackCount),
            IsRestartSessionMaintained = restart,
            IsResourceUsageAcceptable = resources,
            ObservedCpuPercent = includeStructuredResourceObservations ? observedCpuPercent ?? 45.5 : null,
            ObservedGpuPercent = includeStructuredResourceObservations ? observedGpuPercent ?? 60 : null,
            ObservedMemoryMegabytes = includeStructuredResourceObservations ? observedMemoryMegabytes ?? 12000 : null,
            Notes = "manual test"
        };
    }
}
