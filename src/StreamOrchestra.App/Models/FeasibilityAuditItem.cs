namespace StreamOrchestra.App.Models;

public sealed record FeasibilityAuditItem(
    string Id,
    string Title,
    string Status,
    string Evidence);
