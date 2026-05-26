using System.IO;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class FavoriteStorageService
{
    private static readonly StreamNavigationService NavigationService = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FavoriteStorageService(string? dataFolder = null)
    {
        DataFolder = dataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamOrchestra",
            "Data");

        Directory.CreateDirectory(DataFolder);
    }

    public string DataFolder { get; }

    public string FavoritesFilePath => Path.Combine(DataFolder, "favorites.json");

    public IReadOnlyList<StreamEntry> LoadFavorites()
    {
        return NormalizeFavorites(JsonFileStorage.LoadList<StreamEntry>(FavoritesFilePath, SerializerOptions));
    }

    public void SaveFavorites(IReadOnlyList<StreamEntry> favorites)
    {
        JsonFileStorage.Save(FavoritesFilePath, NormalizeFavorites(favorites), SerializerOptions);
    }

    public static IReadOnlyList<StreamEntry> OrderForDisplay(IReadOnlyList<StreamEntry> favorites)
    {
        return NormalizeFavorites(favorites)
            .OrderByDescending(favorite => favorite.LastUsedAt)
            .ThenBy(favorite => favorite.Name)
            .ToArray();
    }

    public static string CreateFavoriteId(string? name, IReadOnlyCollection<StreamEntry> existingFavorites)
    {
        var normalizedCharacters = (name ?? "").Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();
        var baseId = string.Join(
            "_",
            new string(normalizedCharacters).Split('_', StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "favorite";
        }
        else
        {
            baseId = $"favorite_{baseId}";
        }

        var existingIds = existingFavorites
            .Select(favorite => favorite.Id)
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

    private static IReadOnlyList<StreamEntry> NormalizeFavorites(IReadOnlyList<StreamEntry> favorites)
    {
        var normalizedFavorites = new List<StreamEntry>();
        foreach (var favorite in favorites.Where(favorite => favorite is not null))
        {
            var normalizedUrl = NavigationService.NormalizeUrl(favorite.Url ?? "");
            if (normalizedUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedName = string.IsNullOrWhiteSpace(favorite.Name)
                ? NavigationService.CreateDisplayName(normalizedUrl)
                : favorite.Name.Trim();
            var normalizedId = CreateUniqueFavoriteId(favorite.Id, normalizedName, normalizedFavorites);

            normalizedFavorites.Add(new StreamEntry
            {
                Id = normalizedId,
                Name = normalizedName,
                Platform = string.IsNullOrWhiteSpace(favorite.Platform) ? "SOOP" : favorite.Platform.Trim(),
                Url = normalizedUrl,
                Memo = favorite.Memo?.Trim() ?? "",
                LastUsedAt = favorite.LastUsedAt
            });
        }

        return normalizedFavorites;
    }

    private static string CreateUniqueFavoriteId(
        string? requestedId,
        string name,
        IReadOnlyCollection<StreamEntry> existingFavorites)
    {
        var baseId = string.IsNullOrWhiteSpace(requestedId)
            ? CreateFavoriteId(name, existingFavorites)
            : requestedId.Trim();
        var existingIds = existingFavorites
            .Select(favorite => favorite.Id)
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
