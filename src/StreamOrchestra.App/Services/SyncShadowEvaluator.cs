using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class SyncShadowEvaluator
{
    public static SyncShadowEvaluationSummary Evaluate(
        IReadOnlyList<SyncShadowSessionEvaluation> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var independent = sessions
            .Where(session => !string.IsNullOrWhiteSpace(session.IndependentSessionId))
            .GroupBy(session => session.IndependentSessionId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var sessionErrors = independent.Select(SessionErrors).ToArray();
        var playbackHours = independent
            .Where(session => double.IsFinite(session.ActivePlaybackHours) && session.ActivePlaybackHours > 0)
            .Sum(session => session.ActivePlaybackHours);
        var verifiedSeeks = independent.Sum(session => Math.Max(0, session.VerifiedHardSeekCount));
        var evaluatedCorrections = independent.Sum(session => Math.Max(0, session.EvaluatedCorrectionCount));
        var wrongCorrections = independent.Sum(session => Math.Clamp(
            session.WrongCorrectionCount,
            0,
            Math.Max(0, session.EvaluatedCorrectionCount)));
        return new SyncShadowEvaluationSummary
        {
            BaselinePairwiseAbsoluteProxyErrorMilliseconds = Distribution(
                sessionErrors.Select(item => item.Baseline).OfType<double>()),
            CandidatePairwiseAbsoluteProxyErrorMilliseconds = Distribution(
                sessionErrors.Select(item => item.Candidate).OfType<double>()),
            InitialConvergenceSeconds = Distribution(independent
                .Select(session => session.InitialConvergenceSeconds)
                .OfType<double>()
                .Where(value => double.IsFinite(value) && value >= 0)),
            VerifiedHardSeeksPerPlaybackHour = playbackHours > 0
                ? verifiedSeeks / playbackHours
                : null,
            FailedHardSeekCount = independent.Sum(session => Math.Max(0, session.FailedHardSeekCount)),
            WrongCorrectionRate = evaluatedCorrections > 0
                ? wrongCorrections / (double)evaluatedCorrections
                : null,
            ManualAdjustmentEventCount = independent.Sum(session =>
                Math.Max(0, session.ManualAdjustmentEventCount)),
            TotalAbsoluteManualAdjustmentMilliseconds = independent
                .Where(session => double.IsFinite(session.TotalAbsoluteManualAdjustmentMilliseconds))
                .Sum(session => Math.Max(0, session.TotalAbsoluteManualAdjustmentMilliseconds)),
            ConfidenceCalibration = Calibration(independent.SelectMany(session =>
                session.ConfidenceSamples)),
            CandidateErrorByStratum = sessionErrors
                .Where(item => item.Candidate is not null)
                .GroupBy(item => StratumKey(item.Session.Strata), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => Distribution(group.Select(item => item.Candidate!.Value)),
                    StringComparer.Ordinal)
        };
    }

    private static (SyncShadowSessionEvaluation Session, double? Baseline, double? Candidate)
        SessionErrors(SyncShadowSessionEvaluation session)
    {
        var baseline = new List<double>();
        var candidate = new List<double>();
        foreach (var pair in session.PairLabels)
        {
            var values = new[]
            {
                pair.BaselineLeftDelayMilliseconds,
                pair.BaselineRightDelayMilliseconds,
                pair.CandidateLeftDelayMilliseconds,
                pair.CandidateRightDelayMilliseconds,
                pair.AcceptedLeftDelayMilliseconds,
                pair.AcceptedRightDelayMilliseconds
            };
            if (values.Any(value => !double.IsFinite(value)))
            {
                continue;
            }

            var acceptedDifference = pair.AcceptedLeftDelayMilliseconds -
                                     pair.AcceptedRightDelayMilliseconds;
            baseline.Add(Math.Abs(
                pair.BaselineLeftDelayMilliseconds - pair.BaselineRightDelayMilliseconds -
                acceptedDifference));
            candidate.Add(Math.Abs(
                pair.CandidateLeftDelayMilliseconds - pair.CandidateRightDelayMilliseconds -
                acceptedDifference));
        }

        return (
            session,
            baseline.Count == 0 ? null : Percentile(baseline, 0.5),
            candidate.Count == 0 ? null : Percentile(candidate, 0.5));
    }

    private static SyncDistributionSummary Distribution(IEnumerable<double> source)
    {
        var values = source.Where(double.IsFinite).OrderBy(value => value).ToArray();
        return values.Length == 0
            ? new SyncDistributionSummary(0, null, null, null)
            : new SyncDistributionSummary(
                values.Length,
                Percentile(values, 0.5),
                Percentile(values, 0.9),
                Percentile(values, 0.95));
    }

    private static IReadOnlyList<SyncConfidenceCalibrationBin> Calibration(
        IEnumerable<SyncConfidenceEvaluationSample> source) => source
        .Where(sample =>
            !string.IsNullOrWhiteSpace(sample.Domain) &&
            double.IsFinite(sample.ConfidenceScore) &&
            sample.ConfidenceScore is >= 0 and <= 1)
        .GroupBy(sample => new
        {
            Domain = sample.Domain.Trim().ToLowerInvariant(),
            Bin = Math.Min(4, (int)(sample.ConfidenceScore * 5))
        })
        .OrderBy(group => group.Key.Domain, StringComparer.Ordinal)
        .ThenBy(group => group.Key.Bin)
        .Select(group =>
        {
            var intervalSamples = group
                .Where(sample => sample.PredictionIntervalCovered is not null)
                .ToArray();
            var widths = group
                .Select(sample => sample.PredictionIntervalWidthMilliseconds)
                .OfType<double>()
                .Where(value => double.IsFinite(value) && value >= 0)
                .ToArray();
            return new SyncConfidenceCalibrationBin(
                group.Key.Domain,
                group.Key.Bin,
                group.Count(),
                group.Average(sample => sample.ConfidenceScore),
                group.Count(sample => sample.OutcomeSucceeded) / (double)group.Count(),
                intervalSamples.Length == 0
                    ? null
                    : intervalSamples.Count(sample => sample.PredictionIntervalCovered == true) /
                      (double)intervalSamples.Length,
                widths.Length == 0 ? null : widths.Average());
        })
        .ToArray();

    private static double Percentile(IEnumerable<double> source, double percentile)
    {
        var values = source.OrderBy(value => value).ToArray();
        if (values.Length == 1)
        {
            return values[0];
        }

        var position = Math.Clamp(percentile, 0, 1) * (values.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        var fraction = position - lower;
        return values[lower] + (values[upper] - values[lower]) * fraction;
    }

    private static string StratumKey(SyncEvaluationStrata strata) => string.Join(
        "|",
        strata.NetworkBucket,
        strata.PcLoadBucket,
        strata.ChannelBucket,
        strata.QualityBucket,
        strata.CdnBucket,
        strata.PlaybackBucket,
        strata.SourceBucket);
}
