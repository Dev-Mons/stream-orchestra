using System.IO;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class DiagnosticReportService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ExternalBrowserDiscoveryService? _externalBrowserDiscoveryService;
    private readonly ExternalBrowserLaunchPlanService _externalBrowserLaunchPlanService;
    private readonly ExternalBrowserLaunchScriptService _externalBrowserLaunchScriptService;
    private readonly FeasibilityAuditService _feasibilityAuditService;

    public DiagnosticReportService(
        ExternalBrowserDiscoveryService? externalBrowserDiscoveryService = null,
        ExternalBrowserLaunchPlanService? externalBrowserLaunchPlanService = null,
        ExternalBrowserLaunchScriptService? externalBrowserLaunchScriptService = null,
        FeasibilityAuditService? feasibilityAuditService = null)
    {
        _externalBrowserDiscoveryService = externalBrowserDiscoveryService;
        _externalBrowserLaunchPlanService = externalBrowserLaunchPlanService ?? new ExternalBrowserLaunchPlanService();
        _externalBrowserLaunchScriptService = externalBrowserLaunchScriptService ?? new ExternalBrowserLaunchScriptService();
        _feasibilityAuditService = feasibilityAuditService ?? new FeasibilityAuditService();
    }

    public DiagnosticReport CreateReport(
        WebViewProfileService profileService,
        PresetStorageService presetStorageService,
        FavoriteStorageService favoriteStorageService,
        FeasibilityResultStorageService feasibilityResultStorageService,
        FeasibilityDecision feasibilityDecision,
        WorkspacePreset? externalBrowserFallbackWorkspace = null,
        IReadOnlyList<LayoutPreset>? layouts = null)
    {
        var feasibilityResults = feasibilityResultStorageService.LoadResults();
        var latestResult = feasibilityResults
            .OrderByDescending(result => result.CapturedAt)
            .FirstOrDefault();
        var appState = presetStorageService.LoadAppState();
        var workspaces = presetStorageService.LoadWorkspaces();
        var favorites = favoriteStorageService.LoadFavorites();
        var externalBrowserDiscoveryService = _externalBrowserDiscoveryService ??
            new ExternalBrowserDiscoveryService(presetStorageService.DataFolder);
        var externalBrowserCandidateStorageService = new ExternalBrowserCandidateStorageService(presetStorageService.DataFolder);
        var externalBrowsers = externalBrowserDiscoveryService.Discover();
        var feasibilityAudit = _feasibilityAuditService.CreateAudit(feasibilityResults, feasibilityDecision);

        return new DiagnosticReport
        {
            GeneratedAt = DateTimeOffset.Now,
            ProfileRootFolder = profileService.BaseProfileFolder,
            ProfileGroups = profileService.Groups
                .Append(profileService.ExplorerGroup)
                .OrderBy(group => group.Id)
                .ToArray(),
            DataFolder = presetStorageService.DataFolder,
            DataFiles =
            [
                GetFileStatus("appstate", presetStorageService.AppStateFilePath),
                GetFileStatus("workspaces", presetStorageService.WorkspacesFilePath),
                GetFileStatus("favorites", favoriteStorageService.FavoritesFilePath),
                GetFileStatus("feasibility-results", feasibilityResultStorageService.ResultsFilePath),
                GetFileStatus("external-browsers", externalBrowserCandidateStorageService.CandidatesFilePath)
            ],
            WorkspaceDiagnostics = CreateWorkspaceDiagnostics(workspaces, favorites, appState),
            ExternalBrowsers = externalBrowsers,
            ExternalBrowserFallbackPlan = externalBrowserFallbackWorkspace is null
                ? null
                : _externalBrowserLaunchPlanService.CreatePlan(
                    externalBrowserFallbackWorkspace,
                    externalBrowsers,
                    presetStorageService.DataFolder,
                    layouts),
            FeasibilityResultCount = feasibilityResults.Count,
            LatestFeasibilityResult = latestResult,
            FeasibilitySameAccountLabels =
                FeasibilityProfileGroupEvidenceService.GetLatestSameAccountAccountLabels(feasibilityResults),
            HasConflictingFeasibilityAccountLabels =
                FeasibilityProfileGroupEvidenceService.HasConflictingSameAccountLabels(feasibilityResults),
            FeasibilityDecision = feasibilityDecision,
            FeasibilityAudit = feasibilityAudit,
            FeasibilitySuggestedRecordShapes = _feasibilityAuditService.CreateSuggestedRecordShapes(feasibilityAudit)
        };
    }

    private static WorkspaceDiagnostics CreateWorkspaceDiagnostics(
        IReadOnlyList<WorkspacePreset> workspaces,
        IReadOnlyList<StreamEntry> favorites,
        AppState? appState)
    {
        var lastSession = appState?.LastSession;
        var lastSessionSlots = lastSession?.Slots?
            .Where(slot => slot is not null)
            .Select(slot => slot!)
            .Where(IsValidSlotId)
            .ToArray() ?? [];
        return new WorkspaceDiagnostics(
            SavedWorkspaceCount: workspaces.Count,
            FavoriteCount: favorites.Count,
            HasLastSession: lastSession is not null,
            LastWorkspaceId: appState?.LastWorkspaceId,
            SelectedSlotId: appState?.SelectedSlotId,
            LastSessionLayoutId: lastSession?.LayoutId,
            LastSessionSlotCount: lastSessionSlots.Length,
            LastSessionActiveStreamCount: lastSessionSlots.Count(HasLaunchableStreamUrl));
    }

    private static bool IsValidSlotId(WorkspaceSlot slot)
    {
        return slot.SlotId is >= 1 and <= PlaybackTestPlanService.MaxSlotCount;
    }

    private static bool HasLaunchableStreamUrl(WorkspaceSlot? slot)
    {
        if (slot is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(slot.StreamUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(slot.StreamUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https";
    }

    public string SaveReport(DiagnosticReport report, string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        var path = Path.Combine(dataFolder, $"diagnostic-report-{report.GeneratedAt:yyyyMMdd-HHmmss}.json");
        JsonFileStorage.Save(path, report, SerializerOptions);

        return path;
    }

    public string? SaveExternalBrowserFallbackScript(DiagnosticReport report, string dataFolder)
    {
        if (report.ExternalBrowserFallbackPlan is not { CanLaunch: true } plan)
        {
            return null;
        }

        return _externalBrowserLaunchScriptService.SaveScript(plan, dataFolder, report.GeneratedAt);
    }

    private static DiagnosticDataFile GetFileStatus(string name, string path)
    {
        var fileInfo = new FileInfo(path);
        return new DiagnosticDataFile(
            name,
            path,
            fileInfo.Exists,
            fileInfo.Exists ? fileInfo.Length : 0);
    }
}
