using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

/// <summary>
/// Bounded, on-device shadow estimator. Its defaults are diagnostic starting values only; no output from
/// this class is allowed to drive production control until held-out calibration and policy review pass.
/// </summary>
public sealed record SyncTimelineEstimatorOptions
{
    public int WindowSize { get; init; } = 31;

    public int MinimumMadSampleCount { get; init; } = 5;

    public double MadGateMultiplier { get; init; } = 6;

    public double MinimumMadScaleMilliseconds { get; init; } = 25;

    public double MinimumOutlierGateMilliseconds { get; init; } = 250;

    public double KalmanMeasurementNoiseMilliseconds { get; init; } = 120;

    public double KalmanInitialOffsetDeviationMilliseconds { get; init; } = 1000;

    public double KalmanInitialDriftDeviationMillisecondsPerSecond { get; init; } = 20;

    public double KalmanOffsetProcessNoise { get; init; } = 4;

    public double KalmanDriftProcessNoise { get; init; } = 0.04;

    public double HuberDeltaMilliseconds { get; init; } = 150;

    public double PredictionStandardDeviations { get; init; } = 1.96;

    public double CusumAllowanceMilliseconds { get; init; } = 25;

    public double CusumDiagnosticThresholdMilliseconds { get; init; } = 600;
}

public sealed class SyncTimelineEstimator
{
    private readonly SyncTimelineEstimatorOptions _options;
    private readonly Dictionary<string, LaneState> _lanes = new(StringComparer.Ordinal);

    public SyncTimelineEstimator(SyncTimelineEstimatorOptions? options = null)
    {
        _options = options ?? new SyncTimelineEstimatorOptions();
        if (_options.WindowSize is < 3 or > 256 ||
            _options.MinimumMadSampleCount is < 3 or > 128 ||
            _options.MinimumMadSampleCount > _options.WindowSize ||
            !IsPositiveFinite(_options.MadGateMultiplier) ||
            !IsPositiveFinite(_options.MinimumMadScaleMilliseconds) ||
            !IsPositiveFinite(_options.MinimumOutlierGateMilliseconds) ||
            !IsPositiveFinite(_options.KalmanMeasurementNoiseMilliseconds) ||
            !IsPositiveFinite(_options.KalmanInitialOffsetDeviationMilliseconds) ||
            !IsPositiveFinite(_options.KalmanInitialDriftDeviationMillisecondsPerSecond) ||
            !IsPositiveFinite(_options.KalmanOffsetProcessNoise) ||
            !IsPositiveFinite(_options.KalmanDriftProcessNoise) ||
            !IsPositiveFinite(_options.HuberDeltaMilliseconds) ||
            !IsPositiveFinite(_options.PredictionStandardDeviations) ||
            !IsPositiveFinite(_options.CusumAllowanceMilliseconds) ||
            !IsPositiveFinite(_options.CusumDiagnosticThresholdMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    public SyncEstimatorShadowResult Observe(SyncEstimatorObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var laneIdentity = NormalizeLane(observation.LaneIdentity);
        if (!IsValid(observation))
        {
            return InvalidResult(observation, "invalid");
        }

        var resetReason = SyncEstimatorResetReason.None;
        if (!_lanes.TryGetValue(laneIdentity, out var state))
        {
            if (observation.Disposition != SyncEstimatorObservationDisposition.NewEvidence)
            {
                return InvalidResult(observation, RejectionReason(observation.Disposition));
            }

            state = LaneState.Create(observation, _options);
            _lanes[laneIdentity] = state;
            resetReason = SyncEstimatorResetReason.Initial;
        }
        else if (!state.SourceIdentity.Equals(observation.SourceIdentity, StringComparison.Ordinal))
        {
            if (observation.Disposition != SyncEstimatorObservationDisposition.NewEvidence)
            {
                _lanes.Remove(laneIdentity);
                return InvalidResult(observation, RejectionReason(observation.Disposition));
            }

            state = LaneState.Create(observation, _options);
            _lanes[laneIdentity] = state;
            resetReason = SyncEstimatorResetReason.SourceChanged;
        }
        else if (state.SourceEpoch != observation.SourceEpoch)
        {
            if (observation.Disposition != SyncEstimatorObservationDisposition.NewEvidence)
            {
                _lanes.Remove(laneIdentity);
                return InvalidResult(observation, RejectionReason(observation.Disposition));
            }

            state = LaneState.Create(observation, _options);
            _lanes[laneIdentity] = state;
            resetReason = SyncEstimatorResetReason.EpochChanged;
        }
        else if (state.MonotonicFrequency != observation.MonotonicFrequency ||
                 observation.ObservedMonotonicTicks <= state.LastMonotonicTicks)
        {
            return BuildResult(
                observation,
                state,
                SyncEstimatorResetReason.None,
                accepted: false,
                rejectionReason: "invalid",
                innovation: null);
        }

        if (resetReason != SyncEstimatorResetReason.None)
        {
            AcceptFirst(state, observation);
            return BuildResult(
                observation,
                state,
                resetReason,
                accepted: true,
                rejectionReason: "none",
                innovation: 0);
        }

        Predict(state, observation.ObservedMonotonicTicks);
        if (observation.Disposition != SyncEstimatorObservationDisposition.NewEvidence)
        {
            return BuildResult(
                observation,
                state,
                SyncEstimatorResetReason.None,
                accepted: false,
                rejectionReason: RejectionReason(observation.Disposition),
                innovation: null);
        }

        var innovation = observation.RawOffsetMilliseconds - state.KalmanOffset;
        if (IsMadOutlier(state.Samples, observation.RawOffsetMilliseconds))
        {
            return BuildResult(
                observation,
                state,
                SyncEstimatorResetReason.None,
                accepted: false,
                rejectionReason: "outlier",
                innovation);
        }

        UpdateCusum(state, innovation);
        UpdateKalman(state, observation.RawOffsetMilliseconds);
        AddSample(state, observation);
        state.LastMonotonicTicks = observation.ObservedMonotonicTicks;
        state.LastObservedAtUtc = observation.ObservedAtUtc;
        return BuildResult(
            observation,
            state,
            SyncEstimatorResetReason.None,
            accepted: true,
            rejectionReason: "none",
            innovation);
    }

    public bool Reset(string laneIdentity) => _lanes.Remove(NormalizeLane(laneIdentity));

    public void ResetAll() => _lanes.Clear();

    private void AcceptFirst(LaneState state, SyncEstimatorObservation observation)
    {
        state.KalmanOffset = observation.RawOffsetMilliseconds;
        state.KalmanDrift = 0;
        state.LastMonotonicTicks = observation.ObservedMonotonicTicks;
        state.OriginMonotonicTicks = observation.ObservedMonotonicTicks;
        state.LastObservedAtUtc = observation.ObservedAtUtc;
        AddSample(state, observation);
    }

    private void Predict(LaneState state, long observedMonotonicTicks)
    {
        var deltaSeconds = Math.Max(
            0,
            (observedMonotonicTicks - state.LastMonotonicTicks) /
            (double)state.MonotonicFrequency);
        if (deltaSeconds <= 0)
        {
            return;
        }

        state.KalmanOffset += state.KalmanDrift * deltaSeconds;
        var p00 = state.P00;
        var p01 = state.P01;
        var p10 = state.P10;
        var p11 = state.P11;
        state.P00 = p00 + deltaSeconds * (p01 + p10) +
                    deltaSeconds * deltaSeconds * p11 +
                    _options.KalmanOffsetProcessNoise * deltaSeconds;
        state.P01 = p01 + deltaSeconds * p11;
        state.P10 = p10 + deltaSeconds * p11;
        state.P11 = p11 + _options.KalmanDriftProcessNoise * deltaSeconds;
        state.LastMonotonicTicks = observedMonotonicTicks;
    }

    private void UpdateKalman(LaneState state, double measurement)
    {
        var measurementVariance = Math.Pow(_options.KalmanMeasurementNoiseMilliseconds, 2);
        var innovationVariance = state.P00 + measurementVariance;
        var gainOffset = state.P00 / innovationVariance;
        var gainDrift = state.P10 / innovationVariance;
        var innovation = measurement - state.KalmanOffset;
        state.KalmanOffset += gainOffset * innovation;
        state.KalmanDrift += gainDrift * innovation;

        var p00 = state.P00;
        var p01 = state.P01;
        var p10 = state.P10;
        var p11 = state.P11;
        state.P00 = Math.Max(0.000001, (1 - gainOffset) * p00);
        state.P01 = (1 - gainOffset) * p01;
        state.P10 = p10 - gainDrift * p00;
        state.P11 = Math.Max(0.000001, p11 - gainDrift * p01);
        var symmetricCrossTerm = (state.P01 + state.P10) / 2;
        state.P01 = symmetricCrossTerm;
        state.P10 = symmetricCrossTerm;
    }

    private void UpdateCusum(LaneState state, double innovation)
    {
        state.ChangePointSuspected = false;
        state.PositiveCusum = Math.Max(
            0,
            state.PositiveCusum + innovation - _options.CusumAllowanceMilliseconds);
        state.NegativeCusum = Math.Min(
            0,
            state.NegativeCusum + innovation + _options.CusumAllowanceMilliseconds);
        state.ChangePointSuspected =
            state.PositiveCusum >= _options.CusumDiagnosticThresholdMilliseconds ||
            -state.NegativeCusum >= _options.CusumDiagnosticThresholdMilliseconds;
        if (state.ChangePointSuspected)
        {
            state.PositiveCusum = 0;
            state.NegativeCusum = 0;
        }
    }

    private bool IsMadOutlier(IReadOnlyList<Sample> samples, double measurement)
    {
        if (samples.Count < _options.MinimumMadSampleCount)
        {
            return false;
        }

        var median = Median(samples.Select(sample => sample.OffsetMilliseconds));
        var mad = Median(samples.Select(sample => Math.Abs(sample.OffsetMilliseconds - median)));
        var scale = Math.Max(_options.MinimumMadScaleMilliseconds, 1.4826 * mad);
        var gate = Math.Max(_options.MinimumOutlierGateMilliseconds, _options.MadGateMultiplier * scale);
        return Math.Abs(measurement - median) > gate;
    }

    private void AddSample(LaneState state, SyncEstimatorObservation observation)
    {
        var seconds = (observation.ObservedMonotonicTicks - state.OriginMonotonicTicks) /
                      (double)state.MonotonicFrequency;
        state.Samples.Add(new Sample(seconds, observation.RawOffsetMilliseconds));
        if (state.Samples.Count > _options.WindowSize)
        {
            state.Samples.RemoveAt(0);
        }
    }

    private SyncEstimatorShadowResult BuildResult(
        SyncEstimatorObservation observation,
        LaneState state,
        SyncEstimatorResetReason resetReason,
        bool accepted,
        string rejectionReason,
        double? innovation)
    {
        var kalmanDeviation = Math.Sqrt(Math.Max(0, state.P00));
        var currentSeconds = Math.Max(
            0,
            (observation.ObservedMonotonicTicks - state.OriginMonotonicTicks) /
            (double)state.MonotonicFrequency);
        var huber = FitHuber(state.Samples, currentSeconds);
        var timelineScore = CalculateTimelineScore(
            observation.IsEpochStable,
            observation.IndependentEvidenceCount,
            kalmanDeviation);
        var controllability = Math.Clamp(observation.ControllabilityScore, 0, 1);
        return new SyncEstimatorShadowResult
        {
            ObservationIdentity = observation.ObservationIdentity,
            ProgressKeyIdentity = observation.ProgressKeyIdentity,
            SourceIdentity = observation.SourceIdentity,
            SourceEpoch = observation.SourceEpoch,
            ResetReason = resetReason,
            ChangePointSuspected = state.ChangePointSuspected,
            ActiveBaseline = new SyncEstimatorEstimate
            {
                EstimatorId = "legacy",
                IsShadowOnly = false,
                ObservationAccepted = accepted,
                RejectionReason = rejectionReason,
                OffsetMilliseconds = accepted
                    ? observation.ActiveBaselineOffsetMilliseconds ?? observation.RawOffsetMilliseconds
                    : null,
                TimelineScore = timelineScore,
                BiasScore = 0,
                ControllabilityScore = controllability
            },
            KalmanCandidate = Estimate(
                "kalman-shadow",
                accepted,
                rejectionReason,
                state.KalmanOffset,
                state.KalmanDrift,
                kalmanDeviation,
                innovation,
                timelineScore,
                controllability),
            HuberCandidate = Estimate(
                "huber-shadow",
                accepted,
                rejectionReason,
                huber.OffsetMilliseconds,
                huber.DriftMillisecondsPerSecond,
                huber.StandardDeviationMilliseconds,
                innovation,
                timelineScore,
                controllability)
        };
    }

    private SyncEstimatorEstimate Estimate(
        string estimatorId,
        bool accepted,
        string rejectionReason,
        double offset,
        double drift,
        double deviation,
        double? innovation,
        double timelineScore,
        double controllabilityScore)
    {
        var interval = _options.PredictionStandardDeviations * Math.Max(0, deviation);
        return new SyncEstimatorEstimate
        {
            EstimatorId = estimatorId,
            IsShadowOnly = true,
            ObservationAccepted = accepted,
            RejectionReason = rejectionReason,
            OffsetMilliseconds = offset,
            DriftMillisecondsPerSecond = drift,
            StandardDeviationMilliseconds = deviation,
            PredictionLowerMilliseconds = offset - interval,
            PredictionUpperMilliseconds = offset + interval,
            InnovationMilliseconds = innovation,
            TimelineScore = timelineScore,
            BiasScore = 0,
            ControllabilityScore = controllabilityScore
        };
    }

    private HuberFit FitHuber(IReadOnlyList<Sample> samples, double predictionSeconds)
    {
        if (samples.Count == 0)
        {
            return new HuberFit(0, 0, 0);
        }

        if (samples.Count == 1)
        {
            var horizon = Math.Max(0, predictionSeconds - samples[0].Seconds);
            return new HuberFit(
                samples[0].OffsetMilliseconds,
                0,
                Math.Sqrt(_options.KalmanOffsetProcessNoise * horizon));
        }

        var weights = Enumerable.Repeat(1d, samples.Count).ToArray();
        var intercept = 0d;
        var slope = 0d;
        for (var iteration = 0; iteration < 4; iteration++)
        {
            (intercept, slope) = WeightedLine(samples, weights);
            for (var index = 0; index < samples.Count; index++)
            {
                var residual = samples[index].OffsetMilliseconds -
                               (intercept + slope * samples[index].Seconds);
                var absoluteResidual = Math.Abs(residual);
                weights[index] = absoluteResidual <= _options.HuberDeltaMilliseconds
                    ? 1
                    : _options.HuberDeltaMilliseconds / absoluteResidual;
            }
        }

        var currentSeconds = Math.Max(samples[^1].Seconds, predictionSeconds);
        var residualVariance = samples
            .Select(sample =>
            {
                var residual = sample.OffsetMilliseconds - (intercept + slope * sample.Seconds);
                return residual * residual;
            })
            .Average();
        var projectionHorizon = Math.Max(0, currentSeconds - samples[^1].Seconds);
        return new HuberFit(
            intercept + slope * currentSeconds,
            slope,
            Math.Sqrt(Math.Max(
                0,
                residualVariance + _options.KalmanOffsetProcessNoise * projectionHorizon)));
    }

    private static (double Intercept, double Slope) WeightedLine(
        IReadOnlyList<Sample> samples,
        IReadOnlyList<double> weights)
    {
        var sumWeight = 0d;
        var sumX = 0d;
        var sumY = 0d;
        var sumXX = 0d;
        var sumXY = 0d;
        for (var index = 0; index < samples.Count; index++)
        {
            var weight = weights[index];
            var sample = samples[index];
            sumWeight += weight;
            sumX += weight * sample.Seconds;
            sumY += weight * sample.OffsetMilliseconds;
            sumXX += weight * sample.Seconds * sample.Seconds;
            sumXY += weight * sample.Seconds * sample.OffsetMilliseconds;
        }

        var determinant = sumWeight * sumXX - sumX * sumX;
        if (sumWeight <= 0 || Math.Abs(determinant) < 0.000000001)
        {
            return (sumWeight <= 0 ? 0 : sumY / sumWeight, 0);
        }

        return (
            (sumY * sumXX - sumX * sumXY) / determinant,
            (sumWeight * sumXY - sumX * sumY) / determinant);
    }

    private static double CalculateTimelineScore(bool stable, int evidenceCount, double deviation)
    {
        if (!stable || evidenceCount < 2 || !double.IsFinite(deviation))
        {
            return 0;
        }

        var support = Math.Min(1, evidenceCount / 10d);
        var precision = 1 / (1 + Math.Max(0, deviation) / 1000);
        return Math.Clamp(support * precision, 0, 1);
    }

    private static SyncEstimatorShadowResult InvalidResult(
        SyncEstimatorObservation observation,
        string rejectionReason)
    {
        SyncEstimatorEstimate Invalid(string id, bool shadow) => new()
        {
            EstimatorId = id,
            IsShadowOnly = shadow,
            ObservationAccepted = false,
            RejectionReason = rejectionReason
        };

        return new SyncEstimatorShadowResult
        {
            ObservationIdentity = observation.ObservationIdentity,
            ProgressKeyIdentity = observation.ProgressKeyIdentity,
            SourceIdentity = observation.SourceIdentity,
            SourceEpoch = Math.Max(0, observation.SourceEpoch),
            ActiveBaseline = Invalid("legacy", false),
            KalmanCandidate = Invalid("kalman-shadow", true),
            HuberCandidate = Invalid("huber-shadow", true)
        };
    }

    private static string RejectionReason(SyncEstimatorObservationDisposition disposition) =>
        disposition switch
        {
            SyncEstimatorObservationDisposition.Duplicate => "duplicate",
            SyncEstimatorObservationDisposition.Stale => "stale",
            SyncEstimatorObservationDisposition.Rollback => "rollback",
            _ => "invalid"
        };

    private static bool IsValid(SyncEstimatorObservation observation) =>
        !string.IsNullOrWhiteSpace(observation.SourceIdentity) &&
        observation.SourceEpoch > 0 &&
        double.IsFinite(observation.RawOffsetMilliseconds) &&
        (observation.ActiveBaselineOffsetMilliseconds is null ||
         double.IsFinite(observation.ActiveBaselineOffsetMilliseconds.Value)) &&
        observation.ObservedMonotonicTicks > 0 &&
        observation.MonotonicFrequency > 0 &&
        observation.ControllabilityScore is >= 0 and <= 1 &&
        double.IsFinite(observation.ControllabilityScore);

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static string NormalizeLane(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "primary-video" : value.Trim();

    private static double Median(IEnumerable<double> source)
    {
        var values = source.OrderBy(value => value).ToArray();
        if (values.Length == 0)
        {
            return 0;
        }

        var middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2
            : values[middle];
    }

    private sealed class LaneState
    {
        public required string SourceIdentity { get; init; }

        public long SourceEpoch { get; init; }

        public long MonotonicFrequency { get; init; }

        public long OriginMonotonicTicks { get; set; }

        public long LastMonotonicTicks { get; set; }

        public DateTimeOffset LastObservedAtUtc { get; set; }

        public double KalmanOffset { get; set; }

        public double KalmanDrift { get; set; }

        public double P00 { get; set; }

        public double P01 { get; set; }

        public double P10 { get; set; }

        public double P11 { get; set; }

        public List<Sample> Samples { get; } = [];

        public double PositiveCusum { get; set; }

        public double NegativeCusum { get; set; }

        public bool ChangePointSuspected { get; set; }

        public static LaneState Create(
            SyncEstimatorObservation observation,
            SyncTimelineEstimatorOptions options) => new()
        {
            SourceIdentity = observation.SourceIdentity,
            SourceEpoch = observation.SourceEpoch,
            MonotonicFrequency = observation.MonotonicFrequency,
            OriginMonotonicTicks = observation.ObservedMonotonicTicks,
            LastMonotonicTicks = observation.ObservedMonotonicTicks,
            LastObservedAtUtc = observation.ObservedAtUtc,
            P00 = Math.Pow(options.KalmanInitialOffsetDeviationMilliseconds, 2),
            P11 = Math.Pow(options.KalmanInitialDriftDeviationMillisecondsPerSecond, 2)
        };
    }

    private sealed record Sample(double Seconds, double OffsetMilliseconds);

    private sealed record HuberFit(
        double OffsetMilliseconds,
        double DriftMillisecondsPerSecond,
        double StandardDeviationMilliseconds);
}
