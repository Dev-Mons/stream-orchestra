namespace StreamOrchestra.App.Models;

public sealed class DiagnosticReport
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;

    public string ProfileRootFolder { get; init; } = "";

    public IReadOnlyList<ProfileGroup> ProfileGroups { get; init; } = [];

    public string DataFolder { get; init; } = "";

    public IReadOnlyList<DiagnosticDataFile> DataFiles { get; init; } = [];

    public WorkspaceDiagnostics WorkspaceDiagnostics { get; init; } = new(
        0,
        0,
        false,
        null,
        null,
        null,
        0,
        0);

    public IReadOnlyList<ExternalBrowserInfo> ExternalBrowsers { get; init; } = [];

    public ExternalBrowserFallbackPlan? ExternalBrowserFallbackPlan { get; init; }

    public int FeasibilityResultCount { get; init; }

    public FeasibilityTestResult? LatestFeasibilityResult { get; init; }

    public FeasibilityDecision FeasibilityDecision { get; init; } = new(
        "pending",
        "검증 대기",
        "SOOP 동시 재생 결과가 아직 기록되지 않았습니다.",
        "SOOP에서 9개 이상 재생 테스트를 실행하고 결과를 기록하세요.");

    public IReadOnlyList<FeasibilityAuditItem> FeasibilityAudit { get; init; } = [];

    public IReadOnlyList<string> FeasibilitySuggestedRecordShapes { get; init; } = [];
}
