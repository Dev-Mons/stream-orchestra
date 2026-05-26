namespace StreamOrchestra.App.Models;

public sealed record RuntimeDiagnosticsSnapshot(
    DateTimeOffset CapturedAt,
    int WebViewProcessCount,
    double WebViewWorkingSetMegabytes,
    double WebViewPrivateMemoryMegabytes,
    double? WebViewCpuPercent);
