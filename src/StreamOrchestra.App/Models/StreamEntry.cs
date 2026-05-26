namespace StreamOrchestra.App.Models;

public sealed class StreamEntry
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Platform { get; init; } = "SOOP";

    public string Url { get; init; } = "";

    public string Memo { get; init; } = "";

    public DateTimeOffset LastUsedAt { get; init; } = DateTimeOffset.Now;
}
