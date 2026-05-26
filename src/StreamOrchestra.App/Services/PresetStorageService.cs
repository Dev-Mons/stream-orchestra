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
        return JsonFileStorage.LoadList<WorkspacePreset>(WorkspacesFilePath, SerializerOptions);
    }

    public void SaveWorkspaces(IReadOnlyList<WorkspacePreset> workspaces)
    {
        JsonFileStorage.Save(WorkspacesFilePath, workspaces, SerializerOptions);
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
}
