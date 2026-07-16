using System.Diagnostics;
using System.IO;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class SoopRecordingService : IDisposable
{
    private readonly object _sync = new();
    private Process? _process;
    private bool _stopRequested;

    public bool IsRecording
    {
        get
        {
            lock (_sync)
            {
                if (_process is null)
                {
                    return false;
                }

                try
                {
                    return !_process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }
    }

    public static bool IsSupportedSoopUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var host = uri.Host;
        return IsHostOrSubdomain(host, "sooplive.com") ||
               IsHostOrSubdomain(host, "sooplive.co.kr") ||
               IsHostOrSubdomain(host, "afreecatv.com");
    }

    public static IReadOnlyList<string> BuildArguments(RecordingRequest request)
    {
        var format = request.QualityId switch
        {
            "1080" => "best[height<=1080]/best",
            "720" => "best[height<=720]/best",
            "540" => "best[height<=540]/best",
            "360" => "best[height<=360]/best",
            _ => "best"
        };
        var timestamp = request.StartedAt.LocalDateTime.ToString("yyyyMMdd_HHmmss");

        return
        [
            "--ignore-config",
            "--no-playlist",
            "--newline",
            "--progress",
            "--no-part",
            "--hls-use-mpegts",
            "--windows-filenames",
            "--trim-filenames", "180",
            "--format", format,
            "--paths", request.OutputFolder,
            "--output", $"%(title).100B [%(id)s] {timestamp}.%(ext)s",
            request.StreamUrl
        ];
    }

    public async Task<RecordingResult> RecordAsync(
        string executablePath,
        RecordingRequest request,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("녹화 도구를 찾을 수 없습니다.", executablePath);
        }

        if (!IsSupportedSoopUrl(request.StreamUrl))
        {
            throw new ArgumentException("SOOP 방송 주소를 입력해 주세요.", nameof(request));
        }

        Directory.CreateDirectory(request.OutputFolder);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = request.OutputFolder
        };
        foreach (var argument in BuildArguments(request))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, e) => ReportLine(output, e.Data);
        process.ErrorDataReceived += (_, e) => ReportLine(output, e.Data);

        lock (_sync)
        {
            if (_process is not null)
            {
                process.Dispose();
                throw new InvalidOperationException("이미 녹화가 진행 중입니다.");
            }

            _process = process;
            _stopRequested = false;
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("녹화 프로세스를 시작하지 못했습니다.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var registration = cancellationToken.Register(Stop);
            await process.WaitForExitAsync(CancellationToken.None);

            bool stopRequested;
            lock (_sync)
            {
                stopRequested = _stopRequested;
            }

            if (stopRequested || cancellationToken.IsCancellationRequested)
            {
                return new RecordingResult(RecordingCompletion.Stopped, process.ExitCode, "녹화를 중지했습니다.");
            }

            return process.ExitCode == 0
                ? new RecordingResult(RecordingCompletion.Completed, 0, "방송이 종료되어 녹화를 마쳤습니다.")
                : new RecordingResult(
                    RecordingCompletion.Failed,
                    process.ExitCode,
                    "녹화에 실패했습니다. 방송 상태와 아래 로그를 확인해 주세요.");
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }

            process.Dispose();
        }
    }

    public void Stop()
    {
        Process? process;
        lock (_sync)
        {
            _stopRequested = true;
            process = _process;
        }

        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 종료와 중지 요청이 겹친 경우 이미 끝난 프로세스로 간주한다.
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static bool IsHostOrSubdomain(string host, string expectedHost) =>
        host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith('.' + expectedHost, StringComparison.OrdinalIgnoreCase);

    private static void ReportLine(IProgress<string>? progress, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            progress?.Report(line);
        }
    }
}
