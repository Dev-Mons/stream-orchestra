using System.Security.Cryptography;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncTelemetryIdentityKeyStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "StreamOrchestra.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Store_IsLazyProtectedStableAndExplicitlyDeletable()
    {
        var path = Path.Combine(_root, SyncTelemetryIdentityKeyStore.DefaultFileName);
        var store = new SyncTelemetryIdentityKeyStore(path, new XorProtector());

        Assert.False(store.Exists);

        var first = store.LoadOrCreate();
        var persisted = File.ReadAllBytes(path);
        var second = store.LoadOrCreate();

        Assert.True(store.Exists);
        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, persisted);
        Assert.DoesNotContain(Convert.ToHexString(first), Convert.ToHexString(persisted), StringComparison.Ordinal);
        Assert.True(store.Delete());
        Assert.False(store.Exists);
        Assert.False(store.Delete());

        CryptographicOperations.ZeroMemory(first);
        CryptographicOperations.ZeroMemory(second);
        CryptographicOperations.ZeroMemory(persisted);
    }

    [Fact]
    public void DeletingKey_RotatesFutureCrossSessionIdentities()
    {
        var path = Path.Combine(_root, SyncTelemetryIdentityKeyStore.DefaultFileName);
        var store = new SyncTelemetryIdentityKeyStore(path, new XorProtector());
        var firstKey = store.LoadOrCreate();
        var firstHash = new SyncTelemetryPrivacy(firstKey).CreateOpaqueIdentity(
            SyncTelemetryIdentityPurpose.Channel,
            "channel-sentinel");

        Assert.True(store.Delete());
        var secondKey = store.LoadOrCreate();
        var secondHash = new SyncTelemetryPrivacy(secondKey).CreateOpaqueIdentity(
            SyncTelemetryIdentityPurpose.Channel,
            "channel-sentinel");

        Assert.NotEqual(firstKey, secondKey);
        Assert.NotEqual(firstHash, secondHash);

        CryptographicOperations.ZeroMemory(firstKey);
        CryptographicOperations.ZeroMemory(secondKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class XorProtector : ISyncBiasDataProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Select(value => (byte)(value ^ 0xA5)).ToArray();

        public byte[] Unprotect(byte[] ciphertext) => Protect(ciphertext);
    }
}
