using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class HlsTimelineParser
{
    private const string FirstTimestampPrefix = "#EXT-X-FIRST-SEGMENT-TIMESTAMP:";
    private static readonly Regex ExplicitTimezonePattern = new(
        @"(?:[zZ]|[+-][0-9]{2}:[0-9]{2})\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly SyncTelemetryPrivacy _privacy;

    public HlsTimelineParser(SyncTelemetryPrivacy? privacy = null)
    {
        _privacy = privacy ?? new SyncTelemetryPrivacy();
    }

    public static bool IsHlsPlaylistResource(string? requestUri, string? contentType)
    {
        if (Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) &&
            (uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
             uri.AbsolutePath.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return mediaType?.Equals("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) == true ||
               mediaType?.Equals("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) == true ||
               mediaType?.Equals("audio/mpegurl", StringComparison.OrdinalIgnoreCase) == true ||
               mediaType?.Equals("audio/x-mpegurl", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Compatibility adapter for timeline-only callers. HTTP Date remains response metadata and is never
    /// promoted to a playlist-tail clock.
    /// </summary>
    public TimelineObservation? Parse(
        string playlist,
        DateTimeOffset? responseDateUtc,
        DateTimeOffset observedAtUtc)
    {
        return ParsePlaylist(new HlsPlaylistParseRequest(
            playlist,
            "https://invalid.local/playlist.m3u8",
            "application/vnd.apple.mpegurl",
            responseDateUtc,
            observedAtUtc))?.TimelineCandidate;
    }

    public HlsPlaylistParseResult? ParsePlaylist(HlsPlaylistParseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlaylistText))
        {
            return null;
        }

        var lines = request.PlaylistText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0 || !lines[0].Equals("#EXTM3U", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Uri.TryCreate(request.RequestUri, UriKind.Absolute, out var baseUri);
        var playlistIdentity = _privacy.CreateUrlIdentity(request.RequestUri);
        var variants = new List<HlsVariant>();
        var renditions = new List<HlsMediaRendition>();
        var segments = new List<HlsSegment>();
        var pendingParts = new List<HlsPart>();
        var privateExtensions = new List<HlsPrivateExtension>();
        var preloadHints = new List<HlsPreloadHint>();
        var renditionReports = new List<HlsRenditionReport>();
        var warnings = new HashSet<string>(StringComparer.Ordinal);

        long? mediaSequence = null;
        long? discontinuitySequence = null;
        var currentDiscontinuity = 0L;
        double? targetDuration = null;
        double? partTarget = null;
        var hasEndList = false;
        var hasLowLatencySyntax = false;
        HlsServerControl? serverControl = null;
        HlsSkipMetadata? skipMetadata = null;
        var skippedSegments = 0;
        double? pendingDuration = null;
        HlsByteRange? pendingByteRange = null;
        HlsMapReference? currentMap = null;
        var pendingGap = false;
        string? pendingPdtText = null;
        DateTimeOffset? pendingPdtUtc = null;
        var pendingPdtHasTimezone = false;
        Dictionary<string, string>? pendingVariantAttributes = null;

        foreach (var line in lines.Skip(1))
        {
            if (!line.StartsWith('#'))
            {
                if (pendingVariantAttributes is not null)
                {
                    var resource = CreateResource(baseUri, line, null, warnings);
                    if (resource is not null)
                    {
                        variants.Add(new HlsVariant
                        {
                            Resource = resource,
                            Bandwidth = ParseLong(Attribute(pendingVariantAttributes, "BANDWIDTH")),
                            ResolutionBucket = NormalizeResolution(Attribute(pendingVariantAttributes, "RESOLUTION")),
                            CodecBucket = BucketCodecs(Attribute(pendingVariantAttributes, "CODECS")),
                            AudioGroupHash = _privacy.CreateOpaqueIdentity(
                                "hls-group",
                                Attribute(pendingVariantAttributes, "AUDIO"))
                        });
                    }

                    pendingVariantAttributes = null;
                    continue;
                }

                if (pendingDuration is null)
                {
                    continue;
                }

                var segmentResource = ResolveImplicitByteRange(
                    CreateResource(baseUri, line, pendingByteRange, warnings),
                    segments.LastOrDefault()?.Resource,
                    warnings);
                if (segmentResource is not null)
                {
                    var sequence = (mediaSequence ?? 0) + skippedSegments + segments.Count;
                    var segment = new HlsSegment
                    {
                        MediaSequence = sequence,
                        DiscontinuitySequence = currentDiscontinuity,
                        DurationSeconds = pendingDuration.Value,
                        Resource = segmentResource,
                        Map = currentMap,
                        Parts = pendingParts.ToArray(),
                        IsGap = pendingGap,
                        ProgramDateTimeText = pendingPdtText,
                        ProgramDateTimeUtc = pendingPdtUtc,
                        ProgramDateTimeHasTimezone = pendingPdtHasTimezone
                    };
                    ValidatePdtContinuity(segments.LastOrDefault(), segment, warnings);
                    segments.Add(segment);
                }

                pendingDuration = null;
                pendingByteRange = null;
                pendingGap = false;
                pendingPdtText = null;
                pendingPdtUtc = null;
                pendingPdtHasTimezone = false;
                pendingParts.Clear();
                continue;
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                pendingDuration = ParsePositiveDouble(ValueAfterColon(line).Split(',', 2)[0]);
                if (pendingDuration is null)
                {
                    warnings.Add("malformed-duration");
                }

                continue;
            }

            if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
            {
                pendingVariantAttributes = ParseAttributes(ValueAfterColon(line));
                continue;
            }

            if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.OrdinalIgnoreCase))
            {
                var attributes = ParseAttributes(ValueAfterColon(line));
                var uriText = Attribute(attributes, "URI");
                renditions.Add(new HlsMediaRendition
                {
                    Kind = ParseRenditionKind(Attribute(attributes, "TYPE")),
                    GroupHash = _privacy.CreateOpaqueIdentity("hls-group", Attribute(attributes, "GROUP-ID")),
                    NameHash = _privacy.CreateOpaqueIdentity("hls-rendition", Attribute(attributes, "NAME")),
                    Resource = string.IsNullOrWhiteSpace(uriText)
                        ? null
                        : CreateResource(baseUri, uriText, null, warnings),
                    IsDefault = IsYes(Attribute(attributes, "DEFAULT")),
                    IsAutoselect = IsYes(Attribute(attributes, "AUTOSELECT"))
                });
                continue;
            }

            if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.OrdinalIgnoreCase))
            {
                mediaSequence = ParseLong(ValueAfterColon(line));
                continue;
            }

            if (line.StartsWith("#EXT-X-DISCONTINUITY-SEQUENCE:", StringComparison.OrdinalIgnoreCase))
            {
                discontinuitySequence = ParseLong(ValueAfterColon(line));
                currentDiscontinuity = discontinuitySequence ?? 0;
                continue;
            }

            if (line.Equals("#EXT-X-DISCONTINUITY", StringComparison.OrdinalIgnoreCase))
            {
                currentDiscontinuity++;
                continue;
            }

            if (line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.OrdinalIgnoreCase))
            {
                targetDuration = ParsePositiveDouble(ValueAfterColon(line));
                continue;
            }

            if (line.StartsWith("#EXT-X-PART-INF:", StringComparison.OrdinalIgnoreCase))
            {
                hasLowLatencySyntax = true;
                partTarget = ParsePositiveDouble(Attribute(ParseAttributes(ValueAfterColon(line)), "PART-TARGET"));
                continue;
            }

            if (line.StartsWith("#EXT-X-PART:", StringComparison.OrdinalIgnoreCase))
            {
                hasLowLatencySyntax = true;
                var attributes = ParseAttributes(ValueAfterColon(line));
                var duration = ParsePositiveDouble(Attribute(attributes, "DURATION"));
                var resource = CreateResource(
                    baseUri,
                    Attribute(attributes, "URI"),
                    ParseByteRange(Attribute(attributes, "BYTERANGE"), warnings),
                    warnings);
                resource = ResolveImplicitByteRange(
                    resource,
                    pendingParts.LastOrDefault()?.Resource ?? segments.LastOrDefault()?.Resource,
                    warnings);
                if (duration is null || resource is null)
                {
                    warnings.Add("malformed-playlist");
                    continue;
                }

                pendingParts.Add(new HlsPart
                {
                    MediaSequence = (mediaSequence ?? 0) + skippedSegments + segments.Count,
                    DiscontinuitySequence = currentDiscontinuity,
                    Index = pendingParts.Count,
                    DurationSeconds = duration.Value,
                    Resource = resource,
                    IsIndependent = IsYes(Attribute(attributes, "INDEPENDENT")),
                    IsGap = IsYes(Attribute(attributes, "GAP"))
                });
                continue;
            }

            if (line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.OrdinalIgnoreCase))
            {
                pendingByteRange = ParseByteRange(ValueAfterColon(line), warnings);
                continue;
            }

            if (line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase))
            {
                var attributes = ParseAttributes(ValueAfterColon(line));
                var mapResource = CreateResource(
                    baseUri,
                    Attribute(attributes, "URI"),
                    ParseByteRange(Attribute(attributes, "BYTERANGE"), warnings),
                    warnings);
                mapResource = ResolveImplicitByteRange(mapResource, currentMap?.Resource, warnings);
                currentMap = mapResource is null ? null : new HlsMapReference(mapResource);
                continue;
            }

            if (line.Equals("#EXT-X-GAP", StringComparison.OrdinalIgnoreCase))
            {
                pendingGap = true;
                continue;
            }

            if (line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:", StringComparison.OrdinalIgnoreCase))
            {
                pendingPdtText = ValueAfterColon(line).Trim();
                pendingPdtHasTimezone = ExplicitTimezonePattern.IsMatch(pendingPdtText);
                if (!pendingPdtHasTimezone)
                {
                    warnings.Add("pdt-timezone-missing");
                    pendingPdtUtc = null;
                }
                else if (DateTimeOffset.TryParse(
                             pendingPdtText,
                             CultureInfo.InvariantCulture,
                             DateTimeStyles.AdjustToUniversal,
                             out var parsedPdt))
                {
                    pendingPdtUtc = parsedPdt.ToUniversalTime();
                }
                else
                {
                    warnings.Add("malformed-playlist");
                    pendingPdtUtc = null;
                }

                continue;
            }

            if (line.StartsWith(FirstTimestampPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var rawValue = ValueAfterColon(line).Trim();
                var status = double.TryParse(
                                 rawValue,
                                 NumberStyles.Float,
                                 CultureInfo.InvariantCulture,
                                 out var vendorValue) &&
                             double.IsFinite(vendorValue) && vendorValue >= 0
                    ? "unverified"
                    : "malformed";
                if (status == "malformed")
                {
                    warnings.Add("malformed-vendor-timestamp");
                }

                privateExtensions.Add(new HlsPrivateExtension(
                    "ext-x-first-segment-timestamp",
                    rawValue,
                    status));
                continue;
            }

            if (line.StartsWith("#EXT-X-PRELOAD-HINT:", StringComparison.OrdinalIgnoreCase))
            {
                hasLowLatencySyntax = true;
                var attributes = ParseAttributes(ValueAfterColon(line));
                var hintResource = CreateResource(baseUri, Attribute(attributes, "URI"), null, warnings);
                preloadHints.Add(new HlsPreloadHint(
                    NormalizeCode(Attribute(attributes, "TYPE")),
                    hintResource));
                continue;
            }

            if (line.StartsWith("#EXT-X-RENDITION-REPORT:", StringComparison.OrdinalIgnoreCase))
            {
                hasLowLatencySyntax = true;
                var attributes = ParseAttributes(ValueAfterColon(line));
                var reportResource = CreateResource(baseUri, Attribute(attributes, "URI"), null, warnings);
                if (reportResource is not null)
                {
                    renditionReports.Add(new HlsRenditionReport(
                        reportResource,
                        ParseLong(Attribute(attributes, "LAST-MSN")),
                        ParseInt(Attribute(attributes, "LAST-PART"))));
                }

                continue;
            }

            if (line.StartsWith("#EXT-X-SERVER-CONTROL:", StringComparison.OrdinalIgnoreCase))
            {
                hasLowLatencySyntax = true;
                var attributes = ParseAttributes(ValueAfterColon(line));
                serverControl = new HlsServerControl(
                    IsYes(Attribute(attributes, "CAN-BLOCK-RELOAD")),
                    ParsePositiveDouble(Attribute(attributes, "HOLD-BACK")),
                    ParsePositiveDouble(Attribute(attributes, "PART-HOLD-BACK")),
                    ParsePositiveDouble(Attribute(attributes, "CAN-SKIP-UNTIL")),
                    IsYes(Attribute(attributes, "CAN-SKIP-DATERANGES")));
                continue;
            }

            if (line.StartsWith("#EXT-X-SKIP:", StringComparison.OrdinalIgnoreCase))
            {
                hasLowLatencySyntax = true;
                var attributes = ParseAttributes(ValueAfterColon(line));
                skipMetadata = new HlsSkipMetadata(
                    ParseInt(Attribute(attributes, "SKIPPED-SEGMENTS")),
                    !string.IsNullOrWhiteSpace(Attribute(attributes, "RECENTLY-REMOVED-DATERANGES")));
                skippedSegments = skipMetadata.SkippedSegments ?? 0;
                continue;
            }

            if (line.StartsWith("#EXT-X-SOOP-", StringComparison.OrdinalIgnoreCase))
            {
                var separator = line.IndexOf(':');
                var name = separator > 0 ? line[1..separator].ToLowerInvariant() : line[1..].ToLowerInvariant();
                privateExtensions.Add(new HlsPrivateExtension(
                    NormalizeCode(name),
                    separator > 0 ? line[(separator + 1)..] : "",
                    "unverified"));
                continue;
            }

            if (line.Equals("#EXT-X-ENDLIST", StringComparison.OrdinalIgnoreCase))
            {
                hasEndList = true;
            }
        }

        var hasMasterSyntax = variants.Count > 0 || renditions.Count > 0;
        var hasMediaSyntax = segments.Count > 0 || pendingParts.Count > 0 || mediaSequence is not null;
        var kind = hasMasterSyntax && hasMediaSyntax
            ? HlsPlaylistKind.Unknown
            : hasMasterSyntax
                ? HlsPlaylistKind.Master
                : hasMediaSyntax
                    ? HlsPlaylistKind.Media
                    : HlsPlaylistKind.Unknown;
        if (hasMasterSyntax && hasMediaSyntax)
        {
            warnings.Add("malformed-playlist");
        }
        var renditionKind = DetermineMediaRenditionKind(kind, request.ContentType, segments);
        var document = new HlsPlaylistDocument
        {
            Kind = kind,
            RenditionKind = renditionKind,
            PlaylistIdentity = playlistIdentity,
            RuntimePlaylistUri = request.RequestUri,
            MediaSequence = mediaSequence,
            DiscontinuitySequence = discontinuitySequence,
            TargetDurationSeconds = targetDuration,
            PartTargetDurationSeconds = partTarget,
            HasEndList = hasEndList,
            Variants = variants.ToArray(),
            Renditions = renditions.ToArray(),
            Segments = segments.ToArray(),
            TrailingParts = pendingParts.ToArray(),
            PrivateExtensions = privateExtensions.ToArray(),
            PreloadHints = preloadHints.ToArray(),
            RenditionReports = renditionReports.ToArray(),
            ServerControl = serverControl,
            SkipMetadata = skipMetadata,
            HasLowLatencySyntax = hasLowLatencySyntax,
            WarningCodes = warnings.Order().ToArray(),
            ResponseDateUtc = request.ResponseDateUtc?.ToUniversalTime()
        };
        var progressKey = CreateProgressKey(document);
        var candidate = CreateTimelineCandidate(document, progressKey, request.ObservedAtUtc);
        return new HlsPlaylistParseResult(document, progressKey, candidate);
    }

    private TimelineObservation? CreateTimelineCandidate(
        HlsPlaylistDocument document,
        HlsProgressKey? progressKey,
        DateTimeOffset observedAtUtc)
    {
        if (document.Kind != HlsPlaylistKind.Media || document.Segments.Count == 0 || progressKey is null)
        {
            return null;
        }

        var tailDiscontinuity = document.TrailingParts.LastOrDefault()?.DiscontinuitySequence ??
                                document.Segments[^1].DiscontinuitySequence;
        var anchorIndex = -1;
        for (var index = document.Segments.Count - 1; index >= 0; index--)
        {
            var segment = document.Segments[index];
            if (segment.DiscontinuitySequence != tailDiscontinuity)
            {
                break;
            }

            if (segment.ProgramDateTimeUtc is not null && segment.ProgramDateTimeHasTimezone)
            {
                anchorIndex = index;
                break;
            }
        }

        if (anchorIndex < 0)
        {
            return null;
        }

        var anchor = document.Segments[anchorIndex];
        var duration = document.Segments
            .Skip(anchorIndex)
            .Where(segment => segment.DiscontinuitySequence == tailDiscontinuity)
            .Sum(segment => segment.DurationSeconds);
        duration += document.TrailingParts
            .Where(part => part.DiscontinuitySequence == tailDiscontinuity)
            .Sum(part => part.DurationSeconds);
        var durations = document.Segments.Select(segment => segment.DurationSeconds).ToArray();
        return new TimelineObservation
        {
            Source = SyncTimelineSource.ProgramDateTime,
            EdgeUtc = anchor.ProgramDateTimeUtc!.Value.AddSeconds(duration),
            MediaToUtcOffsetMs = null,
            SegmentDurationSec = Median(durations),
            Confidence = 0.65,
            ObservedAtUtc = observedAtUtc.ToUniversalTime(),
            PlaylistIdentityHash = document.PlaylistIdentity.PersistenceHash,
            CdnHostBucket = document.PlaylistIdentity.HostBucket,
            RenditionKind = document.RenditionKind,
            ProgressKeyHash = progressKey.PersistenceHash
        };
    }

    private HlsProgressKey? CreateProgressKey(HlsPlaylistDocument document)
    {
        HlsResourceReference? tailResource = null;
        long? lastMediaSequence = null;
        long? lastDiscontinuity = null;
        int? lastPartIndex = null;
        if (document.TrailingParts.Count > 0)
        {
            var part = document.TrailingParts[^1];
            tailResource = part.Resource;
            lastMediaSequence = part.MediaSequence;
            lastDiscontinuity = part.DiscontinuitySequence;
            lastPartIndex = part.Index;
        }
        else if (document.Segments.Count > 0)
        {
            var segment = document.Segments[^1];
            tailResource = segment.Resource;
            lastMediaSequence = segment.MediaSequence;
            lastDiscontinuity = segment.DiscontinuitySequence;
        }

        if (tailResource is null)
        {
            return null;
        }

        var tailProgramDateTime = document.Segments
            .LastOrDefault(segment => segment.DiscontinuitySequence == lastDiscontinuity &&
                                      !string.IsNullOrWhiteSpace(segment.ProgramDateTimeText))
            ?.ProgramDateTimeText;
        var tailProgramDateTimeHash = _privacy.CreateOpaqueIdentity(
            "hls-tail-pdt",
            tailProgramDateTime);
        var canonical = string.Join(
            "|",
            document.PlaylistIdentity.PersistenceHash,
            lastDiscontinuity?.ToString(CultureInfo.InvariantCulture),
            lastMediaSequence?.ToString(CultureInfo.InvariantCulture),
            lastPartIndex?.ToString(CultureInfo.InvariantCulture),
            tailResource.PersistenceIdentity.PersistenceHash,
            tailResource.ByteRange?.Canonical ?? "none",
            tailProgramDateTimeHash);
        return new HlsProgressKey
        {
            PersistenceHash = _privacy.CreateOpaqueIdentity("hls-progress", canonical),
            LastMediaSequence = lastMediaSequence,
            LastDiscontinuitySequence = lastDiscontinuity,
            LastPartIndex = lastPartIndex,
            TailResourceHash = tailResource.PersistenceIdentity.PersistenceHash,
            TailByteRange = tailResource.ByteRange,
            TailProgramDateTimeHash = tailProgramDateTimeHash
        };
    }

    private static HlsResourceReference? ResolveImplicitByteRange(
        HlsResourceReference? resource,
        HlsResourceReference? previous,
        ISet<string> warnings)
    {
        if (resource?.ByteRange is not { Offset: null } range)
        {
            return resource;
        }

        if (previous?.ByteRange is not { Offset: { } previousOffset } previousRange ||
            !previous.PersistenceIdentity.PersistenceHash.Equals(
                resource.PersistenceIdentity.PersistenceHash,
                StringComparison.Ordinal) ||
            previousOffset > long.MaxValue - previousRange.Length)
        {
            warnings.Add("invalid-byte-range");
            return null;
        }

        return resource with
        {
            ByteRange = range with { Offset = previousOffset + previousRange.Length }
        };
    }

    private HlsResourceReference? CreateResource(
        Uri? baseUri,
        string? rawValue,
        HlsByteRange? byteRange,
        ISet<string> warnings)
    {
        var unquoted = Unquote(rawValue);
        if (string.IsNullOrWhiteSpace(unquoted) ||
            !Uri.TryCreate(baseUri, unquoted, out var resolved) ||
            !resolved.IsAbsoluteUri)
        {
            warnings.Add("invalid-uri");
            return null;
        }

        var builder = new UriBuilder(resolved) { Fragment = "" };
        var runtimeUri = builder.Uri.AbsoluteUri;
        return new HlsResourceReference(runtimeUri, _privacy.CreateUrlIdentity(runtimeUri), byteRange);
    }

    private static void ValidatePdtContinuity(
        HlsSegment? previous,
        HlsSegment current,
        ISet<string> warnings)
    {
        if (previous?.ProgramDateTimeUtc is null || current.ProgramDateTimeUtc is null ||
            previous.DiscontinuitySequence != current.DiscontinuitySequence)
        {
            return;
        }

        var expected = previous.ProgramDateTimeUtc.Value.AddSeconds(previous.DurationSeconds);
        if (Math.Abs((current.ProgramDateTimeUtc.Value - expected).TotalMilliseconds) > 250)
        {
            warnings.Add("pdt-discontinuous");
        }
    }

    private static HlsRenditionKind DetermineMediaRenditionKind(
        HlsPlaylistKind kind,
        string? contentType,
        IReadOnlyList<HlsSegment> segments)
    {
        if (kind == HlsPlaylistKind.Master)
        {
            return HlsRenditionKind.Variant;
        }

        var mediaType = contentType?.Split(';', 2)[0].Trim();
        if (mediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return HlsRenditionKind.Audio;
        }

        if (mediaType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return HlsRenditionKind.Video;
        }

        var extensions = segments
            .Select(segment => Path.GetExtension(new Uri(segment.Resource.RuntimeUri).AbsolutePath))
            .Where(extension => !string.IsNullOrEmpty(extension))
            .Select(extension => extension.ToLowerInvariant())
            .Distinct()
            .ToArray();
        if (extensions.Length > 0 && extensions.All(extension => extension is ".aac" or ".m4a" or ".mp3"))
        {
            return HlsRenditionKind.Audio;
        }

        if (extensions.Length > 0 && extensions.All(extension => extension is ".vtt" or ".webvtt"))
        {
            return HlsRenditionKind.Subtitles;
        }

        return HlsRenditionKind.Unknown;
    }

    private static HlsRenditionKind ParseRenditionKind(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "AUDIO" => HlsRenditionKind.Audio,
        "VIDEO" => HlsRenditionKind.Video,
        "SUBTITLES" => HlsRenditionKind.Subtitles,
        _ => HlsRenditionKind.Unknown
    };

    private static Dictionary<string, string> ParseAttributes(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var current = new StringBuilder();
        var quoted = false;
        foreach (var character in value)
        {
            if (character == '"')
            {
                quoted = !quoted;
                current.Append(character);
                continue;
            }

            if (character == ',' && !quoted)
            {
                AddAttribute(result, current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        AddAttribute(result, current.ToString());
        return result;
    }

    private static void AddAttribute(IDictionary<string, string> attributes, string item)
    {
        var separator = item.IndexOf('=');
        if (separator <= 0)
        {
            return;
        }

        attributes[item[..separator].Trim()] = Unquote(item[(separator + 1)..]) ?? "";
    }

    private static string? Attribute(IReadOnlyDictionary<string, string> attributes, string name) =>
        attributes.TryGetValue(name, out var value) ? value : null;

    private static string ValueAfterColon(string line)
    {
        var separator = line.IndexOf(':');
        return separator >= 0 ? line[(separator + 1)..] : "";
    }

    private static string? Unquote(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is { Length: >= 2 } && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private static double? ParsePositiveDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed) && parsed > 0
            ? parsed
            : null;

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;

    private static HlsByteRange? ParseByteRange(string? value, ISet<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var pieces = Unquote(value)!.Split('@', 2);
        if (!long.TryParse(pieces[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ||
            length <= 0 ||
            (pieces.Length == 2 &&
             (!long.TryParse(pieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) ||
              offset < 0)))
        {
            warnings.Add("invalid-byte-range");
            return null;
        }

        return new HlsByteRange(
            length,
            pieces.Length == 2
                ? long.Parse(pieces[1], CultureInfo.InvariantCulture)
                : null);
    }

    private static bool IsYes(string? value) => value?.Equals("YES", StringComparison.OrdinalIgnoreCase) == true;

    private static string NormalizeResolution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Regex.IsMatch(value, @"\A[0-9]{2,5}x[0-9]{2,5}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return "unknown";
        }

        return value.ToLowerInvariant();
    }

    private static string BucketCodecs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var codecs = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(codec => codec.Split('.', 2)[0].ToLowerInvariant())
            .Where(codec => codec.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
            .Distinct()
            .Order()
            .Take(4)
            .ToArray();
        return codecs.Length == 0 ? "unknown" : string.Join('+', codecs);
    }

    private static string NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = new string(value.Trim().ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character == '-')
            .Take(32)
            .ToArray());
        return normalized.Length == 0 ? "unknown" : normalized;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }
}
