using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class SyncPilotCapabilityAnalyzer
{
    public const int TargetUnitCount = 60;
    public const int DevelopmentUnitTarget = 42;
    public const int HoldoutUnitTarget = 18;
    public const int TargetBroadcastDayClusterCount = 20;
    public const int TargetDistinctChannelCount = 12;
    private static readonly TimeSpan WarmUpDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MinimumObservationDuration = TimeSpan.FromMinutes(17);

    public static SyncPilotCapabilityReport Analyze(
        IReadOnlyList<SyncTelemetrySnapshot> snapshots,
        IReadOnlyList<string>? privacyViolations = null,
        DateTimeOffset? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var units = snapshots
            .Select((snapshot, index) => new Unit(snapshot, index))
            .GroupBy(unit => unit.SessionId, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(unit => unit.Snapshot.GeneratedAtUtc)
                .ThenBy(unit => unit.InputIndex)
                .Last())
            .OrderBy(unit => unit.StartedAtUtc)
            .ThenBy(unit => unit.SessionId, StringComparer.Ordinal)
            .ToArray();
        var invalidReasons = units
            .SelectMany(ValidateUnit)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var eligible = units.Where(unit => !ValidateUnit(unit).Any()).ToArray();
        var primary = eligible
            .GroupBy(unit => unit.ChannelSetDayKey, StringComparer.Ordinal)
            .SelectMany(group => group.Take(3))
            .OrderBy(unit => unit.StartedAtUtc)
            .ThenBy(unit => unit.SessionId, StringComparer.Ordinal)
            .ToArray();
        var sensitivityOnlyCount = eligible.Length - primary.Length;
        var valid = primary;
        var availability = new List<SyncPilotAvailabilityMetric>();

        Add(availability, "pdt", valid, unit => unit.Playlists.Any(item => item.HasProgramDateTime));
        Add(availability, "pdt-explicit-timezone", valid, unit =>
            unit.Playlists.Any(item => item.ProgramDateTimeCount > 0 &&
                                       item.ProgramDateTimeTimezoneCount == item.ProgramDateTimeCount));
        Add(availability, "pdt-millisecond-or-finer", valid, unit => unit.Playlists.Any(item =>
            item.ProgramDateTimePrecisionBucket is "milliseconds" or "microseconds-or-finer"));
        Add(availability, "discontinuity-observed", valid, unit =>
            unit.Playlists.Any(item => item.DiscontinuityCount > 0 || item.EpochResetReason == "discontinuity"));
        Add(availability, "source-switch-observed", valid, unit =>
            unit.Playlists.Any(item => item.IsSourceSwitch));
        Add(availability, "master-playlist", valid, unit =>
            unit.Playlists.Any(item => item.PlaylistKind == "master"));
        Add(availability, "video-rendition", valid, unit =>
            unit.Playlists.Any(item => item.RenditionKind == "video" || item.VideoRenditionCount > 0));
        Add(availability, "audio-rendition", valid, unit =>
            unit.Playlists.Any(item => item.RenditionKind == "audio" || item.AudioRenditionCount > 0));
        Add(availability, "master-rendition-association", valid, unit =>
            unit.Playlists.Any(item => item.MasterAssociationObserved));
        Add(availability, "extensionless-hls", valid, unit =>
            unit.Network.Any(item => item.IsExtensionlessResource) ||
            unit.Playlists.Any(item => item.IsExtensionlessResource));
        Add(availability, "hls-mime", valid, unit => unit.Network.Any(item =>
            item.ContentTypeBucket.Contains("mpegurl", StringComparison.Ordinal)));
        Add(availability, "duplicate", valid, unit => unit.Playlists.Any(item => item.IsDuplicate));
        Add(availability, "stale", valid, unit => unit.Playlists.Any(item => item.IsStale));
        Add(availability, "rollback", valid, unit => unit.Playlists.Any(item => item.IsRollback));
        Add(availability, "ll-hls-syntax", valid, unit =>
            unit.Playlists.Any(item => item.HasLowLatencySyntax));
        Add(availability, "vendor-extension", valid, unit =>
            unit.Playlists.Any(item => item.ExtensionBuckets.Any(bucket => bucket != "ll-hls")));
        Add(availability, "request-start-timing", valid, unit =>
            unit.Network.Any(item => item.RequestStartObserved));
        Add(availability, "headers-timing", valid, unit =>
            unit.Network.Any(item => item.HeadersReceivedAt is not null));
        Add(availability, "body-timing", valid, unit =>
            unit.Network.Any(item => item.BodyCompletedAt is not null));
        Add(availability, "http-date", valid, unit => unit.Network.Any(item => item.HasDateHeader));
        Add(availability, "http-age", valid, unit => unit.Network.Any(item => item.HasAgeHeader));
        Add(availability, "rvfc", valid, unit => unit.Players.Any(item => item.UsedVideoFrameCallback));
        Add(availability, "fresh-rvfc", valid, unit => unit.Players.Any(item =>
            item.UsedVideoFrameCallback && item.FrameAgeMilliseconds is >= 0 and <= 2000));
        Add(availability, "frame-metrics", valid, unit => unit.Players.Any(item =>
            item.TotalVideoFrames is >= 0 || item.PresentedFrames is >= 0));

        var bucketCounts = new List<SyncPilotBucketCount>();
        AddBuckets(bucketCounts, valid, "pdt-precision", unit =>
            unit.Playlists.Select(item => item.ProgramDateTimePrecisionBucket));
        AddBuckets(bucketCounts, valid, "tracking-disposition", unit =>
            unit.Playlists.Select(item => item.TrackingDisposition));
        AddBuckets(bucketCounts, valid, "epoch-reset", unit =>
            unit.Playlists.Select(item => item.EpochResetReason));
        AddBuckets(bucketCounts, valid, "extension", unit =>
            unit.Playlists.SelectMany(item => item.ExtensionBuckets));
        AddBuckets(bucketCounts, valid, "content-type", unit =>
            unit.Network.Select(item => item.ContentTypeBucket));
        AddBuckets(bucketCounts, valid, "correlation", unit =>
            unit.Network.Select(item => $"{item.CorrelationSource}:{item.CorrelationStatus}"));
        AddBuckets(bucketCounts, valid, "network", unit => unit.Players.Select(item => item.NetworkBucket));
        AddBuckets(bucketCounts, valid, "pc-load", unit => unit.Players.Select(item => item.PcLoadBucket));
        AddBuckets(bucketCounts, valid, "channel", unit => unit.Players.Select(item => item.ChannelId));
        AddBuckets(bucketCounts, valid, "quality", unit => unit.Players.Select(item => item.QualityBucket));
        AddBuckets(bucketCounts, valid, "cdn", unit => unit.Players.Select(item => item.CdnBucket));
        AddBuckets(bucketCounts, valid, "playback", unit => unit.Players.Select(item => item.PlaybackBucket));
        AddBuckets(bucketCounts, valid, "source", unit => unit.Players.Select(item => item.SourceBucket));

        var validCount = valid.Length;
        var clusterCount = valid
            .Select(unit => unit.BroadcastDayClusterKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var channelCount = valid
            .SelectMany(unit => unit.Players.Select(player => player.ChannelId))
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new SyncPilotCapabilityReport
        {
            GeneratedAtUtc = (generatedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            InputSnapshotCount = snapshots.Count,
            IndependentUnitCount = units.Length,
            ValidUnitCount = validCount,
            SensitivityOnlyUnitCount = sensitivityOnlyCount,
            BroadcastDayClusterCount = clusterCount,
            DistinctChannelCount = channelCount,
            DevelopmentUnitCount = Math.Min(validCount, DevelopmentUnitTarget),
            TemporalHoldoutUnitCount = Math.Min(
                Math.Max(0, validCount - DevelopmentUnitTarget),
                HoldoutUnitTarget),
            DroppedEventCount = valid.Sum(unit => Math.Max(0, unit.Snapshot.DroppedEventCount)),
            CollectionStatus = validCount == 0
                ? "not-started"
                : validCount >= TargetUnitCount &&
                  clusterCount >= TargetBroadcastDayClusterCount &&
                  channelCount >= TargetDistinctChannelCount
                    ? "ready-for-analysis"
                    : "collecting",
            InvalidUnitReasons = invalidReasons,
            PrivacyViolations = privacyViolations?.Distinct(StringComparer.Ordinal).Order().ToArray() ?? [],
            Availability = availability,
            BucketCounts = bucketCounts
                .OrderBy(item => item.Dimension, StringComparer.Ordinal)
                .ThenBy(item => item.Bucket, StringComparer.Ordinal)
                .ToArray(),
            CdpCorrelation = CdpCorrelationCoverageGate.Evaluate(valid
                .Select(unit => unit.Snapshot)
                .ToArray())
        };
    }

    private static IEnumerable<string> ValidateUnit(Unit unit)
    {
        if (!unit.Snapshot.IsEnabled)
        {
            yield return $"{unit.SessionId}:disabled-snapshot";
        }

        if (unit.Snapshot.SchemaVersion != SyncTelemetrySchema.SchemaVersion)
        {
            yield return $"{unit.SessionId}:schema-{unit.Snapshot.SchemaVersion}";
        }

        if (unit.Snapshot.Sessions.Count == 0)
        {
            yield return $"{unit.SessionId}:missing-session-event";
        }

        else if (unit.Snapshot.GeneratedAtUtc.ToUniversalTime() - unit.StartedAtUtc <
                 MinimumObservationDuration)
        {
            yield return $"{unit.SessionId}:duration-shorter-than-17-minutes";
        }

        if (unit.Players.Select(item => item.SlotId).Distinct().Count() < 2)
        {
            yield return $"{unit.SessionId}:fewer-than-two-player-slots";
        }
    }

    private static void Add(
        ICollection<SyncPilotAvailabilityMetric> destination,
        string metricId,
        IReadOnlyList<Unit> units,
        Func<Unit, bool> observed)
    {
        var observedCount = units.Count(observed);
        destination.Add(new SyncPilotAvailabilityMetric(
            metricId,
            units.Count,
            observedCount,
            units.Count == 0 ? null : observedCount / (double)units.Count));
    }

    private static void AddBuckets(
        ICollection<SyncPilotBucketCount> destination,
        IReadOnlyList<Unit> units,
        string dimension,
        Func<Unit, IEnumerable<string>> selector)
    {
        var counts = units
            .SelectMany(unit => selector(unit)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Select(value => (Unit: unit.SessionId, Value: value)))
            .GroupBy(item => item.Value, StringComparer.Ordinal)
            .Select(group => new SyncPilotBucketCount(dimension, group.Key, group.Count()));
        foreach (var count in counts)
        {
            destination.Add(count);
        }
    }

    private sealed record Unit(SyncTelemetrySnapshot Snapshot, int InputIndex)
    {
        public string SessionId => Snapshot.Sessions.FirstOrDefault()?.SessionId ?? $"missing-{InputIndex}";

        public DateTimeOffset StartedAtUtc => Snapshot.Sessions.FirstOrDefault()?.StartedAt.Utc ??
                                              Snapshot.GeneratedAtUtc;

        public IReadOnlyList<SyncNetworkTelemetry> Network => Snapshot.Network
            .Where(item => IsAfterWarmUp(item.RequestStartedAt))
            .ToArray();

        public IReadOnlyList<SyncPlaylistTelemetry> Playlists => Snapshot.Playlists
            .Where(item => IsAfterWarmUp(item.ObservedAt))
            .ToArray();

        public IReadOnlyList<SyncPlayerTelemetry> Players => Snapshot.Players
            .Where(item => IsAfterWarmUp(item.HostReceivedAt))
            .ToArray();

        public string ChannelSetDayKey => $"{StartedAtUtc:yyyy-MM-dd}:" + string.Join(
            ",",
            Players.Select(player => player.ChannelId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        public string BroadcastDayClusterKey => $"{StartedAtUtc:yyyy-MM-dd}:" + string.Join(
            ",",
            Players.Select(player => player.BroadcastSessionId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        private bool IsAfterWarmUp(SyncTelemetryClockSample sample)
        {
            var session = Snapshot.Sessions.FirstOrDefault();
            if (session is { MonotonicFrequency: > 0 } &&
                sample.MonotonicTicks > 0 &&
                session.StartedAt.MonotonicTicks > 0)
            {
                var warmupTicks = WarmUpDuration.TotalSeconds * session.MonotonicFrequency;
                return sample.MonotonicTicks - session.StartedAt.MonotonicTicks >= warmupTicks;
            }

            return sample.Utc.ToUniversalTime() - StartedAtUtc >= WarmUpDuration;
        }
    }
}
