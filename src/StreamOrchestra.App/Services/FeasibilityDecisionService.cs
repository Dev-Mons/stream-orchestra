using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class FeasibilityDecisionService
{
    private static readonly string[] PlanRequiredProfileGroups = ["A", "B", "C", "D"];

    public FeasibilityDecision Decide(IReadOnlyList<FeasibilityTestResult> results)
    {
        if (results.Count == 0)
        {
            return new FeasibilityDecision(
                "pending",
                "검증 대기",
                "SOOP 동시 재생 결과가 아직 기록되지 않았습니다.",
                "SOOP에서 9개 이상 재생 테스트를 실행하고 계정/재실행/리소스 증거와 함께 결과를 기록하세요.");
        }

        var consistentResults = results
            .Where(FeasibilityScenarioService.IsPlaybackCountConsistent)
            .ToArray();
        if (consistentResults.Length == 0)
        {
            return new FeasibilityDecision(
                "pending",
                "검증 대기",
                "기록된 결과의 시나리오와 슬롯 수가 일치하지 않습니다.",
                "시나리오와 슬롯 수가 일치하도록 SOOP 테스트 결과를 다시 기록하세요.");
        }

        var latestNinePlusResult = consistentResults
            .Where(result => result.PlaybackCount >= 9)
            .OrderByDescending(result => result.CapturedAt)
            .FirstOrDefault();

        if (latestNinePlusResult is not null)
        {
            if (!FeasibilityOutcomeService.IsKnown(latestNinePlusResult))
            {
                return new FeasibilityDecision(
                    "pending",
                    "검증 대기",
                    "최근 9개 이상 테스트 결과의 outcome이 success, partial, failure 중 하나가 아닙니다.",
                    "SOOP 테스트 결과를 유효한 outcome으로 다시 기록하세요.");
            }

            if (IsSuccessfulEmbeddedWebView2Result(latestNinePlusResult) &&
                HasSameAccountEvidenceForAllPlanGroups(consistentResults))
            {
                return new FeasibilityDecision(
                    "continue_webview2_mvp",
                    "WebView2 MVP 계속",
                    $"{latestNinePlusResult.PlaybackCount}개 재생, 세션 유지, 리소스 조건이 충족되었습니다.",
                "내장 WebView2 MVP 경로를 계속 진행하세요.");
            }

            if (FeasibilityOutcomeService.IsFailure(latestNinePlusResult) &&
                FeasibilityScenarioService.IsPlanNinePlusPlaybackScenario(latestNinePlusResult))
            {
                return new FeasibilityDecision(
                    "switch_external_browser",
                    "외부 브라우저 제어 검토",
                    $"최근 {latestNinePlusResult.PlaybackCount}개 테스트가 실패했습니다. Chrome/Edge/Whale/Brave/Vivaldi 제어 방식으로 전환하는 경로를 검토하세요.",
                    "WPF의 브라우저 스크립트 버튼 또는 `StreamOrchestra.Tools fallback`으로 외부 브라우저 전환 스크립트를 내보내세요.");
            }

            if (FeasibilityOutcomeService.IsFailure(latestNinePlusResult))
            {
                return new FeasibilityDecision(
                    "continue_webview2_experiments",
                    "WebView2 추가 실험",
                    $"최근 {latestNinePlusResult.PlaybackCount}개 실패 기록이 Phase 0 계획 시나리오와 일치하지 않습니다.",
                    "계획 시나리오의 9/12/16개 테스트를 다시 실행하거나, 실패가 재현되면 외부 브라우저 fallback을 내보내세요.");
            }

            if (FeasibilityOutcomeService.IsSuccess(latestNinePlusResult) ||
                FeasibilityOutcomeService.IsPartial(latestNinePlusResult))
            {
                return new FeasibilityDecision(
                    "continue_webview2_experiments",
                    "WebView2 추가 실험",
                    $"최근 {latestNinePlusResult.PlaybackCount}개 테스트에서 성공 기준 전체가 충족되지 않았습니다.",
                    "프로필 그룹/레이아웃 조건을 조정해 다시 테스트하거나, 실패가 재현되면 외부 브라우저 fallback을 내보내세요.");
            }
        }

        var bestPlayableResult = consistentResults
            .Where(result => FeasibilityOutcomeService.IsSuccess(result) ||
                FeasibilityOutcomeService.IsPartial(result))
            .OrderByDescending(result => result.PlaybackCount)
            .ThenByDescending(result => result.CapturedAt)
            .FirstOrDefault();

        if (bestPlayableResult is not null)
        {
            return new FeasibilityDecision(
                "continue_webview2_experiments",
                "WebView2 추가 실험",
                $"{bestPlayableResult.PlaybackCount}개까지 가능성이 있으나 성공 기준 전체가 충족되지 않았습니다.",
                "9개 이상 테스트를 다시 실행하고 누락된 계정/재실행/리소스 증거를 기록하세요.");
        }

        if (consistentResults.All(result => !FeasibilityOutcomeService.IsKnown(result)))
        {
            return new FeasibilityDecision(
                "pending",
                "검증 대기",
                "기록된 결과의 outcome이 success, partial, failure 중 하나가 아닙니다.",
                "SOOP 테스트 결과를 유효한 outcome으로 다시 기록하세요.");
        }

        return new FeasibilityDecision(
            "switch_external_browser",
            "외부 브라우저 제어 검토",
            "기록된 결과에서 WebView2 프로필 분리 효과를 확인하지 못했습니다.",
            "WPF의 브라우저 스크립트 버튼 또는 `StreamOrchestra.Tools fallback`으로 외부 브라우저 전환 스크립트를 내보내세요.");
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

    private static bool HasStructuredResourceObservation(FeasibilityTestResult result)
    {
        return FeasibilityResourceObservationService.HasCompleteValidObservation(result);
    }

    private static bool HasSameAccountEvidenceForAllPlanGroups(IReadOnlyList<FeasibilityTestResult> results)
    {
        var coveredGroups = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountCoveredGroups(results);
        var coveredGroupSet = coveredGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return PlanRequiredProfileGroups.All(coveredGroupSet.Contains);
    }
}
