using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncEstimatorCalibrationPipelineTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-08-18T00:00:00Z");

    [Fact]
    public void Analyze_UsesChronologicalDisjointSplitAndNeverTunesOnHoldout()
    {
        var options = new SyncEstimatorCalibrationPipelineOptions
        {
            DevelopmentSessionTarget = 2,
            HoldoutSessionTarget = 1,
            MinimumDevelopmentSessionsForTuning = 2
        };
        var sessions = new[]
        {
            Session("session-3", 3),
            Session("session-1", 1),
            Session("session-2", 2)
        };
        var first = SyncEstimatorCalibrationPipeline.Analyze(sessions, options, Start.AddDays(1));
        var changedHoldout = sessions.Select(session => session.IndependentSessionId == "session-3"
            ? session with
            {
                Observations = session.Observations.Select(observation => observation with
                {
                    RawOffsetMilliseconds = observation.RawOffsetMilliseconds + 100_000,
                    ReferenceOffsetMilliseconds = observation.ReferenceOffsetMilliseconds - 100_000
                }).ToArray()
            }
            : session).ToArray();
        var second = SyncEstimatorCalibrationPipeline.Analyze(changedHoldout, options, Start.AddDays(1));

        Assert.Equal(["session-1", "session-2"], first.DevelopmentSessionIds);
        Assert.Equal(["session-3"], first.HoldoutSessionIds);
        Assert.Empty(first.DevelopmentSessionIds.Intersect(first.HoldoutSessionIds));
        Assert.Equal("ready-for-review", first.Status);
        Assert.Equal(first.SelectedOptions, second.SelectedOptions);
        Assert.Equal(8, first.TuningTrace.Count);
    }

    [Fact]
    public void Analyze_ComparesAllEstimatorsAtSameObservationIdsAndCalibratesThreeDomains()
    {
        var report = SyncEstimatorCalibrationPipeline.Analyze(
            [Session("dev-1", 1), Session("dev-2", 2), Session("holdout", 3)],
            new SyncEstimatorCalibrationPipelineOptions
            {
                DevelopmentSessionTarget = 2,
                HoldoutSessionTarget = 1,
                MinimumDevelopmentSessionsForTuning = 2
            });

        Assert.All(report.HoldoutMetrics, metric => Assert.Equal(8, metric.MatchedObservationCount));
        Assert.Equal(
            ["controllability", "manual-bias", "timeline"],
            report.HoldoutConfidenceCalibration
                .Select(item => item.Domain)
                .Distinct()
                .Order()
                .ToArray());
        Assert.Contains(report.HoldoutIntervalCoverageByStratum, metric =>
            metric.EstimatorId == "kalman" && metric.ObservationCount == 8);
        Assert.Contains(report.HoldoutIntervalCoverageByStratum, metric =>
            metric.EstimatorId == "huber" && metric.MeanWidthMilliseconds >= 0);
    }

    [Fact]
    public void Analyze_ExcludesDuplicateSessionsAndNonIndependentReferences()
    {
        var duplicate = Session("duplicate", 1);
        var invalid = Session("invalid", 2) with
        {
            Observations = Session("invalid", 2).Observations.Select((observation, index) =>
                index == 0 ? observation with { IsIndependentReference = false } : observation).ToArray()
        };

        var report = SyncEstimatorCalibrationPipeline.Analyze(
            [duplicate, duplicate, invalid],
            new SyncEstimatorCalibrationPipelineOptions
            {
                DevelopmentSessionTarget = 2,
                HoldoutSessionTarget = 1,
                MinimumDevelopmentSessionsForTuning = 1
            });

        Assert.Equal(0, report.IndependentSessionCount);
        Assert.Contains(report.ExclusionReasons, item =>
            item.EndsWith("duplicate-independent-session", StringComparison.Ordinal));
        Assert.Contains(report.ExclusionReasons, item =>
            item.EndsWith("invalid-or-non-independent-reference", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_LocksFortyTwoDevelopmentAndEighteenHoldoutWithEvidenceAndRollback()
    {
        var sessions = Enumerable.Range(1, 60)
            .Select(index => Session($"session-{index:00}", index, observationCount: 3))
            .ToArray();

        var report = SyncEstimatorCalibrationPipeline.Analyze(
            sessions,
            generatedAtUtc: Start.AddDays(2));

        Assert.Equal("ready-for-review", report.Status);
        Assert.Equal(42, report.DevelopmentSessionCount);
        Assert.Equal(18, report.HoldoutSessionCount);
        Assert.Equal(64, report.EvidenceSha256.Length);
        Assert.Equal(new SyncTimelineEstimatorOptions(), report.RollbackOptions);
        Assert.Contains(report.FaultCoverage, item => item.FaultKind == "utc-step");
    }

    private static SyncEstimatorCalibrationSession Session(
        string id,
        int day,
        int observationCount = 8) => new()
    {
        IndependentSessionId = id,
        StartedAtUtc = Start.AddDays(day),
        IsIndependentSession = true,
        Observations = Enumerable.Range(1, observationCount).Select(index =>
        {
            var reference = 1000 + index * 2;
            return new SyncEstimatorCalibrationObservation
            {
                ObservationId = $"{id}-observation-{index}",
                SourceIdentity = $"source-{id}",
                SourceEpoch = 1,
                RawOffsetMilliseconds = reference + (index % 2 == 0 ? 8 : -8),
                LegacyOffsetMilliseconds = reference + 20,
                ReferenceOffsetMilliseconds = reference,
                IsIndependentReference = true,
                ObservedAtUtc = Start.AddDays(day).AddSeconds(index),
                ObservedMonotonicTicks = index * 1000,
                MonotonicFrequency = 1000,
                IndependentEvidenceCount = index,
                TimelineConfidence = 0.8,
                ManualBiasConfidence = 0.7,
                ControllabilityConfidence = 0.9,
                TimelineOutcomeSucceeded = true,
                ManualBiasOutcomeSucceeded = true,
                ControllabilityOutcomeSucceeded = true,
                FaultKind = index == observationCount ? "utc-step" : "normal",
                Strata = new SyncEvaluationStrata(
                    "wifi",
                    "low",
                    "seen",
                    "1080p",
                    "cdn-a",
                    "normal",
                    "pdt")
            };
        }).ToArray()
    };
}
