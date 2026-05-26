using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class FeasibilityResultOrderingServiceTests
{
    [Fact]
    public void LatestOrDefault_UsesLaterInputOrderWhenTimestampsMatch()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        var first = CreateResult("first", capturedAt);
        var second = CreateResult("second", capturedAt);

        var latest = FeasibilityResultOrderingService.LatestOrDefault([first, second]);

        Assert.Equal("second", latest?.Id);
    }

    [Fact]
    public void OrderLatestFirst_OrdersByTimestampThenLaterInputOrder()
    {
        var older = CreateResult("older", new DateTimeOffset(2026, 5, 26, 11, 0, 0, TimeSpan.Zero));
        var tiedFirst = CreateResult("tied_first", new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var tiedSecond = CreateResult("tied_second", new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));

        var ordered = FeasibilityResultOrderingService.OrderLatestFirst([older, tiedFirst, tiedSecond]);

        Assert.Equal(["tied_second", "tied_first", "older"], ordered.Select(result => result.Id));
    }

    private static FeasibilityTestResult CreateResult(string id, DateTimeOffset capturedAt)
    {
        return new FeasibilityTestResult
        {
            Id = id,
            CapturedAt = capturedAt,
            PlaybackCount = 9,
            ScenarioId = "groups_a_b_c_9_slot_threshold",
            Outcome = "partial"
        };
    }
}
