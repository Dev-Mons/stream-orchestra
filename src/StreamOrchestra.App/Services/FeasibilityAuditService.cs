using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class FeasibilityAuditService
{
    private static readonly string[] PlanRequiredProfileGroups = ["A", "B", "C", "D"];
    private const int GroupAPlanPlaybackCount = 4;

    public string CreateAuditText(
        IReadOnlyList<FeasibilityTestResult> results,
        FeasibilityDecision decision)
    {
        var auditItems = CreateAudit(results, decision);
        var summary = CreateSummary(auditItems);
        var lines = new List<string>
        {
            "Stream Orchestra Plan Audit",
            $"Decision: {decision.Title} ({decision.Code})",
            $"Next action: {decision.NextAction}",
            $"Results recorded: {results.Count}",
            $"Plan audit: {summary.ToCompactText()}",
            $"Plan verification: [{CreatePlanVerificationStatus(auditItems)}]"
        };

        var successGate = auditItems.FirstOrDefault(item => item.Id == "phase0_success_gate");
        if (successGate is not null)
        {
            lines.Add($"Success gate: [{successGate.Status}] {successGate.Evidence}");
        }

        lines.AddRange(auditItems.Select(item => $"[{item.Status}] {item.Title}: {item.Evidence}"));
        var suggestedRecordShapes = CreateSuggestedRecordShapes(auditItems);
        if (suggestedRecordShapes.Count > 0)
        {
            lines.Add("Suggested record shapes:");
            lines.AddRange(suggestedRecordShapes.Select(suggestion => $"- {suggestion}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public IReadOnlyList<string> CreateSuggestedRecordShapes(IReadOnlyList<FeasibilityAuditItem> auditItems)
    {
        return auditItems
            .Where(item => !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase))
            .SelectMany(CreateSuggestedRecordShapes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public FeasibilityAuditSummary CreateSummary(IReadOnlyList<FeasibilityAuditItem> auditItems)
    {
        return new FeasibilityAuditSummary(
            CountStatus(auditItems, "pass"),
            CountStatus(auditItems, "pending"),
            CountStatus(auditItems, "fail"));
    }

    public string CreatePlanVerificationStatus(IReadOnlyList<FeasibilityAuditItem> auditItems)
    {
        return CreatePlanVerificationStatusCore(auditItems);
    }

    public IReadOnlyList<FeasibilityAuditItem> CreateAudit(
        IReadOnlyList<FeasibilityTestResult> results,
        FeasibilityDecision decision)
    {
        var ninePlusResults = results
            .Where(result => result.PlaybackCount >= 9 &&
                FeasibilityScenarioService.IsPlaybackCountConsistent(result))
            .ToArray();
        var latestNinePlusResult = ninePlusResults
            .OrderByDescending(result => result.CapturedAt)
            .FirstOrDefault();
        var latestPlanNinePlusResult = ninePlusResults
            .Where(FeasibilityScenarioService.IsPlanNinePlusPlaybackScenario)
            .OrderByDescending(result => result.CapturedAt)
            .FirstOrDefault();
        var latestNinePlusIsFailure = latestNinePlusResult is not null &&
            FeasibilityOutcomeService.IsFailure(latestNinePlusResult) &&
            FeasibilityScenarioService.IsPlanNinePlusPlaybackScenario(latestNinePlusResult);
        var latestNinePlusIsSuccessful = latestNinePlusResult is not null &&
            IsSuccessfulEmbeddedWebView2Result(latestNinePlusResult) &&
            HasSameAccountEvidenceForAllPlanGroups(results);

        return
        [
            new FeasibilityAuditItem(
                "manual_result_recorded",
                "Manual feasibility result recorded",
                results.Count > 0 ? "pass" : "pending",
                results.Count > 0
                    ? $"{results.Count} result(s) recorded."
                    : "No feasibility result has been recorded."),
            CreateGroupAPlaybackAuditItem(results),
            CreateExactPlaybackAuditItem(
                results,
                "eight_plus_playback",
                "SOOP 8-slot split-profile playback",
                8,
                "groups_a_b_8_slots",
                "No 8-slot result is recorded."),
            CreateExactPlaybackAuditItem(
                results,
                "nine_plus_playback",
                "SOOP 9-slot threshold playback",
                9,
                "groups_a_b_c_9_slot_threshold",
                "No 9-slot threshold result is recorded."),
            CreateExactPlaybackAuditItem(
                results,
                "twelve_slot_playback",
                "SOOP 12-slot playback",
                12,
                "groups_a_b_c_12_slots",
                "No 12-slot result is recorded."),
            CreateExactPlaybackAuditItem(
                results,
                "sixteen_slot_playback",
                "SOOP 16-slot playback",
                16,
                "groups_a_b_c_d_16_slots",
                "No 16-slot result is recorded."),
            CreateSameAccountSessionAuditItem(results, latestPlanNinePlusResult),
            CreateLatestNinePlusBooleanAuditItem(
                "restart_session",
                "App restart keeps login session",
                latestPlanNinePlusResult,
                result => result.IsRestartSessionMaintained,
                "No 9+ slot result has restart=True.",
                "No 9+ slot plan-scenario restart result recorded."),
            CreateLatestNinePlusBooleanAuditItem(
                "resource_acceptability",
                "CPU/GPU/memory acceptable",
                latestPlanNinePlusResult,
                result => result.IsResourceUsageAcceptable,
                "No 9+ slot result has resources=True.",
                "No 9+ slot plan-scenario resource acceptability result recorded."),
            new FeasibilityAuditItem(
                "resource_observations",
                "Structured resource observations captured",
                latestPlanNinePlusResult is not null &&
                    FeasibilityOutcomeService.IsKnown(latestPlanNinePlusResult) &&
                    HasStructuredResourceObservation(latestPlanNinePlusResult) ? "pass" : "pending",
                latestPlanNinePlusResult is not null &&
                    FeasibilityOutcomeService.IsKnown(latestPlanNinePlusResult) &&
                    HasStructuredResourceObservation(latestPlanNinePlusResult)
                    ? FormatResourceEvidence(latestPlanNinePlusResult)
                    : latestPlanNinePlusResult is not null &&
                        !FeasibilityOutcomeService.IsKnown(latestPlanNinePlusResult)
                            ? "Latest 9+ slot result has invalid outcome."
                            : "Record CPU %, GPU %, and memory MB from the latest 9+ slot plan-scenario test."),
            new FeasibilityAuditItem(
                "phase0_success_gate",
                "Phase 0 WebView2 success gate",
                latestNinePlusIsSuccessful
                    ? "pass"
                    : latestNinePlusIsFailure || decision.Code.Equals("switch_external_browser", StringComparison.OrdinalIgnoreCase)
                        ? "fail"
                        : "pending",
                latestNinePlusIsSuccessful
                    ? FormatResultEvidence(latestNinePlusResult!)
            : $"Decision: {decision.Title} ({decision.Code}). {decision.Detail}")
        ];
    }

    private static FeasibilityAuditItem CreateGroupAPlaybackAuditItem(IReadOnlyList<FeasibilityTestResult> results)
    {
        var latestGroupAResult = results
            .Where(result => result.PlaybackCount == GroupAPlanPlaybackCount)
            .Where(IsGroupAPlaybackResult)
            .OrderByDescending(result => result.CapturedAt)
            .FirstOrDefault();
        var consistencyError = latestGroupAResult is null
            ? null
            : FeasibilityScenarioService.ValidatePlaybackCountConsistency(
                latestGroupAResult.PlaybackCount,
                latestGroupAResult.ScenarioId);

        return new FeasibilityAuditItem(
            "group_a_playback",
            "Group A 4-slot single-profile playback tested",
            latestGroupAResult is null
                ? "pending"
                : consistencyError is not null
                    ? "fail"
                    : IsSuccessOrPartial(latestGroupAResult)
                        ? "pass"
                        : IsFailure(latestGroupAResult)
                            ? "fail"
                            : "pending",
            latestGroupAResult is null
                ? "No 4-slot Group A only or isolated Group A result is recorded."
                : consistencyError is not null
                    ? consistencyError
                    : IsFailure(latestGroupAResult)
                        ? $"Latest Group A attempt failed: {FormatResultEvidence(latestGroupAResult)}"
                        : FormatResultEvidence(latestGroupAResult));
    }

    private static FeasibilityAuditItem CreateSameAccountSessionAuditItem(
        IReadOnlyList<FeasibilityTestResult> results,
        FeasibilityTestResult? latestNinePlusResult)
    {
        const string id = "same_account_session";
        const string title = "Same SOOP account session persists across A-D";

        if (latestNinePlusResult is not null &&
            FeasibilityOutcomeService.IsKnown(latestNinePlusResult) &&
            !latestNinePlusResult.IsSameAccountSessionMaintained)
        {
            return new FeasibilityAuditItem(
                id,
                title,
                "fail",
                "Latest 9+ slot result has account=False.");
        }

        var coveredGroups = GetCoveredSameAccountProfileGroups(results);
        var accountLabels = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountAccountLabels(results);
        if (accountLabels.Count > 1)
        {
            return new FeasibilityAuditItem(
                id,
                title,
                "fail",
                $"Same-account evidence has conflicting account labels: {string.Join(", ", accountLabels)}.");
        }

        var missingGroups = PlanRequiredProfileGroups
            .Where(group => !coveredGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missingGroups.Length == 0)
        {
            return new FeasibilityAuditItem(
                id,
                title,
                "pass",
                accountLabels.Count == 0
                    ? $"Same-account evidence covers groups {string.Join("/", PlanRequiredProfileGroups)}."
                    : $"Same-account evidence covers groups {string.Join("/", PlanRequiredProfileGroups)} with account label {accountLabels[0]}.");
        }

        var coveredText = coveredGroups.Count == 0
            ? "n/a"
            : string.Join("/", coveredGroups);

        return new FeasibilityAuditItem(
            id,
            title,
            "pending",
            $"Missing same-account profile-group evidence for group(s): {string.Join(", ", missingGroups)}. Covered: {coveredText}.");
    }

    private static FeasibilityAuditItem CreateLatestNinePlusBooleanAuditItem(
        string id,
        string title,
        FeasibilityTestResult? latestNinePlusResult,
        Func<FeasibilityTestResult, bool> predicate,
        string failEvidence,
        string pendingEvidence)
    {
        if (latestNinePlusResult is null)
        {
            return new FeasibilityAuditItem(id, title, "pending", pendingEvidence);
        }

        if (!FeasibilityOutcomeService.IsKnown(latestNinePlusResult))
        {
            return new FeasibilityAuditItem(
                id,
                title,
                "pending",
                "Latest 9+ slot result has invalid outcome.");
        }

        return predicate(latestNinePlusResult)
            ? new FeasibilityAuditItem(id, title, "pass", FormatResultEvidence(latestNinePlusResult))
            : new FeasibilityAuditItem(id, title, "fail", failEvidence);
    }

    private static FeasibilityAuditItem CreateExactPlaybackAuditItem(
        IReadOnlyList<FeasibilityTestResult> results,
        string id,
        string title,
        int playbackCount,
        string expectedScenarioId,
        string pendingEvidence)
    {
        var latestRelevantResult = results
            .Where(result => result.PlaybackCount == playbackCount)
            .OrderByDescending(result => result.CapturedAt)
            .FirstOrDefault();
        var consistencyError = latestRelevantResult is null
            ? null
            : ValidateExactPlaybackGateScenario(latestRelevantResult, expectedScenarioId);

        return new FeasibilityAuditItem(
            id,
            title,
            latestRelevantResult is null
                ? "pending"
                : consistencyError is not null
                    ? "fail"
                    : FeasibilityOutcomeService.IsSuccess(latestRelevantResult) ||
                        FeasibilityOutcomeService.IsPartial(latestRelevantResult)
                        ? "pass"
                        : FeasibilityOutcomeService.IsFailure(latestRelevantResult)
                            ? "fail"
                            : "pending",
            latestRelevantResult is null
                ? pendingEvidence
                : consistencyError is not null
                    ? consistencyError
                    : FeasibilityOutcomeService.IsFailure(latestRelevantResult)
                        ? $"Latest {playbackCount}-slot attempt failed: {FormatResultEvidence(latestRelevantResult)}"
                        : FormatResultEvidence(latestRelevantResult));
    }

    private static string? ValidateExactPlaybackGateScenario(
        FeasibilityTestResult result,
        string expectedScenarioId)
    {
        var consistencyError = FeasibilityScenarioService.ValidatePlaybackCountConsistency(
            result.PlaybackCount,
            result.ScenarioId);
        if (consistencyError is not null)
        {
            return consistencyError;
        }

        if (string.Equals(result.ScenarioId?.Trim(), expectedScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"Plan gate requires scenario {expectedScenarioId}.";
    }

    private static IReadOnlyList<string> GetCoveredSameAccountProfileGroups(IReadOnlyList<FeasibilityTestResult> results)
    {
        return FeasibilityProfileGroupEvidenceService.GetLatestSameAccountCoveredGroups(results);
    }

    private static bool HasSameAccountEvidenceForAllPlanGroups(IReadOnlyList<FeasibilityTestResult> results)
    {
        var coveredGroups = GetCoveredSameAccountProfileGroups(results);
        var coveredGroupSet = coveredGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return PlanRequiredProfileGroups.All(coveredGroupSet.Contains) &&
            !FeasibilityProfileGroupEvidenceService.HasConflictingSameAccountLabels(results);
    }

    private static bool IsSuccessfulEmbeddedWebView2Result(FeasibilityTestResult result)
    {
        return FeasibilityOutcomeService.IsSuccess(result) &&
            FeasibilityScenarioService.IsPlanNinePlusPlaybackScenario(result) &&
            result.PlaybackCount >= 9 &&
            result.IsSameAccountSessionMaintained &&
            FeasibilityProfileGroupEvidenceService.HasRequiredGroups(result.PlaybackCount, result.VerifiedProfileGroups) &&
            result.IsRestartSessionMaintained &&
            result.IsResourceUsageAcceptable &&
            HasStructuredResourceObservation(result);
    }

    private static bool IsSuccessOrPartial(FeasibilityTestResult result)
    {
        return FeasibilityOutcomeService.IsSuccess(result) ||
            FeasibilityOutcomeService.IsPartial(result);
    }

    private static bool IsFailure(FeasibilityTestResult result)
    {
        return FeasibilityOutcomeService.IsFailure(result);
    }

    private static bool IsGroupAPlaybackResult(FeasibilityTestResult result)
    {
        return string.Equals(result.ScenarioId, "group_a_first_slots", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.ScenarioId, "isolated_group_a", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.ScenarioId, "manual_group_a", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasStructuredResourceObservation(FeasibilityTestResult result)
    {
        return FeasibilityResourceObservationService.HasCompleteValidObservation(result);
    }

    private static int CountStatus(IReadOnlyList<FeasibilityAuditItem> auditItems, string status)
    {
        return auditItems.Count(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreatePlanVerificationStatusCore(IReadOnlyList<FeasibilityAuditItem> auditItems)
    {
        if (auditItems.Count == 0)
        {
            return "pending";
        }

        if (auditItems.Any(item => item.Status.Equals("fail", StringComparison.OrdinalIgnoreCase)))
        {
            return "fail";
        }

        return auditItems.All(item => item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase))
            ? "pass"
            : "pending";
    }

    private static string FormatResultEvidence(FeasibilityTestResult result)
    {
        return $"{result.Outcome}, {result.PlaybackCount} slot(s), {result.ScenarioName} ({result.ScenarioId}), groups={FeasibilityProfileGroupEvidenceService.FormatGroups(result.VerifiedProfileGroups)}, account={FormatAccountLabel(result.AccountLabel)}, {result.CapturedAt:yyyy-MM-dd HH:mm:ss}";
    }

    private static string FormatResourceEvidence(FeasibilityTestResult result)
    {
        return $"cpu={result.ObservedCpuPercent!.Value:0.##}%, gpu={result.ObservedGpuPercent!.Value:0.##}%, memory={result.ObservedMemoryMegabytes!.Value:0.##} MB from {result.ScenarioName}.";
    }

    private static string FormatAccountLabel(string? accountLabel)
    {
        return string.IsNullOrWhiteSpace(accountLabel) ? "n/a" : accountLabel.Trim();
    }

    private static IEnumerable<string> CreateSuggestedRecordShapes(FeasibilityAuditItem auditItem)
    {
        return auditItem.Id switch
        {
            "group_a_playback" =>
            [
                "record --group A --outcome partial --account --profile-groups A --account-label <label> --notes \"Group A isolated SOOP test\""
            ],
            "eight_plus_playback" =>
            [
                "record --count 8 --outcome partial --account --profile-groups A,B --account-label <label> --notes \"8-slot SOOP playback\""
            ],
            "nine_plus_playback" =>
            [
                CreateNineSlotSuccessRecordShape()
            ],
            "twelve_slot_playback" =>
            [
                "record --count 12 --outcome partial --account --profile-groups A,B,C --account-label <label> --notes \"12-slot SOOP playback\""
            ],
            "sixteen_slot_playback" =>
            [
                "record --count 16 --outcome partial --account --profile-groups A,B,C,D --account-label <label> --notes \"16-slot SOOP playback\""
            ],
            "same_account_session" =>
            [
                "record --group A --outcome partial --account --profile-groups A --account-label <label> --notes \"Group A same-account check\"",
                "record --group B --outcome partial --account --profile-groups B --account-label <label> --notes \"Group B same-account check\"",
                "record --group C --outcome partial --account --profile-groups C --account-label <label> --notes \"Group C same-account check\"",
                "record --group D --outcome partial --account --profile-groups D --account-label <label> --notes \"Group D same-account check\""
            ],
            "restart_session" or "resource_acceptability" or "resource_observations" or "phase0_success_gate" =>
            [
                CreateNineSlotSuccessRecordShape()
            ],
            _ => []
        };
    }

    private static string CreateNineSlotSuccessRecordShape()
    {
        return "record --count 9 --outcome success --account --profile-groups A,B,C --restart --resources --cpu-percent <0-100> --gpu-percent <0-100> --memory-mb <value> --account-label <label> --notes \"9-slot SOOP threshold\"";
    }
}
