using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncBiasPriorServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-18T00:00:00Z");

    [Fact]
    public void ContextIdentityDropsRotatingQueryAndNeverRetainsRawChannelText()
    {
        var service = CreateService(out _);
        const string sentinel = "private-channel-sentinel";

        var first = service.CreateContext(
            $"https://play.sooplive.com/{sentinel}?token=one",
            "1080P",
            "edge.sooplive.co.kr")!;
        var second = service.CreateContext(
            $"https://play.sooplive.com/{sentinel}?token=two#fragment",
            "1080p",
            "edge.sooplive.co.kr")!;

        Assert.Equal(first, second);
        Assert.DoesNotContain(sentinel, first.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("token", first.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1080p", first.QualityBucket);
    }

    [Fact]
    public void ExplicitAlignmentConfirmationCreatesPairLabelsOncePerIndependentSession()
    {
        var service = CreateService(out var store);
        var members = Members(service, "broadcast-a", "broadcast-b", firstDelay: 1000, secondDelay: 0);

        Assert.True(service.RecordAlignmentConfirmation(members, Now));
        Assert.False(service.RecordAlignmentConfirmation(members, Now.AddMinutes(1)));

        var document = store.Load();
        var pair = Assert.Single(document.PairObservations);
        Assert.Equal(1000, pair.DelayDifferenceMilliseconds);
        Assert.True(pair.IsIndependentSession);
        Assert.True(pair.IsStableFinal);
        Assert.Equal(2, document.ManualEvents.Count);
    }

    [Fact]
    public void SuggestionsRequireIndependentSupportAndACompatibleGroupComponent()
    {
        var estimator = new SyncBiasEstimator(new SyncBiasEstimatorOptions
        {
            MinimumIndependentSessionSupport = 2
        });
        var service = CreateService(out _, estimator);
        service.RecordAlignmentConfirmation(
            Members(service, "session-1a", "session-1b", 1000, 0),
            Now.AddDays(-2));
        service.RecordAlignmentConfirmation(
            Members(service, "session-2a", "session-2b", 1000, 0),
            Now.AddDays(-1));
        var current = Members(service, "session-3a", "session-3b", 0, 0);

        var suggestions = service.GetCompatibleGroupSuggestions(current, Now);

        Assert.Equal(2, suggestions.Count);
        Assert.All(suggestions.Values, suggestion => Assert.True(suggestion.IsSuggestionOnly));
        Assert.Equal(
            1000,
            suggestions[1].SuggestedDelayMilliseconds -
            suggestions[2].SuggestedDelayMilliseconds);
        Assert.Empty(service.GetCompatibleGroupSuggestions([current[0]], Now));
    }

    [Fact]
    public void AcceptRejectAndRevertEventsNeverBecomePairLabels()
    {
        var service = CreateService(out var store);
        var member = Members(service, "session-a", "session-b", 0, 0)[0];
        var suggestion = new SyncBiasSuggestion
        {
            SuggestionId = "suggestion-hash",
            SuggestedDelayMilliseconds = 500,
            HierarchyLevel = SyncBiasHierarchyLevel.Channel,
            ComponentId = "component",
            IndependentSessionSupport = 3
        };
        var components = new SyncManualDelayComponents(500, 0, 500);

        service.RecordSuggestionEvent(member, suggestion, SyncBiasManualEventKind.SuggestionAccepted, components, Now);
        service.RecordSuggestionEvent(member, suggestion, SyncBiasManualEventKind.SuggestionRejected, components, Now);
        service.RecordSuggestionEvent(member, suggestion, SyncBiasManualEventKind.SuggestionReverted, components, Now);

        var document = store.Load();
        Assert.Empty(document.PairObservations);
        Assert.Equal(3, document.ManualEvents.Count);
        Assert.Equal(
            3,
            document.ManualEvents.Select(item => item.EventKind).Distinct().Count());
    }

    [Fact]
    public void RejectingOneMemberSuppressesGaugeDependentSuggestionsForTheGroupSession()
    {
        var estimator = new SyncBiasEstimator(new SyncBiasEstimatorOptions
        {
            MinimumIndependentSessionSupport = 2
        });
        var service = CreateService(out _, estimator);
        service.RecordAlignmentConfirmation(
            Members(service, "old-1a", "old-1b", 1000, 0),
            Now.AddDays(-2));
        service.RecordAlignmentConfirmation(
            Members(service, "old-2a", "old-2b", 1000, 0),
            Now.AddDays(-1));
        var current = Members(service, "current-a", "current-b", 0, 0);
        var suggestion = service.GetCompatibleGroupSuggestions(current, Now)[1];
        service.RecordSuggestionEvent(
            current[0],
            suggestion,
            SyncBiasManualEventKind.SuggestionRejected,
            new SyncManualDelayComponents(0, 0, 0),
            Now);

        Assert.Empty(service.GetCompatibleGroupSuggestions(current, Now));
    }

    private static SyncBiasPriorService CreateService(
        out MemoryStore store,
        SyncBiasEstimator? estimator = null)
    {
        store = new MemoryStore();
        return new SyncBiasPriorService(
            Enumerable.Repeat((byte)17, 32).ToArray(),
            store,
            estimator);
    }

    private static IReadOnlyList<SyncBiasGroupMember> Members(
        SyncBiasPriorService service,
        string firstBroadcast,
        string secondBroadcast,
        int firstDelay,
        int secondDelay) =>
    [
        new SyncBiasGroupMember(
            1,
            service.CreateContext("https://play.sooplive.com/channel-a", "1080p", "cdn")!,
            firstBroadcast,
            firstDelay,
            0,
            firstDelay),
        new SyncBiasGroupMember(
            2,
            service.CreateContext("https://play.sooplive.com/channel-b", "1080p", "cdn")!,
            secondBroadcast,
            secondDelay,
            0,
            secondDelay)
    ];

    private sealed class MemoryStore : ISyncBiasPriorStore
    {
        private SyncBiasPriorDocument _document = new();

        public SyncBiasPriorDocument Load() => _document;

        public void Save(SyncBiasPriorDocument document) => _document = document;

        public void DeleteAll() => _document = new SyncBiasPriorDocument();

        public void ExportPrivacySafe(string destinationPath)
        {
        }
    }
}
