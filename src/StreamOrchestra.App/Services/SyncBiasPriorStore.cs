using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public interface ISyncBiasDataProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] ciphertext);
}

public interface ISyncBiasPriorStore
{
    SyncBiasPriorDocument Load();

    void Save(SyncBiasPriorDocument document);

    void DeleteAll();

    void ExportPrivacySafe(string destinationPath);
}

public sealed class WindowsDpapiSyncBiasProtector : ISyncBiasDataProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    public byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);

    public byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, protect: false);

    private static byte[] Transform(byte[] value, bool protect)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is available only on Windows.");
        }

        var input = DataBlob.From(value);
        try
        {
            DataBlob output;
            var succeeded = protect
                ? CryptProtectData(
                    ref input,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output)
                : CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);
            if (!succeeded)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var result = new byte[output.Length];
                if (output.Length > 0)
                {
                    Marshal.Copy(output.Data, result, 0, output.Length);
                }
                return result;
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                {
                    LocalFree(output.Data);
                }
            }
        }
        finally
        {
            input.Dispose();
        }
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;

        public IntPtr Data;

        public static DataBlob From(byte[] value)
        {
            var data = value.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(value.Length);
            if (value.Length > 0)
            {
                Marshal.Copy(value, 0, data, value.Length);
            }
            return new DataBlob { Length = value.Length, Data = data };
        }

        public void Dispose()
        {
            if (Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Data);
                Data = IntPtr.Zero;
                Length = 0;
            }
        }
    }
}

public sealed class EncryptedSyncBiasPriorStore : ISyncBiasPriorStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly string _filePath;
    private readonly ISyncBiasDataProtector _protector;

    public EncryptedSyncBiasPriorStore(string filePath, ISyncBiasDataProtector? protector = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }
        _filePath = Path.GetFullPath(filePath);
        _protector = protector ?? new WindowsDpapiSyncBiasProtector();
    }

    public SyncBiasPriorDocument Load()
    {
        if (!File.Exists(_filePath))
        {
            return new SyncBiasPriorDocument();
        }

        try
        {
            var encrypted = File.ReadAllBytes(_filePath);
            var plaintext = _protector.Unprotect(encrypted);
            try
            {
                return JsonSerializer.Deserialize<SyncBiasPriorDocument>(plaintext, SerializerOptions) ??
                       new SyncBiasPriorDocument();
            }
            finally
            {
                Array.Clear(plaintext);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            System.ComponentModel.Win32Exception or CryptographicException)
        {
            return new SyncBiasPriorDocument();
        }
    }

    public void Save(SyncBiasPriorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        var encrypted = _protector.Protect(plaintext);
        Array.Clear(plaintext);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, encrypted);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            Array.Clear(encrypted);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void DeleteAll()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    public void ExportPrivacySafe(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("A destination path is required.", nameof(destinationPath));
        }

        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        File.WriteAllText(
            fullDestination,
            JsonSerializer.Serialize(Load(), SerializerOptions));
    }
}

public sealed class SyncBiasIdentityKeyStore
{
    private readonly string _filePath;
    private readonly ISyncBiasDataProtector _protector;

    public SyncBiasIdentityKeyStore(string filePath, ISyncBiasDataProtector? protector = null)
    {
        _filePath = Path.GetFullPath(filePath);
        _protector = protector ?? new WindowsDpapiSyncBiasProtector();
    }

    public byte[] LoadOrCreate()
    {
        if (File.Exists(_filePath))
        {
            var decrypted = _protector.Unprotect(File.ReadAllBytes(_filePath));
            if (decrypted.Length >= 32)
            {
                return decrypted;
            }
            Array.Clear(decrypted);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = _protector.Protect(key);
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, encrypted);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            Array.Clear(encrypted);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        return key;
    }
}
