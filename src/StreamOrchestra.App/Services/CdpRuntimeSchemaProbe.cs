using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StreamOrchestra.App.Services;

public sealed record CdpRuntimeSchemaProbeReport
{
    public string ProbeVersion { get; init; } = "stream-sync-cdp-runtime-probe-v1";

    public DateTimeOffset CapturedAtUtc { get; init; }

    public string Status { get; init; } = "failed";

    public string FailureCode { get; init; } = "none";

    public string RuntimeVersion { get; init; } = "unavailable";

    public string RuntimeBucket { get; init; } = "unknown";

    public string ProtocolVersion { get; init; } = "unknown";

    public string Product { get; init; } = "unknown";

    public bool BrowserVersionSchemaCompatible { get; init; }

    public bool WebViewInitialized { get; init; }

    public bool NetworkEnableSucceeded { get; init; }

    public bool ProbeNavigationStarted { get; init; }

    public int RequestWillBeSentEventCount { get; init; }

    public int ResponseReceivedEventCount { get; init; }

    public int LoadingFinishedEventCount { get; init; }

    public int LoadingFailedEventCount { get; init; }

    public bool RequestWillBeSentSchemaCompatible { get; init; }

    public bool ResponseReceivedSchemaCompatible { get; init; }

    public bool LoadingFinishedSchemaCompatible { get; init; }

    public bool LoadingFailedSchemaCompatible { get; init; }

    public bool RequestFrameAssociationObserved { get; init; }

    public bool NavigationGenerationAssociationObserved { get; init; }

    public bool LifecycleTimestampsOrdered { get; init; }

    public bool LifecycleClockSkewAdjusted { get; init; }

    public double LifecycleClockSkewToleranceMilliseconds { get; init; } =
        CdpNetworkCorrelationTracker.MaximumLifecycleClockSkewMilliseconds;

    public int? ObservedStatusCode { get; init; }

    public string ObservedMimeType { get; init; } = "unknown";

    public double? RequestToHeadersMilliseconds { get; init; }

    public double? HeadersToBodyMilliseconds { get; init; }

    public string CorrelationStatus { get; init; } = "unavailable";

    public bool NetworkDisableSucceeded { get; init; }

    public string ProbeScope { get; init; } = "ephemeral-loopback-only";

    public bool RawUrlPersisted { get; init; }

    public bool IsCompatible =>
        Status == "compatible" &&
        FailureCode == "none" &&
        BrowserVersionSchemaCompatible &&
        WebViewInitialized &&
        NetworkEnableSucceeded &&
        ProbeNavigationStarted &&
        RequestWillBeSentSchemaCompatible &&
        ResponseReceivedSchemaCompatible &&
        LoadingFinishedSchemaCompatible &&
        LoadingFailedSchemaCompatible &&
        RequestFrameAssociationObserved &&
        NavigationGenerationAssociationObserved &&
        LifecycleTimestampsOrdered &&
        CorrelationStatus == "correlated" &&
        NetworkDisableSucceeded &&
        !RawUrlPersisted;
}

public static class CdpRuntimeSchemaProbe
{
    private const int NavigationGeneration = 1;

    public static async Task<CdpRuntimeSchemaProbeReport> RunAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(20);
        if (effectiveTimeout < TimeSpan.FromSeconds(2) || effectiveTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var completion = new TaskCompletionSource<CdpRuntimeSchemaProbeReport>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profileRoot = Path.Combine(
            Path.GetTempPath(),
            "StreamOrchestra.RuntimeProbe",
            Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    completion.TrySetResult(await RunOnStaAsync(
                        profileRoot,
                        effectiveTimeout,
                        cancellationToken));
                }
                catch (Exception ex)
                {
                    completion.TrySetResult(FailedReport(ex));
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "StreamOrchestra.CdpRuntimeSchemaProbe"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        CdpRuntimeSchemaProbeReport report;
        try
        {
            report = await completion.Task.WaitAsync(
                effectiveTimeout + TimeSpan.FromSeconds(15),
                cancellationToken);
        }
        finally
        {
            var stopped = await Task.Run(
                () => thread.Join(TimeSpan.FromSeconds(5)),
                CancellationToken.None);
            if (stopped)
            {
                TryDeleteProbeProfile(profileRoot, retryCount: 20);
            }
        }

        return report;
    }

    private static async Task<CdpRuntimeSchemaProbeReport> RunOnStaAsync(
        string profileRoot,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await using var server = new LoopbackProbeServer();
        Directory.CreateDirectory(profileRoot);

        WebView2? browser = null;
        Window? window = null;
        CoreWebView2DevToolsProtocolEventReceiver? requestReceiver = null;
        CoreWebView2DevToolsProtocolEventReceiver? responseReceiver = null;
        CoreWebView2DevToolsProtocolEventReceiver? finishedReceiver = null;
        CoreWebView2DevToolsProtocolEventReceiver? failedReceiver = null;
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>? requestHandler = null;
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>? responseHandler = null;
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>? finishedHandler = null;
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>? failedHandler = null;
        var networkEnabled = false;
        var networkDisabled = false;

        try
        {
            browser = new WebView2 { Width = 2, Height = 2 };
            window = new Window
            {
                Width = 2,
                Height = 2,
                Left = -10_000,
                Top = -10_000,
                Opacity = 0,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Content = browser
            };
            window.Show();

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: profileRoot,
                options: new CoreWebView2EnvironmentOptions());
            await browser.EnsureCoreWebView2Async(environment);
            var core = browser.CoreWebView2;
            var runtimeVersion = environment.BrowserVersionString ?? "unavailable";
            var runtimeBucket = RuntimeBucket(runtimeVersion);
            var browserVersionJson = await core.CallDevToolsProtocolMethodAsync(
                "Browser.getVersion",
                "{}");
            var compatibility = CdpRuntimeSchemaValidator.ValidateBrowserVersion(
                browserVersionJson,
                runtimeBucket);
            var tracker = new CdpNetworkCorrelationTracker(compatibility);
            var lifecycleReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var manifestUri = new Uri(server.BaseUri, "probe/live.m3u8").AbsoluteUri;
            var failedUri = new Uri(server.BaseUri, "probe/fail").AbsoluteUri;
            string? manifestRequestId = null;
            string? failedRequestId = null;
            var manifestRequestCompatible = false;
            var manifestResponseCompatible = false;
            var manifestFinishedCompatible = false;
            var failedRequestCompatible = false;
            var loadingFailedCompatible = false;
            int? observedStatusCode = null;
            var observedMimeType = "";
            double? requestTimestamp = null;
            double? responseTimestamp = null;
            double? finishedTimestamp = null;
            var requestEventCount = 0;
            var responseEventCount = 0;
            var finishedEventCount = 0;
            var failedEventCount = 0;

            void CompleteWhenReady()
            {
                if (manifestRequestCompatible &&
                    manifestResponseCompatible &&
                    manifestFinishedCompatible &&
                    failedRequestCompatible &&
                    loadingFailedCompatible)
                {
                    lifecycleReady.TrySetResult();
                }
            }

            requestReceiver = core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
            responseReceiver = core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
            finishedReceiver = core.GetDevToolsProtocolEventReceiver("Network.loadingFinished");
            failedReceiver = core.GetDevToolsProtocolEventReceiver("Network.loadingFailed");

            requestHandler = (_, args) =>
            {
                requestEventCount++;
                var accepted = tracker.ObserveRequestWillBeSent(
                    args.ParameterObjectAsJson,
                    Stopwatch.GetTimestamp(),
                    DateTimeOffset.UtcNow,
                    NavigationGeneration);
                if (!TryReadRequest(args.ParameterObjectAsJson, out var requestId, out var requestUri))
                {
                    return;
                }

                if (requestUri.Equals(manifestUri, StringComparison.Ordinal))
                {
                    manifestRequestId = requestId;
                    manifestRequestCompatible = accepted;
                    requestTimestamp = TryReadTimestamp(args.ParameterObjectAsJson);
                }
                else if (requestUri.Equals(failedUri, StringComparison.Ordinal))
                {
                    failedRequestId = requestId;
                    failedRequestCompatible = accepted;
                }

                CompleteWhenReady();
            };
            responseHandler = (_, args) =>
            {
                responseEventCount++;
                var accepted = tracker.ObserveResponseReceived(
                    args.ParameterObjectAsJson,
                    Stopwatch.GetTimestamp(),
                    DateTimeOffset.UtcNow);
                if (TryReadRequestId(args.ParameterObjectAsJson, out var requestId) &&
                    requestId.Equals(manifestRequestId, StringComparison.Ordinal))
                {
                    manifestResponseCompatible = accepted;
                    responseTimestamp = TryReadTimestamp(args.ParameterObjectAsJson);
                    TryReadResponseMetadata(
                        args.ParameterObjectAsJson,
                        out observedStatusCode,
                        out observedMimeType);
                }

                CompleteWhenReady();
            };
            finishedHandler = (_, args) =>
            {
                finishedEventCount++;
                var accepted = tracker.ObserveLoadingFinished(
                    args.ParameterObjectAsJson,
                    Stopwatch.GetTimestamp(),
                    DateTimeOffset.UtcNow);
                if (TryReadRequestId(args.ParameterObjectAsJson, out var requestId) &&
                    requestId.Equals(manifestRequestId, StringComparison.Ordinal))
                {
                    manifestFinishedCompatible = accepted;
                    finishedTimestamp = TryReadTimestamp(args.ParameterObjectAsJson);
                }

                CompleteWhenReady();
            };
            failedHandler = (_, args) =>
            {
                failedEventCount++;
                var accepted = tracker.ObserveLoadingFailed(
                    args.ParameterObjectAsJson,
                    Stopwatch.GetTimestamp(),
                    DateTimeOffset.UtcNow);
                if (TryReadRequestId(args.ParameterObjectAsJson, out var requestId) &&
                    requestId.Equals(failedRequestId, StringComparison.Ordinal))
                {
                    loadingFailedCompatible = accepted;
                }

                CompleteWhenReady();
            };

            requestReceiver.DevToolsProtocolEventReceived += requestHandler;
            responseReceiver.DevToolsProtocolEventReceived += responseHandler;
            finishedReceiver.DevToolsProtocolEventReceived += finishedHandler;
            failedReceiver.DevToolsProtocolEventReceived += failedHandler;

            await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
            networkEnabled = true;
            core.Navigate(new Uri(server.BaseUri, "index.html").AbsoluteUri);
            var lifecycleCompleted = true;
            try
            {
                await lifecycleReady.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                lifecycleCompleted = false;
            }

            var correlation = tracker.MatchResponse(
                manifestUri,
                statusCode: observedStatusCode ?? 200,
                contentType: string.IsNullOrWhiteSpace(observedMimeType)
                    ? "application/vnd.apple.mpegurl"
                    : observedMimeType,
                NavigationGeneration,
                Stopwatch.GetTimestamp());
            await core.CallDevToolsProtocolMethodAsync("Network.disable", "{}");
            networkDisabled = true;
            networkEnabled = false;

            var ordered = correlation.RequestStartedAt is not null &&
                          correlation.HeadersReceivedAt is not null &&
                          correlation.BodyCompletedAt is not null &&
                          correlation.HeadersReceivedAt.MonotonicTicks >=
                          correlation.RequestStartedAt.MonotonicTicks &&
                          correlation.BodyCompletedAt.MonotonicTicks >=
                          correlation.HeadersReceivedAt.MonotonicTicks;
            var status = compatibility.IsCompatible &&
                         tracker.IsSchemaCompatible &&
                         manifestRequestCompatible &&
                         manifestResponseCompatible &&
                         manifestFinishedCompatible &&
                         failedRequestCompatible &&
                         loadingFailedCompatible &&
                         correlation.Status == "correlated" &&
                         !string.IsNullOrWhiteSpace(correlation.FrameId) &&
                         correlation.NavigationGeneration == NavigationGeneration &&
                         ordered &&
                         networkDisabled
                ? "compatible"
                : "incompatible";

            return new CdpRuntimeSchemaProbeReport
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                Status = status,
                FailureCode = status == "compatible"
                    ? "none"
                    : lifecycleCompleted
                        ? "schema-validation-failed"
                        : "lifecycle-timeout",
                RuntimeVersion = runtimeVersion,
                RuntimeBucket = runtimeBucket,
                ProtocolVersion = compatibility.ProtocolVersion,
                Product = compatibility.Product,
                BrowserVersionSchemaCompatible = compatibility.IsCompatible,
                WebViewInitialized = true,
                NetworkEnableSucceeded = true,
                ProbeNavigationStarted = true,
                RequestWillBeSentEventCount = requestEventCount,
                ResponseReceivedEventCount = responseEventCount,
                LoadingFinishedEventCount = finishedEventCount,
                LoadingFailedEventCount = failedEventCount,
                RequestWillBeSentSchemaCompatible = manifestRequestCompatible && failedRequestCompatible,
                ResponseReceivedSchemaCompatible = manifestResponseCompatible,
                LoadingFinishedSchemaCompatible = manifestFinishedCompatible,
                LoadingFailedSchemaCompatible = loadingFailedCompatible,
                RequestFrameAssociationObserved = !string.IsNullOrWhiteSpace(correlation.FrameId),
                NavigationGenerationAssociationObserved =
                    correlation.NavigationGeneration == NavigationGeneration,
                LifecycleTimestampsOrdered = ordered,
                LifecycleClockSkewAdjusted = correlation.LifecycleClockSkewAdjusted,
                ObservedStatusCode = observedStatusCode,
                ObservedMimeType = string.IsNullOrWhiteSpace(observedMimeType)
                    ? "unknown"
                    : observedMimeType,
                RequestToHeadersMilliseconds = requestTimestamp is { } requestTick &&
                                               responseTimestamp is { } responseTick
                    ? (responseTick - requestTick) * 1000
                    : null,
                HeadersToBodyMilliseconds = responseTimestamp is { } headersAt &&
                                             finishedTimestamp is { } bodyAt
                    ? (bodyAt - headersAt) * 1000
                    : null,
                CorrelationStatus = correlation.Status,
                NetworkDisableSucceeded = networkDisabled,
                RawUrlPersisted = false
            };
        }
        finally
        {
            if (requestReceiver is not null && requestHandler is not null)
            {
                requestReceiver.DevToolsProtocolEventReceived -= requestHandler;
            }

            if (responseReceiver is not null && responseHandler is not null)
            {
                responseReceiver.DevToolsProtocolEventReceived -= responseHandler;
            }

            if (finishedReceiver is not null && finishedHandler is not null)
            {
                finishedReceiver.DevToolsProtocolEventReceived -= finishedHandler;
            }

            if (failedReceiver is not null && failedHandler is not null)
            {
                failedReceiver.DevToolsProtocolEventReceived -= failedHandler;
            }

            if (networkEnabled && browser?.CoreWebView2 is { } core)
            {
                try
                {
                    await core.CallDevToolsProtocolMethodAsync("Network.disable", "{}");
                    networkDisabled = true;
                }
                catch (Exception)
                {
                    networkDisabled = false;
                }
            }

            browser?.Dispose();
            window?.Close();
        }
    }

    private static CdpRuntimeSchemaProbeReport FailedReport(Exception exception) => new()
    {
        CapturedAtUtc = DateTimeOffset.UtcNow,
        FailureCode = exception switch
        {
            TimeoutException => "timeout",
            OperationCanceledException => "cancelled",
            WebView2RuntimeNotFoundException => "runtime-unavailable",
            _ => $"probe-exception-{exception.GetType().Name.ToLowerInvariant()}"
        },
        RuntimeVersion = AvailableRuntimeVersion(),
        RuntimeBucket = RuntimeBucket(AvailableRuntimeVersion()),
        RawUrlPersisted = false
    };

    private static string AvailableRuntimeVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString() ?? "unavailable";
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return "unavailable";
        }
    }

    private static string RuntimeBucket(string runtimeVersion)
    {
        var major = runtimeVersion.Split('.', 2)[0];
        return int.TryParse(major, out var parsed) && parsed > 0
            ? $"webview2-{parsed}"
            : "webview2-unknown";
    }

    private static bool TryReadRequest(
        string json,
        out string requestId,
        out string requestUri)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            requestId = root.GetProperty("requestId").GetString() ?? "";
            requestUri = root.GetProperty("request").GetProperty("url").GetString() ?? "";
            return requestId.Length > 0 && requestUri.Length > 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            requestId = "";
            requestUri = "";
            return false;
        }
    }

    private static bool TryReadRequestId(string json, out string requestId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            requestId = document.RootElement.GetProperty("requestId").GetString() ?? "";
            return requestId.Length > 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            requestId = "";
            return false;
        }
    }

    private static double? TryReadTimestamp(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("timestamp", out var value) &&
                   value.TryGetDouble(out var parsed) &&
                   double.IsFinite(parsed)
                ? parsed
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadResponseMetadata(
        string json,
        out int? statusCode,
        out string mimeType)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var response = document.RootElement.GetProperty("response");
            statusCode = response.TryGetProperty("status", out var status) &&
                         status.TryGetDouble(out var parsedStatus) &&
                         double.IsFinite(parsedStatus)
                ? (int)Math.Clamp(Math.Round(parsedStatus), 0, 999)
                : null;
            mimeType = response.TryGetProperty("mimeType", out var mime) &&
                       mime.ValueKind == JsonValueKind.String
                ? mime.GetString() ?? ""
                : "";
            return statusCode is not null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            statusCode = null;
            mimeType = "";
            return false;
        }
    }

    private static void TryDeleteProbeProfile(string profileRoot, int retryCount)
    {
        var fullRoot = Path.GetFullPath(profileRoot);
        var expectedParent = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "StreamOrchestra.RuntimeProbe"));
        if (!fullRoot.StartsWith(
                expectedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        for (var attempt = 0; attempt < Math.Max(1, retryCount); attempt++)
        {
            try
            {
                if (Directory.Exists(fullRoot))
                {
                    Directory.Delete(fullRoot, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt + 1 < retryCount)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    private sealed class LoopbackProbeServer : IAsyncDisposable
    {
        private readonly object _clientGate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<Task> _clientTasks = [];
        private readonly Task _acceptLoop;

        public LoopbackProbeServer()
        {
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUri = new Uri($"http://127.0.0.1:{endpoint.Port}/");
            _acceptLoop = AcceptLoopAsync();
        }

        public Uri BaseUri { get; }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                await _acceptLoop;
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // Expected while stopping the ephemeral listener.
            }

            Task[] clientTasks;
            lock (_clientGate)
            {
                clientTasks = _clientTasks.ToArray();
            }

            try
            {
                await Task.WhenAll(clientTasks);
            }
            catch (Exception ex) when (
                ex is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
            {
                // Expected for speculative connections that are cancelled during shutdown.
            }

            _stop.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    break;
                }

                var clientTask = HandleAsync(client, _stop.Token);
                lock (_clientGate)
                {
                    _clientTasks.Add(clientTask);
                }
            }
        }

        private static async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                var buffer = new byte[8192];
                var length = await stream.ReadAsync(buffer, cancellationToken);
                if (length <= 0)
                {
                    return;
                }

                var request = Encoding.ASCII.GetString(buffer, 0, length);
                var requestLine = request.Split("\r\n", 2, StringSplitOptions.None)[0];
                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var path = parts.Length >= 2 ? parts[1].Split('?', 2)[0] : "/";
                if (path.Equals("/probe/fail", StringComparison.Ordinal))
                {
                    return;
                }

                var (status, contentType, body) = path switch
                {
                    "/index.html" => (
                        "200 OK",
                        "text/html; charset=utf-8",
                        "<!doctype html><script>fetch('/probe/live.m3u8',{cache:'no-store'})" +
                        ".then(response=>response.text()).catch(()=>{})" +
                        ".finally(()=>fetch('/probe/fail',{cache:'no-store'}).catch(()=>{}));</script>"),
                    "/probe/live.m3u8" => (
                        "200 OK",
                        "application/vnd.apple.mpegurl",
                        "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:6\n" +
                        "#EXT-X-MEDIA-SEQUENCE:1\n#EXTINF:6.0,\nsegment-1.ts\n"),
                    _ => ("404 Not Found", "text/plain; charset=utf-8", "not found")
                };
                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\nCache-Control: no-store\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(headers, cancellationToken);
                await stream.WriteAsync(bodyBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }
    }
}
