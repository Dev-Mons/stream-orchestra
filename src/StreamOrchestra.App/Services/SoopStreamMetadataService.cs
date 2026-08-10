using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class SoopStreamMetadataService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly HttpClient _httpClient;

    public SoopStreamMetadataService(HttpClient? httpClient = null, string? cacheFolder = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        CacheFolder = cacheFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamOrchestra",
            "RecordingThumbnails");
    }

    public string CacheFolder { get; }

    public async Task<SoopResolvedStreamMetadata> ResolveAsync(
        string executablePath,
        RecordingRequest request,
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadataAsync(executablePath, request, cancellationToken);
        var thumbnailUrl = metadata.ThumbnailUrl;
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            try
            {
                thumbnailUrl = await ReadPageThumbnailUrlAsync(request.StreamUrl, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // SOOP 페이지 조회가 실패해도 제목/방송자 메타데이터는 그대로 사용한다.
            }
        }

        string? thumbnailPath = null;
        if (!string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            try
            {
                thumbnailPath = await DownloadThumbnailAsync(
                    thumbnailUrl,
                    request.StreamUrl,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                // 메타데이터는 유지하고 썸네일만 없는 상태로 계속한다.
            }
        }

        return new SoopResolvedStreamMetadata(metadata.DisplayName, metadata.Title, thumbnailPath);
    }

    public async Task<string?> ReadPageThumbnailUrlAsync(
        string streamUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) ||
            !SoopRecordingService.IsSupportedSoopUrl(streamUrl))
        {
            return null;
        }

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParsePageThumbnailUrl(html);
    }

    public async Task<SoopStreamMetadata> ReadMetadataAsync(
        string executablePath,
        RecordingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("녹화 도구를 찾을 수 없습니다.", executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in BuildMetadataArguments(request))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("방송 정보를 불러오지 못했습니다.");
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // 프로세스 종료와 취소가 겹친 경우 이미 끝난 것으로 본다.
            }
        });
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "방송 정보를 불러오지 못했습니다."
                : error.Trim());
        }

        return ParseMetadataJson(output);
    }

    public static IReadOnlyList<string> BuildMetadataArguments(RecordingRequest request)
    {
        var hasUsername = !string.IsNullOrWhiteSpace(request.Username);
        var hasPassword = !string.IsNullOrEmpty(request.Password);
        if (hasUsername != hasPassword)
        {
            throw new ArgumentException("SOOP ID와 비밀번호를 모두 입력해 주세요.", nameof(request));
        }

        List<string> arguments =
        [
            "--ignore-config",
            "--no-playlist",
            "--skip-download",
            "--no-warnings",
            "--socket-timeout", "15",
            "--dump-single-json"
        ];
        if (hasUsername)
        {
            arguments.Add("--username");
            arguments.Add(request.Username!.Trim());
            arguments.Add("--password");
            arguments.Add(request.Password!);
        }

        arguments.Add(request.StreamUrl);
        return arguments;
    }

    public static SoopStreamMetadata ParseMetadataJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var title = ReadString(root, "title") ?? "SOOP 라이브 방송";
        var displayName = ReadString(root, "uploader")
                          ?? ReadString(root, "channel")
                          ?? ReadString(root, "channel_id")
                          ?? "SOOP";
        var thumbnail = ReadString(root, "thumbnail") ?? ReadLastThumbnail(root);
        return new SoopStreamMetadata(displayName, title, thumbnail);
    }

    public static string? ParsePageThumbnailUrl(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        foreach (var pattern in PageThumbnailPatterns())
        {
            var match = Regex.Match(
                html,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            if (!match.Success)
            {
                continue;
            }

            var candidate = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<string> DownloadThumbnailAsync(
        string thumbnailUrl,
        string streamUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException("올바른 방송 썸네일 주소가 아닙니다.");
        }

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var extension = response.Content.Headers.ContentType?.MediaType?.Equals(
            "image/png",
            StringComparison.OrdinalIgnoreCase) == true
            ? ".png"
            : ".jpg";
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(streamUrl)));
        Directory.CreateDirectory(CacheFolder);
        var destination = Path.Combine(CacheFolder, $"{hash}{extension}");
        var temporary = $"{destination}.tmp.{Guid.NewGuid():N}";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(temporary))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }

    private static string? ReadLastThumbnail(JsonElement root)
    {
        if (!root.TryGetProperty("thumbnails", out var thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return thumbnails.EnumerateArray()
            .Select(thumbnail => ReadString(thumbnail, "url"))
            .LastOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }

    private static string[] PageThumbnailPatterns() =>
    [
        "<meta[^>]+(?:property|name)\\s*=\\s*[\\\"'](?:og:image|twitter:image)[\\\"'][^>]+content\\s*=\\s*[\\\"'](?<url>[^\\\"']+)[\\\"']",
        "<meta[^>]+content\\s*=\\s*[\\\"'](?<url>[^\\\"']+)[\\\"'][^>]+(?:property|name)\\s*=\\s*[\\\"'](?:og:image|twitter:image)[\\\"']"
    ];
}
