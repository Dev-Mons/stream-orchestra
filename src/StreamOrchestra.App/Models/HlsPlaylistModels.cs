using System.Text.Json.Serialization;

namespace StreamOrchestra.App.Models;

public enum HlsPlaylistKind
{
    Unknown,
    Master,
    Media
}

public enum HlsRenditionKind
{
    Unknown,
    Variant,
    Video,
    Audio,
    Subtitles
}

public enum HlsProgressDisposition
{
    NewEvidence,
    Duplicate,
    Stale,
    Rollback
}

public enum HlsEpochResetReason
{
    None,
    InitialObservation,
    Navigation,
    Session,
    Rendition,
    Source,
    Discontinuity,
    SequenceRollback
}

public sealed record SyncUrlIdentity(
    string SchemeBucket,
    string HostBucket,
    string PathBucket,
    string PersistenceHash);

public sealed record HlsByteRange(long Length, long? Offset)
{
    public bool IsValid => Length > 0 && Offset is null or >= 0;

    public string Canonical => $"{Length}@{Offset?.ToString() ?? "relative"}";
}

/// <summary>
/// RuntimeUri is retained only in memory while the playlist is parsed. PersistenceIdentity is used for
/// internal comparisons without retaining signed query strings.
/// </summary>
public sealed record HlsResourceReference(
    [property: JsonIgnore] string RuntimeUri,
    SyncUrlIdentity PersistenceIdentity,
    HlsByteRange? ByteRange = null);

public sealed record HlsMapReference(HlsResourceReference Resource);

public sealed record HlsPart
{
    public long MediaSequence { get; init; }

    public long DiscontinuitySequence { get; init; }

    public int Index { get; init; }

    public double DurationSeconds { get; init; }

    public required HlsResourceReference Resource { get; init; }

    public bool IsIndependent { get; init; }

    public bool IsGap { get; init; }
}

public sealed record HlsSegment
{
    public long MediaSequence { get; init; }

    public long DiscontinuitySequence { get; init; }

    public double DurationSeconds { get; init; }

    public required HlsResourceReference Resource { get; init; }

    public HlsMapReference? Map { get; init; }

    public IReadOnlyList<HlsPart> Parts { get; init; } = [];

    public bool IsGap { get; init; }

    public string? ProgramDateTimeText { get; init; }

    public DateTimeOffset? ProgramDateTimeUtc { get; init; }

    public bool ProgramDateTimeHasTimezone { get; init; }
}

public sealed record HlsVariant
{
    public required HlsResourceReference Resource { get; init; }

    public long? Bandwidth { get; init; }

    public string ResolutionBucket { get; init; } = "";

    public string CodecBucket { get; init; } = "";

    public string AudioGroupHash { get; init; } = "";
}

public sealed record HlsMediaRendition
{
    public HlsRenditionKind Kind { get; init; }

    public string GroupHash { get; init; } = "";

    public string NameHash { get; init; } = "";

    public HlsResourceReference? Resource { get; init; }

    public bool IsDefault { get; init; }

    public bool IsAutoselect { get; init; }
}

public sealed record HlsPrivateExtension(
    string NameBucket,
    [property: JsonIgnore] string RawValue,
    string ValidationStatus);

public sealed record HlsPreloadHint(
    string TypeBucket,
    HlsResourceReference? Resource);

public sealed record HlsRenditionReport(
    HlsResourceReference Resource,
    long? LastMediaSequence,
    int? LastPartIndex);

public sealed record HlsServerControl(
    bool CanBlockReload,
    double? HoldBackSeconds,
    double? PartHoldBackSeconds,
    double? CanSkipUntilSeconds,
    bool CanSkipDateRanges);

public sealed record HlsSkipMetadata(
    int? SkippedSegments,
    bool HasRecentlyRemovedDateRanges);

public sealed record HlsPlaylistDocument
{
    public HlsPlaylistKind Kind { get; init; }

    public HlsRenditionKind RenditionKind { get; init; }

    public required SyncUrlIdentity PlaylistIdentity { get; init; }

    [JsonIgnore]
    public string RuntimePlaylistUri { get; init; } = "";

    public long? MediaSequence { get; init; }

    public long? DiscontinuitySequence { get; init; }

    public double? TargetDurationSeconds { get; init; }

    public double? PartTargetDurationSeconds { get; init; }

    public bool HasEndList { get; init; }

    public IReadOnlyList<HlsVariant> Variants { get; init; } = [];

    public IReadOnlyList<HlsMediaRendition> Renditions { get; init; } = [];

    public IReadOnlyList<HlsSegment> Segments { get; init; } = [];

    public IReadOnlyList<HlsPart> TrailingParts { get; init; } = [];

    public IReadOnlyList<HlsPrivateExtension> PrivateExtensions { get; init; } = [];

    public IReadOnlyList<HlsPreloadHint> PreloadHints { get; init; } = [];

    public IReadOnlyList<HlsRenditionReport> RenditionReports { get; init; } = [];

    public HlsServerControl? ServerControl { get; init; }

    public HlsSkipMetadata? SkipMetadata { get; init; }

    public bool HasLowLatencySyntax { get; init; }

    public IReadOnlyList<string> WarningCodes { get; init; } = [];

    public DateTimeOffset? ResponseDateUtc { get; init; }
}

public sealed record HlsProgressKey
{
    public required string PersistenceHash { get; init; }

    public long? LastMediaSequence { get; init; }

    public long? LastDiscontinuitySequence { get; init; }

    public int? LastPartIndex { get; init; }

    public string TailResourceHash { get; init; } = "";

    public HlsByteRange? TailByteRange { get; init; }

    public string TailProgramDateTimeHash { get; init; } = "";
}

public sealed record HlsPlaylistParseRequest(
    string PlaylistText,
    string RequestUri,
    string? ContentType,
    DateTimeOffset? ResponseDateUtc,
    DateTimeOffset ObservedAtUtc);

public sealed record HlsPlaylistParseResult(
    HlsPlaylistDocument Document,
    HlsProgressKey? ProgressKey,
    TimelineObservation? TimelineCandidate);

public sealed record HlsPlaylistTrackingContext
{
    public int SlotId { get; init; }

    public int NavigationGeneration { get; init; }

    public string SessionIdentity { get; init; } = "";

    public string TimelineLaneIdentity { get; init; } = "";

    public string PlaylistIdentityHash { get; init; } = "";

    public HlsRenditionKind RenditionKind { get; init; }

    public string SourceIdentity { get; init; } = "";

    public required HlsPlaylistDocument Document { get; init; }

    public required HlsProgressKey ProgressKey { get; init; }

    public long ObservedMonotonicTicks { get; init; }

    public long MonotonicFrequency { get; init; } = TimeSpan.TicksPerSecond;
}

public sealed record HlsPlaylistTrackingResult(
    long SourceEpoch,
    HlsProgressDisposition Disposition,
    HlsEpochResetReason ResetReason,
    bool IsEstimatorEvidence,
    bool IsEpochStable,
    int IndependentEvidenceCount);

public sealed record HlsTimestampUnwrapResult(
    long UnwrappedValue,
    bool IsRollover,
    bool WasReset);
