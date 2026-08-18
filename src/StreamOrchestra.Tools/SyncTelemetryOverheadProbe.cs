using System.Diagnostics;
using System.Runtime.InteropServices;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tools;

public sealed record SyncTelemetryOverheadTrial(
    int TrialNumber,
    int Iterations,
    double ElapsedMilliseconds,
    double WallNanosecondsPerCheck,
    double ProcessCpuMilliseconds,
    double ProcessCpuNanosecondsPerCheck,
    double ProcessCpuPercent,
    long AllocatedBytes,
    double AllocatedBytesPerCheck,
    long ManagedHeapDeltaBytes,
    long WorkingSetDeltaBytes,
    int UnexpectedEnabledObservations);

public sealed record SyncTelemetryOverheadReport(
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    string MeasuredPath,
    string FrameworkDescription,
    string OsDescription,
    string ProcessArchitecture,
    int ProcessorCount,
    int IterationsPerTrial,
    int TrialCount,
    int WarmupIterations,
    IReadOnlyList<SyncTelemetryOverheadTrial> Trials,
    double WallNanosecondsPerCheckP50,
    double WallNanosecondsPerCheckP95,
    double ProcessCpuNanosecondsPerCheckP50,
    double ProcessCpuNanosecondsPerCheckP95,
    long MaximumAllocatedBytesPerTrial,
    double MaximumAllocatedBytesPerCheck,
    long MaximumAbsoluteManagedHeapDeltaBytes,
    long MaximumAbsoluteWorkingSetDeltaBytes,
    int UnexpectedEnabledObservationCount)
{
    public bool RemainedDisabled => UnexpectedEnabledObservationCount == 0;
}

public static class SyncTelemetryOverheadProbe
{
    public const int DefaultIterations = 20_000_000;
    public const int DefaultTrials = 9;
    public const int MaximumIterations = 250_000_000;
    public const int MaximumTrials = 50;

    public static SyncTelemetryOverheadReport Measure(
        int iterations = DefaultIterations,
        int trialCount = DefaultTrials,
        int? warmupIterations = null)
    {
        if (iterations is < 1_000 or > MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iterations),
                $"Iterations must be between 1,000 and {MaximumIterations:N0}.");
        }

        if (trialCount is < 1 or > MaximumTrials)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trialCount),
                $"Trial count must be between 1 and {MaximumTrials}.");
        }

        var warmup = warmupIterations ?? Math.Min(iterations, 250_000);
        if (warmup is < 0 or > MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(warmupIterations));
        }

        var controller = new SyncTelemetrySessionController();
        var unexpectedEnabledObservations = RunChecks(controller, warmup);
        var trials = new List<SyncTelemetryOverheadTrial>(trialCount);
        using var process = Process.GetCurrentProcess();

        for (var trialNumber = 1; trialNumber <= trialCount; trialNumber++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();

            var processCpuBefore = process.TotalProcessorTime;
            var workingSetBefore = Environment.WorkingSet;
            var managedHeapBefore = GC.GetTotalMemory(forceFullCollection: false);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var startedAt = Stopwatch.GetTimestamp();

            var trialUnexpectedEnabledObservations = RunChecks(controller, iterations);

            var completedAt = Stopwatch.GetTimestamp();
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            var managedHeapAfter = GC.GetTotalMemory(forceFullCollection: false);
            var workingSetAfter = Environment.WorkingSet;
            var processCpuAfter = process.TotalProcessorTime;

            var elapsedSeconds = (completedAt - startedAt) / (double)Stopwatch.Frequency;
            var elapsedMilliseconds = elapsedSeconds * 1_000;
            var wallNanosecondsPerCheck = elapsedSeconds * 1_000_000_000 / iterations;
            var processCpu = processCpuAfter - processCpuBefore;
            var processCpuNanosecondsPerCheck = processCpu.TotalSeconds * 1_000_000_000 / iterations;
            var processCpuPercent = elapsedSeconds <= 0
                ? 0
                : processCpu.TotalSeconds / elapsedSeconds / Environment.ProcessorCount * 100;
            var allocatedBytes = Math.Max(0, allocatedAfter - allocatedBefore);

            trials.Add(new SyncTelemetryOverheadTrial(
                trialNumber,
                iterations,
                elapsedMilliseconds,
                wallNanosecondsPerCheck,
                processCpu.TotalMilliseconds,
                processCpuNanosecondsPerCheck,
                Math.Max(0, processCpuPercent),
                allocatedBytes,
                allocatedBytes / (double)iterations,
                managedHeapAfter - managedHeapBefore,
                workingSetAfter - workingSetBefore,
                trialUnexpectedEnabledObservations));
            unexpectedEnabledObservations += trialUnexpectedEnabledObservations;
        }

        return new SyncTelemetryOverheadReport(
            SchemaVersion: 1,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            MeasuredPath: "SyncTelemetrySessionController.IsEnabled (default disabled)",
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            OsDescription: RuntimeInformation.OSDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            IterationsPerTrial: iterations,
            TrialCount: trialCount,
            WarmupIterations: warmup,
            Trials: trials,
            WallNanosecondsPerCheckP50: Percentile(
                trials.Select(trial => trial.WallNanosecondsPerCheck),
                0.50),
            WallNanosecondsPerCheckP95: Percentile(
                trials.Select(trial => trial.WallNanosecondsPerCheck),
                0.95),
            ProcessCpuNanosecondsPerCheckP50: Percentile(
                trials.Select(trial => trial.ProcessCpuNanosecondsPerCheck),
                0.50),
            ProcessCpuNanosecondsPerCheckP95: Percentile(
                trials.Select(trial => trial.ProcessCpuNanosecondsPerCheck),
                0.95),
            MaximumAllocatedBytesPerTrial: trials.Max(trial => trial.AllocatedBytes),
            MaximumAllocatedBytesPerCheck: trials.Max(trial => trial.AllocatedBytesPerCheck),
            MaximumAbsoluteManagedHeapDeltaBytes: trials.Max(
                trial => AbsoluteWithoutOverflow(trial.ManagedHeapDeltaBytes)),
            MaximumAbsoluteWorkingSetDeltaBytes: trials.Max(
                trial => AbsoluteWithoutOverflow(trial.WorkingSetDeltaBytes)),
            UnexpectedEnabledObservationCount: unexpectedEnabledObservations);
    }

    private static int RunChecks(SyncTelemetrySessionController controller, int iterations)
    {
        var enabledObservations = 0;
        for (var index = 0; index < iterations; index++)
        {
            if (controller.IsEnabled)
            {
                enabledObservations++;
            }
        }

        GC.KeepAlive(controller);
        return enabledObservations;
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private static long AbsoluteWithoutOverflow(long value) =>
        value == long.MinValue ? long.MaxValue : Math.Abs(value);
}
