using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.App.Views;

public partial class StreamSlotView
{
    private static readonly JsonSerializerOptions SyncJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HlsTimelineParser _hlsTimelineParser = new();
    private readonly Dictionary<ulong, SyncPlayerCandidate> _syncPlayerCandidates = [];
    private string? _syncBridgeScriptId;
    private int _syncNavigationGeneration;
    private CoreWebView2Frame? _syncCommandFrame;
    private Point? _lastSyncPopupAnchor;
    private SyncBadgeState? _lastSyncBadgeState;
    private string _syncBroadcasterName = "";
    private int _syncBroadcasterNameConfidence;

    public SyncMemberSnapshot? LatestSyncSnapshot { get; private set; }

    public TimelineObservation? LatestTimeline { get; private set; }

    public string SyncDisplayName => _syncBroadcasterNameConfidence >= 2 &&
                                     _navigationService.IsMeaningfulDisplayName(_syncBroadcasterName)
        ? _syncBroadcasterName
        : _navigationService.IsMeaningfulDisplayName(CurrentStreamName)
            ? CurrentStreamName
            : _navigationService.IsMeaningfulDisplayName(_syncBroadcasterName)
                ? _syncBroadcasterName
                : "방송 정보 확인 중";

    private async Task InitializeStreamSyncAsync()
    {
        if (_syncBridgeScriptId is not null || Browser.CoreWebView2 is null)
        {
            return;
        }

        _syncBridgeScriptId = await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            CreateStreamSyncBridgeScript());
        Browser.CoreWebView2.NavigationStarting += (_, _) => ResetStreamSyncObservations();
        Browser.CoreWebView2.WebMessageReceived += (_, args) =>
            ReceiveSyncMessage(frame: null, args);
        Browser.CoreWebView2.FrameCreated += (_, args) => WireSyncFrame(args.Frame);
        Browser.CoreWebView2.WebResourceResponseReceived += ReceiveHlsResponse;
    }

    private void ResetStreamSyncObservations()
    {
        _syncNavigationGeneration++;
        _syncPlayerCandidates.Clear();
        _syncCommandFrame = null;
        LatestSyncSnapshot = null;
        LatestTimeline = null;
        _syncBroadcasterName = "";
        _syncBroadcasterNameConfidence = 0;
    }

    private void WireSyncFrame(CoreWebView2Frame frame)
    {
        var frameId = frame.FrameId;
        frame.WebMessageReceived += (_, args) => ReceiveSyncMessage(frame, args);
        frame.FrameCreated += (_, args) => WireSyncFrame(args.Frame);
        frame.Destroyed += (_, _) =>
        {
            _syncPlayerCandidates.Remove(frameId);
            if (ReferenceEquals(_syncCommandFrame, frame))
            {
                _syncCommandFrame = null;
                SelectPrimarySyncCandidate();
            }
        };
    }

    private void ReceiveSyncMessage(CoreWebView2Frame? frame, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsTrustedSoopSource(args.Source))
        {
            return;
        }

        SyncStatusMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<SyncStatusMessage>(args.WebMessageAsJson, SyncJsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (message is null || !message.Type.Equals("stream-sync-status", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        UpdateSyncBroadcasterName(message.StreamName, message.StreamNameConfidence);
        var now = DateTimeOffset.UtcNow;
        var snapshot = new SyncMemberSnapshot
        {
            HasVideo = message.HasVideo,
            IsSoop = true,
            CurrentTime = message.CurrentTime,
            PlaybackRate = message.PlaybackRate,
            Paused = message.Paused,
            ReadyState = message.ReadyState,
            Buffering = message.Buffering,
            BufferSec = message.BufferSec,
            SeekableStart = message.SeekableStart,
            SeekableEnd = message.SeekableEnd,
            LastBufferEventAt = message.LastBufferEventAt,
            ViewportArea = Math.Max(0, message.Width) * Math.Max(0, message.Height),
            ObservedAtUtc = now
        };
        var key = frame?.FrameId ?? 0;
        _syncPlayerCandidates[key] = new SyncPlayerCandidate(frame, snapshot);
        SelectPrimarySyncCandidate();
    }

    private void UpdateSyncBroadcasterName(string? streamName, int confidence)
    {
        if (!_navigationService.IsMeaningfulDisplayName(streamName))
        {
            return;
        }

        var normalized = string.Join(
            " ",
            streamName!.Trim().Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        var normalizedConfidence = Math.Clamp(confidence, 0, 3);
        if (_syncBroadcasterName.Equals(normalized, StringComparison.Ordinal))
        {
            _syncBroadcasterNameConfidence = Math.Max(
                _syncBroadcasterNameConfidence,
                normalizedConfidence);
            return;
        }

        if (!string.IsNullOrEmpty(_syncBroadcasterName) &&
            normalizedConfidence <= _syncBroadcasterNameConfidence)
        {
            return;
        }

        _syncBroadcasterName = normalized;
        _syncBroadcasterNameConfidence = normalizedConfidence;
        PlaybackStateChanged?.Invoke(this);
    }

    private void SelectPrimarySyncCandidate()
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2);
        foreach (var staleKey in _syncPlayerCandidates
                     .Where(item => item.Value.Snapshot.ObservedAtUtc < cutoff)
                     .Select(item => item.Key)
                     .ToArray())
        {
            _syncPlayerCandidates.Remove(staleKey);
        }

        var primary = _syncPlayerCandidates.Values
            .Where(candidate => candidate.Snapshot.HasVideo)
            .OrderByDescending(candidate => candidate.Snapshot.ViewportArea)
            .ThenByDescending(candidate => candidate.Snapshot.ObservedAtUtc)
            .FirstOrDefault();
        if (primary is null)
        {
            return;
        }

        LatestSyncSnapshot = primary.Snapshot;
        _syncCommandFrame = primary.Frame;
    }

    private async void ReceiveHlsResponse(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (!IsHlsPlaylistUrl(args.Request.Uri) || args.Response.StatusCode is < 200 or >= 300)
        {
            return;
        }

        var generation = _syncNavigationGeneration;
        DateTimeOffset? responseDateUtc = null;
        try
        {
            var dateHeader = args.Response.Headers.GetHeader("Date");
            if (DateTimeOffset.TryParse(
                    dateHeader,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedDate))
            {
                responseDateUtc = parsedDate.ToUniversalTime();
            }
        }
        catch
        {
            // Date is optional; PROGRAM-DATE-TIME can still provide an absolute timeline.
        }

        try
        {
            await using var content = await args.Response.GetContentAsync();
            if (content is null)
            {
                return;
            }

            using var reader = new StreamReader(content);
            var playlist = await reader.ReadToEndAsync();
            var observedAt = DateTimeOffset.UtcNow;
            var observation = await Task.Run(() =>
                _hlsTimelineParser.Parse(playlist, responseDateUtc, observedAt));
            if (observation is null || generation != _syncNavigationGeneration)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() => UpdateTimelineObservation(observation));
        }
        catch
        {
            // HLS responses can expire or be consumed by the player before inspection completes.
        }
    }

    private void UpdateTimelineObservation(TimelineObservation observation)
    {
        var previous = LatestTimeline;
        if (previous is not null && previous.Source == observation.Source)
        {
            var observationGap = observation.ObservedAtUtc - previous.ObservedAtUtc;
            if (observationGap <= TimeSpan.Zero)
            {
                return;
            }

            if (observationGap <= TimeSpan.FromSeconds(15))
            {
                if (observation.EdgeUtc <= previous.EdgeUtc)
                {
                    return;
                }

                var maximumExpectedJump = Math.Max(
                    8000,
                    Math.Max(previous.SegmentDurationSec, observation.SegmentDurationSec) * 3000);
                var projectedPreviousEdge = previous.EdgeUtc.Add(observationGap);
                if (Math.Abs((observation.EdgeUtc - projectedPreviousEdge).TotalMilliseconds) >
                    maximumExpectedJump)
                {
                    return;
                }

                if (previous.MediaToUtcOffsetMs is { } previousOffset &&
                    observation.MediaToUtcOffsetMs is { } nextOffset)
                {
                    if (Math.Abs(nextOffset - previousOffset) > maximumExpectedJump)
                    {
                        return;
                    }

                    observation = observation with
                    {
                        MediaToUtcOffsetMs = previousOffset * 0.8 + nextOffset * 0.2
                    };
                }
            }
        }

        if (previous is null ||
            observation.Confidence >= previous.Confidence ||
            observation.ObservedAtUtc - previous.ObservedAtUtc > TimeSpan.FromSeconds(15))
        {
            LatestTimeline = observation;
        }
    }

    public Task ExecuteSyncCommandAsync(SyncCommand command)
    {
        if (Browser.CoreWebView2 is null)
        {
            return Task.CompletedTask;
        }

        var action = command.Type switch
        {
            SyncCommandType.SetRate => "rate",
            SyncCommandType.Seek => "seek",
            SyncCommandType.Pause => "pause",
            SyncCommandType.Resume => "resume",
            SyncCommandType.ResetRate => "reset-rate",
            _ => ""
        };
        if (string.IsNullOrEmpty(action))
        {
            return Task.CompletedTask;
        }

        var json = JsonSerializer.Serialize(new
        {
            type = "stream-sync-command",
            action,
            value = command.Value
        });

        try
        {
            if (_syncCommandFrame is { } frame && frame.IsDestroyed() == 0)
            {
                frame.PostWebMessageAsJson(json);
            }
            else
            {
                Browser.CoreWebView2.PostWebMessageAsJson(json);
            }
        }
        catch
        {
            // A navigation can invalidate a frame between status receipt and command delivery.
        }

        return Task.CompletedTask;
    }

    public void SetSyncBadge(SyncBadgeState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetSyncBadge(state));
            return;
        }

        if (!state.IsVisible)
        {
            SyncStatusPopup.IsOpen = false;
            _lastSyncPopupAnchor = null;
            _lastSyncBadgeState = null;
            return;
        }

        if (_lastSyncBadgeState is { } previous &&
            previous.RuntimeState == state.RuntimeState &&
            previous.TimelineSource == state.TimelineSource &&
            previous.Text.Equals(state.Text, StringComparison.Ordinal) &&
            SyncStatusPopup.IsOpen)
        {
            return;
        }

        _lastSyncBadgeState = state;
        SyncStatusTextBlock.Text = state.Text;
        var color = state.RuntimeState switch
        {
            SyncRuntimeState.Running => Color.FromRgb(31, 122, 78),
            SyncRuntimeState.Recovering => Color.FromRgb(180, 58, 58),
            SyncRuntimeState.Preparing or SyncRuntimeState.Waiting or SyncRuntimeState.Degraded =>
                Color.FromRgb(154, 105, 24),
            _ => Color.FromRgb(62, 73, 86)
        };
        SyncStatusBorder.Background = new SolidColorBrush(Color.FromArgb(225, color.R, color.G, color.B));
        SyncStatusBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 226, 232, 240));
        SyncStatusPopup.IsOpen = true;
        RefreshSyncBadgePlacement(forceRefresh: true);
        QueueRefreshOpenOverlayPlacement();
    }

    private void RefreshSyncBadgePlacement(bool forceRefresh)
    {
        if (!IsLoaded || !SyncStatusPopup.IsOpen)
        {
            return;
        }

        var anchor = GetSlotScreenPoint(12, 12);
        if (!forceRefresh && !HasPointChanged(_lastSyncPopupAnchor, anchor))
        {
            return;
        }

        _lastSyncPopupAnchor = anchor;
        NudgePopupPlacement(SyncStatusPopup);
        SetPopupScreenPosition(SyncStatusPopup, anchor);
    }

    private static bool IsHlsPlaylistUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrustedSoopSource(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("sooplive.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sooplive.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("sooplive.co.kr", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sooplive.co.kr", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateStreamSyncBridgeScript()
    {
        return """
(() => {
  if (window.__streamOrchestraSyncBridgeInstalled) return;

  const host = location.hostname.toLowerCase();
  const isSoop = host === "sooplive.com" || host.endsWith(".sooplive.com") ||
    host === "sooplive.co.kr" || host.endsWith(".sooplive.co.kr");
  if (!isSoop || !window.chrome?.webview) return;

  window.__streamOrchestraSyncBridgeInstalled = true;
  let lastBufferEventAt = 0;

  function mediaRanges(ranges) {
    const result = [];
    for (let index = 0; index < ranges.length; index += 1) {
      result.push([ranges.start(index), ranges.end(index)]);
    }
    return result;
  }

  function primaryVideo() {
    return Array.from(document.querySelectorAll("video"))
      .sort((left, right) => {
        const leftScore = (left.paused ? 0 : 1e12) + left.clientWidth * left.clientHeight;
        const rightScore = (right.paused ? 0 : 1e12) + right.clientWidth * right.clientHeight;
        return rightScore - leftScore;
      })[0] || null;
  }

  function attach(video) {
    if (!video || video.__streamOrchestraSyncWired) return;
    video.__streamOrchestraSyncWired = true;
    for (const eventName of ["waiting", "stalled", "error"]) {
      video.addEventListener(eventName, () => { lastBufferEventAt = Date.now(); }, true);
    }
  }

  function cleanStreamName(value) {
    const normalized = String(value || "").replace(/\s+/g, " ").trim();
    const withoutServiceName = normalized
      .replace(/\s*(?:[-|·]\s*)?(?:SOOP|AfreecaTV)\s*$/i, "")
      .trim();
    if (!withoutServiceName ||
        /^(?:SOOP|embed|about:blank)$/i.test(withoutServiceName) ||
        /^\d{6,}$/.test(withoutServiceName) ||
        /^https?:\/\//i.test(withoutServiceName) ||
        withoutServiceName.length > 80) {
      return "";
    }

    return withoutServiceName;
  }

  function broadcasterIdentity() {
    const selectors = [
      ".broadcast_information .nickname",
      ".broadcast_info .nickname",
      ".broadcast_information .bj_name",
      ".broadcast_info .bj_name",
      "#player_info .nickname",
      "#bj_nick",
      ".bj_name",
      "[class*='broadcast'] [class*='nickname']",
      "[class*='station'] [class*='nickname']"
    ];
    for (const selector of selectors) {
      const name = cleanStreamName(document.querySelector(selector)?.textContent);
      if (name) return { name, confidence: 3 };
    }

    for (const selector of ["meta[name='author']", "meta[property='og:profile:username']"]) {
      const name = cleanStreamName(document.querySelector(selector)?.content);
      if (name) return { name, confidence: 3 };
    }

    for (const script of document.querySelectorAll("script[type='application/ld+json']")) {
      try {
        const values = JSON.parse(script.textContent || "null");
        const items = Array.isArray(values) ? values : [values];
        for (const item of items) {
          const name = cleanStreamName(item?.author?.name || item?.creator?.name);
          if (name) return { name, confidence: 3 };
        }
      } catch {}
    }

    const openGraphTitle = cleanStreamName(
      document.querySelector("meta[property='og:title']")?.content);
    if (openGraphTitle) return { name: openGraphTitle, confidence: 2 };

    const title = cleanStreamName(document.title);
    return { name: title, confidence: title ? 1 : 0 };
  }

  function postStatus() {
    const video = primaryVideo();
    const identity = broadcasterIdentity();
    attach(video);
    if (!video) {
      window.chrome.webview.postMessage({
        type: "stream-sync-status",
        hasVideo: false,
        streamName: identity.name,
        streamNameConfidence: identity.confidence
      });
      return;
    }

    const buffered = mediaRanges(video.buffered);
    const seekable = mediaRanges(video.seekable);
    const bufferEnd = buffered.length ? buffered[buffered.length - 1][1] : null;
    const seekableStart = seekable.length ? seekable[0][0] : null;
    const seekableEnd = seekable.length ? seekable[seekable.length - 1][1] : null;
    window.chrome.webview.postMessage({
      type: "stream-sync-status",
      hasVideo: true,
      streamName: identity.name,
      streamNameConfidence: identity.confidence,
      currentTime: video.currentTime,
      playbackRate: video.playbackRate,
      paused: video.paused,
      readyState: video.readyState,
      buffering: video.readyState < 3 && !video.paused,
      bufferSec: bufferEnd === null ? null : Math.max(0, bufferEnd - video.currentTime),
      seekableStart,
      seekableEnd,
      lastBufferEventAt,
      width: video.clientWidth || video.videoWidth || 0,
      height: video.clientHeight || video.videoHeight || 0
    });
  }

  window.chrome.webview.addEventListener("message", event => {
    const message = event.data;
    if (!message || message.type !== "stream-sync-command") return;
    const video = primaryVideo();
    if (!video) return;

    try {
      if (message.action === "rate") {
        const rate = Number(message.value);
        if (Number.isFinite(rate) && Math.abs(video.playbackRate - rate) >= 0.002) {
          video.playbackRate = rate;
        }
      } else if (message.action === "seek") {
        const target = Number(message.value);
        const seekable = mediaRanges(video.seekable);
        if (Number.isFinite(target) && seekable.some(range => target >= range[0] && target <= range[1])) {
          video.currentTime = target;
        }
      } else if (message.action === "pause") {
        video.pause();
      } else if (message.action === "resume") {
        video.play().catch(() => {});
      } else if (message.action === "reset-rate") {
        video.playbackRate = 1;
      }
    } catch {}
  });

  setInterval(postStatus, 500);
  postStatus();
})();
""";
    }

    private sealed record SyncPlayerCandidate(
        CoreWebView2Frame? Frame,
        SyncMemberSnapshot Snapshot);

    private sealed class SyncStatusMessage
    {
        public string Type { get; init; } = "";

        public bool HasVideo { get; init; }

        public string? StreamName { get; init; }

        public int StreamNameConfidence { get; init; }

        public double CurrentTime { get; init; }

        public double PlaybackRate { get; init; } = 1;

        public bool Paused { get; init; }

        public int ReadyState { get; init; }

        public bool Buffering { get; init; }

        public double? BufferSec { get; init; }

        public double? SeekableStart { get; init; }

        public double? SeekableEnd { get; init; }

        public long LastBufferEventAt { get; init; }

        public double Width { get; init; }

        public double Height { get; init; }
    }
}
