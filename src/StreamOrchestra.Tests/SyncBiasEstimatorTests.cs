using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncBiasEstimatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-18T00:00:00Z");

    [Fact]
    public void PairwiseSolutionPreservesDifferencesAndFixesOnlyAComponentGauge()
    {
        var estimator = Estimator(minimumSupport: 2);
        var observations = new List<SyncBiasPairObservation>();
        AddSession(observations, "session-1", a: 1500, b: 500, c: 1000, daysAgo: 2);
        AddSession(observations, "session-2", a: 2200, b: 1200, c: 1700, daysAgo: 1);

        var a = estimator.Estimate(Context("a"), observations, Now)!;
        var b = estimator.Estimate(Context("b"), observations, Now)!;
        var c = estimator.Estimate(Context("c"), observations, Now)!;

        Assert.Equal(1000, a.SuggestedDelayMilliseconds - b.SuggestedDelayMilliseconds);
        Assert.Equal(500, a.SuggestedDelayMilliseconds - c.SuggestedDelayMilliseconds);
        Assert.Equal(0, a.SuggestedDelayMilliseconds + b.SuggestedDelayMilliseconds +
                        c.SuggestedDelayMilliseconds);
        Assert.Equal(a.ComponentId, b.ComponentId);
        Assert.Equal(b.ComponentId, c.ComponentId);
    }

    [Fact]
    public void DisconnectedComponentsAreNotGivenACommonGauge()
    {
        var estimator = Estimator(minimumSupport: 2);
        var observations = new List<SyncBiasPairObservation>();
        AddPairSessions(observations, Context("a"), Context("b"), 1000, "ab");
        AddPairSessions(observations, Context("d"), Context("e"), -400, "de");

        var a = estimator.Estimate(Context("a"), observations, Now)!;
        var d = estimator.Estimate(Context("d"), observations, Now)!;

        Assert.NotEqual(a.ComponentId, d.ComponentId);
        Assert.Equal(1000,
            a.SuggestedDelayMilliseconds -
            estimator.Estimate(Context("b"), observations, Now)!.SuggestedDelayMilliseconds);
    }

    [Fact]
    public void HierarchyFallsBackFromCdnToChannelQualityThenChannel()
    {
        var estimator = Estimator(minimumSupport: 2);
        var observations = new List<SyncBiasPairObservation>();
        var aCdn1 = Context("a", "1080p", "cdn-1");
        var bCdn1 = Context("b", "1080p", "cdn-1");
        var aCdn2 = Context("a", "1080p", "cdn-2");
        var bCdn2 = Context("b", "1080p", "cdn-2");
        observations.Add(Pair("s1", aCdn1, bCdn1, 800, 2));
        observations.Add(Pair("s2", aCdn2, bCdn2, 800, 1));

        var qualityFallback = estimator.Estimate(
            Context("a", "1080p", "unseen-cdn"),
            observations,
            Now)!;
        var channelFallback = estimator.Estimate(
            Context("a", "720p", "unseen-cdn"),
            observations,
            Now)!;

        Assert.Equal(SyncBiasHierarchyLevel.ChannelQuality, qualityFallback.HierarchyLevel);
        Assert.Equal(SyncBiasHierarchyLevel.Channel, channelFallback.HierarchyLevel);
    }

    [Fact]
    public void SparseUnseenAndNonLabelsRemainSuggestionFree()
    {
        var estimator = Estimator(minimumSupport: 2);
        var observations = new List<SyncBiasPairObservation>
        {
            Pair("one-session", Context("a"), Context("b"), 1000, 1),
            Pair("rejected", Context("a"), Context("b"), 1000, 1) with
            {
                EventKind = SyncBiasManualEventKind.SuggestionRejected
            },
            Pair("not-independent", Context("a"), Context("b"), 1000, 1) with
            {
                IsIndependentSession = false
            }
        };

        Assert.Null(estimator.Estimate(Context("a"), observations, Now));
        Assert.Null(estimator.Estimate(Context("unseen"), observations, Now));
    }

    [Fact]
    public void RepeatedEventsInOneSessionCountAsOneIndependentSupport()
    {
        var estimator = Estimator(minimumSupport: 2);
        var observations = new[]
        {
            Pair("same-session", Context("a"), Context("b"), 1000, 2),
            Pair("same-session", Context("a"), Context("b"), 1100, 1)
        };

        Assert.Null(estimator.Estimate(Context("a"), observations, Now));
    }

    [Fact]
    public void RecencyDecayWeightsNewIndependentSessionMoreThanOldConflict()
    {
        var estimator = new SyncBiasEstimator(new SyncBiasEstimatorOptions
        {
            MinimumIndependentSessionSupport = 2,
            RecencyHalfLife = TimeSpan.FromDays(10)
        });
        var observations = new[]
        {
            Pair("old", Context("a"), Context("b"), 2000, 90),
            Pair("new", Context("a"), Context("b"), 0, 1)
        };

        var a = estimator.Estimate(Context("a"), observations, Now)!;
        var b = estimator.Estimate(Context("b"), observations, Now)!;

        Assert.InRange(Math.Abs(a.SuggestedDelayMilliseconds - b.SuggestedDelayMilliseconds), 0, 500);
    }

    private static SyncBiasEstimator Estimator(int minimumSupport) => new(new SyncBiasEstimatorOptions
    {
        MinimumIndependentSessionSupport = minimumSupport
    });

    private static void AddSession(
        ICollection<SyncBiasPairObservation> observations,
        string session,
        int a,
        int b,
        int c,
        int daysAgo)
    {
        observations.Add(Pair(session, Context("a"), Context("b"), a - b, daysAgo));
        observations.Add(Pair(session, Context("a"), Context("c"), a - c, daysAgo));
        observations.Add(Pair(session, Context("b"), Context("c"), b - c, daysAgo));
    }

    private static void AddPairSessions(
        ICollection<SyncBiasPairObservation> observations,
        SyncBiasContext left,
        SyncBiasContext right,
        int difference,
        string prefix)
    {
        observations.Add(Pair($"{prefix}-1", left, right, difference, 2));
        observations.Add(Pair($"{prefix}-2", left, right, difference, 1));
    }

    private static SyncBiasPairObservation Pair(
        string session,
        SyncBiasContext left,
        SyncBiasContext right,
        double difference,
        int daysAgo) => new()
    {
        ObservationId = $"{session}:{left.StableChannelHash}:{right.StableChannelHash}",
        IndependentSessionHash = session,
        Left = left,
        Right = right,
        DelayDifferenceMilliseconds = difference,
        OccurredAtUtc = Now.AddDays(-daysAgo),
        IsIndependentSession = true,
        IsStableFinal = true,
        EventKind = SyncBiasManualEventKind.AlignmentConfirmed
    };

    private static SyncBiasContext Context(
        string channel,
        string quality = "1080p",
        string cdn = "cdn") => new(channel, quality, cdn);
}
