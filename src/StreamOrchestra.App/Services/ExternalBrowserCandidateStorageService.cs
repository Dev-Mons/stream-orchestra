using System.IO;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class ExternalBrowserCandidateStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ExternalBrowserCandidateStorageService(string? dataFolder = null)
    {
        DataFolder = dataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamOrchestra",
            "Data");

        Directory.CreateDirectory(DataFolder);
    }

    public string DataFolder { get; }

    public string CandidatesFilePath => Path.Combine(DataFolder, "external-browsers.json");

    public IReadOnlyList<ExternalBrowserCandidate> LoadCandidates()
    {
        return NormalizeCandidates(JsonFileStorage.LoadList<ExternalBrowserCandidate>(CandidatesFilePath, SerializerOptions));
    }

    public void SaveCandidates(IReadOnlyList<ExternalBrowserCandidate> candidates)
    {
        JsonFileStorage.Save(CandidatesFilePath, NormalizeCandidates(candidates), SerializerOptions);
    }

    public static IReadOnlyList<ExternalBrowserCandidate> MergeCandidates(
        IReadOnlyList<ExternalBrowserCandidate> primaryCandidates,
        IReadOnlyList<ExternalBrowserCandidate> secondaryCandidates)
    {
        var normalizedCandidates = NormalizeCandidates(primaryCandidates)
            .Concat(NormalizeCandidates(secondaryCandidates));
        var mergedCandidates = new List<ExternalBrowserCandidate>();

        foreach (var group in normalizedCandidates.GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group.ToArray();
            var name = candidates
                .Select(candidate => candidate.Name)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key;
            var paths = candidates
                .SelectMany(candidate => candidate.CandidatePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            mergedCandidates.Add(new ExternalBrowserCandidate(group.Key, name, paths));
        }

        return mergedCandidates;
    }

    private static IReadOnlyList<ExternalBrowserCandidate> NormalizeCandidates(
        IReadOnlyList<ExternalBrowserCandidate> candidates)
    {
        return candidates
            .Select(NormalizeCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();
    }

    private static ExternalBrowserCandidate? NormalizeCandidate(ExternalBrowserCandidate candidate)
    {
        var id = NormalizeId(candidate.Id);
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var paths = (candidate.CandidatePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(candidate.Name)
            ? id
            : candidate.Name.Trim();

        return new ExternalBrowserCandidate(id, name, paths);
    }

    private static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "";
        }

        var normalizedCharacters = id.Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();

        return string.Join(
            "_",
            new string(normalizedCharacters).Split('_', StringSplitOptions.RemoveEmptyEntries));
    }
}
