using System.IO;
using System.Security.Cryptography;
using System.Text;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class HlsIdentityService
{
    private readonly byte[] _identityKey;

    public HlsIdentityService(byte[]? identityKey = null)
    {
        _identityKey = identityKey is { Length: >= 16 }
            ? identityKey.ToArray()
            : RandomNumberGenerator.GetBytes(32);
    }

    public SyncUrlIdentity CreateUrlIdentity(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return new SyncUrlIdentity("unknown", "unknown", "invalid", "");
        }

        var scheme = uri.Scheme is "http" or "https" ? uri.Scheme.ToLowerInvariant() : "other";
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        var canonical = $"{scheme}://{uri.IdnHost.ToLowerInvariant()}{port}{uri.AbsolutePath}";
        return new SyncUrlIdentity(
            scheme,
            BucketHost(uri.IdnHost),
            BucketPath(uri.AbsolutePath),
            CreateOpaqueIdentity("hls-url", canonical));
    }

    public string CreateOpaqueIdentity(string domain, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        using var hmac = new HMACSHA256(_identityKey);
        var normalizedDomain = string.IsNullOrWhiteSpace(domain)
            ? "opaque"
            : domain.Trim().ToLowerInvariant();
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{normalizedDomain}\0{value}"));
        return Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static string BucketHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "unknown";
        }

        var normalized = host.Trim('.').ToLowerInvariant();
        if (System.Net.IPAddress.TryParse(normalized, out _))
        {
            return "ip-address";
        }

        var labels = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2)
        {
            return normalized;
        }

        var suffixLength = labels[^1].Length == 2 && labels[^2].Length <= 3 ? 3 : 2;
        return string.Join('.', labels.TakeLast(Math.Min(labels.Length, suffixLength)));
    }

    private static string BucketPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "root";
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var extension = Path.GetExtension(segments[^1]).ToLowerInvariant();
        var extensionBucket = extension.Length is > 1 and <= 12 &&
                              extension.Skip(1).All(char.IsLetterOrDigit)
            ? extension
            : ".other";
        return $"depth-{Math.Min(segments.Length, 9)}{extensionBucket}";
    }
}
