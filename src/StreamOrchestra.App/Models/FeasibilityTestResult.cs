namespace StreamOrchestra.App.Models;

public sealed class FeasibilityTestResult
{
    public string Id { get; init; } = "";

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    public int PlaybackCount { get; init; }

    public string ScenarioId { get; init; } = "unspecified";

    public string ScenarioName { get; init; } = "Unspecified";

    public string Outcome { get; init; } = "";

    public RuntimeDiagnosticsSnapshot Diagnostics { get; init; } = new(
        DateTimeOffset.Now,
        WebViewProcessCount: 0,
        WebViewWorkingSetMegabytes: 0,
        WebViewPrivateMemoryMegabytes: 0,
        WebViewCpuPercent: null);

    public bool IsSameAccountSessionMaintained { get; init; }

    public string AccountLabel { get; init; } = "";

    public bool IsRestartSessionMaintained { get; init; }

    public bool IsResourceUsageAcceptable { get; init; }

    public IReadOnlyList<string> VerifiedProfileGroups { get; init; } = [];

    public double? ObservedCpuPercent { get; init; }

    public double? ObservedGpuPercent { get; init; }

    public double? ObservedMemoryMegabytes { get; init; }

    public string DecisionCode { get; set; } = "";

    public string DecisionTitle { get; set; } = "";

    public string DecisionDetail { get; set; } = "";

    public string DecisionNextAction { get; set; } = "";

    public string Notes { get; init; } = "";
}
