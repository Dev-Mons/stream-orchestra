namespace StreamOrchestra.App.Models;

public sealed record ExternalBrowserCandidate(
    string Id,
    string Name,
    IReadOnlyList<string> CandidatePaths);
