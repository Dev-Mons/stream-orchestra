namespace StreamOrchestra.App.Models;

public sealed record FeasibilityDecision(
    string Code,
    string Title,
    string Detail,
    string NextAction = "");
