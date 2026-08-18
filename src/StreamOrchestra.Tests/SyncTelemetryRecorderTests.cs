using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;
using System.Text.Json.Nodes;

namespace StreamOrchestra.Tests;

public sealed class SyncTelemetryRecorderTests : IDisposable
{
    private readonly string _rootFolder = Path.Combine(
        Path.GetTempPath(),
        "StreamOrchestra.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DisabledRecorder_IsStaticAndDoesNotRetainEvents()
    {
        var recorder = SyncTelemetryRecorder.Disabled;

        recorder.RecordSession(new SyncSessionTelemetry(
            "ignored",
            new SyncTelemetryClockSample(DateTimeOffset.UtcNow, 1)));

        var snapshot = recorder.CreateSnapshot();
        var summary = recorder.CreateSummary();

        Assert.False(recorder.IsEnabled);
        Assert.False(snapshot.IsEnabled);
        Assert.Empty(snapshot.Sessions);
        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.GeneratedAtUtc);
        Assert.Same(snapshot, recorder.CreateSnapshot());
        Assert.Same(SyncTelemetrySummary.Disabled, summary);
        Assert.Equal(SyncTelemetryPrivacy.EmptyUrlIdentity, recorder.CreateUrlIdentity(
            "https://example.com/live.m3u8?token=must-not-be-read"));
    }

    [Fact]
    public void UrlIdentity_DropsCredentialsAndQueryAndUsesKeyedStableHash()
    {
        var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var privacy = new SyncTelemetryPrivacy(key);

        var first = privacy.CreateUrlIdentity(
            "https://viewer:password@edge.video.sooplive.co.kr/private/channel/live.m3u8?token=secret-one&sig=signed-one");
        var second = privacy.CreateUrlIdentity(
            "https://edge.video.sooplive.co.kr/private/channel/live.m3u8?token=secret-two&sig=signed-two");
        var otherKey = new SyncTelemetryPrivacy(Enumerable.Repeat((byte)42, 32).ToArray())
            .CreateUrlIdentity("https://edge.video.sooplive.co.kr/private/channel/live.m3u8?token=secret-one");

        Assert.Equal("https", first.SchemeBucket);
        Assert.Equal("sooplive.co.kr", first.HostBucket);
        Assert.Equal("depth-3.m3u8", first.PathBucket);
        Assert.Equal(first.PersistenceHash, second.PersistenceHash);
        Assert.NotEqual(first.PersistenceHash, otherKey.PersistenceHash);
        Assert.DoesNotContain("private", first.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", first.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", first.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recorder_RehashesCallerIdentifiersAndRebucketsHosts()
    {
        var recorder = SyncTelemetryRecorder.CreateEnabled(new SyncTelemetryRecorderOptions
        {
            SessionId = "rehash-session",
            IdentityKey = Enumerable.Repeat((byte)9, 32).ToArray()
        });
        var stamp = new SyncTelemetryClockSample(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 10);
        const string callerToken = "0123456789abcdef01234567";

        recorder.RecordNetwork(new SyncNetworkTelemetry(
            recorder.SessionId,
            1,
            callerToken,
            new SyncUrlIdentity(
                "https",
                "private-token.edge.example.com",
                "depth-1.m3u8",
                callerToken),
            "resource-token-sentinel",
            stamp,
            null,
            null,
            OutcomeCode: "outcome-secret-sentinel"));

        var persisted = recorder.CreateSnapshot().Network.Single();
        Assert.NotEqual(callerToken, persisted.RequestId);
        Assert.NotEqual(callerToken, persisted.Resource.PersistenceHash);
        Assert.Equal("example.com", persisted.Resource.HostBucket);
        Assert.Equal("unknown", persisted.ResourceKind);
        Assert.Equal("unknown", persisted.OutcomeCode);
        Assert.DoesNotContain("sentinel", persisted.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeText_RemovesHeadersBearerJwtAndSignedUrls()
    {
        var privacy = new SyncTelemetryPrivacy(Enumerable.Repeat((byte)7, 32).ToArray());
        var raw = """
            Authorization: Bearer auth-sentinel
            Cookie: session=cookie-sentinel; other=value
            url=https://user:pass@cdn.example.com/private/live.m3u8?token=query-sentinel&sig=signature-sentinel
            password=password-sentinel access_token=access-sentinel
            jwt=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJzZW50aW5lbCJ9.signatureSentinel
            """;

        var sanitized = privacy.SanitizeText(raw);

        Assert.Contains("[url:example.com:depth-2.m3u8:", sanitized);
        Assert.Contains("[redacted]", sanitized);
        Assert.Contains("[redacted-jwt]", sanitized);
        foreach (var secret in new[]
                 {
                     "auth-sentinel", "cookie-sentinel", "query-sentinel", "signature-sentinel",
                     "password-sentinel", "access-sentinel", "signatureSentinel", "user:pass"
                 })
        {
            Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EnabledRecorder_KeepsVersionedBoundedTypedShadowTrace()
    {
        var clock = new FakeTelemetryClock();
        var recorder = SyncTelemetryRecorder.CreateEnabled(
            new SyncTelemetryRecorderOptions
            {
                MaxEventsPerCategory = 2,
                SessionId = "session-1",
                AppVersion = "0.10.1",
                RuntimeBucket = "webview2-runtime",
                IdentityKey = Enumerable.Repeat((byte)11, 32).ToArray()
            },
            clock);
        var sample = clock.Capture();
        var resource = recorder.CreateUrlIdentity(
            "https://cdn.example.com/live/index.m3u8?token=query-secret");

        for (var index = 0; index < 3; index++)
        {
            recorder.RecordNetwork(new SyncNetworkTelemetry(
                recorder.SessionId,
                1,
                $"request-{index}",
                resource,
                "playlist",
                sample,
                sample,
                sample,
                200,
                "application/vnd.apple.mpegurl; charset=utf-8",
                "hit",
                4096,
                OutcomeCode: "ok"));
        }

        recorder.RecordPlaylist(new SyncPlaylistTelemetry(
            recorder.SessionId,
            1,
            "playlist-token-secret",
            resource,
            "media",
            "video",
            "progress-token-secret",
            1,
            sample));
        recorder.RecordPlayer(new SyncPlayerTelemetry(
            recorder.SessionId,
            1,
            1,
            sample,
            12.5,
            [new MediaTimeRange(10, 15), new MediaTimeRange(double.NaN, 20)],
            [new MediaTimeRange(5, 20)],
            "frame",
            true,
            PageSampleMonotonicMilliseconds: 123.5));
        recorder.RecordEstimate(new SyncEstimateTelemetry(
            recorder.SessionId,
            1,
            "kalman-shadow",
            "playlist-1",
            1,
            sample,
            125,
            120,
            0.2,
            35,
            true,
            ObservationId: "observation-secret",
            ProgressKey: "progress-token-secret"));
        recorder.RecordDecision(new SyncDecisionTelemetry(
            recorder.SessionId,
            1,
            "decision-1",
            1,
            sample,
            new SyncPolicyDecision("legacy", "running", 10, "seek", 10, true, "legacy decision"),
            new SyncPolicyDecision("interval-v1", "shadow", 10.1, "rate", 0.99, false, "low confidence"),
            CandidateIsShadowOnly: false,
            TickId: "tick-secret"));
        recorder.RecordAction(new SyncActionTelemetry(
            recorder.SessionId,
            1,
            "command-1",
            1,
            "seek",
            10,
            "verified",
            sample,
            DecisionId: "decision-1",
            ObservedMediaTimeSeconds: 10,
            OutcomeCode: "position-confirmed"));
        recorder.RecordManualEvent(new SyncManualEventTelemetry(
            recorder.SessionId,
            1,
            "manual-1",
            "channel-hash",
            "broadcast-hash",
            "suggestion-accepted",
            sample,
            100,
            0,
            50,
            150,
            SuggestionId: "suggestion-secret",
            IsStableFinalAcceptance: true,
            IsIndependentSession: true));

        var snapshot = recorder.CreateSnapshot();
        var summary = recorder.CreateSummary();

        Assert.True(snapshot.IsEnabled);
        Assert.Equal(SyncTelemetrySchema.SchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(SyncTelemetrySchema.ModelVersion, snapshot.ModelVersion);
        Assert.Single(snapshot.Sessions);
        Assert.True(snapshot.Sessions[0].MonotonicFrequency > 0);
        Assert.Equal(2, snapshot.Network.Count);
        Assert.All(snapshot.Network, item => Assert.Matches("^[0-9a-f]{24}$", item.RequestId));
        Assert.Equal("application/vnd.apple.mpegurl", snapshot.Network[0].ContentTypeBucket);
        Assert.Equal("ok", snapshot.Network[0].OutcomeCode);
        Assert.Single(snapshot.Playlists);
        Assert.Matches("^[0-9a-f]{24}$", snapshot.Playlists[0].PlaylistId);
        Assert.Matches("^[0-9a-f]{24}$", snapshot.Playlists[0].ProgressKey);
        Assert.DoesNotContain("token-secret", snapshot.Playlists[0].ToString(), StringComparison.Ordinal);
        Assert.Single(snapshot.Players);
        Assert.Single(snapshot.Players[0].BufferedRanges);
        Assert.Equal(123.5, snapshot.Players[0].PageSampleMonotonicMilliseconds);
        Assert.NotEqual(snapshot.Estimates[0].RawOffsetMilliseconds, snapshot.Estimates[0].FilteredOffsetMilliseconds);
        Assert.Matches("^[0-9a-f]{24}$", snapshot.Estimates[0].ObservationId);
        Assert.True(snapshot.Decisions[0].CandidateIsShadowOnly);
        Assert.Equal("verified", snapshot.Actions[0].Stage);
        Assert.Equal("position-confirmed", snapshot.Actions[0].OutcomeCode);
        Assert.Equal("suggestion-accepted", snapshot.ManualEvents[0].EventType);
        Assert.True(snapshot.ManualEvents[0].IsIndependentSession);
        Assert.Equal(1, summary.DroppedEventCount);
        Assert.Equal(2, summary.NetworkEventCount);
        var sequences = snapshot.Network.Select(item => item.Sequence)
            .Concat(snapshot.Playlists.Select(item => item.Sequence))
            .Concat(snapshot.Players.Select(item => item.Sequence))
            .Concat(snapshot.Estimates.Select(item => item.Sequence))
            .Concat(snapshot.Decisions.Select(item => item.Sequence))
            .Concat(snapshot.Actions.Select(item => item.Sequence))
            .Concat(snapshot.ManualEvents.Select(item => item.Sequence))
            .Order()
            .ToArray();
        Assert.Equal(sequences.Length, sequences.Distinct().Count());
    }

    [Fact]
    public void DiagnosticPersistence_ScrubsNestedUrlsHeadersAndTelemetryIdentifiers()
    {
        Directory.CreateDirectory(_rootFolder);
        var clock = new FakeTelemetryClock();
        var recorder = SyncTelemetryRecorder.CreateEnabled(
            new SyncTelemetryRecorderOptions
            {
                SessionId = "session-1",
                IdentityKey = Enumerable.Repeat((byte)19, 32).ToArray()
            },
            clock);
        var sample = clock.Capture();
        recorder.RecordNetwork(new SyncNetworkTelemetry(
            recorder.SessionId,
            1,
            "request-recorder-cookie-secret",
            recorder.CreateUrlIdentity("https://cdn.example.com/live.m3u8?token=recorder-query-secret"),
            "playlist",
            sample,
            sample,
            sample,
            OutcomeCode: "ok"));
        var report = new DiagnosticReport
        {
            GeneratedAt = new DateTimeOffset(2026, 8, 18, 1, 2, 3, TimeSpan.Zero),
            SyncTelemetry = recorder.CreateSummary(),
            ExternalBrowserFallbackPlan = new ExternalBrowserFallbackPlan(
                true,
                "Authorization: Bearer reason-auth-secret",
                1,
                1,
                [
                    new ExternalBrowserSlotLaunchPlan(
                        1,
                        "Stream",
                        "https://viewer:password@video.example.com/private/live.m3u8?token=report-query-secret&sig=report-signature-secret",
                        "edge",
                        "Edge",
                        "C:\\Browsers\\edge.exe",
                        "C:\\Data\\Slot1",
                        [
                            "--new-window",
                            "https://video.example.com/private/live.m3u8?token=argument-query-secret",
                            "Cookie: session=argument-cookie-secret"
                        ])
                ])
        };
        var service = new DiagnosticReportService(syncTelemetryRecorder: recorder);

        var reportPath = service.SaveReport(report, _rootFolder);
        var telemetryPath = service.SaveSyncTelemetrySnapshot(_rootFolder);

        Assert.NotNull(telemetryPath);
        var persisted = File.ReadAllText(reportPath) + File.ReadAllText(telemetryPath!);
        Assert.Contains("syncTelemetry", persisted);
        Assert.Contains("https://video.example.com/private/live.m3u8", persisted);
        foreach (var secret in new[]
                 {
                     "reason-auth-secret", "report-query-secret", "report-signature-secret",
                     "argument-query-secret", "argument-cookie-secret", "recorder-query-secret",
                     "recorder-cookie-secret", "viewer:password"
                 })
        {
            Assert.DoesNotContain(secret, persisted, StringComparison.Ordinal);
        }

        var reportJson = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
        var slot = reportJson["externalBrowserFallbackPlan"]!["slots"]![0]!.AsObject();
        var sanitizedUrl = slot["streamUrl"]!.GetValue<string>();
        var arguments = slot["arguments"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
        Assert.True(Uri.TryCreate(sanitizedUrl, UriKind.Absolute, out var parsedUrl));
        Assert.Equal("https", parsedUrl.Scheme);
        Assert.Contains(sanitizedUrl, arguments);
        Assert.Equal("https://viewer:password@video.example.com/private/live.m3u8?token=report-query-secret&sig=report-signature-secret",
            report.ExternalBrowserFallbackPlan!.Slots[0].StreamUrl);
    }

    [Fact]
    public void DiagnosticPersistence_DoesNotCreateTelemetryFileWhenDisabled()
    {
        Directory.CreateDirectory(_rootFolder);
        var service = new DiagnosticReportService(syncTelemetryRecorder: SyncTelemetryRecorder.Disabled);

        Assert.Null(service.SaveSyncTelemetrySnapshot(_rootFolder));
        Assert.Empty(Directory.GetFiles(_rootFolder, "sync-telemetry-*.json"));
    }

    [Fact]
    public void Recorder_DropsInvalidOrderingAndNonFinitePlayerSamplesBeforeSerialization()
    {
        Directory.CreateDirectory(_rootFolder);
        var recorder = SyncTelemetryRecorder.CreateEnabled(new SyncTelemetryRecorderOptions
        {
            SessionId = "validation-session",
            IdentityKey = Enumerable.Repeat((byte)23, 32).ToArray()
        });
        var request = new SyncTelemetryClockSample(
            new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.FromHours(9)),
            200);
        var earlierHeaders = new SyncTelemetryClockSample(request.Utc.AddMilliseconds(1), 199);
        var resource = new SyncUrlIdentity("https", "example.com", "depth-1.m3u8", "raw-token-sentinel");

        recorder.RecordNetwork(new SyncNetworkTelemetry(
            recorder.SessionId,
            1,
            "request-secret",
            resource,
            "playlist",
            request,
            earlierHeaders,
            null));
        recorder.RecordPlayer(new SyncPlayerTelemetry(
            recorder.SessionId,
            1,
            1,
            request,
            double.NaN,
            [],
            [],
            "sample",
            false));
        recorder.RecordEstimate(new SyncEstimateTelemetry(
            recorder.SessionId,
            1,
            "kalman-shadow",
            "source-secret",
            1,
            request,
            double.PositiveInfinity,
            10,
            double.NegativeInfinity,
            -1,
            true,
            TimelineConfidence: 2));

        var snapshot = recorder.CreateSnapshot();
        var path = new DiagnosticReportService(syncTelemetryRecorder: recorder)
            .SaveSyncTelemetrySnapshot(_rootFolder);

        Assert.Empty(snapshot.Network);
        Assert.Empty(snapshot.Players);
        Assert.Single(snapshot.Estimates);
        Assert.Null(snapshot.Estimates[0].RawOffsetMilliseconds);
        Assert.Null(snapshot.Estimates[0].DriftMillisecondsPerSecond);
        Assert.Null(snapshot.Estimates[0].StandardDeviationMilliseconds);
        Assert.Equal(1, snapshot.Estimates[0].TimelineConfidence);
        Assert.Equal(TimeSpan.Zero, snapshot.Estimates[0].EstimatedAt.Utc.Offset);
        Assert.Equal(2, snapshot.DroppedEventCount);
        Assert.NotNull(path);
        Assert.DoesNotContain("source-secret", File.ReadAllText(path!), StringComparison.Ordinal);
    }

    [Fact]
    public void PrivacySafeSerializer_FailsClosedForNestedSecretPropertyNames()
    {
        var service = new DiagnosticReportService();
        var payload = new
        {
            AuthorizationHeader = "Bearer authorization-sentinel",
            CookieHeader = "session=cookie-sentinel",
            RequestHeaders = new { Safe = "header-sentinel" },
            RawBody = "raw-body-sentinel",
            SignedUrl = "https://example.com/live.m3u8?sig=signed-url-sentinel",
            Nested = new[]
            {
                new { SafeUrl = "https://example.com/live.m3u8?token=nested-query-sentinel" }
            }
        };

        var json = service.SerializePrivacySafe(payload);

        Assert.Contains("[redacted]", json);
        Assert.Contains("https://example.com/live.m3u8", json);
        foreach (var secret in new[]
                 {
                     "authorization-sentinel", "cookie-sentinel", "header-sentinel", "raw-body-sentinel",
                     "signed-url-sentinel", "nested-query-sentinel"
                 })
        {
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CreateReport_ExposesOnlyEnabledTelemetrySummary()
    {
        Directory.CreateDirectory(_rootFolder);
        var dataFolder = Path.Combine(_rootFolder, "Data");
        var recorder = SyncTelemetryRecorder.CreateEnabled(new SyncTelemetryRecorderOptions
        {
            SessionId = "summary-session",
            IdentityKey = Enumerable.Repeat((byte)29, 32).ToArray()
        });
        var report = new DiagnosticReportService(syncTelemetryRecorder: recorder).CreateReport(
            new WebViewProfileService(Path.Combine(_rootFolder, "Profiles")),
            new PresetStorageService(dataFolder),
            new FavoriteStorageService(dataFolder),
            new FeasibilityResultStorageService(dataFolder),
            new FeasibilityDecision("pending", "Pending", "Pending"));
        var disabledReport = new DiagnosticReportService().CreateReport(
            new WebViewProfileService(Path.Combine(_rootFolder, "Profiles2")),
            new PresetStorageService(Path.Combine(_rootFolder, "Data2")),
            new FavoriteStorageService(Path.Combine(_rootFolder, "Data2")),
            new FeasibilityResultStorageService(Path.Combine(_rootFolder, "Data2")),
            new FeasibilityDecision("pending", "Pending", "Pending"));

        Assert.True(report.SyncTelemetry.IsEnabled);
        Assert.Equal(1, report.SyncTelemetry.SessionCount);
        Assert.False(disabledReport.SyncTelemetry.IsEnabled);
        Assert.Equal(0, disabledReport.SyncTelemetry.SessionCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootFolder))
        {
            Directory.Delete(_rootFolder, recursive: true);
        }
    }

    private sealed class FakeTelemetryClock : ISyncTelemetryClock
    {
        private readonly DateTimeOffset _origin = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);
        private long _tick;

        public SyncTelemetryClockSample Capture()
        {
            var tick = Interlocked.Increment(ref _tick);
            return new SyncTelemetryClockSample(
                _origin.AddMilliseconds(tick),
                10_000 + tick);
        }
    }
}
