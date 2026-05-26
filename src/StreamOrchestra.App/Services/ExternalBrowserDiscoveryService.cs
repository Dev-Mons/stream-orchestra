using System.IO;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class ExternalBrowserDiscoveryService
{
    private readonly IReadOnlyList<ExternalBrowserCandidate> _candidates;

    public ExternalBrowserDiscoveryService(IReadOnlyList<ExternalBrowserCandidate>? candidates = null)
    {
        _candidates = candidates is null
            ? CreateDefaultCandidates()
            : ExternalBrowserCandidateStorageService.MergeCandidates(candidates, []);
    }

    public ExternalBrowserDiscoveryService(string dataFolder)
    {
        var customCandidates = new ExternalBrowserCandidateStorageService(dataFolder).LoadCandidates();
        _candidates = ExternalBrowserCandidateStorageService.MergeCandidates(
            CreateDefaultCandidates(),
            customCandidates);
    }

    public IReadOnlyList<ExternalBrowserInfo> Discover()
    {
        return _candidates
            .Select(candidate =>
            {
                var candidatePaths = candidate.CandidatePaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var executablePath = candidatePaths.FirstOrDefault(File.Exists);

                return new ExternalBrowserInfo(
                    candidate.Id,
                    candidate.Name,
                    executablePath is not null,
                    executablePath,
                    candidatePaths);
            })
            .ToArray();
    }

    private static IReadOnlyList<ExternalBrowserCandidate> CreateDefaultCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            new ExternalBrowserCandidate(
                "edge",
                "Microsoft Edge",
                [
                    Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe")
                ]),
            new ExternalBrowserCandidate(
                "chrome",
                "Google Chrome",
                [
                    Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe")
                ]),
            new ExternalBrowserCandidate(
                "whale",
                "Naver Whale",
                [
                    Path.Combine(programFiles, "Naver", "Naver Whale", "Application", "whale.exe"),
                    Path.Combine(programFilesX86, "Naver", "Naver Whale", "Application", "whale.exe"),
                    Path.Combine(localAppData, "Naver", "Naver Whale", "Application", "whale.exe")
                ]),
            new ExternalBrowserCandidate(
                "brave",
                "Brave",
                [
                    Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                    Path.Combine(programFilesX86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                    Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
                ]),
            new ExternalBrowserCandidate(
                "vivaldi",
                "Vivaldi",
                [
                    Path.Combine(programFiles, "Vivaldi", "Application", "vivaldi.exe"),
                    Path.Combine(programFilesX86, "Vivaldi", "Application", "vivaldi.exe"),
                    Path.Combine(localAppData, "Vivaldi", "Application", "vivaldi.exe")
                ])
        ];
    }
}
