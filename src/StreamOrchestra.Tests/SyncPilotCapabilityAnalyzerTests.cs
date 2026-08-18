using System.Text.Json;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;
using StreamOrchestra.Tools;

namespace StreamOrchestra.Tests;

public sealed class SyncPilotCapabilityAnalyzerTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "StreamOrchestra.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Analyze_UsesIndependentSessionUnitsAndReportsCapabilityAndStrata()
    {
        var first = Snapshot("session-1", hasPdt: true, usedRvfc: true);
        var duplicate = first with { GeneratedAtUtc = first.GeneratedAtUtc.AddMinutes(1) };
        var second = Snapshot("session-2", hasPdt: false, usedRvfc: false);

        var report = SyncPilotCapabilityAnalyzer.Analyze(
            [first, duplicate, second],
            generatedAtUtc: DateTimeOffset.Parse("2026-08-18T02:00:00Z"));

        Assert.Equal(3, report.InputSnapshotCount);
        Assert.Equal(2, report.IndependentUnitCount);
        Assert.Equal(2, report.ValidUnitCount);
        Assert.Equal("collecting", report.CollectionStatus);
        var pdt = report.Availability.Single(item => item.MetricId == "pdt");
        Assert.Equal(2, pdt.EligibleUnitCount);
        Assert.Equal(1, pdt.ObservedUnitCount);
        Assert.Equal(0.5, pdt.AvailabilityRate);
        Assert.Equal(2, report.BucketCounts.Single(item =>
            item.Dimension == "quality" && item.Bucket == "1080p").UnitCount);
    }

    [Fact]
    public void Analyze_RejectsSnapshotWithoutTwoPlayerSlotsOrCurrentSchema()
    {
        var oneSlot = Snapshot("one-slot", hasPdt: true, usedRvfc: true) with
        {
            Players = Snapshot("temp", true, true).Players.Take(1).ToArray()
        };
        var oldSchema = Snapshot("old-schema", true, true) with { SchemaVersion = 1 };

        var report = SyncPilotCapabilityAnalyzer.Analyze([oneSlot, oldSchema]);

        Assert.Equal(0, report.ValidUnitCount);
        Assert.Contains(report.InvalidUnitReasons, reason => reason.EndsWith("fewer-than-two-player-slots"));
        Assert.Contains(report.InvalidUnitReasons, reason => reason.EndsWith("schema-1"));
    }

    [Fact]
    public void Analyze_ExcludesWarmupEventsAndCapsSameChannelSetDayAtThreePrimaryUnits()
    {
        var snapshots = Enumerable.Range(1, 4)
            .Select(index => Snapshot($"session-{index}", hasPdt: true, usedRvfc: true))
            .ToArray();
        snapshots[0] = snapshots[0] with
        {
            Playlists = snapshots[0].Playlists.Select(item => item with
            {
                ObservedAt = new SyncTelemetryClockSample(
                    DateTimeOffset.Parse("2026-08-18T01:01:59Z"),
                    119000)
            }).ToArray()
        };

        var report = SyncPilotCapabilityAnalyzer.Analyze(snapshots);

        Assert.Equal(3, report.ValidUnitCount);
        Assert.Equal(1, report.SensitivityOnlyUnitCount);
        var pdt = report.Availability.Single(item => item.MetricId == "pdt");
        Assert.Equal(2, pdt.ObservedUnitCount);
    }

    [Fact]
    public void Analyze_RejectsSessionShorterThanWarmupPlusFifteenMinuteObservation()
    {
        var shortSession = Snapshot("short", true, true) with
        {
            GeneratedAtUtc = DateTimeOffset.Parse("2026-08-18T01:16:59Z")
        };

        var report = SyncPilotCapabilityAnalyzer.Analyze([shortSession]);

        Assert.Equal(0, report.ValidUnitCount);
        Assert.Contains(report.InvalidUnitReasons, reason =>
            reason.EndsWith("duration-shorter-than-17-minutes", StringComparison.Ordinal));
    }

    [Fact]
    public void PrivacyAudit_FindsRawUrlSecretAndForbiddenPropertyButAcceptsSafeSnapshot()
    {
        var safe = JsonSerializer.Serialize(Snapshot("safe", true, true), JsonOptions);
        var unsafeJson = """
            {"requestUrl":"https://example.test/live.m3u8?token=secret"}
            """;

        Assert.Empty(SyncTelemetryPrivacyAudit.FindViolations(safe));
        var violations = SyncTelemetryPrivacyAudit.FindViolations(unsafeJson);
        Assert.Contains(violations, item => item.StartsWith("forbidden-property:"));
        Assert.Contains(violations, item => item.StartsWith("raw-url:"));
        Assert.Contains(violations, item => item.StartsWith("secret-pattern:"));
    }

    [Fact]
    public void Command_AnalyzesFolderAndWritesJsonReport()
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "sync-telemetry-1.json");
        var outputPath = Path.Combine(_root, "pilot-report.json");
        File.WriteAllText(input, JsonSerializer.Serialize(Snapshot("command", true, true), JsonOptions));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = SyncPilotCommand.Execute(
            ["analyze", "--input", _root, "--output", outputPath],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Independent valid units: 1/60", output.ToString());
        Assert.Contains("- pdt: 1/1", output.ToString());
        Assert.Equal("", error.ToString());
        using var report = JsonDocument.Parse(File.ReadAllText(outputPath));
        Assert.Equal(1, report.RootElement.GetProperty("validUnitCount").GetInt32());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static SyncTelemetrySnapshot Snapshot(string session, bool hasPdt, bool usedRvfc)
    {
        var started = DateTimeOffset.Parse("2026-08-18T01:00:00Z");
        var sessionEvent = new SyncSessionTelemetry(
            session,
            new SyncTelemetryClockSample(started, 1),
            "test",
            "runtime",
            1000);
        var playlist = new SyncPlaylistTelemetry(
            session,
            1,
            "playlist-hmac",
            new SyncUrlIdentity("https", "host-hmac", "depth-1.m3u8", "url-hmac"),
            "media",
            "video",
            "progress-hmac",
            1,
            new SyncTelemetryClockSample(started.AddMinutes(2).AddSeconds(1), 121000),
            HasProgramDateTime: hasPdt)
        {
            ProgramDateTimeCount = hasPdt ? 1 : 0,
            ProgramDateTimeTimezoneCount = hasPdt ? 1 : 0,
            ProgramDateTimePrecisionBucket = hasPdt ? "milliseconds" : "none",
            TrackingDisposition = "new-evidence",
            ContentTypeBucket = "application/vnd.apple.mpegurl"
        };
        var players = new[] { 1, 2 }.Select(slot => new SyncPlayerTelemetry(
            session,
            slot,
            1,
            new SyncTelemetryClockSample(started.AddMinutes(2).AddSeconds(2), 122000),
            100,
            [new MediaTimeRange(90, 110)],
            [new MediaTimeRange(80, 120)],
            "sample",
            usedRvfc)
        {
            FrameAgeMilliseconds = usedRvfc ? 10 : null,
            TotalVideoFrames = 100,
            ChannelId = $"channel-{slot}",
            BroadcastSessionId = "broadcast",
            QualityBucket = "1080p",
            CdnBucket = "cdn",
            PcLoadBucket = "low",
            NetworkBucket = "unknown",
            PlaybackBucket = "normal",
            SourceBucket = hasPdt ? "pdt" : "unknown"
        }).ToArray();
        return new SyncTelemetrySnapshot(
            SyncTelemetrySchema.SchemaVersion,
            SyncTelemetrySchema.ModelVersion,
            true,
            started.AddMinutes(17),
            [sessionEvent],
            [],
            [playlist],
            players,
            [],
            [],
            [],
            [],
            0);
    }
}
