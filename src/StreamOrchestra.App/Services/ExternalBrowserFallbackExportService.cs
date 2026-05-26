using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class ExternalBrowserFallbackExportService
{
    private readonly ExternalBrowserDiscoveryService? _externalBrowserDiscoveryService;
    private readonly ExternalBrowserLaunchPlanService _externalBrowserLaunchPlanService;
    private readonly ExternalBrowserLaunchScriptService _externalBrowserLaunchScriptService;

    public ExternalBrowserFallbackExportService(
        ExternalBrowserDiscoveryService? externalBrowserDiscoveryService = null,
        ExternalBrowserLaunchPlanService? externalBrowserLaunchPlanService = null,
        ExternalBrowserLaunchScriptService? externalBrowserLaunchScriptService = null)
    {
        _externalBrowserDiscoveryService = externalBrowserDiscoveryService;
        _externalBrowserLaunchPlanService = externalBrowserLaunchPlanService ?? new ExternalBrowserLaunchPlanService();
        _externalBrowserLaunchScriptService = externalBrowserLaunchScriptService ?? new ExternalBrowserLaunchScriptService();
    }

    public ExternalBrowserFallbackExportResult SaveScript(
        WorkspacePreset? workspace,
        string dataFolder,
        DateTimeOffset generatedAt,
        string missingWorkspaceReason = "No last saved session is available.",
        IReadOnlyList<LayoutPreset>? layouts = null)
    {
        if (workspace is null)
        {
            return new ExternalBrowserFallbackExportResult(
                ScriptSaved: false,
                Reason: missingWorkspaceReason,
                ScriptPath: null,
                Plan: null);
        }

        var browsers = (_externalBrowserDiscoveryService ?? new ExternalBrowserDiscoveryService(dataFolder))
            .Discover();
        var plan = _externalBrowserLaunchPlanService.CreatePlan(workspace, browsers, dataFolder, layouts);
        if (!plan.CanLaunch)
        {
            return new ExternalBrowserFallbackExportResult(
                ScriptSaved: false,
                Reason: plan.Reason,
                ScriptPath: null,
                Plan: plan);
        }

        var scriptPath = _externalBrowserLaunchScriptService.SaveScript(plan, dataFolder, generatedAt);
        return new ExternalBrowserFallbackExportResult(
            ScriptSaved: true,
            Reason: plan.Reason,
            ScriptPath: scriptPath,
            Plan: plan);
    }
}
