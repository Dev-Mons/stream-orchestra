using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed record SyncEstimatorCalibrationPipelineOptions
{
    public int DevelopmentSessionTarget { get; init; } = 42;

    public int HoldoutSessionTarget { get; init; } = 18;

    public int MinimumDevelopmentSessionsForTuning { get; init; } = 2;

    public SyncTimelineEstimatorOptions RollbackOptions { get; init; } = new();
}

public static class SyncEstimatorCalibrationPipeline
{
    public static SyncEstimatorCalibrationReport Analyze(
        IReadOnlyList<SyncEstimatorCalibrationSession> sessions,
        SyncEstimatorCalibrationPipelineOptions? pipelineOptions = null,
        DateTimeOffset? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var options = pipelineOptions ?? new SyncEstimatorCalibrationPipelineOptions();
        ValidateOptions(options);

        var exclusions = new List<string>();
        var valid = sessions
            .Select((session, index) => (Session: session, Index: index))
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.Session.IndependentSessionId)
                    ? $"missing-{item.Index}"
                    : item.Session.IndependentSessionId.Trim(),
                StringComparer.Ordinal)
            .SelectMany(group =>
            {
                if (group.Count() != 1)
                {
                    exclusions.Add($"{group.Key}:duplicate-independent-session");
                    return Array.Empty<SyncEstimatorCalibrationSession>();
                }

                var session = group.Single().Session;
                var reason = ValidateSession(session);
                if (reason is not null)
                {
                    exclusions.Add($"{group.Key}:{reason}");
                    return Array.Empty<SyncEstimatorCalibrationSession>();
                }

                return [session with { IndependentSessionId = group.Key }];
            })
            .OrderBy(session => session.StartedAtUtc)
            .ThenBy(session => session.IndependentSessionId, StringComparer.Ordinal)
            .ToArray();

        var development = valid.Take(options.DevelopmentSessionTarget).ToArray();
        var holdout = valid
            .Skip(options.DevelopmentSessionTarget)
            .Take(options.HoldoutSessionTarget)
            .ToArray();
        var rollback = options.RollbackOptions;
        var selected = rollback;
        IReadOnlyList<SyncCalibrationTuningStep> trace = [];
        if (development.Length >= options.MinimumDevelopmentSessionsForTuning)
        {
            (selected, trace) = Tune(development, rollback);
        }

        // The holdout is first evaluated only after the development-only tuning call above returns.
        var developmentSamples = Compare(development, selected);
        var holdoutSamples = Compare(holdout, selected);
        var confidence = EvaluateConfidence(holdout, holdoutSamples);
        var status = development.Length < options.DevelopmentSessionTarget
            ? "insufficient-development"
            : holdout.Length < options.HoldoutSessionTarget
                ? "collecting-holdout"
                : "ready-for-review";
        return new SyncEstimatorCalibrationReport
        {
            GeneratedAtUtc = (generatedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            EvidenceSha256 = EvidenceHash(valid),
            Status = status,
            IndependentSessionCount = valid.Length,
            DevelopmentSessionCount = development.Length,
            HoldoutSessionCount = holdout.Length,
            DevelopmentSessionIds = development.Select(session => session.IndependentSessionId).ToArray(),
            HoldoutSessionIds = holdout.Select(session => session.IndependentSessionId).ToArray(),
            ExclusionReasons = exclusions.Order(StringComparer.Ordinal).ToArray(),
            SelectedOptions = selected,
            RollbackOptions = rollback,
            TuningTrace = trace,
            DevelopmentMetrics = Metrics(developmentSamples),
            HoldoutMetrics = Metrics(holdoutSamples),
            HoldoutConfidenceCalibration = confidence,
            HoldoutIntervalCoverageByStratum = IntervalCoverage(holdoutSamples),
            FaultCoverage = FaultCoverage(development, holdout)
        };
    }

    private static (SyncTimelineEstimatorOptions Selected, IReadOnlyList<SyncCalibrationTuningStep> Trace)
        Tune(
            IReadOnlyList<SyncEstimatorCalibrationSession> development,
            SyncTimelineEstimatorOptions starting)
    {
        var current = starting;
        var trace = new List<SyncCalibrationTuningStep>();
        current = TuneParameter(
            development,
            current,
            "madGateMultiplier",
            [4, 6, 8],
            (candidate, value) => candidate with { MadGateMultiplier = value },
            trace);
        current = TuneParameter(
            development,
            current,
            "huberDeltaMilliseconds",
            [100, 150, 250],
            (candidate, value) => candidate with { HuberDeltaMilliseconds = value },
            trace);
        current = TuneParameter(
            development,
            current,
            "kalmanMeasurementNoiseMilliseconds",
            [80, 120, 200],
            (candidate, value) => candidate with { KalmanMeasurementNoiseMilliseconds = value },
            trace);
        current = TuneParameter(
            development,
            current,
            "kalmanOffsetProcessNoise",
            [1, 4, 16],
            (candidate, value) => candidate with { KalmanOffsetProcessNoise = value },
            trace);
        current = TuneParameter(
            development,
            current,
            "kalmanDriftProcessNoise",
            [0.01, 0.04, 0.16],
            (candidate, value) => candidate with { KalmanDriftProcessNoise = value },
            trace);
        current = TuneParameter(
            development,
            current,
            "maximumAbsoluteDriftMillisecondsPerSecond",
            [25, 50, 100],
            (candidate, value) => candidate with
            {
                MaximumAbsoluteDriftMillisecondsPerSecond = value
            },
            trace);
        current = TuneParameter(
            development,
            current,
            "cusumAllowanceMilliseconds",
            [10, 25, 50],
            (candidate, value) => candidate with { CusumAllowanceMilliseconds = value },
            trace);
        current = TuneParameter(
            development,
            current,
            "cusumDiagnosticThresholdMilliseconds",
            [400, 600, 900],
            (candidate, value) => candidate with { CusumDiagnosticThresholdMilliseconds = value },
            trace);
        return (current, trace);
    }

    private static SyncTimelineEstimatorOptions TuneParameter(
        IReadOnlyList<SyncEstimatorCalibrationSession> development,
        SyncTimelineEstimatorOptions current,
        string parameter,
        IReadOnlyList<double> values,
        Func<SyncTimelineEstimatorOptions, double, SyncTimelineEstimatorOptions> apply,
        ICollection<SyncCalibrationTuningStep> trace)
    {
        var scored = values
            .Select((value, index) =>
            {
                var candidate = apply(current, value);
                var score = Score(Compare(development, candidate));
                return (Candidate: candidate, Value: value, Index: index, score.Score, score.Mae);
            })
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Index)
            .First();
        trace.Add(new SyncCalibrationTuningStep(
            parameter,
            values,
            scored.Value,
            scored.Mae));
        return scored.Candidate;
    }

    private static (double Score, double Mae) Score(IReadOnlyList<ComparisonSample> samples)
    {
        if (samples.Count == 0)
        {
            return (double.PositiveInfinity, double.PositiveInfinity);
        }

        var errors = samples.SelectMany(sample => new[]
        {
            Math.Abs(sample.Kalman - sample.Reference),
            Math.Abs(sample.Huber - sample.Reference)
        }).ToArray();
        var covered = samples.Count(sample =>
            sample.Reference >= sample.KalmanLower && sample.Reference <= sample.KalmanUpper);
        var coverage = covered / (double)samples.Count;
        var mae = errors.Average();
        return (mae + Math.Abs(0.95 - coverage) * 100, mae);
    }

    private static IReadOnlyList<ComparisonSample> Compare(
        IReadOnlyList<SyncEstimatorCalibrationSession> sessions,
        SyncTimelineEstimatorOptions options)
    {
        var samples = new List<ComparisonSample>();
        foreach (var session in sessions)
        {
            var estimator = new SyncTimelineEstimator(options);
            foreach (var observation in session.Observations
                         .OrderBy(item => item.ObservedMonotonicTicks)
                         .ThenBy(item => item.ObservationId, StringComparer.Ordinal))
            {
                var result = estimator.Observe(new SyncEstimatorObservation
                {
                    LaneIdentity = "primary-video",
                    SourceIdentity = observation.SourceIdentity,
                    SourceEpoch = observation.SourceEpoch,
                    ObservationIdentity = observation.ObservationId,
                    ProgressKeyIdentity = observation.ObservationId,
                    RawOffsetMilliseconds = observation.RawOffsetMilliseconds,
                    ActiveBaselineOffsetMilliseconds = observation.LegacyOffsetMilliseconds,
                    ObservedAtUtc = observation.ObservedAtUtc,
                    ObservedMonotonicTicks = observation.ObservedMonotonicTicks,
                    MonotonicFrequency = observation.MonotonicFrequency,
                    Disposition = observation.Disposition,
                    IsEpochStable = observation.IsEpochStable,
                    IndependentEvidenceCount = observation.IndependentEvidenceCount,
                    ControllabilityScore = observation.ControllabilityConfidence
                });
                if (!result.ObservationIdentity.Equals(observation.ObservationId, StringComparison.Ordinal) ||
                    !result.ActiveBaseline.ObservationAccepted ||
                    !result.KalmanCandidate.ObservationAccepted ||
                    !result.HuberCandidate.ObservationAccepted ||
                    result.ActiveBaseline.OffsetMilliseconds is not { } legacy ||
                    result.KalmanCandidate.OffsetMilliseconds is not { } kalman ||
                    result.HuberCandidate.OffsetMilliseconds is not { } huber ||
                    result.KalmanCandidate.PredictionLowerMilliseconds is not { } kalmanLower ||
                    result.KalmanCandidate.PredictionUpperMilliseconds is not { } kalmanUpper ||
                    result.HuberCandidate.PredictionLowerMilliseconds is not { } huberLower ||
                    result.HuberCandidate.PredictionUpperMilliseconds is not { } huberUpper)
                {
                    continue;
                }

                samples.Add(new ComparisonSample(
                    session.IndependentSessionId,
                    observation.ObservationId,
                    observation.ReferenceOffsetMilliseconds,
                    legacy,
                    kalman,
                    huber,
                    kalmanLower,
                    kalmanUpper,
                    huberLower,
                    huberUpper,
                    StratumKey(observation.Strata)));
            }
        }

        return samples;
    }

    private static IReadOnlyList<SyncEstimatorErrorMetric> Metrics(
        IReadOnlyList<ComparisonSample> samples)
    {
        SyncEstimatorErrorMetric Build(string estimator, Func<ComparisonSample, double> selector)
        {
            var errors = samples
                .Select(sample => Math.Abs(selector(sample) - sample.Reference))
                .Order()
                .ToArray();
            return new SyncEstimatorErrorMetric(
                estimator,
                errors.Length,
                errors.Length == 0 ? null : errors.Average(),
                Percentile(errors, 0.5),
                Percentile(errors, 0.9),
                Percentile(errors, 0.95));
        }

        return
        [
            Build("legacy", sample => sample.Legacy),
            Build("kalman", sample => sample.Kalman),
            Build("huber", sample => sample.Huber)
        ];
    }

    private static IReadOnlyList<SyncConfidenceCalibrationBin> EvaluateConfidence(
        IReadOnlyList<SyncEstimatorCalibrationSession> holdout,
        IReadOnlyList<ComparisonSample> samples)
    {
        var sampleIndex = samples.ToDictionary(
            sample => $"{sample.SessionId}\u001f{sample.ObservationId}",
            StringComparer.Ordinal);
        var evaluations = holdout.Select(session => new SyncShadowSessionEvaluation
        {
            IndependentSessionId = session.IndependentSessionId,
            ConfidenceSamples = session.Observations.SelectMany(observation =>
            {
                sampleIndex.TryGetValue(
                    $"{session.IndependentSessionId}\u001f{observation.ObservationId}",
                    out var matched);
                var result = new List<SyncConfidenceEvaluationSample>(3);
                AddConfidence(
                    result,
                    "timeline",
                    observation.TimelineConfidence,
                    observation.TimelineOutcomeSucceeded,
                    matched is null
                        ? null
                        : observation.ReferenceOffsetMilliseconds >= matched.KalmanLower &&
                          observation.ReferenceOffsetMilliseconds <= matched.KalmanUpper,
                    matched is null ? null : matched.KalmanUpper - matched.KalmanLower);
                AddConfidence(
                    result,
                    "manual-bias",
                    observation.ManualBiasConfidence,
                    observation.ManualBiasOutcomeSucceeded);
                AddConfidence(
                    result,
                    "controllability",
                    observation.ControllabilityConfidence,
                    observation.ControllabilityOutcomeSucceeded);
                return result;
            }).ToArray()
        }).ToArray();
        return SyncShadowEvaluator.Evaluate(evaluations).ConfidenceCalibration;
    }

    private static void AddConfidence(
        ICollection<SyncConfidenceEvaluationSample> destination,
        string domain,
        double score,
        bool? outcome,
        bool? intervalCovered = null,
        double? intervalWidth = null)
    {
        if (outcome is null || !double.IsFinite(score) || score is < 0 or > 1)
        {
            return;
        }

        destination.Add(new SyncConfidenceEvaluationSample(
            domain,
            score,
            outcome.Value,
            intervalCovered,
            intervalWidth));
    }

    private static IReadOnlyList<SyncIntervalCoverageMetric> IntervalCoverage(
        IReadOnlyList<ComparisonSample> samples) => new[]
        {
            (Id: "kalman", Lower: (Func<ComparisonSample, double>)(sample => sample.KalmanLower),
                Upper: (Func<ComparisonSample, double>)(sample => sample.KalmanUpper)),
            (Id: "huber", Lower: (Func<ComparisonSample, double>)(sample => sample.HuberLower),
                Upper: (Func<ComparisonSample, double>)(sample => sample.HuberUpper))
        }
        .SelectMany(estimator => samples
            .GroupBy(sample => sample.Stratum, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var values = group.ToArray();
                return new SyncIntervalCoverageMetric(
                    estimator.Id,
                    group.Key,
                    values.Length,
                    values.Count(sample => sample.Reference >= estimator.Lower(sample) &&
                                                  sample.Reference <= estimator.Upper(sample)) /
                    (double)values.Length,
                    values.Average(sample => estimator.Upper(sample) - estimator.Lower(sample)));
            }))
        .ToArray();

    private static IReadOnlyList<SyncCalibrationFaultCoverage> FaultCoverage(
        IReadOnlyList<SyncEstimatorCalibrationSession> development,
        IReadOnlyList<SyncEstimatorCalibrationSession> holdout) => development
        .SelectMany(FaultKinds)
        .Concat(holdout.SelectMany(FaultKinds))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .Select(kind => new SyncCalibrationFaultCoverage(
            kind,
            development.Count(session => FaultKinds(session).Contains(kind, StringComparer.Ordinal)),
            holdout.Count(session => FaultKinds(session).Contains(kind, StringComparer.Ordinal))))
        .ToArray();

    private static IEnumerable<string> FaultKinds(SyncEstimatorCalibrationSession session) =>
        session.Observations
            .Select(observation => NormalizeBucket(observation.FaultKind))
            .Distinct(StringComparer.Ordinal);

    private static string? ValidateSession(SyncEstimatorCalibrationSession session)
    {
        if (string.IsNullOrWhiteSpace(session.IndependentSessionId))
        {
            return "missing-independent-session-id";
        }

        if (!session.IsIndependentSession)
        {
            return "not-independent-session";
        }

        if (session.Observations.Count == 0)
        {
            return "missing-observations";
        }

        if (session.Observations.Any(observation =>
                string.IsNullOrWhiteSpace(observation.ObservationId)) ||
            session.Observations.Select(observation => observation.ObservationId)
                .Distinct(StringComparer.Ordinal).Count() != session.Observations.Count)
        {
            return "ambiguous-observation-id";
        }

        if (session.Observations.Any(observation =>
                !observation.IsIndependentReference ||
                !double.IsFinite(observation.ReferenceOffsetMilliseconds) ||
                !double.IsFinite(observation.RawOffsetMilliseconds) ||
                !double.IsFinite(observation.LegacyOffsetMilliseconds) ||
                string.IsNullOrWhiteSpace(observation.SourceIdentity) ||
                observation.SourceEpoch <= 0 ||
                observation.ObservedAtUtc == default ||
                observation.ObservedMonotonicTicks <= 0 ||
                observation.MonotonicFrequency <= 0 ||
                !double.IsFinite(observation.TimelineConfidence) ||
                observation.TimelineConfidence is < 0 or > 1 ||
                !double.IsFinite(observation.ManualBiasConfidence) ||
                observation.ManualBiasConfidence is < 0 or > 1 ||
                !double.IsFinite(observation.ControllabilityConfidence) ||
                observation.ControllabilityConfidence is < 0 or > 1 ||
                observation.TimelineOutcomeSucceeded is null ||
                observation.ManualBiasOutcomeSucceeded is null ||
                observation.ControllabilityOutcomeSucceeded is null))
        {
            return "invalid-or-non-independent-reference";
        }

        return null;
    }

    private static void ValidateOptions(SyncEstimatorCalibrationPipelineOptions options)
    {
        if (options.DevelopmentSessionTarget < 1 ||
            options.HoldoutSessionTarget < 1 ||
            options.MinimumDevelopmentSessionsForTuning < 1 ||
            options.MinimumDevelopmentSessionsForTuning > options.DevelopmentSessionTarget)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static string EvidenceHash(IReadOnlyList<SyncEstimatorCalibrationSession> sessions)
    {
        var builder = new StringBuilder();
        foreach (var session in sessions)
        {
            builder.Append(session.IndependentSessionId).Append('|')
                .Append(session.StartedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Append('\n');
            foreach (var observation in session.Observations.OrderBy(item => item.ObservationId, StringComparer.Ordinal))
            {
                builder.Append(observation.ObservationId).Append('|')
                    .Append(observation.SourceIdentity).Append('|')
                    .Append(observation.SourceEpoch.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.RawOffsetMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.LegacyOffsetMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.ReferenceOffsetMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.ObservedMonotonicTicks.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.MonotonicFrequency.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.Disposition).Append('|')
                    .Append(observation.IsEpochStable).Append('|')
                    .Append(observation.IndependentEvidenceCount.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.TimelineConfidence.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.ManualBiasConfidence.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.ControllabilityConfidence.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(observation.TimelineOutcomeSucceeded).Append('|')
                    .Append(observation.ManualBiasOutcomeSucceeded).Append('|')
                    .Append(observation.ControllabilityOutcomeSucceeded).Append('|')
                    .Append(NormalizeBucket(observation.FaultKind)).Append('|')
                    .Append(StratumKey(observation.Strata))
                    .Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

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
        var fraction = position - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
    }

    private static string StratumKey(SyncEvaluationStrata strata) => string.Join(
        "|",
        NormalizeBucket(strata.NetworkBucket),
        NormalizeBucket(strata.PcLoadBucket),
        NormalizeBucket(strata.ChannelBucket),
        NormalizeBucket(strata.QualityBucket),
        NormalizeBucket(strata.CdnBucket),
        NormalizeBucket(strata.PlaybackBucket),
        NormalizeBucket(strata.SourceBucket));

    private static string NormalizeBucket(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();

    private sealed record ComparisonSample(
        string SessionId,
        string ObservationId,
        double Reference,
        double Legacy,
        double Kalman,
        double Huber,
        double KalmanLower,
        double KalmanUpper,
        double HuberLower,
        double HuberUpper,
        string Stratum);
}
