namespace StreamOrchestra.App.Models;

public sealed record SlotRuntimeState(
    int SlotId,
    string StreamName,
    string StreamUrl,
    bool IsMuted,
    string ProfileGroupId);
