using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed record SyncSuggestionTemporalEvaluatorOptions
{
    public int DevelopmentSessionTarget { get; init; } = 42;

    public int HoldoutSessionTarget { get; init; } = 18;

    public SyncBiasEstimatorOptions EstimatorOptions { get; init; } = new();
}

public static class SyncSuggestionTemporalEvaluator
{
    public static SyncSuggestionTemporalHoldoutReport Evaluate(
        SyncBiasPriorDocument document,
        SyncSuggestionTemporalEvaluatorOptions? evaluatorOptions = null,
        DateTimeOffset? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = evaluatorOptions ?? new SyncSuggestionTemporalEvaluatorOptions();
        if (options.DevelopmentSessionTarget < 1 || options.HoldoutSessionTarget < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(evaluatorOptions));
        }

        var eligible = document.PairObservations.Where(IsEligible).ToArray();
        var ineligibleCount = document.PairObservations.Count - eligible.Length;
        var labelSessions = eligible
            .GroupBy(observation => observation.IndependentSessionHash, StringComparer.Ordinal)
            .Select(group => new LabelSession(
                group.Key,
                group.Min(observation => observation.OccurredAtUtc.ToUniversalTime()),
                group.OrderBy(observation => observation.OccurredAtUtc).ToArray()))
            .OrderBy(session => session.StartedAtUtc)
            .ThenBy(session => session.Id, StringComparer.Ordinal)
            .ToArray();
        var development = labelSessions.Take(options.DevelopmentSessionTarget).ToArray();
        var holdout = labelSessions
            .Skip(options.DevelopmentSessionTarget)
            .Take(options.HoldoutSessionTarget)
            .ToArray();
        var developmentLabels = development.SelectMany(session => session.Observations).ToArray();
        var seenChannels = developmentLabels
            .SelectMany(observation => new[]
            {
                observation.Left.StableChannelHash,
                observation.Right.StableChannelHash
            })
            .ToHashSet(StringComparer.Ordinal);
        var estimator = new SyncBiasEstimator(options.EstimatorOptions);
        var evaluated = new List<EvaluatedPair>();
        foreach (var session in holdout)
        {
            foreach (var label in session.Observations)
            {
                var left = estimator.Estimate(label.Left, developmentLabels, session.StartedAtUtc);
                var right = estimator.Estimate(label.Right, developmentLabels, session.StartedAtUtc);
                var connected = left is not null && right is not null &&
                                left.ComponentId.Equals(right.ComponentId, StringComparison.Ordinal);
                var seen = seenChannels.Contains(label.Left.StableChannelHash) &&
                           seenChannels.Contains(label.Right.StableChannelHash)
                    ? "seen"
                    : seenChannels.Contains(label.Left.StableChannelHash) ||
                      seenChannels.Contains(label.Right.StableChannelHash)
                        ? "mixed"
                        : "unseen";
                evaluated.Add(new EvaluatedPair(
                    seen,
                    connected ? "connected" : "disconnected",
                    connected
                        ? Math.Abs(
                            left!.SuggestedDelayMilliseconds - right!.SuggestedDelayMilliseconds -
                            label.DelayDifferenceMilliseconds)
                        : null,
                    left,
                    right,
                    session.StartedAtUtc,
                    MeanTrainingAgeDays(developmentLabels, session.StartedAtUtc)));
            }
        }

        var allSegments = new[] { "seen", "mixed", "unseen", "connected", "disconnected" };
        var pairMetrics = allSegments.Select(segment =>
        {
            var pairs = evaluated.Where(item => item.SeenSegment == segment || item.GraphSegment == segment)
                .ToArray();
            var errors = pairs.Select(item => item.AbsoluteErrorMilliseconds)
                .OfType<double>()
                .Order()
                .ToArray();
            return new SyncSuggestionPairMetric(
                segment,
                pairs.Length,
                errors.Length,
                pairs.Length == 0 ? null : errors.Length / (double)pairs.Length,
                Percentile(errors, 0.5),
                Percentile(errors, 0.9));
        }).ToArray();
        var hierarchy = evaluated
            .SelectMany(item => new[] { item.Left, item.Right }
                .OfType<SyncBiasSuggestion>()
                .Select(suggestion => (Suggestion: suggestion, item.MeanTrainingAgeDays)))
            .GroupBy(item => item.Suggestion.HierarchyLevel)
            .OrderBy(group => group.Key)
            .Select(group => new SyncSuggestionHierarchyMetric(
                HierarchyName(group.Key),
                group.Count(),
                group.Average(item => item.Suggestion.IndependentSessionSupport),
                group.Average(item => item.MeanTrainingAgeDays)))
            .ToArray();
        var labelSessionIds = labelSessions.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var eventSessionIds = document.ManualEvents
            .Select(item => item.IndependentSessionHash)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var status = development.Length < options.DevelopmentSessionTarget
            ? "insufficient-development"
            : holdout.Length < options.HoldoutSessionTarget
                ? "collecting-holdout"
                : "ready-for-review";
        return new SyncSuggestionTemporalHoldoutReport
        {
            Status = status,
            GeneratedAtUtc = (generatedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            EligibleIndependentSessionCount = labelSessions.Length,
            DevelopmentSessionCount = development.Length,
            HoldoutSessionCount = holdout.Length,
            ExcludedUnlabeledSessionCount = eventSessionIds.Count(id => !labelSessionIds.Contains(id)),
            ExcludedIneligibleLabelCount = ineligibleCount,
            DevelopmentSessionIds = development.Select(session => session.Id).ToArray(),
            HoldoutSessionIds = holdout.Select(session => session.Id).ToArray(),
            PairMetrics = pairMetrics,
            HierarchyMetrics = hierarchy,
            EventSummary = EventSummary(document.ManualEvents),
            UsesOnlyExplicitStableIndependentLabels = true,
            TreatsUnadjustedSessionsAsZeroLabels = false,
            IsLocalOnly = true
        };
    }

    private static SyncSuggestionEventSummary EventSummary(
        IReadOnlyList<SyncBiasManualEvent> events)
    {
        var accepted = events
            .Where(item => item.EventKind == SyncBiasManualEventKind.SuggestionAccepted)
            .ToArray();
        var adjustments = new List<double>();
        foreach (var acceptance in accepted)
        {
            if (acceptance.UserResidualMilliseconds is not { } acceptedResidual)
            {
                continue;
            }

            var following = events
                .Where(item => item.EventKind == SyncBiasManualEventKind.UserAdjusted &&
                               item.IndependentSessionHash.Equals(
                                   acceptance.IndependentSessionHash,
                                   StringComparison.Ordinal) &&
                               item.Context.Equals(acceptance.Context) &&
                               item.OccurredAtUtc > acceptance.OccurredAtUtc &&
                               item.UserResidualMilliseconds is not null)
                .OrderBy(item => item.OccurredAtUtc)
                .FirstOrDefault();
            if (following?.UserResidualMilliseconds is { } residual)
            {
                adjustments.Add(Math.Abs(residual - acceptedResidual));
            }
        }

        return new SyncSuggestionEventSummary
        {
            ShownCount = events.Count(item => item.EventKind == SyncBiasManualEventKind.SuggestionShown),
            AcceptedCount = accepted.Length,
            RejectedCount = events.Count(item => item.EventKind == SyncBiasManualEventKind.SuggestionRejected),
            RevertedCount = events.Count(item => item.EventKind == SyncBiasManualEventKind.SuggestionReverted),
            PostSuggestionResidualAdjustmentCount = adjustments.Count,
            TotalAbsolutePostSuggestionResidualAdjustmentMilliseconds = adjustments.Sum()
        };
    }

    private static bool IsEligible(SyncBiasPairObservation observation) =>
        observation.EventKind == SyncBiasManualEventKind.AlignmentConfirmed &&
        observation.IsIndependentSession &&
        observation.IsStableFinal &&
        !string.IsNullOrWhiteSpace(observation.IndependentSessionHash) &&
        !string.IsNullOrWhiteSpace(observation.Left.StableChannelHash) &&
        !string.IsNullOrWhiteSpace(observation.Right.StableChannelHash) &&
        double.IsFinite(observation.DelayDifferenceMilliseconds);

    private static double MeanTrainingAgeDays(
        IReadOnlyList<SyncBiasPairObservation> observations,
        DateTimeOffset evaluationTime) => observations.Count == 0
        ? 0
        : observations.Average(observation => Math.Max(
            0,
            (evaluationTime - observation.OccurredAtUtc.ToUniversalTime()).TotalDays));

    private static double? Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var position = Math.Clamp(percentile, 0, 1) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sortedValues[lower] +
               (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }

    private static string HierarchyName(SyncBiasHierarchyLevel level) => level switch
    {
        SyncBiasHierarchyLevel.ChannelQualityCdn => "channel-quality-cdn",
        SyncBiasHierarchyLevel.ChannelQuality => "channel-quality",
        SyncBiasHierarchyLevel.Channel => "channel",
        _ => "none"
    };

    private sealed record LabelSession(
        string Id,
        DateTimeOffset StartedAtUtc,
        IReadOnlyList<SyncBiasPairObservation> Observations);

    private sealed record EvaluatedPair(
        string SeenSegment,
        string GraphSegment,
        double? AbsoluteErrorMilliseconds,
        SyncBiasSuggestion? Left,
        SyncBiasSuggestion? Right,
        DateTimeOffset EvaluatedAtUtc,
        double MeanTrainingAgeDays);
}
