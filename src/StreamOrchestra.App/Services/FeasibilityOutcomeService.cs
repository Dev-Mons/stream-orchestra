using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class FeasibilityOutcomeService
{
    public static bool IsKnown(FeasibilityTestResult result)
    {
        return IsKnown(result.Outcome);
    }

    public static bool IsKnown(string? outcome)
    {
        return IsSuccess(outcome) || IsPartial(outcome) || IsFailure(outcome);
    }

    public static bool IsSuccess(FeasibilityTestResult result)
    {
        return IsSuccess(result.Outcome);
    }

    public static bool IsSuccess(string? outcome)
    {
        return IsOutcome(outcome, "success");
    }

    public static bool IsPartial(FeasibilityTestResult result)
    {
        return IsPartial(result.Outcome);
    }

    public static bool IsPartial(string? outcome)
    {
        return IsOutcome(outcome, "partial");
    }

    public static bool IsFailure(FeasibilityTestResult result)
    {
        return IsFailure(result.Outcome);
    }

    public static bool IsFailure(string? outcome)
    {
        return IsOutcome(outcome, "failure");
    }

    private static bool IsOutcome(string? actualOutcome, string expectedOutcome)
    {
        return string.Equals(
            actualOutcome?.Trim(),
            expectedOutcome,
            StringComparison.OrdinalIgnoreCase);
    }
}
