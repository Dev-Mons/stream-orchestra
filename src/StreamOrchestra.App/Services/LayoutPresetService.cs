using System.IO;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class LayoutPresetService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyList<LayoutPreset> LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        var layouts = JsonSerializer.Deserialize<IReadOnlyList<LayoutPreset>>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"No layouts were found in {path}.");

        Validate(layouts);

        return layouts;
    }

    public IReadOnlyList<LayoutPreset> LoadFromDefaultLocation()
    {
        return LoadFromFile(GetDefaultLayoutFilePath());
    }

    public static LayoutPreset SelectDefaultLayout(IReadOnlyList<LayoutPreset> layouts)
    {
        if (layouts.Count == 0)
        {
            throw new InvalidOperationException("At least one layout preset is required.");
        }

        return layouts.FirstOrDefault(layout => layout.Id.Equals(LayoutPresetIds.Default, StringComparison.OrdinalIgnoreCase))
            ?? layouts[0];
    }

    public static LayoutPreset SelectPlaybackTestLayout(
        IReadOnlyList<LayoutPreset> layouts,
        LayoutPreset? currentLayout,
        int targetVisibleSlotCount)
    {
        if (layouts.Count == 0)
        {
            throw new InvalidOperationException("At least one layout preset is required.");
        }

        var targetSlotIds = Enumerable.Range(1, targetVisibleSlotCount).ToArray();
        var selectedLayout = currentLayout ?? SelectDefaultLayout(layouts);
        if (ContainsAllSlots(selectedLayout, targetSlotIds))
        {
            return selectedLayout;
        }

        return layouts.FirstOrDefault(layout =>
                   layout.Id.Equals(LayoutPresetIds.Tournament, StringComparison.OrdinalIgnoreCase) &&
                   ContainsAllSlots(layout, targetSlotIds))
               ?? layouts.FirstOrDefault(layout => ContainsAllSlots(layout, targetSlotIds))
               ?? throw new InvalidOperationException(
                   $"No layout can show playback test slot(s): {string.Join(", ", targetSlotIds)}.");
    }

    public static LayoutPreset SelectLayoutContainingSlots(
        IReadOnlyList<LayoutPreset> layouts,
        LayoutPreset? currentLayout,
        IReadOnlyCollection<int> targetSlotIds)
    {
        if (layouts.Count == 0)
        {
            throw new InvalidOperationException("At least one layout preset is required.");
        }

        var selectedLayout = currentLayout ?? SelectDefaultLayout(layouts);
        if (targetSlotIds.Count == 0 || ContainsAllSlots(selectedLayout, targetSlotIds))
        {
            return selectedLayout;
        }

        return layouts.FirstOrDefault(layout =>
                   layout.Id.Equals(LayoutPresetIds.Tournament, StringComparison.OrdinalIgnoreCase) &&
                   ContainsAllSlots(layout, targetSlotIds))
               ?? layouts.FirstOrDefault(layout => ContainsAllSlots(layout, targetSlotIds))
               ?? throw new InvalidOperationException(
                   $"No layout can show target slot(s): {string.Join(", ", targetSlotIds.OrderBy(slotId => slotId))}.");
    }

    private static bool ContainsAllSlots(LayoutPreset layout, IReadOnlyCollection<int> targetSlotIds)
    {
        var visibleSlotIds = layout.Slots
            .Select(slot => slot.SlotId)
            .ToHashSet();

        return targetSlotIds.All(visibleSlotIds.Contains);
    }

    public string GetDefaultLayoutFilePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "layouts.json");
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException("Layout preset file was not found.", path);
    }

    public static void Validate(IReadOnlyList<LayoutPreset> layouts)
    {
        if (layouts.Count == 0)
        {
            throw new InvalidOperationException("At least one layout preset is required.");
        }

        var layoutIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var layout in layouts)
        {
            if (layout is null)
            {
                throw new InvalidOperationException("Layout entry is required.");
            }

            ValidateLayout(layout, layoutIds);
        }
    }

    private static void ValidateLayout(LayoutPreset layout, HashSet<string> layoutIds)
    {
        if (string.IsNullOrWhiteSpace(layout.Id))
        {
            throw new InvalidOperationException("Layout id is required.");
        }

        if (!layoutIds.Add(layout.Id))
        {
            throw new InvalidOperationException($"Duplicate layout id: {layout.Id}.");
        }

        if (string.IsNullOrWhiteSpace(layout.Name))
        {
            throw new InvalidOperationException($"Layout {layout.Id} requires a name.");
        }

        if (layout.GridColumns <= 0 || layout.GridRows <= 0)
        {
            throw new InvalidOperationException($"Layout {layout.Id} has an invalid grid size.");
        }

        if (layout.Slots is null || layout.Slots.Count == 0)
        {
            throw new InvalidOperationException($"Layout {layout.Id} must contain at least one slot.");
        }

        var slotIds = new HashSet<int>();
        var occupiedCells = new HashSet<(int X, int Y)>();

        foreach (var slot in layout.Slots)
        {
            if (slot is null)
            {
                throw new InvalidOperationException($"Layout {layout.Id} contains a null slot entry.");
            }

            ValidateSlot(layout, slot, slotIds, occupiedCells);
        }
    }

    private static void ValidateSlot(
        LayoutPreset layout,
        LayoutSlot slot,
        HashSet<int> slotIds,
        HashSet<(int X, int Y)> occupiedCells)
    {
        if (slot.SlotId is < 1 or > PlaybackTestPlanService.MaxSlotCount)
        {
            throw new InvalidOperationException($"Layout {layout.Id} has invalid slot id {slot.SlotId}.");
        }

        if (!slotIds.Add(slot.SlotId))
        {
            throw new InvalidOperationException($"Layout {layout.Id} contains duplicate slot {slot.SlotId}.");
        }

        if (slot.X < 0 || slot.Y < 0 || slot.W <= 0 || slot.H <= 0)
        {
            throw new InvalidOperationException($"Layout {layout.Id} slot {slot.SlotId} has invalid coordinates.");
        }

        if (slot.X + slot.W > layout.GridColumns || slot.Y + slot.H > layout.GridRows)
        {
            throw new InvalidOperationException($"Layout {layout.Id} slot {slot.SlotId} exceeds the grid bounds.");
        }

        for (var y = slot.Y; y < slot.Y + slot.H; y++)
        {
            for (var x = slot.X; x < slot.X + slot.W; x++)
            {
                if (!occupiedCells.Add((x, y)))
                {
                    throw new InvalidOperationException($"Layout {layout.Id} slot {slot.SlotId} overlaps another slot at {x},{y}.");
                }
            }
        }
    }
}
