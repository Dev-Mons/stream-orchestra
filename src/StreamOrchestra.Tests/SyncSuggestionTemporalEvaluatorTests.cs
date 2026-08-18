using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncSuggestionTemporalEvaluatorTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-08-18T00:00:00Z");
    private static readonly SyncBiasContext ChannelA = new("channel-a", "1080p", "cdn-a");
    private static readonly SyncBiasContext ChannelB = new("channel-b", "1080p", "cdn-a");
    private static readonly SyncBiasContext ChannelX = new("channel-x", "720p", "cdn-x");
    private static readonly SyncBiasContext ChannelY = new("channel-y", "720p", "cdn-y");

    [Fact]
    public void Evaluate_UsesLockedTemporalLabelsAndSeparatesSeenAndGraphCoverage()
    {
        var observations = new[]
        {
            Pair("dev-1", 1, ChannelA, ChannelB, 200),
            Pair("dev-2", 2, ChannelA, ChannelB, 220),
            Pair("dev-3", 3, ChannelA, ChannelB, 180),
            Pair("holdout-seen", 4, ChannelA, ChannelB, 210),
            Pair("holdout-unseen", 5, ChannelX, ChannelY, 500)
        };

        var report = SyncSuggestionTemporalEvaluator.Evaluate(
            new SyncBiasPriorDocument { PairObservations = observations },
            new SyncSuggestionTemporalEvaluatorOptions
            {
                DevelopmentSessionTarget = 3,
                HoldoutSessionTarget = 2
            });

        Assert.Equal("ready-for-review", report.Status);
        Assert.Equal(["dev-1", "dev-2", "dev-3"], report.DevelopmentSessionIds);
        Assert.Equal(["holdout-seen", "holdout-unseen"], report.HoldoutSessionIds);
        var seen = report.PairMetrics.Single(item => item.Segment == "seen");
        var unseen = report.PairMetrics.Single(item => item.Segment == "unseen");
        var connected = report.PairMetrics.Single(item => item.Segment == "connected");
        var disconnected = report.PairMetrics.Single(item => item.Segment == "disconnected");
        Assert.Equal(1, seen.EligiblePairCount);
        Assert.Equal(1, seen.SuggestedPairCount);
        Assert.Equal(1, unseen.EligiblePairCount);
        Assert.Equal(0, unseen.SuggestedPairCount);
        Assert.Equal(1, connected.SuggestedPairCount);
        Assert.Equal(1, disconnected.EligiblePairCount);
        Assert.Contains(report.HierarchyMetrics, item =>
            item.HierarchyLevel == "channel-quality-cdn" &&
            item.MeanIndependentSessionSupport == 3);
    }

    [Fact]
    public void Evaluate_RejectsImplicitOrUnstableLabelsAndNeverCreatesZeroLabels()
    {
        var eligible = Pair("eligible", 1, ChannelA, ChannelB, 100);
        var unstable = Pair("unstable", 2, ChannelA, ChannelB, 0) with
        {
            IsStableFinal = false
        };
        var unrelatedEvent = Event(
            "unlabeled-session",
            ChannelA,
            SyncBiasManualEventKind.UserAdjusted,
            Start.AddDays(3),
            residual: 0);

        var report = SyncSuggestionTemporalEvaluator.Evaluate(
            new SyncBiasPriorDocument
            {
                PairObservations = [eligible, unstable],
                ManualEvents = [unrelatedEvent]
            },
            new SyncSuggestionTemporalEvaluatorOptions
            {
                DevelopmentSessionTarget = 1,
                HoldoutSessionTarget = 1,
                EstimatorOptions = new SyncBiasEstimatorOptions
                {
                    MinimumIndependentSessionSupport = 1
                }
            });

        Assert.Equal(1, report.EligibleIndependentSessionCount);
        Assert.Equal(1, report.ExcludedIneligibleLabelCount);
        Assert.Equal(1, report.ExcludedUnlabeledSessionCount);
        Assert.True(report.UsesOnlyExplicitStableIndependentLabels);
        Assert.False(report.TreatsUnadjustedSessionsAsZeroLabels);
        Assert.True(report.IsLocalOnly);
    }

    [Fact]
    public void Evaluate_ReportsSuggestionLifecycleAndSubsequentResidualAdjustment()
    {
        var acceptedAt = Start.AddHours(1);
        var events = new[]
        {
            Event("session", ChannelA, SyncBiasManualEventKind.SuggestionShown, acceptedAt.AddMinutes(-1), 100),
            Event("session", ChannelA, SyncBiasManualEventKind.SuggestionAccepted, acceptedAt, 100),
            Event("session", ChannelA, SyncBiasManualEventKind.UserAdjusted, acceptedAt.AddMinutes(2), 175),
            Event("session", ChannelA, SyncBiasManualEventKind.SuggestionRejected, acceptedAt.AddMinutes(3), 175),
            Event("session", ChannelA, SyncBiasManualEventKind.SuggestionReverted, acceptedAt.AddMinutes(4), 0)
        };

        var report = SyncSuggestionTemporalEvaluator.Evaluate(
            new SyncBiasPriorDocument { ManualEvents = events },
            new SyncSuggestionTemporalEvaluatorOptions
            {
                DevelopmentSessionTarget = 1,
                HoldoutSessionTarget = 1
            });

        Assert.Equal(1, report.EventSummary.ShownCount);
        Assert.Equal(1, report.EventSummary.AcceptedCount);
        Assert.Equal(1, report.EventSummary.RejectedCount);
        Assert.Equal(1, report.EventSummary.RevertedCount);
        Assert.Equal(1, report.EventSummary.PostSuggestionResidualAdjustmentCount);
        Assert.Equal(75, report.EventSummary.TotalAbsolutePostSuggestionResidualAdjustmentMilliseconds);
    }

    private static SyncBiasPairObservation Pair(
        string session,
        int day,
        SyncBiasContext left,
        SyncBiasContext right,
        double difference) => new()
    {
        ObservationId = $"observation-{session}",
        IndependentSessionHash = session,
        Left = left,
        Right = right,
        DelayDifferenceMilliseconds = difference,
        OccurredAtUtc = Start.AddDays(day),
        IsIndependentSession = true,
        IsStableFinal = true,
        EventKind = SyncBiasManualEventKind.AlignmentConfirmed
    };

    private static SyncBiasManualEvent Event(
        string session,
        SyncBiasContext context,
        SyncBiasManualEventKind kind,
        DateTimeOffset at,
        int residual) => new()
    {
        EventId = $"{session}-{kind}-{at.Ticks}",
        SuggestionId = kind is SyncBiasManualEventKind.UserAdjusted ? "" : "suggestion",
        IndependentSessionHash = session,
        Context = context,
        EventKind = kind,
        OccurredAtUtc = at,
        AlgorithmPriorMilliseconds = 100,
        UserResidualMilliseconds = residual,
        FinalDelayMilliseconds = 100 + residual
    };
}
