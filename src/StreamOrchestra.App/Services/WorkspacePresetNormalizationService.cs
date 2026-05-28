using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class WorkspacePresetNormalizationService
{
    private readonly StreamNavigationService _navigationService;

    public WorkspacePresetNormalizationService(StreamNavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public WorkspacePreset Normalize(WorkspacePreset workspace, IReadOnlyList<LayoutPreset> layouts)
    {
        var layoutTree = workspace.LayoutTree?.Root is null ? null : workspace.LayoutTree;
        var layoutId = layoutTree is not null ? "dynamic" : NormalizeLayoutId(workspace.LayoutId, layouts);
        var sourceSlots = workspace.Slots ?? [];
        var slotsById = sourceSlots
            .Where(slot => slot is { SlotId: >= 1 and <= PlaybackTestPlanService.MaxSlotCount })
            .GroupBy(slot => slot.SlotId)
            .ToDictionary(group => group.Key, group => group.Last());

        return new WorkspacePreset
        {
            Id = string.IsNullOrWhiteSpace(workspace.Id) ? "workspace_imported" : workspace.Id.Trim(),
            Name = string.IsNullOrWhiteSpace(workspace.Name) ? "Imported Workspace" : workspace.Name.Trim(),
            LayoutId = layoutId,
            LayoutTree = layoutTree,
            Slots = Enumerable.Range(1, PlaybackTestPlanService.MaxSlotCount)
                .Select(slotId => NormalizeSlot(slotId, slotsById))
                .ToArray()
        };
    }

    private WorkspaceSlot NormalizeSlot(int slotId, IReadOnlyDictionary<int, WorkspaceSlot> slotsById)
    {
        var source = slotsById.TryGetValue(slotId, out var slot)
            ? slot
            : new WorkspaceSlot { SlotId = slotId };
        var streamUrl = _navigationService.NormalizeUrl(source.StreamUrl ?? "about:blank");

        return new WorkspaceSlot
        {
            SlotId = slotId,
            StreamUrl = streamUrl,
            StreamName = CreateNormalizedStreamName(streamUrl, source.StreamName),
            Muted = source.Muted,
            ProfileGroupId = GetProfileGroupIdForSlot(slotId)
        };
    }

    private string CreateNormalizedStreamName(string streamUrl, string? streamName)
    {
        if (streamUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return "Empty";
        }

        return string.IsNullOrWhiteSpace(streamName)
            ? _navigationService.CreateDisplayName(streamUrl)
            : streamName.Trim();
    }

    private static string NormalizeLayoutId(string layoutId, IReadOnlyList<LayoutPreset> layouts)
    {
        if (layouts.Any(layout => layout.Id.Equals(layoutId, StringComparison.OrdinalIgnoreCase)))
        {
            return layouts.First(layout => layout.Id.Equals(layoutId, StringComparison.OrdinalIgnoreCase)).Id;
        }

        return layouts.FirstOrDefault(layout => layout.Id == LayoutPresetIds.Default)?.Id
            ?? layouts.FirstOrDefault()?.Id
            ?? LayoutPresetIds.Default;
    }

    private static string GetProfileGroupIdForSlot(int slotId)
    {
        return slotId switch
        {
            >= 1 and <= 4 => "A",
            >= 5 and <= 8 => "B",
            >= 9 and <= 12 => "C",
            >= 13 and <= 16 => "D",
            _ => throw new ArgumentOutOfRangeException(nameof(slotId), slotId, "Slot id must be between 1 and 16.")
        };
    }
}
