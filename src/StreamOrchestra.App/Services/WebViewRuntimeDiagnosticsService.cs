using System.Diagnostics;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class WebViewRuntimeDiagnosticsService
{
    private static readonly string[] WebViewProcessNames =
    [
        "msedgewebview2",
        "Microsoft Edge WebView2"
    ];

    private TimeSpan? _lastProcessorTime;
    private DateTimeOffset? _lastCapturedAt;

    public RuntimeDiagnosticsSnapshot Capture()
    {
        var capturedAt = DateTimeOffset.Now;
        var processes = GetWebViewProcesses();
        var processorTime = TimeSpan.Zero;
        var workingSetBytes = 0L;
        var privateMemoryBytes = 0L;

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    process.Refresh();
                    processorTime += process.TotalProcessorTime;
                    workingSetBytes += process.WorkingSet64;
                    privateMemoryBytes += process.PrivateMemorySize64;
                }
                catch (InvalidOperationException)
                {
                    // A WebView2 process can exit while diagnostics are being sampled.
                }
            }
        }

        var cpuPercent = CalculateCpuPercent(capturedAt, processorTime);

        _lastCapturedAt = capturedAt;
        _lastProcessorTime = processorTime;

        return new RuntimeDiagnosticsSnapshot(
            capturedAt,
            processes.Length,
            BytesToMegabytes(workingSetBytes),
            BytesToMegabytes(privateMemoryBytes),
            cpuPercent);
    }

    private static Process[] GetWebViewProcesses()
    {
        return WebViewProcessNames
            .SelectMany(Process.GetProcessesByName)
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .ToArray();
    }

    private double? CalculateCpuPercent(DateTimeOffset capturedAt, TimeSpan processorTime)
    {
        if (_lastCapturedAt is null || _lastProcessorTime is null)
        {
            return null;
        }

        var elapsedSeconds = (capturedAt - _lastCapturedAt.Value).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return null;
        }

        var processorSeconds = (processorTime - _lastProcessorTime.Value).TotalSeconds;
        var cpuPercent = processorSeconds / elapsedSeconds / Environment.ProcessorCount * 100;

        return Math.Max(0, cpuPercent);
    }

    private static double BytesToMegabytes(long bytes)
    {
        return bytes / 1024d / 1024d;
    }
}
