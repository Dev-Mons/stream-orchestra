using System.Text.Json;
using StreamOrchestra.Tools;

namespace StreamOrchestra.Tests;

public sealed class SyncTelemetryOverheadProbeTests : IDisposable
{
    private readonly string _outputFolder = Path.Combine(
        Path.GetTempPath(),
        "StreamOrchestra.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Measure_DefaultDisabledPath_ProducesBoundedReportWithoutEnabledObservations()
    {
        var report = SyncTelemetryOverheadProbe.Measure(
            iterations: 100_000,
            trialCount: 3,
            warmupIterations: 10_000);

        Assert.Equal(1, report.SchemaVersion);
        Assert.Equal(100_000, report.IterationsPerTrial);
        Assert.Equal(3, report.TrialCount);
        Assert.Equal(3, report.Trials.Count);
        Assert.True(report.RemainedDisabled);
        Assert.Equal(0, report.UnexpectedEnabledObservationCount);
        Assert.All(report.Trials, trial =>
        {
            Assert.Equal(100_000, trial.Iterations);
            Assert.True(trial.ElapsedMilliseconds >= 0);
            Assert.True(trial.WallNanosecondsPerCheck >= 0);
            Assert.True(trial.ProcessCpuNanosecondsPerCheck >= 0);
            Assert.True(trial.AllocatedBytes >= 0);
            Assert.Equal(0, trial.UnexpectedEnabledObservations);
        });
        Assert.True(report.WallNanosecondsPerCheckP95 >= report.WallNanosecondsPerCheckP50);
        Assert.True(report.ProcessCpuNanosecondsPerCheckP95 >= report.ProcessCpuNanosecondsPerCheckP50);
    }

    [Fact]
    public void Execute_WithOutput_WritesMachineReadableReport()
    {
        var outputPath = Path.Combine(_outputFolder, "telemetry-overhead.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = SyncTelemetryOverheadCommand.Execute(
            ["--iterations", "100000", "--trials", "2", "--output", outputPath],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Sync telemetry off-path overhead", output.ToString());
        Assert.Contains("Remained disabled: True", output.ToString());
        Assert.Contains($"JSON report: {Path.GetFullPath(outputPath)}", output.ToString());
        Assert.Equal("", error.ToString());

        using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(100_000, root.GetProperty("iterationsPerTrial").GetInt32());
        Assert.Equal(2, root.GetProperty("trialCount").GetInt32());
        Assert.True(root.GetProperty("remainedDisabled").GetBoolean());
        Assert.Equal(2, root.GetProperty("trials").GetArrayLength());
    }

    [Theory]
    [InlineData("--iterations", "999")]
    [InlineData("--trials", "0")]
    [InlineData("--unknown", "value")]
    public void Execute_InvalidArguments_ReturnsUsageError(string option, string value)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = SyncTelemetryOverheadCommand.Execute([option, value], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage:", error.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputFolder))
        {
            Directory.Delete(_outputFolder, recursive: true);
        }
    }
}
