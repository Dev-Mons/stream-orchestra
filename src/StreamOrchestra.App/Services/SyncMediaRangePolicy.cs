using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class SyncMediaRangePolicy
{
    private const double MergeToleranceSeconds = 0.001;

    public static IReadOnlyList<MediaTimeRange> Normalize(
        IEnumerable<MediaTimeRange>? ranges,
        int maximumCount = 32)
    {
        if (ranges is null)
        {
            return [];
        }

        var ordered = ranges
            .Where(range => range is not null && range.IsValid)
            .OrderBy(range => range.StartSeconds)
            .ThenBy(range => range.EndSeconds)
            .Take(Math.Clamp(maximumCount, 1, 128))
            .ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var normalized = new List<MediaTimeRange>(ordered.Length) { ordered[0] };
        foreach (var range in ordered.Skip(1))
        {
            var previous = normalized[^1];
            if (range.StartSeconds <= previous.EndSeconds + MergeToleranceSeconds)
            {
                normalized[^1] = previous with
                {
                    EndSeconds = Math.Max(previous.EndSeconds, range.EndSeconds)
                };
            }
            else
            {
                normalized.Add(range);
            }
        }

        return normalized;
    }

    public static IReadOnlyList<MediaTimeRange> GetSeekableRanges(SyncMemberSnapshot snapshot)
    {
        var ranges = Normalize(snapshot.SeekableRanges);
        if (ranges.Count > 0)
        {
            return ranges;
        }

        return snapshot.SeekableStart is { } start && snapshot.SeekableEnd is { } end && end > start
            ? [new MediaTimeRange(start, end)]
            : [];
    }

    public static IReadOnlyList<MediaTimeRange> GetBufferedRanges(SyncMemberSnapshot snapshot) =>
        Normalize(snapshot.BufferedRanges);

    public static IReadOnlyList<MediaTimeRange> GetPlayableRanges(SyncMemberSnapshot snapshot)
    {
        var seekable = Normalize(snapshot.SeekableRanges);
        var buffered = Normalize(snapshot.BufferedRanges);
        if (seekable.Count == 0 || buffered.Count == 0)
        {
            return [];
        }

        var intersections = new List<MediaTimeRange>();
        foreach (var seekableRange in seekable)
        {
            foreach (var bufferedRange in buffered)
            {
                var start = Math.Max(seekableRange.StartSeconds, bufferedRange.StartSeconds);
                var end = Math.Min(seekableRange.EndSeconds, bufferedRange.EndSeconds);
                if (end > start)
                {
                    intersections.Add(new MediaTimeRange(start, end));
                }
            }
        }

        return Normalize(intersections);
    }

    public static MediaTimeRange? FindContainingRange(
        IEnumerable<MediaTimeRange>? ranges,
        double mediaTimeSeconds,
        double marginSeconds = 0)
    {
        return Normalize(ranges).FirstOrDefault(range => range.Contains(mediaTimeSeconds, marginSeconds));
    }

    public static bool IsSeekTargetValid(
        SyncMemberSnapshot snapshot,
        double mediaTarget,
        double marginSeconds = 0.05) =>
        FindContainingRange(GetSeekableRanges(snapshot), mediaTarget, marginSeconds) is not null;

    public static bool IsHighConfidenceSeekTargetValid(
        SyncMemberSnapshot snapshot,
        double mediaTarget,
        double marginSeconds = 0.05) =>
        snapshot.SeekableRanges.Count > 0 &&
        snapshot.BufferedRanges.Count > 0 &&
        FindContainingRange(snapshot.SeekableRanges, mediaTarget, marginSeconds) is not null &&
        FindContainingRange(snapshot.BufferedRanges, mediaTarget, marginSeconds) is not null;
}
