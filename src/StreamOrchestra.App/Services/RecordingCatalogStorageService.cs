using System.IO;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class RecordingCatalogStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public RecordingCatalogStorageService(string? dataFolder = null)
    {
        DataFolder = dataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamOrchestra",
            "Data");
        Directory.CreateDirectory(DataFolder);
    }

    public string DataFolder { get; }

    public string CatalogFilePath => Path.Combine(DataFolder, "recordings.json");

    public RecordingCatalogState Load(string defaultOutputFolder)
    {
        var state = JsonFileStorage.LoadSingle<RecordingCatalogState>(CatalogFilePath, SerializerOptions);
        return Normalize(state, defaultOutputFolder);
    }

    public void Save(RecordingCatalogState state)
    {
        JsonFileStorage.Save(CatalogFilePath, Normalize(state, state.OutputFolder), SerializerOptions);
    }

    public static RecordingCatalogState Normalize(
        RecordingCatalogState? state,
        string defaultOutputFolder)
    {
        var outputFolder = string.IsNullOrWhiteSpace(state?.OutputFolder)
            ? defaultOutputFolder
            : state.OutputFolder.Trim();
        var items = new List<RecordingCatalogItem>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in state?.Items ?? [])
        {
            var url = item.StreamUrl?.Trim() ?? "";
            if (!SoopRecordingService.IsSupportedSoopUrl(url) || !usedUrls.Add(url))
            {
                continue;
            }

            var (fallbackName, fallbackTitle) = RecordingSessionViewModel.CreateFriendlyNames(url);
            var id = string.IsNullOrWhiteSpace(item.Id) || !usedIds.Add(item.Id.Trim())
                ? Guid.NewGuid().ToString("N")
                : item.Id.Trim();
            usedIds.Add(id);
            items.Add(new RecordingCatalogItem(
                id,
                url,
                string.IsNullOrWhiteSpace(item.DisplayName) ? fallbackName : item.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(item.DetailTitle) ? fallbackTitle : item.DetailTitle.Trim(),
                NormalizeQuality(item.QualityId),
                string.IsNullOrWhiteSpace(item.ThumbnailPath) ? null : item.ThumbnailPath.Trim(),
                item.RequiresCredentials,
                string.IsNullOrWhiteSpace(item.Username) ? null : item.Username.Trim(),
                item.AddedAt == default ? DateTimeOffset.Now : item.AddedAt));
        }

        return new RecordingCatalogState(outputFolder, items);
    }

    private static string NormalizeQuality(string? qualityId) => qualityId?.Trim().ToLowerInvariant() switch
    {
        "1080" => "1080",
        "720" => "720",
        "540" => "540",
        "360" => "360",
        _ => "best"
    };
}
