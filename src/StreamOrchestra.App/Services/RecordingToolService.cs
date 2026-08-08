using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace StreamOrchestra.App.Services;

public sealed partial class RecordingToolService
{
    private const string YtDlpExecutableName = "yt-dlp.exe";
    private const string FfmpegExecutableName = "ffmpeg.exe";
    private const string FfprobeExecutableName = "ffprobe.exe";
    private const string FfmpegArchiveName = "ffmpeg-master-latest-win64-gpl.zip";
    private static readonly Uri YtDlpExecutableUri = new(
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
    private static readonly Uri YtDlpChecksumsUri = new(
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS");
    private static readonly Uri FfmpegArchiveUri = new(
        $"https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/{FfmpegArchiveName}");
    private static readonly Uri FfmpegChecksumsUri = new(
        "https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/checksums.sha256");

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(15)
    };
    private readonly HttpClient _httpClient;

    public RecordingToolService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        ToolsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamOrchestra",
            "Tools");
    }

    public string ToolsFolder { get; }

    public string ManagedExecutablePath => Path.Combine(ToolsFolder, YtDlpExecutableName);

    public string ManagedFfmpegPath => Path.Combine(ToolsFolder, FfmpegExecutableName);

    public string ManagedFfprobePath => Path.Combine(ToolsFolder, FfprobeExecutableName);

    public string? FindExecutable() => FindExecutable(YtDlpExecutableName, ManagedExecutablePath);

    public string? FindFfmpegExecutable() => FindExecutable(FfmpegExecutableName, ManagedFfmpegPath);

    public string? FindFfprobeExecutable() => FindExecutable(FfprobeExecutableName, ManagedFfprobePath);

    public bool AreRequiredToolsAvailable() =>
        FindExecutable() is not null && FindFfmpegExecutable() is not null;

    private static string? FindExecutable(string executableName, string managedPath)
    {
        var candidates = new[]
        {
            managedPath,
            Path.Combine(AppContext.BaseDirectory, "tools", executableName),
            Path.Combine(AppContext.BaseDirectory, executableName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return pathDirectories
            .Select(directory => Path.Combine(directory, executableName))
            .FirstOrDefault(File.Exists);
    }

    public async Task<string> InstallLatestAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ToolsFolder);
        var ytDlpTemporaryPath = ManagedExecutablePath + ".download";
        var ffmpegArchiveTemporaryPath = Path.Combine(ToolsFolder, FfmpegArchiveName + ".download");
        var ffmpegStagingFolder = Path.Combine(ToolsFolder, $"ffmpeg-staging-{Guid.NewGuid():N}");

        try
        {
            progress?.Report(0);
            await DownloadVerifiedFileAsync(
                YtDlpExecutableUri,
                YtDlpChecksumsUri,
                YtDlpExecutableName,
                ytDlpTemporaryPath,
                progress,
                progressStart: 0,
                progressEnd: 0.1,
                cancellationToken);
            File.Move(ytDlpTemporaryPath, ManagedExecutablePath, overwrite: true);

            await DownloadVerifiedFileAsync(
                FfmpegArchiveUri,
                FfmpegChecksumsUri,
                FfmpegArchiveName,
                ffmpegArchiveTemporaryPath,
                progress,
                progressStart: 0.1,
                progressEnd: 0.9,
                cancellationToken);

            Directory.CreateDirectory(ffmpegStagingFolder);
            var stagedFfmpegPath = Path.Combine(ffmpegStagingFolder, FfmpegExecutableName);
            var stagedFfprobePath = Path.Combine(ffmpegStagingFolder, FfprobeExecutableName);
            ExtractExecutable(ffmpegArchiveTemporaryPath, FfmpegExecutableName, stagedFfmpegPath);
            progress?.Report(0.95);
            ExtractExecutable(ffmpegArchiveTemporaryPath, FfprobeExecutableName, stagedFfprobePath);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(stagedFfmpegPath, ManagedFfmpegPath, overwrite: true);
            File.Move(stagedFfprobePath, ManagedFfprobePath, overwrite: true);
            progress?.Report(1);
            return ManagedExecutablePath;
        }
        finally
        {
            if (File.Exists(ytDlpTemporaryPath))
            {
                File.Delete(ytDlpTemporaryPath);
            }

            if (File.Exists(ffmpegArchiveTemporaryPath))
            {
                File.Delete(ffmpegArchiveTemporaryPath);
            }

            if (Directory.Exists(ffmpegStagingFolder))
            {
                Directory.Delete(ffmpegStagingFolder, recursive: true);
            }
        }
    }

    private async Task DownloadVerifiedFileAsync(
        Uri downloadUri,
        Uri checksumsUri,
        string assetName,
        string destinationPath,
        IProgress<double>? progress,
        double progressStart,
        double progressEnd,
        CancellationToken cancellationToken)
    {
        var checksums = await _httpClient.GetStringAsync(checksumsUri, cancellationToken);
        if (!TryParseExpectedSha256(checksums, assetName, out var expectedHash))
        {
            throw new InvalidDataException($"공식 체크섬에서 {assetName} 항목을 찾지 못했습니다.");
        }

        using var response = await _httpClient.GetAsync(
            downloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true))
        {
            var buffer = new byte[81920];
            long downloadedBytes = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;
                if (totalBytes is > 0)
                {
                    var ratio = Math.Clamp((double)downloadedBytes / totalBytes.Value, 0, 1);
                    progress?.Report(progressStart + ((progressEnd - progressStart) * ratio));
                }
            }
        }

        string actualHash;
        await using (var downloadedFile = File.OpenRead(destinationPath))
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(
                downloadedFile,
                cancellationToken));
        }
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"다운로드한 {assetName}의 SHA-256 체크섬이 일치하지 않습니다.");
        }

        progress?.Report(progressEnd);
    }

    private static void ExtractExecutable(string archivePath, string executableName, string destinationPath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.SingleOrDefault(candidate =>
            candidate.Name.Equals(executableName, StringComparison.OrdinalIgnoreCase) &&
            candidate.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidDataException($"FFmpeg 압축 파일에서 {executableName}을 찾지 못했습니다.");
        }

        entry.ExtractToFile(destinationPath, overwrite: true);
    }

    public static bool TryParseExpectedSha256(string checksums, out string hash)
    {
        return TryParseExpectedSha256(checksums, YtDlpExecutableName, out hash);
    }

    public static bool TryParseExpectedSha256(string checksums, string expectedFileName, out string hash)
    {
        foreach (var line in checksums.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ChecksumPattern().Match(line.Trim());
            if (match.Success &&
                match.Groups["file"].Value.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                hash = match.Groups["hash"].Value;
                return true;
            }
        }

        hash = "";
        return false;
    }

    [GeneratedRegex("^(?<hash>[0-9a-fA-F]{64})\\s+\\*?(?<file>[^\\s]+)$")]
    private static partial Regex ChecksumPattern();
}
