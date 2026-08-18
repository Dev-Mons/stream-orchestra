using System.IO;
using System.Security.Cryptography;

namespace StreamOrchestra.App.Services;

public sealed class SyncTelemetryIdentityKeyStore
{
    public const string DefaultFileName = "sync-telemetry-identity.key";

    private readonly string _filePath;
    private readonly ISyncBiasDataProtector _protector;

    public SyncTelemetryIdentityKeyStore(
        string filePath,
        ISyncBiasDataProtector? protector = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A telemetry identity key path is required.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        _protector = protector ?? new WindowsDpapiSyncBiasProtector();
    }

    public string FilePath => _filePath;

    public bool Exists => File.Exists(_filePath);

    public byte[] LoadOrCreate()
    {
        if (File.Exists(_filePath))
        {
            var encrypted = File.ReadAllBytes(_filePath);
            try
            {
                var decrypted = _protector.Unprotect(encrypted);
                if (decrypted.Length >= 32)
                {
                    return decrypted;
                }

                CryptographicOperations.ZeroMemory(decrypted);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedKey = _protector.Protect(key);
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, protectedKey);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return key;
    }

    public bool Delete()
    {
        if (!File.Exists(_filePath))
        {
            return false;
        }

        File.Delete(_filePath);
        return true;
    }
}
