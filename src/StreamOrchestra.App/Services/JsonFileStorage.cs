using System.IO;
using System.Text.Json;

namespace StreamOrchestra.App.Services;

internal static class JsonFileStorage
{
    public static IReadOnlyList<T> LoadList<T>(string path, JsonSerializerOptions options)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(path);
            var values = JsonSerializer.Deserialize<IReadOnlyList<T>>(stream, options) ?? [];
            return values
                .Where(value => value is not null)
                .ToArray();
        }
        catch (JsonException)
        {
            QuarantineCorruptFile(path);
            return [];
        }
        catch (NotSupportedException)
        {
            QuarantineCorruptFile(path);
            return [];
        }
    }

    public static T? LoadSingle<T>(string path, JsonSerializerOptions options)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, options);
        }
        catch (JsonException)
        {
            QuarantineCorruptFile(path);
            return default;
        }
        catch (NotSupportedException)
        {
            QuarantineCorruptFile(path);
            return default;
        }
    }

    public static void Save<T>(string path, T value, JsonSerializerOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var tempPath = $"{path}.tmp.{Guid.NewGuid():N}";

        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void QuarantineCorruptFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backupPath = $"{path}.corrupt.{DateTimeOffset.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}";
        File.Move(path, backupPath);
    }
}
