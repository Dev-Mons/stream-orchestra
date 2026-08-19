using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public interface IStreamSyncTarget
{
    int SlotId { get; }

    string CurrentUrl { get; }

    string CurrentStreamName { get; }

    string SyncDisplayName { get; }

    SyncMemberSnapshot? LatestSyncSnapshot { get; }

    TimelineObservation? LatestTimeline { get; }

    Task<SyncCommandResult> ExecuteSyncCommandAsync(SyncCommand command);

    void SetSyncBadge(SyncBadgeState state);
}
