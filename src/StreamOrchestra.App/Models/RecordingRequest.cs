namespace StreamOrchestra.App.Models;

public sealed record RecordingRequest(
    string StreamUrl,
    string OutputFolder,
    string QualityId,
    DateTimeOffset StartedAt,
    string? Username = null,
    string? Password = null);
