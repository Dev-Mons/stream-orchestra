using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class FeasibilityDecisionServiceTests
{
    [Fact]
    public void Decide_ReturnsPendingWhenNoResultsExist()
    {
        var service = new FeasibilityDecisionService();

        var decision = service.Decide([]);

        Assert.Equal("pending", decision.Code);
        Assert.Contains("9개 이상", decision.NextAction);
    }

    [Fact]
    public void Decide_ContinuesWebView2MvpWhenPlanSuccessCriteriaAreMet()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resources: true);
        var groupD = CreateResult(
            playbackCount: 4,
            outcome: "partial",
            sameAccountSession: true,
            restartSession: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"]);

        var decision = service.Decide([result, groupD]);

        Assert.Equal("continue_webview2_mvp", decision.Code);
        Assert.Contains("WebView2 MVP", decision.NextAction);
    }

    [Fact]
    public void Decide_ContinuesExperimentsWhenSuccessfulNinePlusScenarioIsAmbiguous()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resources: true,
            scenarioId: "custom_9_slot_note");
        var groupD = CreateResult(
            playbackCount: 4,
            outcome: "partial",
            sameAccountSession: true,
            restartSession: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"]);

        var decision = service.Decide([result, groupD]);

        Assert.Equal("continue_webview2_experiments", decision.Code);
    }

    [Fact]
    public void Decide_ContinuesExperimentsWhenSameAccountEvidenceDoesNotCoverAllPlanGroups()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resources: true);

        var decision = service.Decide([result]);

        Assert.Equal("continue_webview2_experiments", decision.Code);
        Assert.Contains("성공 기준 전체", decision.Detail);
    }

    [Fact]
    public void Decide_ContinuesExperimentsWhenSameAccountLabelsConflict()
    {
        var service = new FeasibilityDecisionService();
        var thresholdSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resources: true,
            accountLabel: "main_soop");
        var groupD = CreateResult(
            playbackCount: 4,
            outcome: "partial",
            sameAccountSession: true,
            restartSession: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"],
            accountLabel: "alt_soop");

        var decision = service.Decide([thresholdSuccess, groupD]);

        Assert.Equal("continue_webview2_experiments", decision.Code);
        Assert.Contains("성공 기준 전체", decision.Detail);
    }

    [Fact]
    public void Decide_ContinuesExperimentsWhenLatestGroupEvidenceContradictsOlderCoverage()
    {
        var service = new FeasibilityDecisionService();
        var thresholdSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var olderGroupDPass = CreateResult(
            playbackCount: 4,
            outcome: "partial",
            sameAccountSession: true,
            restartSession: false,
            resources: false,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"]);
        var newerGroupDFailure = CreateResult(
            playbackCount: 4,
            outcome: "failure",
            sameAccountSession: false,
            restartSession: false,
            resources: false,
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero),
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"]);

        var decision = service.Decide([thresholdSuccess, olderGroupDPass, newerGroupDFailure]);

        Assert.Equal("continue_webview2_experiments", decision.Code);
    }

    [Fact]
    public void Decide_ContinuesExperimentsWhenPlaybackWorksButSessionCriterionIsMissing()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 12,
            outcome: "success",
            sameAccountSession: true,
            restartSession: false,
            resources: true);

        var decision = service.Decide([result]);

        Assert.Equal("continue_webview2_experiments", decision.Code);
    }

    [Fact]
    public void Decide_ContinuesExperimentsWhenSuccessLacksStructuredResourceObservations()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resources: true,
            includeStructuredResourceObservations: false);

        var decision = service.Decide([result]);

        Assert.Equal("continue_webview2_experiments", decision.Code);
    }

    [Theory]
    [MemberData(nameof(InvalidResourceObservationResults))]
    public void Decide_ContinuesExperimentsWhenSuccessHasInvalidResourceObservations(FeasibilityTestResult result)
    {
        var service = new FeasibilityDecisionService();

        var decision = service.Decide([result]);

        Assert.Equal("continue_webview2_experiments", decision.Code);
    }

    [Fact]
    public void Decide_ContinuesExperimentsWhenOnlyPartialResultExists()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 8,
            outcome: "partial",
            sameAccountSession: true,
            restartSession: true,
            resources: true);

        var decision = service.Decide([result]);

        Assert.Equal("continue_webview2_experiments", decision.Code);
    }

    [Fact]
    public void Decide_SwitchesToExternalBrowserWhenLatestNinePlusResultFails()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 16,
            outcome: "failure",
            sameAccountSession: false,
            restartSession: false,
            resources: false);

        var decision = service.Decide([result]);

        Assert.Equal("switch_external_browser", decision.Code);
        Assert.Contains("fallback", decision.NextAction);
    }

    [Fact]
    public void Decide_ContinuesExperimentsWhenFailedNinePlusScenarioIsAmbiguous()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 9,
            outcome: "failure",
            sameAccountSession: false,
            restartSession: false,
            resources: false,
            scenarioId: "custom_9_slot_note");

        var decision = service.Decide([result]);

        Assert.Equal("continue_webview2_experiments", decision.Code);
        Assert.Contains("계획 시나리오", decision.Detail);
    }

    [Fact]
    public void Decide_ReturnsPendingWhenOnlyRecordedResultsHaveScenarioCountMismatch()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 16,
            outcome: "failure",
            sameAccountSession: false,
            restartSession: false,
            resources: false,
            scenarioId: "manual_group_a");

        var decision = service.Decide([result]);

        Assert.Equal("pending", decision.Code);
        Assert.Contains("시나리오와 슬롯 수", decision.Detail);
    }

    [Fact]
    public void Decide_ReturnsPendingWhenLatestNinePlusResultHasInvalidOutcome()
    {
        var service = new FeasibilityDecisionService();
        var olderSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var newerMalformedResult = CreateResult(
            playbackCount: 9,
            outcome: "",
            sameAccountSession: true,
            restartSession: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero));

        var decision = service.Decide([olderSuccess, newerMalformedResult]);

        Assert.Equal("pending", decision.Code);
        Assert.Contains("outcome", decision.Detail);
    }

    [Fact]
    public void Decide_ReturnsPendingWhenOnlyRecordedResultsHaveInvalidOutcomes()
    {
        var service = new FeasibilityDecisionService();
        var result = CreateResult(
            playbackCount: 4,
            outcome: "",
            sameAccountSession: false,
            restartSession: false,
            resources: false);

        var decision = service.Decide([result]);

        Assert.Equal("pending", decision.Code);
        Assert.Contains("outcome", decision.Detail);
    }

    [Fact]
    public void Decide_SwitchesToExternalBrowserWhenNewerNinePlusFailureContradictsOlderSuccess()
    {
        var service = new FeasibilityDecisionService();
        var olderSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var newerFailure = CreateResult(
            playbackCount: 9,
            outcome: "failure",
            sameAccountSession: false,
            restartSession: false,
            resources: false,
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero));

        var decision = service.Decide([olderSuccess, newerFailure]);

        Assert.Equal("switch_external_browser", decision.Code);
    }

    [Fact]
    public void Decide_ContinuesWebView2MvpWhenNewerNinePlusSuccessSupersedesOlderFailure()
    {
        var service = new FeasibilityDecisionService();
        var olderFailure = CreateResult(
            playbackCount: 9,
            outcome: "failure",
            sameAccountSession: false,
            restartSession: false,
            resources: false,
            capturedAt: new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var newerSuccess = CreateResult(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resources: true,
            capturedAt: new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero));
        var groupD = CreateResult(
            playbackCount: 4,
            outcome: "partial",
            sameAccountSession: true,
            restartSession: false,
            resources: false,
            scenarioId: "isolated_group_d",
            verifiedProfileGroups: ["D"]);

        var decision = service.Decide([olderFailure, newerSuccess, groupD]);

        Assert.Equal("continue_webview2_mvp", decision.Code);
    }

    public static IEnumerable<object[]> InvalidResourceObservationResults()
    {
        yield return
        [
            CreateResult(
                playbackCount: 9,
                outcome: "success",
                sameAccountSession: true,
                restartSession: true,
                resources: true,
                observedCpuPercent: double.NaN)
        ];
        yield return
        [
            CreateResult(
                playbackCount: 9,
                outcome: "success",
                sameAccountSession: true,
                restartSession: true,
                resources: true,
                observedGpuPercent: double.PositiveInfinity)
        ];
        yield return
        [
            CreateResult(
                playbackCount: 9,
                outcome: "success",
                sameAccountSession: true,
                restartSession: true,
                resources: true,
                observedMemoryMegabytes: -1)
        ];
    }

    private static FeasibilityTestResult CreateResult(
        int playbackCount,
        string outcome,
        bool sameAccountSession,
        bool restartSession,
        bool resources,
        bool includeStructuredResourceObservations = true,
        DateTimeOffset? capturedAt = null,
        double? observedCpuPercent = null,
        double? observedGpuPercent = null,
        double? observedMemoryMegabytes = null,
        string? scenarioId = null,
        IReadOnlyList<string>? verifiedProfileGroups = null,
        string accountLabel = "")
    {
        capturedAt ??= new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        scenarioId ??= playbackCount switch
        {
            <= 4 => "group_a_first_slots",
            8 => "groups_a_b_8_slots",
            9 => "groups_a_b_c_9_slot_threshold",
            12 => "groups_a_b_c_12_slots",
            16 => "groups_a_b_c_d_16_slots",
            _ => "test_scenario"
        };

        return new FeasibilityTestResult
        {
            Id = Guid.NewGuid().ToString("N"),
            CapturedAt = capturedAt.Value,
            PlaybackCount = playbackCount,
            ScenarioId = scenarioId,
            Outcome = outcome,
            Diagnostics = new RuntimeDiagnosticsSnapshot(
                capturedAt.Value,
                WebViewProcessCount: playbackCount,
                WebViewWorkingSetMegabytes: 1024,
                WebViewPrivateMemoryMegabytes: 800,
                WebViewCpuPercent: 30),
            IsSameAccountSessionMaintained = sameAccountSession,
            AccountLabel = accountLabel,
            VerifiedProfileGroups = verifiedProfileGroups ??
                FeasibilityProfileGroupEvidenceService.GetRequiredGroupsForPlaybackCount(playbackCount),
            IsRestartSessionMaintained = restartSession,
            IsResourceUsageAcceptable = resources,
            ObservedCpuPercent = includeStructuredResourceObservations ? observedCpuPercent ?? 45.5 : null,
            ObservedGpuPercent = includeStructuredResourceObservations ? observedGpuPercent ?? 60 : null,
            ObservedMemoryMegabytes = includeStructuredResourceObservations ? observedMemoryMegabytes ?? 12000 : null
        };
    }
}
