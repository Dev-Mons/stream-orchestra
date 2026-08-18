using System.Globalization;
using System.Text.Json;

namespace StreamOrchestra.Tools;

public static class SyncTelemetryOverheadCommand
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static int Execute(string[] args, TextWriter output, TextWriter error)
    {
        var iterations = SyncTelemetryOverheadProbe.DefaultIterations;
        var trials = SyncTelemetryOverheadProbe.DefaultTrials;
        string? outputPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument is "-h" or "--help")
            {
                WriteUsage(output);
                return 0;
            }

            if (argument.Equals("--iterations", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length ||
                    !int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out iterations))
                {
                    error.WriteLine("--iterations requires an integer value.");
                    WriteUsage(error);
                    return 2;
                }

                continue;
            }

            if (argument.Equals("--trials", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length ||
                    !int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out trials))
                {
                    error.WriteLine("--trials requires an integer value.");
                    WriteUsage(error);
                    return 2;
                }

                continue;
            }

            if (argument.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    error.WriteLine("--output requires a path.");
                    WriteUsage(error);
                    return 2;
                }

                outputPath = args[++index];
                continue;
            }

            error.WriteLine($"Unknown option: {argument}");
            WriteUsage(error);
            return 2;
        }

        SyncTelemetryOverheadReport report;
        try
        {
            report = SyncTelemetryOverheadProbe.Measure(iterations, trials);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error.WriteLine(ex.Message);
            WriteUsage(error);
            return 2;
        }

        output.WriteLine("Sync telemetry off-path overhead");
        output.WriteLine($"Measured path: {report.MeasuredPath}");
        output.WriteLine($"Iterations: {report.IterationsPerTrial:N0} x {report.TrialCount} trial(s)");
        output.WriteLine(
            $"Wall ns/check: p50={report.WallNanosecondsPerCheckP50:F3}, p95={report.WallNanosecondsPerCheckP95:F3}");
        output.WriteLine(
            $"Process CPU ns/check: p50={report.ProcessCpuNanosecondsPerCheckP50:F3}, p95={report.ProcessCpuNanosecondsPerCheckP95:F3}");
        output.WriteLine(
            $"Managed allocation: max={report.MaximumAllocatedBytesPerTrial:N0} bytes/trial, " +
            $"{report.MaximumAllocatedBytesPerCheck:F6} bytes/check");
        output.WriteLine(
            $"Memory deltas: managed|max|={report.MaximumAbsoluteManagedHeapDeltaBytes:N0} bytes, " +
            $"working-set|max|={report.MaximumAbsoluteWorkingSetDeltaBytes:N0} bytes");
        output.WriteLine($"Remained disabled: {report.RemainedDisabled}");

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(
                fullPath,
                JsonSerializer.Serialize(report, SerializerOptions) + Environment.NewLine);
            output.WriteLine($"JSON report: {fullPath}");
        }

        return report.RemainedDisabled ? 0 : 1;
    }

    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(
            "Usage: StreamOrchestra.Tools sync-telemetry-overhead " +
            "[--iterations <1000-250000000>] [--trials <1-50>] [--output <path>]");
    }
}
