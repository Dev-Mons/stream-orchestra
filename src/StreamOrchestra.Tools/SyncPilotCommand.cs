using System.Text.Json;
using System.Text.Json.Serialization;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tools;

public static class SyncPilotCommand
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static int Execute(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            WriteUsage(output);
            return args.Length == 0 ? 2 : 0;
        }

        if (args[0].Equals("calibrate", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteCalibration(args[1..], output, error);
        }

        if (args[0].Equals("suggestion-evaluate", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteSuggestionEvaluation(args[1..], output, error);
        }

        if (args[0].Equals("runtime-probe", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRuntimeProbe(args[1..], output, error);
        }

        if (!args[0].Equals("analyze", StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine($"Unknown sync-pilot command: {args[0]}");
            WriteUsage(error);
            return 2;
        }

        var inputPaths = new List<string>();
        string? outputPath = null;
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index].Equals("--input", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    error.WriteLine("--input requires a file or directory path.");
                    return 2;
                }

                inputPaths.Add(args[++index]);
                continue;
            }

            if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    error.WriteLine("--output requires a file path.");
                    return 2;
                }

                outputPath = args[++index];
                continue;
            }

            error.WriteLine($"Unknown option: {args[index]}");
            WriteUsage(error);
            return 2;
        }

        if (inputPaths.Count == 0)
        {
            error.WriteLine("At least one --input path is required.");
            WriteUsage(error);
            return 2;
        }

        var files = ResolveInputFiles(inputPaths, error);
        if (files is null || files.Count == 0)
        {
            error.WriteLine("No sync telemetry JSON files were found.");
            return 2;
        }

        var snapshots = new List<SyncTelemetrySnapshot>();
        var privacyViolations = new List<string>();
        foreach (var file in files)
        {
            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                error.WriteLine($"Could not read {file}: {ex.Message}");
                return 2;
            }

            privacyViolations.AddRange(SyncTelemetryPrivacyAudit.FindViolations(json)
                .Select(violation => $"{Path.GetFileName(file)}:{violation}"));
            try
            {
                var snapshot = JsonSerializer.Deserialize<SyncTelemetrySnapshot>(json, SerializerOptions);
                if (snapshot is null)
                {
                    privacyViolations.Add($"{Path.GetFileName(file)}:empty-snapshot");
                }
                else
                {
                    snapshots.Add(snapshot);
                }
            }
            catch (JsonException ex)
            {
                privacyViolations.Add($"{Path.GetFileName(file)}:deserialize:{ex.Path ?? "unknown"}");
            }
        }

        var report = SyncPilotCapabilityAnalyzer.Analyze(snapshots, privacyViolations);
        output.WriteLine("Stream sync passive pilot capability report");
        output.WriteLine($"Input snapshots: {report.InputSnapshotCount:N0}");
        output.WriteLine(
            $"Independent valid units: {report.ValidUnitCount:N0}/{report.TargetUnitCount:N0} " +
            $"({report.CollectionStatus})");
        output.WriteLine(
            $"Development/temporal holdout: {report.DevelopmentUnitCount:N0}/" +
            $"{report.TemporalHoldoutUnitCount:N0}");
        output.WriteLine(
            $"Broadcast-day clusters/channels: {report.BroadcastDayClusterCount:N0}/" +
            $"{report.DistinctChannelCount:N0}");
        output.WriteLine($"Sensitivity-only duplicate channel-set/day units: {report.SensitivityOnlyUnitCount:N0}");
        output.WriteLine($"Dropped events: {report.DroppedEventCount:N0}");
        output.WriteLine($"Privacy violations: {report.PrivacyViolations.Count:N0}");
        output.WriteLine($"Invalid unit findings: {report.InvalidUnitReasons.Count:N0}");
        foreach (var metric in report.Availability)
        {
            var rate = metric.AvailabilityRate is { } value ? $"{value:P1}" : "n/a";
            output.WriteLine(
                $"- {metric.MetricId}: {metric.ObservedUnitCount}/{metric.EligibleUnitCount} ({rate})");
        }

        output.WriteLine(
            $"CDP hard-seek coverage gate: " +
            $"{(report.CdpCorrelation.HardSeekCoverageGatePassed ? "passed" : "closed")}");

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(
                fullPath,
                JsonSerializer.Serialize(report, SerializerOptions) + Environment.NewLine);
            output.WriteLine($"JSON report: {fullPath}");
        }

        return report.PrivacyViolations.Count == 0 && report.InvalidUnitReasons.Count == 0 ? 0 : 1;
    }

    private static int ExecuteCalibration(string[] args, TextWriter output, TextWriter error)
    {
        string? inputPath = null;
        string? outputPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--input", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                inputPath = args[++index];
                continue;
            }

            if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                outputPath = args[++index];
                continue;
            }

            error.WriteLine($"Unknown or incomplete option: {args[index]}");
            WriteUsage(error);
            return 2;
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            error.WriteLine("calibrate requires --input <calibration-sessions.json>.");
            WriteUsage(error);
            return 2;
        }

        var fullInputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInputPath))
        {
            error.WriteLine($"Input file does not exist: {fullInputPath}");
            return 2;
        }

        string json;
        try
        {
            json = File.ReadAllText(fullInputPath);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Could not read {fullInputPath}: {ex.Message}");
            return 2;
        }

        var privacyViolations = SyncTelemetryPrivacyAudit.FindViolations(json);
        if (privacyViolations.Count > 0)
        {
            foreach (var violation in privacyViolations)
            {
                error.WriteLine($"Privacy validation failed: {violation}");
            }

            return 1;
        }

        IReadOnlyList<SyncEstimatorCalibrationSession>? sessions;
        try
        {
            sessions = JsonSerializer.Deserialize<List<SyncEstimatorCalibrationSession>>(
                json,
                SerializerOptions);
        }
        catch (JsonException ex)
        {
            error.WriteLine($"Could not deserialize calibration input: {ex.Path ?? "unknown"}");
            return 2;
        }

        if (sessions is null)
        {
            error.WriteLine("Calibration input was empty.");
            return 2;
        }

        var report = SyncEstimatorCalibrationPipeline.Analyze(sessions);
        output.WriteLine("Stream sync estimator calibration report");
        output.WriteLine($"Status: {report.Status}");
        output.WriteLine(
            $"Development/temporal holdout: {report.DevelopmentSessionCount}/" +
            $"{report.HoldoutSessionCount}");
        output.WriteLine($"Evidence SHA-256: {report.EvidenceSha256}");
        output.WriteLine($"Excluded units: {report.ExclusionReasons.Count}");
        foreach (var metric in report.HoldoutMetrics)
        {
            output.WriteLine(
                $"- {metric.EstimatorId}: matched={metric.MatchedObservationCount}, " +
                $"median={Format(metric.MedianAbsoluteErrorMilliseconds)} ms, " +
                $"p95={Format(metric.P95AbsoluteErrorMilliseconds)} ms");
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
            File.WriteAllText(
                fullOutputPath,
                JsonSerializer.Serialize(report, SerializerOptions) + Environment.NewLine);
            output.WriteLine($"Calibration artifact: {fullOutputPath}");
        }

        return report.ExclusionReasons.Count == 0 ? 0 : 1;
    }

    private static int ExecuteSuggestionEvaluation(
        string[] args,
        TextWriter output,
        TextWriter error)
    {
        string? inputPath = null;
        string? outputPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--input", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                inputPath = args[++index];
                continue;
            }

            if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                outputPath = args[++index];
                continue;
            }

            error.WriteLine($"Unknown or incomplete option: {args[index]}");
            WriteUsage(error);
            return 2;
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            error.WriteLine("suggestion-evaluate requires --input <privacy-safe-prior.json>.");
            return 2;
        }

        var fullInputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInputPath))
        {
            error.WriteLine($"Input file does not exist: {fullInputPath}");
            return 2;
        }

        var json = File.ReadAllText(fullInputPath);
        var privacyViolations = SyncTelemetryPrivacyAudit.FindViolations(json);
        if (privacyViolations.Count > 0)
        {
            foreach (var violation in privacyViolations)
            {
                error.WriteLine($"Privacy validation failed: {violation}");
            }

            return 1;
        }

        SyncBiasPriorDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SyncBiasPriorDocument>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            error.WriteLine($"Could not deserialize suggestion input: {ex.Path ?? "unknown"}");
            return 2;
        }

        if (document is null)
        {
            error.WriteLine("Suggestion input was empty.");
            return 2;
        }

        var report = SyncSuggestionTemporalEvaluator.Evaluate(document);
        output.WriteLine("Stream sync suggestion temporal holdout report");
        output.WriteLine($"Status: {report.Status}");
        output.WriteLine(
            $"Development/temporal holdout: {report.DevelopmentSessionCount}/" +
            $"{report.HoldoutSessionCount}");
        output.WriteLine($"Ineligible labels: {report.ExcludedIneligibleLabelCount}");
        output.WriteLine($"Unlabeled sessions excluded: {report.ExcludedUnlabeledSessionCount}");
        foreach (var metric in report.PairMetrics)
        {
            output.WriteLine(
                $"- {metric.Segment}: suggested={metric.SuggestedPairCount}/" +
                $"{metric.EligiblePairCount}, p90={Format(metric.P90AbsoluteProxyErrorMilliseconds)} ms");
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
            File.WriteAllText(
                fullOutputPath,
                JsonSerializer.Serialize(report, SerializerOptions) + Environment.NewLine);
            output.WriteLine($"Suggestion report: {fullOutputPath}");
        }

        return 0;
    }

    private static int ExecuteRuntimeProbe(string[] args, TextWriter output, TextWriter error)
    {
        string? outputPath = null;
        var timeoutSeconds = 20;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                outputPath = args[++index];
                continue;
            }

            if (args[index].Equals("--timeout-seconds", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length &&
                int.TryParse(args[++index], out timeoutSeconds) &&
                timeoutSeconds is >= 2 and <= 120)
            {
                continue;
            }

            error.WriteLine($"Unknown or incomplete option: {args[index]}");
            WriteUsage(error);
            return 2;
        }

        CdpRuntimeSchemaProbeReport report;
        try
        {
            report = CdpRuntimeSchemaProbe.RunAsync(TimeSpan.FromSeconds(timeoutSeconds))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            error.WriteLine($"Runtime probe could not complete: {ex.GetType().Name}");
            return 1;
        }

        output.WriteLine("Stream sync WebView2 CDP runtime schema probe");
        output.WriteLine($"Status: {report.Status}");
        output.WriteLine($"Runtime: {report.RuntimeVersion} ({report.RuntimeBucket})");
        output.WriteLine($"Protocol: {report.ProtocolVersion}");
        output.WriteLine($"Correlation: {report.CorrelationStatus}");
        output.WriteLine($"Failure lifecycle schema: {(report.LoadingFailedSchemaCompatible ? "compatible" : "not-observed")}");
        output.WriteLine($"Network.disable: {(report.NetworkDisableSucceeded ? "verified" : "failed")}");
        output.WriteLine($"Scope: {report.ProbeScope}; raw URL persisted: {report.RawUrlPersisted}");

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
            var json = JsonSerializer.Serialize(report, SerializerOptions) + Environment.NewLine;
            var privacyViolations = SyncTelemetryPrivacyAudit.FindViolations(json);
            if (privacyViolations.Count > 0)
            {
                error.WriteLine("Runtime probe report failed the privacy audit.");
                return 1;
            }

            File.WriteAllText(fullOutputPath, json);
            output.WriteLine($"Runtime evidence: {fullOutputPath}");
        }

        return report.IsCompatible ? 0 : 1;
    }

    public static void WriteUsage(TextWriter writer) => writer.WriteLine(
        "Usage:\n" +
        "  StreamOrchestra.Tools sync-pilot analyze " +
        "--input <telemetry.json|folder> [--input <path> ...] [--output <report.json>]\n" +
        "  StreamOrchestra.Tools sync-pilot calibrate " +
        "--input <calibration-sessions.json> [--output <artifact.json>]\n" +
        "  StreamOrchestra.Tools sync-pilot suggestion-evaluate " +
        "--input <privacy-safe-prior.json> [--output <report.json>]\n" +
        "  StreamOrchestra.Tools sync-pilot runtime-probe " +
        "[--timeout-seconds <2-120>] [--output <evidence.json>]");

    private static string Format(double? value) =>
        value is { } number ? number.ToString("0.###") : "n/a";

    private static IReadOnlyList<string>? ResolveInputFiles(
        IReadOnlyList<string> inputPaths,
        TextWriter error)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputPaths)
        {
            var fullPath = Path.GetFullPath(input);
            if (File.Exists(fullPath))
            {
                files.Add(fullPath);
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.EnumerateFiles(
                             fullPath,
                             "sync-telemetry-*.json",
                             SearchOption.TopDirectoryOnly))
                {
                    files.Add(Path.GetFullPath(file));
                }

                continue;
            }

            error.WriteLine($"Input path does not exist: {fullPath}");
            return null;
        }

        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
