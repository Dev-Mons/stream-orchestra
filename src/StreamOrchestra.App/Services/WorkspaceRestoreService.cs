using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class WorkspaceRestoreService
{
    private readonly WorkspacePresetNormalizationService _normalizationService;
    private readonly WorkspaceSlotVisibilityService _visibilityService;

    public WorkspaceRestoreService(
        WorkspacePresetNormalizationService normalizationService,
        WorkspaceSlotVisibilityService visibilityService)
    {
        _normalizationService = normalizationService;
        _visibilityService = visibilityService;
    }

    public PreparedWorkspace Prepare(WorkspacePreset workspace, IReadOnlyList<LayoutPreset> layouts)
    {
        if (layouts.Count == 0)
        {
            throw new InvalidOperationException("At least one layout preset is required.");
        }

        var normalizedWorkspace = _normalizationService.Normalize(workspace, layouts);
        var layout = layouts.FirstOrDefault(candidate =>
                         candidate.Id.Equals(normalizedWorkspace.LayoutId, StringComparison.OrdinalIgnoreCase))
                     ?? LayoutPresetService.SelectDefaultLayout(layouts);
        var visibleWorkspace = _visibilityService.BlankHiddenSlots(normalizedWorkspace, layout);

        return new PreparedWorkspace(visibleWorkspace, layout);
    }
}

public sealed record PreparedWorkspace(WorkspacePreset Workspace, LayoutPreset Layout);
