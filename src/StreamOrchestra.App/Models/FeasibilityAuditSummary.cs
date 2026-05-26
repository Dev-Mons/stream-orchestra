namespace StreamOrchestra.App.Models;

public sealed record FeasibilityAuditSummary(
    int PassCount,
    int PendingCount,
    int FailCount)
{
    public string ToCompactText()
    {
        return $"pass={PassCount}, pending={PendingCount}, fail={FailCount}";
    }
}
