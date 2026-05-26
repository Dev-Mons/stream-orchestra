namespace StreamOrchestra.App.Models;

public sealed record ExternalBrowserInfo(
    string Id,
    string Name,
    bool IsInstalled,
    string? ExecutablePath,
    IReadOnlyList<string> CandidatePaths);
