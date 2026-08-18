using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class DiagnosticReportService
{
    private static readonly StreamNavigationService NavigationService = new();

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
    private readonly ISyncTelemetryRecorder _syncTelemetryRecorder;
    private readonly SyncTelemetryPrivacy _telemetryPrivacy;

    public DiagnosticReportService(
        ExternalBrowserDiscoveryService? externalBrowserDiscoveryService = null,
        ExternalBrowserLaunchPlanService? externalBrowserLaunchPlanService = null,
        ExternalBrowserLaunchScriptService? externalBrowserLaunchScriptService = null,
        FeasibilityAuditService? feasibilityAuditService = null,
        ISyncTelemetryRecorder? syncTelemetryRecorder = null)
    {
        _externalBrowserDiscoveryService = externalBrowserDiscoveryService;
        _externalBrowserLaunchPlanService = externalBrowserLaunchPlanService ?? new ExternalBrowserLaunchPlanService();
        _externalBrowserLaunchScriptService = externalBrowserLaunchScriptService ?? new ExternalBrowserLaunchScriptService();
        _feasibilityAuditService = feasibilityAuditService ?? new FeasibilityAuditService();
        _syncTelemetryRecorder = syncTelemetryRecorder ?? SyncTelemetryRecorder.Disabled;
        _telemetryPrivacy = new SyncTelemetryPrivacy();
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
        var latestResult = FeasibilityResultOrderingService.LatestOrDefault(feasibilityResults);
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
            FeasibilitySuggestedRecordShapes = _feasibilityAuditService.CreateSuggestedRecordShapes(feasibilityAudit),
            SyncTelemetry = _syncTelemetryRecorder.CreateSummary()
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
            SelectedSlotId: NormalizeSelectedSlotId(appState?.SelectedSlotId),
            LastSessionLayoutId: lastSession?.LayoutId,
            LastSessionSlotCount: lastSessionSlots.Length,
            LastSessionActiveStreamCount: lastSessionSlots.Count(HasLaunchableStreamUrl));
    }

    private static int? NormalizeSelectedSlotId(int? selectedSlotId)
    {
        return selectedSlotId is >= 1 and <= PlaybackTestPlanService.MaxSlotCount
            ? selectedSlotId
            : null;
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

        var normalizedUrl = NavigationService.NormalizeUrl(slot.StreamUrl ?? "");
        if (normalizedUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https";
    }

    public string SaveReport(DiagnosticReport report, string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        var path = Path.Combine(dataFolder, $"diagnostic-report-{report.GeneratedAt:yyyyMMdd-HHmmss}.json");
        SavePrivacySafeJson(path, report);

        return path;
    }

    public string SerializePrivacySafe<T>(T value)
    {
        var root = CreatePrivacySafeNode(value);
        return root.ToJsonString(SerializerOptions);
    }

    public string? SaveSyncTelemetrySnapshot(string dataFolder)
    {
        if (!_syncTelemetryRecorder.IsEnabled)
        {
            return null;
        }

        var snapshot = _syncTelemetryRecorder.CreateSnapshot();
        Directory.CreateDirectory(dataFolder);
        var path = Path.Combine(
            dataFolder,
            $"sync-telemetry-{snapshot.GeneratedAtUtc:yyyyMMdd-HHmmss}.json");
        SavePrivacySafeJson(path, snapshot);
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

    private void SavePrivacySafeJson<T>(string path, T value)
    {
        var root = CreatePrivacySafeNode(value);
        JsonFileStorage.Save(path, root, SerializerOptions);
    }

    private JsonNode CreatePrivacySafeNode<T>(T value)
    {
        var root = JsonSerializer.SerializeToNode(value, SerializerOptions) ?? new JsonObject();
        ScrubNode(root);
        return root;
    }

    private void ScrubNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToArray())
                {
                    if (IsSecretPropertyName(property.Key))
                    {
                        jsonObject[property.Key] = "[redacted]";
                        continue;
                    }

                    ScrubNode(property.Value);
                }
                break;

            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    ScrubNode(jsonArray[index]);
                }
                break;

            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text):
                jsonValue.ReplaceWith(_telemetryPrivacy.SanitizeDiagnosticText(text));
                break;
        }
    }

    private static bool IsSecretPropertyName(string propertyName)
    {
        var normalized = propertyName.Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized is "authorization" or "proxyauthorization" or "cookie" or "cookies" or
                   "setcookie" or "password" or "passwd" or "passphrase" or "accesstoken" or
                   "refreshtoken" or "token" or "apikey" or "secret" or "signature" or
                   "signedquery" or "signedurl" or "requestheaders" or "responseheaders" or
                   "headers" or "rawbody" or "requestbody" or "responsebody" or "playlistbody" or
                   "manifesttext" or "originalurl" or "requesturl" ||
               normalized.EndsWith("authorizationheader", StringComparison.Ordinal) ||
               normalized.EndsWith("cookieheader", StringComparison.Ordinal) ||
               normalized.EndsWith("token", StringComparison.Ordinal) ||
               normalized.EndsWith("password", StringComparison.Ordinal) ||
               normalized.EndsWith("secret", StringComparison.Ordinal) ||
               normalized.EndsWith("signature", StringComparison.Ordinal) ||
               normalized.EndsWith("signedquery", StringComparison.Ordinal) ||
               normalized.EndsWith("signedurl", StringComparison.Ordinal) ||
               normalized.EndsWith("rawbody", StringComparison.Ordinal) ||
               normalized.EndsWith("playlistbody", StringComparison.Ordinal) ||
               normalized.EndsWith("manifesttext", StringComparison.Ordinal);
    }
}
