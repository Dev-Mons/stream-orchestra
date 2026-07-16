namespace StreamOrchestra.App.Models;

public sealed record RecordingRequest(
    string StreamUrl,
    string OutputFolder,
    string QualityId,
    DateTimeOffset StartedAt);
