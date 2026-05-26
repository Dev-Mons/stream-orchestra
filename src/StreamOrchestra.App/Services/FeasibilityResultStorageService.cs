using System.IO;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class FeasibilityResultStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FeasibilityResultStorageService(string? dataFolder = null)
    {
        DataFolder = ResolveDataFolder(dataFolder);

        Directory.CreateDirectory(DataFolder);
    }

    public string DataFolder { get; }

    public string ResultsFilePath => Path.Combine(DataFolder, "feasibility-results.json");

    public static string ResolveDataFolder(string? dataFolder)
    {
        return dataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamOrchestra",
            "Data");
    }

    public static string GetResultsFilePath(string dataFolder)
    {
        return Path.Combine(dataFolder, "feasibility-results.json");
    }

    public IReadOnlyList<FeasibilityTestResult> LoadResults()
    {
        return NormalizeResults(JsonFileStorage.LoadList<FeasibilityTestResult>(ResultsFilePath, SerializerOptions));
    }

    public void SaveResults(IReadOnlyList<FeasibilityTestResult> results)
    {
        JsonFileStorage.Save(ResultsFilePath, NormalizeResults(results), SerializerOptions);
    }

    public void AppendResult(FeasibilityTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var results = LoadResults().ToList();
        results.Add(result);
        SaveResults(results);
    }

    public static void ApplyDecisionSnapshot(FeasibilityTestResult result, FeasibilityDecision decision)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(decision);

        result.DecisionCode = decision.Code;
        result.DecisionTitle = decision.Title;
        result.DecisionDetail = decision.Detail;
        result.DecisionNextAction = decision.NextAction;
    }

    public static string CreateResultId(DateTimeOffset capturedAt, int playbackCount, string? outcome)
    {
        var normalizedCharacters = (outcome ?? "").Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();
        var normalizedOutcome = string.Join(
            "_",
            new string(normalizedCharacters).Split('_', StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(normalizedOutcome))
        {
            normalizedOutcome = "unknown";
        }

        return $"feasibility_{capturedAt:yyyyMMdd_HHmmss}_{playbackCount}_{normalizedOutcome}";
    }

    public static IReadOnlyList<FeasibilityTestResult> NormalizeResults(IReadOnlyList<FeasibilityTestResult>? results)
    {
        IEnumerable<FeasibilityTestResult?> sourceResults = results ?? [];
        var normalizedResults = new List<FeasibilityTestResult>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in sourceResults)
        {
            var normalizedResult = NormalizeResult(result, usedIds);
            if (normalizedResult is not null)
            {
                normalizedResults.Add(normalizedResult);
            }
        }

        return normalizedResults;
    }

    private static FeasibilityTestResult? NormalizeResult(
        FeasibilityTestResult? result,
        HashSet<string> usedIds)
    {
        if (result is null || result.PlaybackCount is < 1 or > PlaybackTestPlanService.MaxSlotCount)
        {
            return null;
        }

        var diagnostics = NormalizeDiagnostics(result.Diagnostics, result.CapturedAt);
        var rawOutcome = result.Outcome?.Trim() ?? "";
        var canonicalOutcome = NormalizeKnownOutcome(rawOutcome);
        var normalizedScenarioId = string.IsNullOrWhiteSpace(result.ScenarioId)
            ? "unspecified"
            : result.ScenarioId.Trim();
        var normalizedScenarioName = string.IsNullOrWhiteSpace(result.ScenarioName)
            ? "Unspecified"
            : result.ScenarioName.Trim();
        var normalizedGroups = FeasibilityProfileGroupEvidenceService.GetScenarioConsistentGroups(
            result.PlaybackCount,
            normalizedScenarioId,
            result.VerifiedProfileGroups);
        var normalizedAccountLabel = result.IsSameAccountSessionMaintained
            ? result.AccountLabel?.Trim() ?? ""
            : "";
        var hasSameAccountEvidence = result.IsSameAccountSessionMaintained &&
            !string.IsNullOrWhiteSpace(normalizedAccountLabel) &&
            normalizedGroups.Count > 0;
        var isFailureOutcome = FeasibilityOutcomeService.IsFailure(canonicalOutcome);
        var normalizedObservedCpuPercent = FeasibilityResourceObservationService.NormalizePercent(result.ObservedCpuPercent);
        var normalizedObservedGpuPercent = FeasibilityResourceObservationService.NormalizePercent(result.ObservedGpuPercent);
        var normalizedObservedMemoryMegabytes = FeasibilityResourceObservationService.NormalizeMemoryMegabytes(
            result.ObservedMemoryMegabytes);
        var normalizedRestartSession = result.IsRestartSessionMaintained &&
            hasSameAccountEvidence &&
            !isFailureOutcome;
        var normalizedResourceUsageAcceptable = result.IsResourceUsageAcceptable &&
            !isFailureOutcome &&
            FeasibilityResourceObservationService.HasCompleteValidObservation(
                normalizedObservedCpuPercent,
                normalizedObservedGpuPercent,
                normalizedObservedMemoryMegabytes);
        var normalizedOutcome = NormalizeOutcome(
            canonicalOutcome,
            result.PlaybackCount,
            normalizedGroups,
            hasSameAccountEvidence,
            normalizedRestartSession,
            normalizedResourceUsageAcceptable);
        var shouldClearDecisionSnapshot = ShouldClearDecisionSnapshot(
            result,
            canonicalOutcome,
            normalizedOutcome,
            normalizedScenarioId,
            normalizedGroups,
            hasSameAccountEvidence,
            normalizedRestartSession,
            normalizedResourceUsageAcceptable,
            normalizedObservedCpuPercent,
            normalizedObservedGpuPercent,
            normalizedObservedMemoryMegabytes);
        var resultId = string.IsNullOrWhiteSpace(result.Id)
            ? CreateResultId(result.CapturedAt, result.PlaybackCount, normalizedOutcome)
            : result.Id.Trim();

        return new FeasibilityTestResult
        {
            Id = CreateUniqueResultId(resultId, usedIds),
            CapturedAt = result.CapturedAt,
            PlaybackCount = result.PlaybackCount,
            ScenarioId = normalizedScenarioId,
            ScenarioName = normalizedScenarioName,
            Outcome = normalizedOutcome,
            Diagnostics = diagnostics,
            IsSameAccountSessionMaintained = hasSameAccountEvidence,
            AccountLabel = hasSameAccountEvidence ? normalizedAccountLabel : "",
            IsRestartSessionMaintained = normalizedRestartSession,
            IsResourceUsageAcceptable = normalizedResourceUsageAcceptable,
            VerifiedProfileGroups = normalizedGroups,
            ObservedCpuPercent = normalizedObservedCpuPercent,
            ObservedGpuPercent = normalizedObservedGpuPercent,
            ObservedMemoryMegabytes = normalizedObservedMemoryMegabytes,
            DecisionCode = shouldClearDecisionSnapshot ? "" : result.DecisionCode?.Trim() ?? "",
            DecisionTitle = shouldClearDecisionSnapshot ? "" : result.DecisionTitle?.Trim() ?? "",
            DecisionDetail = shouldClearDecisionSnapshot ? "" : result.DecisionDetail?.Trim() ?? "",
            DecisionNextAction = shouldClearDecisionSnapshot ? "" : result.DecisionNextAction?.Trim() ?? "",
            Notes = result.Notes?.Trim() ?? ""
        };
    }

    private static string CreateUniqueResultId(string requestedId, HashSet<string> usedIds)
    {
        if (usedIds.Add(requestedId))
        {
            return requestedId;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{requestedId}_{suffix}";
            if (usedIds.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string NormalizeOutcome(
        string outcome,
        int playbackCount,
        IReadOnlyList<string> normalizedGroups,
        bool hasSameAccountEvidence,
        bool restartSession,
        bool resourceUsageAcceptable)
    {
        if (!FeasibilityOutcomeService.IsSuccess(outcome))
        {
            return outcome;
        }

        var hasRequiredGroupEvidence = FeasibilityProfileGroupEvidenceService.HasRequiredGroups(
            playbackCount,
            normalizedGroups);
        return playbackCount >= 9 &&
            hasSameAccountEvidence &&
            hasRequiredGroupEvidence &&
            restartSession &&
            resourceUsageAcceptable
                ? outcome
                : "partial";
    }

    private static string NormalizeKnownOutcome(string outcome)
    {
        if (FeasibilityOutcomeService.IsSuccess(outcome))
        {
            return "success";
        }

        if (FeasibilityOutcomeService.IsPartial(outcome))
        {
            return "partial";
        }

        return FeasibilityOutcomeService.IsFailure(outcome)
            ? "failure"
            : outcome;
    }

    private static bool ShouldClearDecisionSnapshot(
        FeasibilityTestResult result,
        string canonicalOutcome,
        string normalizedOutcome,
        string normalizedScenarioId,
        IReadOnlyList<string> normalizedGroups,
        bool hasSameAccountEvidence,
        bool normalizedRestartSession,
        bool normalizedResourceUsageAcceptable,
        double? normalizedObservedCpuPercent,
        double? normalizedObservedGpuPercent,
        double? normalizedObservedMemoryMegabytes)
    {
        if (!FeasibilityOutcomeService.IsKnown(canonicalOutcome))
        {
            return true;
        }

        if (!string.Equals(canonicalOutcome, normalizedOutcome, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(result.ScenarioId) &&
            normalizedScenarioId.Equals("unspecified", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rawGroups = FeasibilityProfileGroupEvidenceService.Normalize(result.VerifiedProfileGroups);
        if (!rawGroups.SequenceEqual(normalizedGroups, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!NullableDoubleEquals(result.ObservedCpuPercent, normalizedObservedCpuPercent) ||
            !NullableDoubleEquals(result.ObservedGpuPercent, normalizedObservedGpuPercent) ||
            !NullableDoubleEquals(result.ObservedMemoryMegabytes, normalizedObservedMemoryMegabytes))
        {
            return true;
        }

        var rawHasSameAccountEvidence = result.IsSameAccountSessionMaintained &&
            !string.IsNullOrWhiteSpace(result.AccountLabel) &&
            rawGroups.Count > 0;
        return rawHasSameAccountEvidence != hasSameAccountEvidence ||
            result.IsRestartSessionMaintained != normalizedRestartSession ||
            result.IsResourceUsageAcceptable != normalizedResourceUsageAcceptable;
    }

    private static bool NullableDoubleEquals(double? left, double? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Value.Equals(right.Value);
    }

    private static RuntimeDiagnosticsSnapshot NormalizeDiagnostics(
        RuntimeDiagnosticsSnapshot? diagnostics,
        DateTimeOffset capturedAt)
    {
        if (diagnostics is null)
        {
            return new RuntimeDiagnosticsSnapshot(
                capturedAt,
                WebViewProcessCount: 0,
                WebViewWorkingSetMegabytes: 0,
                WebViewPrivateMemoryMegabytes: 0,
                WebViewCpuPercent: null);
        }

        return new RuntimeDiagnosticsSnapshot(
            diagnostics.CapturedAt,
            WebViewProcessCount: Math.Max(0, diagnostics.WebViewProcessCount),
            WebViewWorkingSetMegabytes: NormalizeNonNegativeMegabytes(diagnostics.WebViewWorkingSetMegabytes),
            WebViewPrivateMemoryMegabytes: NormalizeNonNegativeMegabytes(diagnostics.WebViewPrivateMemoryMegabytes),
            WebViewCpuPercent: FeasibilityResourceObservationService.NormalizePercent(diagnostics.WebViewCpuPercent));
    }

    private static double NormalizeNonNegativeMegabytes(double value)
    {
        return double.IsFinite(value) && value >= 0 ? value : 0;
    }
}
