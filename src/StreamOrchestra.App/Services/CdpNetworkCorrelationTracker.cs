using System.Diagnostics;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed record CdpRuntimeCompatibility(
    bool IsCompatible,
    string ProtocolVersion,
    string Product,
    string RuntimeBucket,
    string Reason);

public sealed record CdpNetworkCorrelationResult
{
    public string Status { get; init; } = "unavailable";

    public string RequestId { get; init; } = "";

    public string FrameId { get; init; } = "";

    public int NavigationGeneration { get; init; }

    public SyncTelemetryClockSample? RequestStartedAt { get; init; }

    public SyncTelemetryClockSample? HeadersReceivedAt { get; init; }

    public SyncTelemetryClockSample? BodyCompletedAt { get; init; }

    public int? StatusCode { get; init; }

    public string MimeType { get; init; } = "";

    public string CacheBucket { get; init; } = "unknown";

    public long? EncodedBodyLengthBucket { get; init; }

    public bool SchemaCompatible { get; init; }

    public bool LifecycleClockSkewAdjusted { get; init; }
}

public static class CdpRuntimeSchemaValidator
{
    public static CdpRuntimeCompatibility ValidateBrowserVersion(
        string responseJson,
        string runtimeBucket)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            var protocol = ReadString(root, "protocolVersion");
            var product = ReadString(root, "product");
            if (string.IsNullOrWhiteSpace(protocol) || string.IsNullOrWhiteSpace(product))
            {
                return new CdpRuntimeCompatibility(
                    false,
                    protocol,
                    product,
                    runtimeBucket,
                    "browser-version-schema-mismatch");
            }

            var compatibleProtocol = Version.TryParse(protocol, out var version) &&
                                     version >= new Version(1, 3);
            var compatibleProduct = product.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                                    product.Contains("Edg", StringComparison.OrdinalIgnoreCase);
            return new CdpRuntimeCompatibility(
                compatibleProtocol && compatibleProduct,
                protocol,
                product,
                runtimeBucket,
                compatibleProtocol && compatibleProduct ? "compatible" : "unsupported-runtime");
        }
        catch (JsonException)
        {
            return new CdpRuntimeCompatibility(
                false,
                "",
                "",
                runtimeBucket,
                "browser-version-invalid-json");
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

public sealed class CdpNetworkCorrelationTracker
{
    private const int MaximumTrackedRequests = 4096;
    private static readonly TimeSpan RequestRetention = TimeSpan.FromMinutes(2);
    public const double MaximumLifecycleClockSkewMilliseconds = 5;
    private readonly object _gate = new();
    private readonly Dictionary<string, RequestState> _requests = new(StringComparer.Ordinal);
    private CdpRuntimeCompatibility _runtime;
    private ClockAnchor? _clockAnchor;
    private bool _eventSchemaCompatible = true;

    public CdpNetworkCorrelationTracker(CdpRuntimeCompatibility runtime)
    {
        _runtime = runtime;
    }

    public CdpRuntimeCompatibility Runtime => _runtime;

    public bool IsSchemaCompatible => _runtime.IsCompatible && _eventSchemaCompatible;

    public int TrackedRequestCount
    {
        get
        {
            lock (_gate)
            {
                return _requests.Count;
            }
        }
    }

    public void SetRuntimeCompatibility(CdpRuntimeCompatibility runtime)
    {
        lock (_gate)
        {
            _runtime = runtime;
            _eventSchemaCompatible = true;
            _requests.Clear();
            _clockAnchor = null;
        }
    }

    public bool ObserveRequestWillBeSent(
        string parameterJson,
        long hostReceivedMonotonicTicks,
        DateTimeOffset hostReceivedAtUtc,
        int navigationGeneration)
    {
        lock (_gate)
        {
            if (!_runtime.IsCompatible || !TryParse(parameterJson, out var root) ||
                !TryString(root, "requestId", out var requestId) ||
                !TryDouble(root, "timestamp", out var timestamp) ||
                !root.TryGetProperty("request", out var request) ||
                request.ValueKind != JsonValueKind.Object ||
                !TryString(request, "url", out var rawUrl))
            {
                _eventSchemaCompatible = false;
                return false;
            }

            ObserveClock(timestamp, hostReceivedMonotonicTicks, hostReceivedAtUtc);
            var wallTime = TryDouble(root, "wallTime", out var wallTimeValue)
                ? wallTimeValue
                : (double?)null;
            var frameId = TryString(root, "frameId", out var parsedFrameId) ? parsedFrameId : "";
            var loaderId = TryString(root, "loaderId", out var parsedLoaderId) ? parsedLoaderId : "";
            var requestStamp = wallTime is { } unixSeconds &&
                               TryUnixUtc(unixSeconds, out var wallUtc)
                ? new SyncTelemetryClockSample(wallUtc, ToHostTicks(timestamp))
                : ToStamp(timestamp);
            _requests[requestId] = new RequestState
            {
                RequestId = requestId,
                RawUrl = rawUrl,
                FrameId = frameId,
                LoaderId = loaderId,
                NavigationGeneration = Math.Max(0, navigationGeneration),
                RequestStartedAt = requestStamp,
                LastHostObservedTicks = Math.Max(0, hostReceivedMonotonicTicks)
            };
            Prune(hostReceivedMonotonicTicks);
            return true;
        }
    }

    public bool ObserveResponseReceived(
        string parameterJson,
        long hostReceivedMonotonicTicks,
        DateTimeOffset hostReceivedAtUtc)
    {
        lock (_gate)
        {
            if (!_runtime.IsCompatible || !TryParse(parameterJson, out var root) ||
                !TryString(root, "requestId", out var requestId) ||
                !TryDouble(root, "timestamp", out var timestamp) ||
                !root.TryGetProperty("response", out var response) ||
                response.ValueKind != JsonValueKind.Object ||
                !TryString(response, "url", out var responseUrl) ||
                !TryDouble(response, "status", out var status))
            {
                _eventSchemaCompatible = false;
                return false;
            }

            ObserveClock(timestamp, hostReceivedMonotonicTicks, hostReceivedAtUtc);
            if (!_requests.TryGetValue(requestId, out var state) ||
                !state.RawUrl.Equals(responseUrl, StringComparison.Ordinal))
            {
                return false;
            }

            var mimeType = TryString(response, "mimeType", out var parsedMime) ? parsedMime : "";
            var fromDiskCache = TryBoolean(response, "fromDiskCache", out var disk) && disk;
            var fromServiceWorker = TryBoolean(response, "fromServiceWorker", out var worker) && worker;
            var headersStamp = NormalizeSmallLifecycleSkew(
                ToStamp(timestamp),
                state.RequestStartedAt,
                out var adjusted);
            _requests[requestId] = state with
            {
                HeadersReceivedAt = headersStamp,
                StatusCode = (int)Math.Clamp(Math.Round(status), 0, 999),
                MimeType = mimeType,
                CacheBucket = fromDiskCache || fromServiceWorker ? "hit" : "unknown",
                LastHostObservedTicks = Math.Max(0, hostReceivedMonotonicTicks),
                LifecycleClockSkewAdjusted = state.LifecycleClockSkewAdjusted || adjusted
            };
            Prune(hostReceivedMonotonicTicks);
            return true;
        }
    }

    public bool ObserveLoadingFinished(
        string parameterJson,
        long hostReceivedMonotonicTicks,
        DateTimeOffset hostReceivedAtUtc)
    {
        lock (_gate)
        {
            if (!_runtime.IsCompatible || !TryParse(parameterJson, out var root) ||
                !TryString(root, "requestId", out var requestId) ||
                !TryDouble(root, "timestamp", out var timestamp))
            {
                _eventSchemaCompatible = false;
                return false;
            }

            ObserveClock(timestamp, hostReceivedMonotonicTicks, hostReceivedAtUtc);
            if (!_requests.TryGetValue(requestId, out var state))
            {
                return false;
            }

            long? encodedLength = TryDouble(root, "encodedDataLength", out var length) && length >= 0
                ? BucketLength(length)
                : null;
            var bodyStamp = NormalizeSmallLifecycleSkew(
                ToStamp(timestamp),
                state.HeadersReceivedAt,
                out var adjusted);
            _requests[requestId] = state with
            {
                BodyCompletedAt = bodyStamp,
                EncodedBodyLengthBucket = encodedLength,
                LastHostObservedTicks = Math.Max(0, hostReceivedMonotonicTicks),
                LifecycleClockSkewAdjusted = state.LifecycleClockSkewAdjusted || adjusted
            };
            Prune(hostReceivedMonotonicTicks);
            return true;
        }
    }

    public bool ObserveLoadingFailed(
        string parameterJson,
        long hostReceivedMonotonicTicks,
        DateTimeOffset hostReceivedAtUtc)
    {
        lock (_gate)
        {
            if (!_runtime.IsCompatible || !TryParse(parameterJson, out var root) ||
                !TryString(root, "requestId", out var requestId) ||
                !TryDouble(root, "timestamp", out var timestamp))
            {
                _eventSchemaCompatible = false;
                return false;
            }

            ObserveClock(timestamp, hostReceivedMonotonicTicks, hostReceivedAtUtc);
            if (!_requests.TryGetValue(requestId, out var state))
            {
                return false;
            }

            _requests[requestId] = state with
            {
                Failed = true,
                BodyCompletedAt = ToStamp(timestamp),
                LastHostObservedTicks = Math.Max(0, hostReceivedMonotonicTicks)
            };
            return true;
        }
    }

    public CdpNetworkCorrelationResult MatchResponse(
        string rawRequestUri,
        int statusCode,
        string? contentType,
        int navigationGeneration,
        long hostObservedMonotonicTicks)
    {
        lock (_gate)
        {
            Prune(hostObservedMonotonicTicks);
            if (!IsSchemaCompatible)
            {
                return new CdpNetworkCorrelationResult
                {
                    Status = "runtime-mismatch",
                    SchemaCompatible = false
                };
            }

            var candidates = _requests.Values
                .Where(state => state.NavigationGeneration == navigationGeneration &&
                                state.RawUrl.Equals(rawRequestUri, StringComparison.Ordinal) &&
                                state.HeadersReceivedAt is not null)
                .ToArray();
            if (candidates.Length == 0)
            {
                return new CdpNetworkCorrelationResult
                {
                    Status = "unavailable",
                    SchemaCompatible = true
                };
            }

            if (candidates.Length != 1)
            {
                return new CdpNetworkCorrelationResult
                {
                    Status = "ambiguous",
                    SchemaCompatible = true
                };
            }

            var state = candidates[0];
            if (state.StatusCode != statusCode ||
                (!string.IsNullOrWhiteSpace(contentType) &&
                 !string.IsNullOrWhiteSpace(state.MimeType) &&
                 !MediaTypesEqual(contentType, state.MimeType)) ||
                !AreLifecycleTimesOrdered(state))
            {
                return new CdpNetworkCorrelationResult
                {
                    Status = "invalid",
                    RequestId = state.RequestId,
                    FrameId = state.FrameId,
                    NavigationGeneration = state.NavigationGeneration,
                    SchemaCompatible = true
                };
            }

            _requests.Remove(state.RequestId);
            return new CdpNetworkCorrelationResult
            {
                Status = "correlated",
                RequestId = state.RequestId,
                FrameId = state.FrameId,
                NavigationGeneration = state.NavigationGeneration,
                RequestStartedAt = state.RequestStartedAt,
                HeadersReceivedAt = state.HeadersReceivedAt,
                BodyCompletedAt = state.BodyCompletedAt,
                StatusCode = state.StatusCode,
                MimeType = state.MimeType,
                CacheBucket = state.CacheBucket,
                EncodedBodyLengthBucket = state.EncodedBodyLengthBucket,
                SchemaCompatible = true,
                LifecycleClockSkewAdjusted = state.LifecycleClockSkewAdjusted
            };
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _requests.Clear();
            _clockAnchor = null;
            _eventSchemaCompatible = true;
        }
    }

    private void ObserveClock(double cdpTimestamp, long hostTicks, DateTimeOffset hostUtc)
    {
        if (_clockAnchor is null && double.IsFinite(cdpTimestamp) && hostTicks >= 0)
        {
            _clockAnchor = new ClockAnchor(cdpTimestamp, hostTicks, hostUtc.ToUniversalTime());
        }
    }

    private SyncTelemetryClockSample ToStamp(double cdpTimestamp)
    {
        if (_clockAnchor is not { } anchor)
        {
            return new SyncTelemetryClockSample(DateTimeOffset.UnixEpoch, 0);
        }

        var deltaSeconds = cdpTimestamp - anchor.CdpTimestamp;
        return new SyncTelemetryClockSample(
            SafeAddSeconds(anchor.HostUtc, deltaSeconds),
            ToHostTicks(cdpTimestamp));
    }

    private long ToHostTicks(double cdpTimestamp)
    {
        if (_clockAnchor is not { } anchor)
        {
            return 0;
        }

        var delta = (cdpTimestamp - anchor.CdpTimestamp) * Stopwatch.Frequency;
        if (!double.IsFinite(delta))
        {
            return 0;
        }

        var total = anchor.HostTicks + delta;
        return total switch
        {
            >= long.MaxValue => long.MaxValue,
            <= 0 => 0,
            _ => (long)Math.Round(total)
        };
    }

    private void Prune(long nowTicks)
    {
        var retentionTicks = RequestRetention.TotalSeconds * Stopwatch.Frequency;
        foreach (var requestId in _requests
                     .Where(item => nowTicks - item.Value.LastHostObservedTicks > retentionTicks)
                     .OrderBy(item => item.Value.LastHostObservedTicks)
                     .Select(item => item.Key)
                     .ToArray())
        {
            _requests.Remove(requestId);
        }

        foreach (var requestId in _requests.Values
                     .OrderBy(state => state.LastHostObservedTicks)
                     .Take(Math.Max(0, _requests.Count - MaximumTrackedRequests))
                     .Select(state => state.RequestId)
                     .ToArray())
        {
            _requests.Remove(requestId);
        }
    }

    private static bool AreLifecycleTimesOrdered(RequestState state) =>
        !state.Failed &&
        state.RequestStartedAt is not null &&
        state.HeadersReceivedAt is not null &&
        state.HeadersReceivedAt.MonotonicTicks >= state.RequestStartedAt.MonotonicTicks &&
        (state.BodyCompletedAt is null ||
         state.BodyCompletedAt.MonotonicTicks >= state.HeadersReceivedAt.MonotonicTicks);

    private static SyncTelemetryClockSample NormalizeSmallLifecycleSkew(
        SyncTelemetryClockSample current,
        SyncTelemetryClockSample? lowerBound,
        out bool adjusted)
    {
        adjusted = false;
        if (lowerBound is null || current.MonotonicTicks >= lowerBound.MonotonicTicks)
        {
            return current;
        }

        var skewTicks = lowerBound.MonotonicTicks - current.MonotonicTicks;
        var maximumSkewTicks = Math.Max(
            1L,
            (long)Math.Ceiling(
                Stopwatch.Frequency * MaximumLifecycleClockSkewMilliseconds / 1000));
        if (skewTicks > maximumSkewTicks)
        {
            return current;
        }

        adjusted = true;
        return new SyncTelemetryClockSample(
            current.Utc < lowerBound.Utc ? lowerBound.Utc : current.Utc,
            lowerBound.MonotonicTicks);
    }

    private static long BucketLength(double length)
    {
        if (length <= 0)
        {
            return 0;
        }

        var power = Math.Ceiling(Math.Log2(length));
        return power >= 62 ? long.MaxValue : 1L << (int)power;
    }

    private static bool MediaTypesEqual(string left, string right) =>
        left.Split(';', 2)[0].Trim().Equals(
            right.Split(';', 2)[0].Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryUnixUtc(double unixSeconds, out DateTimeOffset value)
    {
        var milliseconds = unixSeconds * 1000;
        if (!double.IsFinite(milliseconds) ||
            milliseconds < DateTimeOffset.MinValue.ToUnixTimeMilliseconds() ||
            milliseconds > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            value = default;
            return false;
        }

        value = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(milliseconds));
        return true;
    }

    private static DateTimeOffset SafeAddSeconds(DateTimeOffset value, double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            return DateTimeOffset.UnixEpoch;
        }

        try
        {
            return value.AddSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }

    private static bool TryParse(string json, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            root = default;
            return false;
        }
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        if (root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(value);
        }

        value = "";
        return false;
    }

    private static bool TryDouble(JsonElement root, string name, out double value)
    {
        if (root.TryGetProperty(name, out var element) && element.TryGetDouble(out value) &&
            double.IsFinite(value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryBoolean(JsonElement root, string name, out bool value)
    {
        if (root.TryGetProperty(name, out var element) &&
            element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private sealed record RequestState
    {
        public string RequestId { get; init; } = "";
        public string RawUrl { get; init; } = "";
        public string FrameId { get; init; } = "";
        public string LoaderId { get; init; } = "";
        public int NavigationGeneration { get; init; }
        public SyncTelemetryClockSample? RequestStartedAt { get; init; }
        public SyncTelemetryClockSample? HeadersReceivedAt { get; init; }
        public SyncTelemetryClockSample? BodyCompletedAt { get; init; }
        public int? StatusCode { get; init; }
        public string MimeType { get; init; } = "";
        public string CacheBucket { get; init; } = "unknown";
        public long? EncodedBodyLengthBucket { get; init; }
        public bool Failed { get; init; }
        public bool LifecycleClockSkewAdjusted { get; init; }
        public long LastHostObservedTicks { get; init; }
    }

    private sealed record ClockAnchor(
        double CdpTimestamp,
        long HostTicks,
        DateTimeOffset HostUtc);
}
