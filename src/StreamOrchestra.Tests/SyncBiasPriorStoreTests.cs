using System.Text;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncBiasPriorStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "StreamOrchestra.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void EncryptedStoreRoundTripsWithoutWritingViewingHistoryAsPlaintext()
    {
        const string sentinel = "private-channel-sentinel";
        var path = Path.Combine(_root, "priors.dat");
        var store = new EncryptedSyncBiasPriorStore(path, new XorProtector());
        var document = new SyncBiasPriorDocument
        {
            UpdatedAtUtc = DateTimeOffset.Parse("2026-08-18T00:00:00Z"),
            PairObservations = [new SyncBiasPairObservation
            {
                ObservationId = "observation",
                IndependentSessionHash = "session",
                Left = new SyncBiasContext(sentinel, "1080p", "cdn"),
                Right = new SyncBiasContext("other-channel", "1080p", "cdn"),
                DelayDifferenceMilliseconds = 500,
                OccurredAtUtc = DateTimeOffset.Parse("2026-08-18T00:00:00Z"),
                IsIndependentSession = true,
                IsStableFinal = true,
                EventKind = SyncBiasManualEventKind.AlignmentConfirmed
            }]
        };

        store.Save(document);

        Assert.DoesNotContain(sentinel, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        var loaded = store.Load();
        Assert.Equal(sentinel, Assert.Single(loaded.PairObservations).Left.StableChannelHash);
    }

    [Fact]
    public void ExplicitExportAndDeleteAreSupported()
    {
        var path = Path.Combine(_root, "priors.dat");
        var export = Path.Combine(_root, "export.json");
        var store = new EncryptedSyncBiasPriorStore(path, new XorProtector());
        store.Save(new SyncBiasPriorDocument
        {
            ManualEvents = [new SyncBiasManualEvent
            {
                EventId = "event-hash",
                Context = new SyncBiasContext("channel-hash", "unknown", "unknown"),
                EventKind = SyncBiasManualEventKind.SuggestionRejected,
                OccurredAtUtc = DateTimeOffset.Parse("2026-08-18T00:00:00Z")
            }]
        });

        store.ExportPrivacySafe(export);
        store.DeleteAll();

        Assert.Contains("channel-hash", File.ReadAllText(export), StringComparison.Ordinal);
        Assert.False(File.Exists(path));
        Assert.Empty(store.Load().ManualEvents);
    }

    [Fact]
    public void CorruptCiphertextFailsClosedToAnEmptyDocument()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "priors.dat");
        File.WriteAllBytes(path, [1, 2, 3]);
        var store = new EncryptedSyncBiasPriorStore(path, new RejectingProtector());

        var loaded = store.Load();

        Assert.Empty(loaded.PairObservations);
        Assert.Empty(loaded.ManualEvents);
    }

    [Fact]
    public void WindowsDpapiProtectorRoundTripsForTheCurrentUser()
    {
        var protector = new WindowsDpapiSyncBiasProtector();
        var plaintext = Encoding.UTF8.GetBytes("sync-bias-private-data");

        var ciphertext = protector.Protect(plaintext);
        var roundTrip = protector.Unprotect(ciphertext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, roundTrip);
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

    private sealed class RejectingProtector : ISyncBiasDataProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext;

        public byte[] Unprotect(byte[] ciphertext) => throw new System.Security.Cryptography.CryptographicException();
    }
}
