namespace StreamOrchestra.App.Models;

public sealed class AutoUpdateState
{
    public bool Enabled { get; init; } = true;

    public string? SkippedVersion { get; init; }

    public DateTimeOffset? LastCheckUtc { get; init; }
}
