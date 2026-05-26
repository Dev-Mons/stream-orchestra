using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class FeasibilityResultOrderingService
{
    public static FeasibilityTestResult? LatestOrDefault(IEnumerable<FeasibilityTestResult>? results)
    {
        return OrderLatestFirst(results).FirstOrDefault();
    }

    public static IReadOnlyList<FeasibilityTestResult> OrderLatestFirst(IEnumerable<FeasibilityTestResult>? results)
    {
        return OrderByTimestampAndInputOrder(results, latestFirst: true);
    }

    public static IReadOnlyList<FeasibilityTestResult> OrderOldestFirst(IEnumerable<FeasibilityTestResult>? results)
    {
        return OrderByTimestampAndInputOrder(results, latestFirst: false);
    }

    private static IReadOnlyList<FeasibilityTestResult> OrderByTimestampAndInputOrder(
        IEnumerable<FeasibilityTestResult>? results,
        bool latestFirst)
    {
        IEnumerable<FeasibilityTestResult> sourceResults = results ?? [];
        var indexedResults = sourceResults
            .Select((result, index) => new IndexedResult(result, index));

        return latestFirst
            ? indexedResults
                .OrderByDescending(item => item.Result.CapturedAt)
                .ThenByDescending(item => item.Index)
                .Select(item => item.Result)
                .ToArray()
            : indexedResults
                .OrderBy(item => item.Result.CapturedAt)
                .ThenBy(item => item.Index)
                .Select(item => item.Result)
                .ToArray();
    }

    private sealed record IndexedResult(FeasibilityTestResult Result, int Index);
}
