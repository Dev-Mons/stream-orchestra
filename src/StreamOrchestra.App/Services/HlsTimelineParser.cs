using System.Globalization;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class HlsTimelineParser
{
    private const string FirstTimestampPrefix = "#EXT-X-FIRST-SEGMENT-TIMESTAMP:";
    private const string ProgramDateTimePrefix = "#EXT-X-PROGRAM-DATE-TIME:";
    private const string DurationPrefix = "#EXTINF:";

    public TimelineObservation? Parse(
        string playlist,
        DateTimeOffset? responseDateUtc,
        DateTimeOffset observedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(playlist))
        {
            return null;
        }

        var durations = new List<double>();
        double? firstPtsSec = null;
        DateTimeOffset? programDateTimeUtc = null;
        var programDateTimeDurationIndex = -1;

        foreach (var rawLine in playlist.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(DurationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[DurationPrefix.Length..].Split(',')[0];
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) &&
                    duration > 0)
                {
                    durations.Add(duration);
                }

                continue;
            }

            if (line.StartsWith(FirstTimestampPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[FirstTimestampPrefix.Length..];
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ticks) &&
                    ticks >= 0)
                {
                    firstPtsSec = ticks / 10_000_000d;
                }

                continue;
            }

            if (programDateTimeUtc is null &&
                line.StartsWith(ProgramDateTimePrefix, StringComparison.OrdinalIgnoreCase) &&
                DateTimeOffset.TryParse(
                    line[ProgramDateTimePrefix.Length..],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedDate))
            {
                programDateTimeUtc = parsedDate;
                programDateTimeDurationIndex = durations.Count;
            }
        }

        if (durations.Count == 0)
        {
            return null;
        }

        var totalDurationSec = durations.Sum();
        var segmentDurationSec = Median(durations);

        if (programDateTimeUtc is not null)
        {
            var durationBeforeProgramDateTime = durations.Take(programDateTimeDurationIndex).Sum();
            var durationFromProgramDateTime = durations.Skip(programDateTimeDurationIndex).Sum();
            var edgeUtc = programDateTimeUtc.Value.AddSeconds(durationFromProgramDateTime);
            double? mediaToUtcOffsetMs = firstPtsSec is null
                ? null
                : programDateTimeUtc.Value.ToUnixTimeMilliseconds() -
                  (firstPtsSec.Value + durationBeforeProgramDateTime) * 1000;

            return new TimelineObservation
            {
                Source = SyncTimelineSource.ProgramDateTime,
                EdgeUtc = edgeUtc,
                MediaToUtcOffsetMs = mediaToUtcOffsetMs,
                SegmentDurationSec = segmentDurationSec,
                Confidence = 1,
                ObservedAtUtc = observedAtUtc
            };
        }

        if (firstPtsSec is not null && responseDateUtc is not null)
        {
            var edgePtsSec = firstPtsSec.Value + totalDurationSec;
            return new TimelineObservation
            {
                Source = SyncTimelineSource.CdnDate,
                EdgeUtc = responseDateUtc.Value,
                MediaToUtcOffsetMs = responseDateUtc.Value.ToUnixTimeMilliseconds() - edgePtsSec * 1000,
                SegmentDurationSec = segmentDurationSec,
                Confidence = 0.55,
                ObservedAtUtc = observedAtUtc
            };
        }

        return null;
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
