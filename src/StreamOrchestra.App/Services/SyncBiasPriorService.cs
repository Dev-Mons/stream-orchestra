using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed record SyncBiasGroupMember(
    int SlotId,
    SyncBiasContext Context,
    string BroadcastSessionIdentity,
    int FinalDelayMilliseconds,
    int AlgorithmPriorMilliseconds = 0,
    int UserResidualMilliseconds = 0);

public sealed record SyncBiasPriorServiceOptions
{
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(180);

    public int MaximumPairObservations { get; init; } = 4096;

    public int MaximumManualEvents { get; init; } = 4096;
}

public interface ISyncBiasPriorService
{
    bool IsEnabled { get; }

    SyncBiasContext? CreateContext(string? channelIdentity, string? qualityBucket, string? cdnBucket);

    IReadOnlyDictionary<int, SyncBiasSuggestion> GetCompatibleGroupSuggestions(
        IReadOnlyList<SyncBiasGroupMember> members,
        DateTimeOffset nowUtc);

    bool RecordAlignmentConfirmation(
        IReadOnlyList<SyncBiasGroupMember> members,
        DateTimeOffset occurredAtUtc);

    void RecordSuggestionEvent(
        SyncBiasGroupMember member,
        SyncBiasSuggestion suggestion,
        SyncBiasManualEventKind eventKind,
        SyncManualDelayComponents components,
        DateTimeOffset occurredAtUtc);

    void RecordUserAdjustment(
        SyncBiasGroupMember member,
        SyncManualDelayComponents previous,
        SyncManualDelayComponents current,
        DateTimeOffset occurredAtUtc);

    void DeleteAll();

    void ExportPrivacySafe(string destinationPath);
}

public sealed class SyncBiasPriorService : ISyncBiasPriorService
{
    private readonly object _gate = new();
    private readonly byte[] _identityKey;
    private readonly ISyncBiasPriorStore _store;
    private readonly SyncBiasEstimator _estimator;
    private readonly SyncBiasPriorServiceOptions _options;
    private readonly ISyncTelemetryRecorder _telemetry;
    private SyncBiasPriorDocument? _document;

    public SyncBiasPriorService(
        byte[] identityKey,
        ISyncBiasPriorStore store,
        SyncBiasEstimator? estimator = null,
        SyncBiasPriorServiceOptions? options = null,
        ISyncTelemetryRecorder? telemetry = null)
    {
        if (identityKey is not { Length: >= 32 })
        {
            throw new ArgumentException("A 32-byte identity key is required.", nameof(identityKey));
        }
        _identityKey = identityKey.ToArray();
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _estimator = estimator ?? new SyncBiasEstimator();
        _options = options ?? new SyncBiasPriorServiceOptions();
        _telemetry = telemetry ?? SyncTelemetryRecorder.Disabled;
        if (_options.Retention <= TimeSpan.Zero ||
            _options.MaximumPairObservations < 1 ||
            _options.MaximumManualEvents < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    public bool IsEnabled => true;

    public static SyncBiasPriorService CreateDefault(
        string dataFolder,
        ISyncTelemetryRecorder? telemetry = null)
    {
        var protector = new WindowsDpapiSyncBiasProtector();
        var key = new SyncBiasIdentityKeyStore(
            Path.Combine(dataFolder, "sync-bias-identity.key"),
            protector).LoadOrCreate();
        return new SyncBiasPriorService(
            key,
            new EncryptedSyncBiasPriorStore(
                Path.Combine(dataFolder, "sync-bias-priors.dat"),
                protector),
            telemetry: telemetry);
    }

    public SyncBiasContext? CreateContext(
        string? channelIdentity,
        string? qualityBucket,
        string? cdnBucket)
    {
        if (string.IsNullOrWhiteSpace(channelIdentity))
        {
            return null;
        }

        var stableChannelIdentity = NormalizeChannelIdentity(channelIdentity);
        return new SyncBiasContext(
            Hash("channel", stableChannelIdentity),
            NormalizeBucket(qualityBucket),
            string.IsNullOrWhiteSpace(cdnBucket) ||
            string.Equals(cdnBucket, "unknown", StringComparison.OrdinalIgnoreCase)
                ? "unknown"
                : Hash("cdn", cdnBucket.Trim()));
    }

    public IReadOnlyDictionary<int, SyncBiasSuggestion> GetCompatibleGroupSuggestions(
        IReadOnlyList<SyncBiasGroupMember> members,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(members);
        lock (_gate)
        {
            var document = LoadDocument();
            var candidates = members
                .GroupBy(member => member.SlotId)
                .Select(group => group.Last())
                .Select(member => new
                {
                    Member = member,
                    Suggestion = _estimator.Estimate(
                        member.Context,
                        document.PairObservations,
                        nowUtc)
                })
                .Where(item => item.Suggestion is not null &&
                               !WasRejectedForCurrentSession(
                                   document,
                                   item.Member,
                                   item.Suggestion!))
                .ToArray();
            var compatibleComponents = candidates
                .GroupBy(item => item.Suggestion!.ComponentId, StringComparer.Ordinal)
                .Where(group => group.Count() >= 2)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            return candidates
                .Where(item => compatibleComponents.Contains(item.Suggestion!.ComponentId))
                .ToDictionary(item => item.Member.SlotId, item => item.Suggestion!);
        }
    }

    public bool RecordAlignmentConfirmation(
        IReadOnlyList<SyncBiasGroupMember> members,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(members);
        var distinct = members
            .Where(member => member.SlotId > 0 &&
                             !string.IsNullOrWhiteSpace(member.Context.StableChannelHash) &&
                             !string.IsNullOrWhiteSpace(member.BroadcastSessionIdentity))
            .GroupBy(member => member.SlotId)
            .Select(group => group.Last())
            .ToArray();
        if (distinct.Length < 2 ||
            distinct.Select(member => member.Context.StableChannelHash)
                .Distinct(StringComparer.Ordinal)
                .Count() < 2)
        {
            return false;
        }

        var sessionHash = CreateSessionHash(distinct);
        lock (_gate)
        {
            var document = LoadDocument();
            if (document.PairObservations.Any(observation =>
                    observation.IndependentSessionHash.Equals(sessionHash, StringComparison.Ordinal)))
            {
                return false;
            }

            var pairObservations = document.PairObservations.ToList();
            for (var leftIndex = 0; leftIndex < distinct.Length; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < distinct.Length; rightIndex++)
                {
                    var left = distinct[leftIndex];
                    var right = distinct[rightIndex];
                    pairObservations.Add(new SyncBiasPairObservation
                    {
                        ObservationId = Hash(
                            "pair-observation",
                            $"{sessionHash}:{left.SlotId}:{right.SlotId}"),
                        IndependentSessionHash = sessionHash,
                        Left = left.Context,
                        Right = right.Context,
                        DelayDifferenceMilliseconds = left.FinalDelayMilliseconds -
                                                      right.FinalDelayMilliseconds,
                        OccurredAtUtc = occurredAtUtc.ToUniversalTime(),
                        IsIndependentSession = true,
                        IsStableFinal = true,
                        EventKind = SyncBiasManualEventKind.AlignmentConfirmed
                    });
                }
            }

            var events = document.ManualEvents.ToList();
            foreach (var member in distinct)
            {
                var manualEvent = CreateManualEvent(
                    member,
                    suggestion: null,
                    SyncBiasManualEventKind.AlignmentConfirmed,
                    new SyncManualDelayComponents(
                        member.AlgorithmPriorMilliseconds,
                        member.UserResidualMilliseconds,
                        member.FinalDelayMilliseconds),
                    sessionHash,
                    occurredAtUtc);
                events.Add(manualEvent);
                RecordTelemetry(member, manualEvent, previousResidual: null, isStableFinal: true, isIndependent: true);
            }

            SaveDocument(pairObservations, events, occurredAtUtc);
            return true;
        }
    }

    public void RecordSuggestionEvent(
        SyncBiasGroupMember member,
        SyncBiasSuggestion suggestion,
        SyncBiasManualEventKind eventKind,
        SyncManualDelayComponents components,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        if (eventKind is not (SyncBiasManualEventKind.SuggestionShown or
            SyncBiasManualEventKind.SuggestionAccepted or
            SyncBiasManualEventKind.SuggestionRejected or
            SyncBiasManualEventKind.SuggestionReverted))
        {
            throw new ArgumentOutOfRangeException(nameof(eventKind));
        }

        var sessionHash = CreateSessionHash([member]);
        lock (_gate)
        {
            var document = LoadDocument();
            if (document.ManualEvents.Any(existing =>
                    existing.SuggestionId.Equals(suggestion.SuggestionId, StringComparison.Ordinal) &&
                    existing.IndependentSessionHash.Equals(sessionHash, StringComparison.Ordinal) &&
                    existing.EventKind == eventKind))
            {
                return;
            }

            var manualEvent = CreateManualEvent(
                member,
                suggestion,
                eventKind,
                components,
                sessionHash,
                occurredAtUtc);
            var events = document.ManualEvents.Append(manualEvent).ToArray();
            SaveDocument(document.PairObservations, events, occurredAtUtc);
            RecordTelemetry(member, manualEvent, previousResidual: null, isStableFinal: false, isIndependent: false);
        }
    }

    public void RecordUserAdjustment(
        SyncBiasGroupMember member,
        SyncManualDelayComponents previous,
        SyncManualDelayComponents current,
        DateTimeOffset occurredAtUtc)
    {
        var sessionHash = CreateSessionHash([member]);
        lock (_gate)
        {
            var document = LoadDocument();
            var manualEvent = CreateManualEvent(
                member,
                suggestion: null,
                SyncBiasManualEventKind.UserAdjusted,
                current,
                sessionHash,
                occurredAtUtc);
            SaveDocument(
                document.PairObservations,
                document.ManualEvents.Append(manualEvent).ToArray(),
                occurredAtUtc);
            RecordTelemetry(member, manualEvent, previous.UserResidualMilliseconds, false, false);
        }
    }

    public void DeleteAll()
    {
        lock (_gate)
        {
            _store.DeleteAll();
            _document = new SyncBiasPriorDocument();
        }
    }

    public void ExportPrivacySafe(string destinationPath)
    {
        lock (_gate)
        {
            _store.ExportPrivacySafe(destinationPath);
        }
    }

    private SyncBiasPriorDocument LoadDocument() => _document ??= _store.Load();

    private void SaveDocument(
        IEnumerable<SyncBiasPairObservation> pairObservations,
        IEnumerable<SyncBiasManualEvent> events,
        DateTimeOffset occurredAtUtc)
    {
        var cutoff = occurredAtUtc.ToUniversalTime() - _options.Retention;
        _document = new SyncBiasPriorDocument
        {
            UpdatedAtUtc = occurredAtUtc.ToUniversalTime(),
            PairObservations = pairObservations
                .Where(observation => observation.OccurredAtUtc.ToUniversalTime() >= cutoff)
                .OrderByDescending(observation => observation.OccurredAtUtc)
                .Take(_options.MaximumPairObservations)
                .OrderBy(observation => observation.OccurredAtUtc)
                .ToArray(),
            ManualEvents = events
                .Where(manualEvent => manualEvent.OccurredAtUtc.ToUniversalTime() >= cutoff)
                .OrderByDescending(manualEvent => manualEvent.OccurredAtUtc)
                .Take(_options.MaximumManualEvents)
                .OrderBy(manualEvent => manualEvent.OccurredAtUtc)
                .ToArray()
        };
        _store.Save(_document);
    }

    private SyncBiasManualEvent CreateManualEvent(
        SyncBiasGroupMember member,
        SyncBiasSuggestion? suggestion,
        SyncBiasManualEventKind eventKind,
        SyncManualDelayComponents components,
        string sessionHash,
        DateTimeOffset occurredAtUtc) => new()
    {
        EventId = Hash("manual-event", $"{Guid.NewGuid():N}:{eventKind}"),
        SuggestionId = suggestion?.SuggestionId ?? "",
        IndependentSessionHash = sessionHash,
        Context = member.Context,
        EventKind = eventKind,
        OccurredAtUtc = occurredAtUtc.ToUniversalTime(),
        AlgorithmPriorMilliseconds = components.AlgorithmPriorMilliseconds,
        UserResidualMilliseconds = components.UserResidualMilliseconds,
        FinalDelayMilliseconds = components.FinalDelayMilliseconds
    };

    private bool WasRejectedForCurrentSession(
        SyncBiasPriorDocument document,
        SyncBiasGroupMember member,
        SyncBiasSuggestion suggestion)
    {
        var sessionHash = CreateSessionHash([member]);
        return document.ManualEvents.Any(manualEvent =>
            manualEvent.SuggestionId.Equals(suggestion.SuggestionId, StringComparison.Ordinal) &&
            manualEvent.IndependentSessionHash.Equals(sessionHash, StringComparison.Ordinal) &&
            manualEvent.EventKind == SyncBiasManualEventKind.SuggestionRejected);
    }

    private string CreateSessionHash(IEnumerable<SyncBiasGroupMember> members)
    {
        var identities = members
            .Select(member => member.BroadcastSessionIdentity?.Trim())
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return Hash("independent-session", string.Join("|", identities));
    }

    private void RecordTelemetry(
        SyncBiasGroupMember member,
        SyncBiasManualEvent manualEvent,
        int? previousResidual,
        bool isStableFinal,
        bool isIndependent)
    {
        if (!_telemetry.IsEnabled)
        {
            return;
        }

        _telemetry.RecordManualEvent(new SyncManualEventTelemetry(
            _telemetry.SessionId,
            member.SlotId,
            manualEvent.EventId,
            member.Context.StableChannelHash,
            manualEvent.IndependentSessionHash,
            EventType(manualEvent.EventKind),
            new SyncTelemetryClockSample(manualEvent.OccurredAtUtc, Stopwatch.GetTimestamp()),
            manualEvent.AlgorithmPriorMilliseconds,
            previousResidual,
            manualEvent.UserResidualMilliseconds,
            manualEvent.FinalDelayMilliseconds,
            manualEvent.SuggestionId,
            isStableFinal,
            isIndependent,
            ContextBucket(member.Context)));
    }

    private static string EventType(SyncBiasManualEventKind eventKind) => eventKind switch
    {
        SyncBiasManualEventKind.AlignmentConfirmed => "alignment-confirmed",
        SyncBiasManualEventKind.SuggestionShown => "suggestion-shown",
        SyncBiasManualEventKind.SuggestionAccepted => "suggestion-accepted",
        SyncBiasManualEventKind.SuggestionRejected => "suggestion-rejected",
        SyncBiasManualEventKind.SuggestionReverted => "suggestion-reverted",
        _ => "user-adjusted"
    };

    private static string ContextBucket(SyncBiasContext context) =>
        context.CdnBucket != "unknown"
            ? "channel-quality-cdn"
            : context.QualityBucket != "unknown"
                ? "channel-quality"
                : "channel";

    private string Hash(string domain, string value)
    {
        using var hmac = new HMACSHA256(_identityKey);
        return Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"{domain}\0{value}"))).ToLowerInvariant()[..32];
    }

    private static string NormalizeBucket(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = new string(value.Trim().ToLowerInvariant()
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Take(24)
            .ToArray());
        return normalized.Length == 0 ? "unknown" : normalized;
    }

    private static string NormalizeChannelIdentity(string value)
    {
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return trimmed;
        }

        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}{port}" +
               uri.AbsolutePath.TrimEnd('/');
    }
}

public sealed class DisabledSyncBiasPriorService : ISyncBiasPriorService
{
    public static DisabledSyncBiasPriorService Instance { get; } = new();

    private DisabledSyncBiasPriorService()
    {
    }

    public bool IsEnabled => false;

    public SyncBiasContext? CreateContext(string? channelIdentity, string? qualityBucket, string? cdnBucket) => null;

    public IReadOnlyDictionary<int, SyncBiasSuggestion> GetCompatibleGroupSuggestions(
        IReadOnlyList<SyncBiasGroupMember> members,
        DateTimeOffset nowUtc) => new Dictionary<int, SyncBiasSuggestion>();

    public bool RecordAlignmentConfirmation(
        IReadOnlyList<SyncBiasGroupMember> members,
        DateTimeOffset occurredAtUtc) => false;

    public void RecordSuggestionEvent(
        SyncBiasGroupMember member,
        SyncBiasSuggestion suggestion,
        SyncBiasManualEventKind eventKind,
        SyncManualDelayComponents components,
        DateTimeOffset occurredAtUtc)
    {
    }

    public void RecordUserAdjustment(
        SyncBiasGroupMember member,
        SyncManualDelayComponents previous,
        SyncManualDelayComponents current,
        DateTimeOffset occurredAtUtc)
    {
    }

    public void DeleteAll()
    {
    }

    public void ExportPrivacySafe(string destinationPath)
    {
    }
}
