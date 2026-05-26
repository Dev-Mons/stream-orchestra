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
        DataFolder = dataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamOrchestra",
            "Data");

        Directory.CreateDirectory(DataFolder);
    }

    public string DataFolder { get; }

    public string ResultsFilePath => Path.Combine(DataFolder, "feasibility-results.json");

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
        return sourceResults
            .Select(NormalizeResult)
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();
    }

    private static FeasibilityTestResult? NormalizeResult(FeasibilityTestResult? result)
    {
        if (result is null || result.PlaybackCount is < 1 or > PlaybackTestPlanService.MaxSlotCount)
        {
            return null;
        }

        var diagnostics = NormalizeDiagnostics(result.Diagnostics, result.CapturedAt);
        var normalizedOutcome = result.Outcome?.Trim() ?? "";
        var normalizedGroups = FeasibilityProfileGroupEvidenceService.Normalize(result.VerifiedProfileGroups);
        var normalizedAccountLabel = result.IsSameAccountSessionMaintained
            ? result.AccountLabel?.Trim() ?? ""
            : "";
        var hasSameAccountEvidence = result.IsSameAccountSessionMaintained &&
            !string.IsNullOrWhiteSpace(normalizedAccountLabel) &&
            normalizedGroups.Count > 0;
        var normalizedRestartSession = result.IsRestartSessionMaintained && hasSameAccountEvidence;
        var normalizedResourceUsageAcceptable = result.IsResourceUsageAcceptable &&
            FeasibilityResourceObservationService.HasCompleteValidObservation(result);

        return new FeasibilityTestResult
        {
            Id = string.IsNullOrWhiteSpace(result.Id)
                ? CreateResultId(result.CapturedAt, result.PlaybackCount, normalizedOutcome)
                : result.Id.Trim(),
            CapturedAt = result.CapturedAt,
            PlaybackCount = result.PlaybackCount,
            ScenarioId = string.IsNullOrWhiteSpace(result.ScenarioId) ? "unspecified" : result.ScenarioId.Trim(),
            ScenarioName = string.IsNullOrWhiteSpace(result.ScenarioName) ? "Unspecified" : result.ScenarioName.Trim(),
            Outcome = normalizedOutcome,
            Diagnostics = diagnostics,
            IsSameAccountSessionMaintained = hasSameAccountEvidence,
            AccountLabel = hasSameAccountEvidence ? normalizedAccountLabel : "",
            IsRestartSessionMaintained = normalizedRestartSession,
            IsResourceUsageAcceptable = normalizedResourceUsageAcceptable,
            VerifiedProfileGroups = normalizedGroups,
            ObservedCpuPercent = result.ObservedCpuPercent,
            ObservedGpuPercent = result.ObservedGpuPercent,
            ObservedMemoryMegabytes = result.ObservedMemoryMegabytes,
            DecisionCode = result.DecisionCode?.Trim() ?? "",
            DecisionTitle = result.DecisionTitle?.Trim() ?? "",
            DecisionDetail = result.DecisionDetail?.Trim() ?? "",
            DecisionNextAction = result.DecisionNextAction?.Trim() ?? "",
            Notes = result.Notes?.Trim() ?? ""
        };
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

        return diagnostics;
    }
}
