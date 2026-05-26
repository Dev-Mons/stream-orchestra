using System.IO;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class PresetStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public PresetStorageService(string? dataFolder = null)
    {
        DataFolder = dataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamOrchestra",
            "Data");

        Directory.CreateDirectory(DataFolder);
    }

    public string DataFolder { get; }

    public string WorkspacesFilePath => Path.Combine(DataFolder, "workspaces.json");

    public string AppStateFilePath => Path.Combine(DataFolder, "appstate.json");

    public IReadOnlyList<WorkspacePreset> LoadWorkspaces()
    {
        return NormalizeWorkspaces(JsonFileStorage.LoadList<WorkspacePreset>(WorkspacesFilePath, SerializerOptions));
    }

    public void SaveWorkspaces(IReadOnlyList<WorkspacePreset> workspaces)
    {
        JsonFileStorage.Save(WorkspacesFilePath, NormalizeWorkspaces(workspaces), SerializerOptions);
    }

    public AppState? LoadAppState()
    {
        return JsonFileStorage.LoadSingle<AppState>(AppStateFilePath, SerializerOptions);
    }

    public void SaveAppState(AppState appState)
    {
        JsonFileStorage.Save(AppStateFilePath, appState, SerializerOptions);
    }

    public static string CreateWorkspaceId(string name, IReadOnlyCollection<WorkspacePreset> existingWorkspaces)
    {
        var normalizedCharacters = name.Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();
        var baseId = string.Join(
            "_",
            new string(normalizedCharacters).Split('_', StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "workspace";
        }
        else
        {
            baseId = $"workspace_{baseId}";
        }

        var existingIds = existingWorkspaces
            .Select(workspace => workspace.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingIds.Contains(baseId))
        {
            return baseId;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseId}_{suffix}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static IReadOnlyList<WorkspacePreset> NormalizeWorkspaces(IReadOnlyList<WorkspacePreset> workspaces)
    {
        IEnumerable<WorkspacePreset?> sourceWorkspaces = workspaces ?? [];
        var normalizedWorkspaces = new List<WorkspacePreset>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var workspace in sourceWorkspaces)
        {
            if (workspace is null)
            {
                continue;
            }

            var id = CreateUniqueWorkspaceId(workspace.Id, usedIds);
            var name = string.IsNullOrWhiteSpace(workspace.Name)
                ? "Imported Workspace"
                : workspace.Name.Trim();

            normalizedWorkspaces.Add(new WorkspacePreset
            {
                Id = id,
                Name = name,
                LayoutId = workspace.LayoutId?.Trim() ?? "",
                Slots = workspace.Slots ?? []
            });
        }

        return normalizedWorkspaces;
    }

    private static string CreateUniqueWorkspaceId(string? requestedId, HashSet<string> usedIds)
    {
        var baseId = string.IsNullOrWhiteSpace(requestedId)
            ? "workspace_imported"
            : requestedId.Trim();

        if (usedIds.Add(baseId))
        {
            return baseId;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseId}_{suffix}";
            if (usedIds.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
