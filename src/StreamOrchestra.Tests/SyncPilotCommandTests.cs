using System.Text.Json;
using System.Text.Json.Serialization;
using StreamOrchestra.App.Models;
using StreamOrchestra.Tools;

namespace StreamOrchestra.Tests;

public sealed class SyncPilotCommandTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "StreamOrchestra.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CalibrateCommand_WritesVersionedArtifactFromPrivacySafeInput()
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "calibration.json");
        var artifact = Path.Combine(_root, "artifact.json");
        File.WriteAllText(input, JsonSerializer.Serialize(
            new[] { CalibrationSession("session-a") },
            JsonOptions));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = SyncPilotCommand.Execute(
            ["calibrate", "--input", input, "--output", artifact],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(artifact));
        Assert.Contains("Status: insufficient-development", output.ToString());
        Assert.Equal("", error.ToString());
        using var report = JsonDocument.Parse(File.ReadAllText(artifact));
        Assert.Equal("stream-sync-calibration-v1", report.RootElement
            .GetProperty("calibrationVersion").GetString());
        Assert.Equal(64, report.RootElement.GetProperty("evidenceSha256").GetString()!.Length);
    }

    [Fact]
    public void SuggestionCommand_WritesLocalOnlyTemporalReport()
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "prior.json");
        var reportPath = Path.Combine(_root, "suggestion.json");
        File.WriteAllText(input, JsonSerializer.Serialize(new SyncBiasPriorDocument(), JsonOptions));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = SyncPilotCommand.Execute(
            ["suggestion-evaluate", "--input", input, "--output", reportPath],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(reportPath));
        Assert.Contains("Unlabeled sessions excluded: 0", output.ToString());
        Assert.Equal("", error.ToString());
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.True(report.RootElement.GetProperty("isLocalOnly").GetBoolean());
        Assert.False(report.RootElement.GetProperty("treatsUnadjustedSessionsAsZeroLabels").GetBoolean());
    }

    [Fact]
    public void RuntimeProbeCommand_RejectsOutOfRangeTimeoutWithoutStartingWebView()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = SyncPilotCommand.Execute(
            ["runtime-probe", "--timeout-seconds", "1"],
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown or incomplete option", error.ToString());
        Assert.Contains("sync-pilot runtime-probe", error.ToString());
    }

    [Fact]
    public void CheckedInRuntimeEvidence_IsCompatibleAndContainsNoProbeUrl()
    {
        var evidencePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "docs",
            "evidence",
            "stream-sync-webview2-runtime.json"));
        var json = File.ReadAllText(evidencePath);

        Assert.DoesNotContain("127.0.0.1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("probe/live", json, StringComparison.Ordinal);
        Assert.DoesNotContain("probe/fail", json, StringComparison.Ordinal);
        using var report = JsonDocument.Parse(json);
        var root = report.RootElement;
        Assert.Equal("compatible", root.GetProperty("status").GetString());
        Assert.Equal("correlated", root.GetProperty("correlationStatus").GetString());
        Assert.True(root.GetProperty("loadingFailedSchemaCompatible").GetBoolean());
        Assert.True(root.GetProperty("networkDisableSucceeded").GetBoolean());
        Assert.False(root.GetProperty("rawUrlPersisted").GetBoolean());
        Assert.True(root.GetProperty("isCompatible").GetBoolean());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static SyncEstimatorCalibrationSession CalibrationSession(string id)
    {
        var start = DateTimeOffset.Parse("2026-08-18T00:00:00Z");
        return new SyncEstimatorCalibrationSession
        {
            IndependentSessionId = id,
            StartedAtUtc = start,
            IsIndependentSession = true,
            Observations =
            [
                new SyncEstimatorCalibrationObservation
                {
                    ObservationId = "observation-a",
                    SourceIdentity = "source-a",
                    SourceEpoch = 1,
                    RawOffsetMilliseconds = 100,
                    LegacyOffsetMilliseconds = 110,
                    ReferenceOffsetMilliseconds = 105,
                    IsIndependentReference = true,
                    ObservedAtUtc = start.AddSeconds(1),
                    ObservedMonotonicTicks = 1000,
                    MonotonicFrequency = 1000,
                    IndependentEvidenceCount = 3,
                    TimelineConfidence = 0.8,
                    ManualBiasConfidence = 0.7,
                    ControllabilityConfidence = 0.9,
                    TimelineOutcomeSucceeded = true,
                    ManualBiasOutcomeSucceeded = true,
                    ControllabilityOutcomeSucceeded = true
                }
            ]
        };
    }
}
