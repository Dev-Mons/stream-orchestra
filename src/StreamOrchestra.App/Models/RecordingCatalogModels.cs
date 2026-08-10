namespace StreamOrchestra.App.Models;

public sealed record RecordingCatalogItem(
    string Id,
    string StreamUrl,
    string DisplayName,
    string DetailTitle,
    string QualityId,
    string? ThumbnailPath,
    bool RequiresCredentials,
    string? Username,
    DateTimeOffset AddedAt);

public sealed record RecordingCatalogState(
    string OutputFolder,
    IReadOnlyList<RecordingCatalogItem> Items);

public sealed record SoopStreamMetadata(
    string DisplayName,
    string Title,
    string? ThumbnailUrl);

public sealed record SoopResolvedStreamMetadata(
    string DisplayName,
    string Title,
    string? ThumbnailPath);
