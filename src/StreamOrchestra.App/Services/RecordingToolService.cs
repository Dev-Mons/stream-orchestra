using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace StreamOrchestra.App.Services;

public sealed partial class RecordingToolService
{
    private const string ExecutableName = "yt-dlp.exe";
    private static readonly Uri ExecutableUri = new(
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
    private static readonly Uri ChecksumsUri = new(
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS");

    private static readonly HttpClient SharedHttpClient = new();
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

    public string ManagedExecutablePath => Path.Combine(ToolsFolder, ExecutableName);

    public string? FindExecutable()
    {
        var candidates = new[]
        {
            ManagedExecutablePath,
            Path.Combine(AppContext.BaseDirectory, "tools", ExecutableName),
            Path.Combine(AppContext.BaseDirectory, ExecutableName)
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
            .Select(directory => Path.Combine(directory, ExecutableName))
            .FirstOrDefault(File.Exists);
    }

    public async Task<string> InstallLatestAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ToolsFolder);
        var temporaryPath = ManagedExecutablePath + ".download";

        try
        {
            progress?.Report(0);
            var checksums = await _httpClient.GetStringAsync(ChecksumsUri, cancellationToken);
            if (!TryParseExpectedSha256(checksums, out var expectedHash))
            {
                throw new InvalidDataException("공식 체크섬에서 yt-dlp.exe 항목을 찾지 못했습니다.");
            }

            using var response = await _httpClient.GetAsync(
                ExecutableUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                temporaryPath,
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
                        progress?.Report((double)downloadedBytes / totalBytes.Value);
                    }
                }
            }

            string actualHash;
            await using (var downloadedFile = File.OpenRead(temporaryPath))
            {
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(
                    downloadedFile,
                    cancellationToken));
            }
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("다운로드한 녹화 도구의 SHA-256 체크섬이 일치하지 않습니다.");
            }

            File.Move(temporaryPath, ManagedExecutablePath, overwrite: true);
            progress?.Report(1);
            return ManagedExecutablePath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static bool TryParseExpectedSha256(string checksums, out string hash)
    {
        foreach (var line in checksums.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ChecksumPattern().Match(line.Trim());
            if (match.Success)
            {
                hash = match.Groups["hash"].Value;
                return true;
            }
        }

        hash = "";
        return false;
    }

    [GeneratedRegex("^(?<hash>[0-9a-fA-F]{64})\\s+\\*?yt-dlp\\.exe$")]
    private static partial Regex ChecksumPattern();
}
