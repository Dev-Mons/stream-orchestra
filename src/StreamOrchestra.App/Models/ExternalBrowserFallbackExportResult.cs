namespace StreamOrchestra.App.Models;

public sealed record ExternalBrowserFallbackExportResult(
    bool ScriptSaved,
    string Reason,
    string? ScriptPath,
    ExternalBrowserFallbackPlan? Plan);
