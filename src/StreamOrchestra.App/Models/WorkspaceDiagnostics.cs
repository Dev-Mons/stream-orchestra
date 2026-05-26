namespace StreamOrchestra.App.Models;

public sealed record WorkspaceDiagnostics(
    int SavedWorkspaceCount,
    int FavoriteCount,
    bool HasLastSession,
    string? LastWorkspaceId,
    int? SelectedSlotId,
    string? LastSessionLayoutId,
    int LastSessionSlotCount,
    int LastSessionActiveStreamCount);
