using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class FavoriteStorageServiceTests : IDisposable
{
    private readonly string _dataFolder;

    public FavoriteStorageServiceTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void LoadFavorites_ReturnsEmptyListWhenFileDoesNotExist()
    {
        var service = new FavoriteStorageService(_dataFolder);

        var favorites = service.LoadFavorites();

        Assert.Empty(favorites);
    }

    [Fact]
    public void SaveFavorites_AndLoadFavorites_RoundTripsFavorites()
    {
        var service = new FavoriteStorageService(_dataFolder);
        var favorite = CreateFavorite("favorite_streamer", "Streamer");

        service.SaveFavorites([favorite]);
        var loadedFavorites = service.LoadFavorites();

        var loadedFavorite = Assert.Single(loadedFavorites);
        Assert.Equal(favorite.Id, loadedFavorite.Id);
        Assert.Equal(favorite.Name, loadedFavorite.Name);
        Assert.Equal("SOOP", loadedFavorite.Platform);
        Assert.Equal(favorite.Url, loadedFavorite.Url);
        Assert.Equal(favorite.LastUsedAt, loadedFavorite.LastUsedAt);
    }

    [Fact]
    public void SaveFavorites_OverwritesExistingFileWithoutLeavingTemporaryFiles()
    {
        var service = new FavoriteStorageService(_dataFolder);
        service.SaveFavorites([CreateFavorite("favorite_old", "Old")]);

        service.SaveFavorites([CreateFavorite("favorite_new", "New")]);

        var loadedFavorite = Assert.Single(service.LoadFavorites());
        Assert.Equal("favorite_new", loadedFavorite.Id);
        Assert.Empty(Directory.GetFiles(_dataFolder, "favorites.json.tmp.*"));
    }

    [Fact]
    public void LoadFavorites_QuarantinesCorruptJsonAndReturnsEmptyList()
    {
        var service = new FavoriteStorageService(_dataFolder);
        File.WriteAllText(service.FavoritesFilePath, "{ invalid json");

        var favorites = service.LoadFavorites();

        Assert.Empty(favorites);
        Assert.False(File.Exists(service.FavoritesFilePath));
        Assert.Single(Directory.GetFiles(_dataFolder, "favorites.json.corrupt.*"));
    }

    [Fact]
    public void LoadFavorites_IgnoresNullEntries()
    {
        var service = new FavoriteStorageService(_dataFolder);
        File.WriteAllText(
            service.FavoritesFilePath,
            """
            [
              null,
              {
                "id": "favorite_streamer",
                "name": "Streamer",
                "platform": "SOOP",
                "url": "https://example.com/streamer"
              }
            ]
            """);

        var favorites = service.LoadFavorites();

        var favorite = Assert.Single(favorites);
        Assert.Equal("favorite_streamer", favorite.Id);
        Assert.Equal("Streamer", favorite.Name);
    }

    [Fact]
    public void LoadFavorites_NormalizesHandEditedEntriesAndDropsBlankUrls()
    {
        var service = new FavoriteStorageService(_dataFolder);
        File.WriteAllText(
            service.FavoritesFilePath,
            """
            [
              {
                "id": " favorite_streamer ",
                "name": " ",
                "platform": " ",
                "url": "www.sooplive.co.kr/streamer",
                "memo": " memo "
              },
              {
                "id": " ",
                "name": "Custom Name",
                "platform": "SOOP",
                "url": "https://example.com/channel"
              },
              {
                "id": "duplicate",
                "name": "First",
                "platform": "SOOP",
                "url": "https://example.com/first"
              },
              {
                "id": "duplicate",
                "name": "Second",
                "platform": "SOOP",
                "url": "https://example.com/second"
              },
              {
                "id": "blank",
                "name": "Blank",
                "platform": "SOOP",
                "url": " "
              }
            ]
            """);

        var favorites = service.LoadFavorites();

        Assert.Equal(4, favorites.Count);
        Assert.Contains(favorites, favorite =>
            favorite.Id == "favorite_streamer" &&
            favorite.Name == "streamer" &&
            favorite.Platform == "SOOP" &&
            favorite.Url == "https://www.sooplive.co.kr/streamer" &&
            favorite.Memo == "memo");
        Assert.Contains(favorites, favorite =>
            favorite.Id == "favorite_custom_name" &&
            favorite.Name == "Custom Name" &&
            favorite.Url == "https://example.com/channel");
        Assert.Contains(favorites, favorite => favorite.Id == "duplicate" && favorite.Name == "First");
        Assert.Contains(favorites, favorite => favorite.Id == "duplicate_2" && favorite.Name == "Second");
        Assert.DoesNotContain(favorites, favorite => favorite.Id == "blank");
    }

    [Fact]
    public void SaveFavorites_NormalizesEntriesBeforeWriting()
    {
        var service = new FavoriteStorageService(_dataFolder);

        service.SaveFavorites(
        [
            new StreamEntry
            {
                Id = " ",
                Name = " ",
                Platform = " ",
                Url = "www.sooplive.co.kr/saved",
                Memo = null!
            }
        ]);

        var savedJson = File.ReadAllText(service.FavoritesFilePath);

        Assert.Contains("\"id\": \"favorite_saved\"", savedJson);
        Assert.Contains("\"name\": \"saved\"", savedJson);
        Assert.Contains("\"platform\": \"SOOP\"", savedJson);
        Assert.Contains("\"url\": \"https://www.sooplive.co.kr/saved\"", savedJson);
        Assert.Contains("\"memo\": \"\"", savedJson);
    }

    [Fact]
    public void CreateFavoriteId_CreatesUniqueStableIds()
    {
        var existingFavorite = CreateFavorite("favorite_streamer", "Streamer");

        var firstId = FavoriteStorageService.CreateFavoriteId("Streamer", []);
        var secondId = FavoriteStorageService.CreateFavoriteId("Streamer", [existingFavorite]);

        Assert.Equal("favorite_streamer", firstId);
        Assert.Equal("favorite_streamer_2", secondId);
    }

    [Fact]
    public void CreateFavoriteId_NormalizesNamesAndFallsBackForBlankIds()
    {
        var existingFavorite = CreateFavorite("favorite", "Existing");

        var normalizedId = FavoriteStorageService.CreateFavoriteId("  SOOP / Main!  ", []);
        var blankFallbackId = FavoriteStorageService.CreateFavoriteId("!!!", [existingFavorite]);

        Assert.Equal("favorite_soop_main", normalizedId);
        Assert.Equal("favorite_2", blankFallbackId);
    }

    [Fact]
    public void OrderForDisplay_SortsByRecentUseThenName()
    {
        var firstUsed = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
        var secondUsed = new DateTimeOffset(2026, 5, 26, 11, 0, 0, TimeSpan.Zero);
        var favorites = new[]
        {
            CreateFavorite("favorite_c", "Charlie", firstUsed),
            CreateFavorite("favorite_b", "Bravo", secondUsed),
            CreateFavorite("favorite_a", "Alpha", secondUsed)
        };

        var orderedFavorites = FavoriteStorageService.OrderForDisplay(favorites);

        Assert.Equal(["favorite_a", "favorite_b", "favorite_c"], orderedFavorites.Select(favorite => favorite.Id));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }

    private static StreamEntry CreateFavorite(
        string id,
        string name,
        DateTimeOffset? lastUsedAt = null)
    {
        return new StreamEntry
        {
            Id = id,
            Name = name,
            Platform = "SOOP",
            Url = "https://example.com/streamer",
            LastUsedAt = lastUsedAt ?? new DateTimeOffset(2026, 5, 26, 0, 0, 0, TimeSpan.Zero)
        };
    }
}
