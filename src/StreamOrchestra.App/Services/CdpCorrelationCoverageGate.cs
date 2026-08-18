using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed record CdpCorrelationCoverageGateOptions
{
    public int MinimumAttemptsPerRuntime { get; init; } = 100;

    public double MinimumCoverageRate { get; init; } = 0.95;

    public double MaximumAmbiguousRate { get; init; } = 0.01;

    public double MaximumInvalidRate { get; init; }
}

public static class CdpCorrelationCoverageGate
{
    public static CdpCorrelationCoverageReport Evaluate(
        IReadOnlyList<SyncTelemetrySnapshot> snapshots,
        CdpCorrelationCoverageGateOptions? gateOptions = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var options = gateOptions ?? new CdpCorrelationCoverageGateOptions();
        ValidateOptions(options);
        var samples = snapshots.SelectMany(snapshot =>
        {
            var runtime = NormalizeRuntime(snapshot.Sessions.FirstOrDefault()?.RuntimeBucket);
            return snapshot.Network
                .Where(item => item.CorrelationSource == "cdp-network")
                .Select(item => (Runtime: runtime, Status: NormalizeStatus(item.CorrelationStatus)));
        }).ToArray();
        var runtimes = samples
            .GroupBy(item => item.Runtime, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var values = group.ToArray();
                var attempts = values.Length;
                var correlated = values.Count(item => item.Status == "correlated");
                var ambiguous = values.Count(item => item.Status == "ambiguous");
                var invalid = values.Count(item => item.Status == "invalid");
                var mismatch = values.Count(item => item.Status == "runtime-mismatch");
                var unavailable = values.Count(item => item.Status == "unavailable");
                var coverage = correlated / (double)attempts;
                var ambiguousRate = ambiguous / (double)attempts;
                var invalidRate = invalid / (double)attempts;
                return new CdpRuntimeCorrelationMetric
                {
                    RuntimeBucket = group.Key,
                    AttemptCount = attempts,
                    CorrelatedCount = correlated,
                    AmbiguousCount = ambiguous,
                    InvalidCount = invalid,
                    RuntimeMismatchCount = mismatch,
                    UnavailableCount = unavailable,
                    CoverageRate = coverage,
                    AmbiguousRate = ambiguousRate,
                    InvalidRate = invalidRate,
                    GatePassed = attempts >= options.MinimumAttemptsPerRuntime &&
                                 coverage >= options.MinimumCoverageRate &&
                                 ambiguousRate <= options.MaximumAmbiguousRate &&
                                 invalidRate <= options.MaximumInvalidRate &&
                                 mismatch == 0
                };
            })
            .ToArray();
        var reasons = new List<string>();
        if (runtimes.Length == 0)
        {
            reasons.Add("no-cdp-samples");
        }

        foreach (var runtime in runtimes.Where(item => !item.GatePassed))
        {
            if (runtime.AttemptCount < options.MinimumAttemptsPerRuntime)
            {
                reasons.Add($"{runtime.RuntimeBucket}:insufficient-attempts");
            }

            if (runtime.CoverageRate < options.MinimumCoverageRate)
            {
                reasons.Add($"{runtime.RuntimeBucket}:coverage-below-threshold");
            }

            if (runtime.AmbiguousRate > options.MaximumAmbiguousRate)
            {
                reasons.Add($"{runtime.RuntimeBucket}:ambiguous-above-threshold");
            }

            if (runtime.InvalidRate > options.MaximumInvalidRate)
            {
                reasons.Add($"{runtime.RuntimeBucket}:invalid-above-threshold");
            }

            if (runtime.RuntimeMismatchCount > 0)
            {
                reasons.Add($"{runtime.RuntimeBucket}:runtime-mismatch");
            }
        }

        return new CdpCorrelationCoverageReport
        {
            HardSeekCoverageGatePassed = runtimes.Length > 0 && runtimes.All(item => item.GatePassed),
            MinimumAttemptsPerRuntime = options.MinimumAttemptsPerRuntime,
            MinimumCoverageRate = options.MinimumCoverageRate,
            MaximumAmbiguousRate = options.MaximumAmbiguousRate,
            MaximumInvalidRate = options.MaximumInvalidRate,
            Runtimes = runtimes,
            Reasons = reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
    }

    private static void ValidateOptions(CdpCorrelationCoverageGateOptions options)
    {
        if (options.MinimumAttemptsPerRuntime < 1 ||
            !IsRate(options.MinimumCoverageRate) ||
            !IsRate(options.MaximumAmbiguousRate) ||
            !IsRate(options.MaximumInvalidRate))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static string NormalizeRuntime(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();

    private static string NormalizeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "correlated" => "correlated",
        "ambiguous" => "ambiguous",
        "invalid" => "invalid",
        "runtime-mismatch" => "runtime-mismatch",
        _ => "unavailable"
    };

    private static bool IsRate(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}
