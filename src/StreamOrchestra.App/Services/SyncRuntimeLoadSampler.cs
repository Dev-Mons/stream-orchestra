using System.Diagnostics;

namespace StreamOrchestra.App.Services;

public sealed class SyncRuntimeLoadSampler : IDisposable
{
    private readonly Process _process;
    private readonly Func<long> _timestampProvider;
    private TimeSpan _previousCpu;
    private long _previousTimestamp;

    public SyncRuntimeLoadSampler(Func<long>? timestampProvider = null)
    {
        _process = Process.GetCurrentProcess();
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _previousCpu = _process.TotalProcessorTime;
        _previousTimestamp = _timestampProvider();
    }

    public string CapturePcLoadBucket()
    {
        var now = _timestampProvider();
        var cpu = _process.TotalProcessorTime;
        var elapsedSeconds = (now - _previousTimestamp) / (double)Stopwatch.Frequency;
        var cpuSeconds = Math.Max(0, (cpu - _previousCpu).TotalSeconds);
        _previousTimestamp = now;
        _previousCpu = cpu;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
        {
            return "unknown";
        }

        var machinePercent = cpuSeconds / elapsedSeconds / Math.Max(1, Environment.ProcessorCount) * 100;
        return machinePercent switch
        {
            < 5 => "low",
            < 20 => "medium",
            _ => "high"
        };
    }

    public void Dispose() => _process.Dispose();
}
