using System.IO;
using System.Text.Json;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class LayoutPresetService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly string? _customLayoutFilePath;

    public LayoutPresetService(string? dataFolder = null)
    {
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            return;
        }

        Directory.CreateDirectory(dataFolder);
        _customLayoutFilePath = Path.Combine(dataFolder, "custom-layouts.json");
    }

    public string? CustomLayoutFilePath => _customLayoutFilePath;

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
        return CombineLayouts(LoadBuiltInLayouts(), LoadCustomLayouts());
    }

    public IReadOnlyList<LayoutPreset> LoadBuiltInLayouts()
    {
        return LoadFromFile(GetDefaultLayoutFilePath());
    }

    public IReadOnlyList<LayoutPreset> LoadCustomLayouts()
    {
        if (_customLayoutFilePath is null || !File.Exists(_customLayoutFilePath))
        {
            return [];
        }

        var layouts = JsonFileStorage.LoadList<LayoutPreset>(_customLayoutFilePath, SerializerOptions);
        ValidateLayouts(layouts, requireAtLeastOne: false);

        return layouts;
    }

    public void SaveCustomLayouts(IReadOnlyList<LayoutPreset> layouts)
    {
        if (_customLayoutFilePath is null)
        {
            throw new InvalidOperationException("A custom layout data folder is required before saving layouts.");
        }

        ValidateLayouts(layouts, requireAtLeastOne: false);
        JsonFileStorage.Save(_customLayoutFilePath, layouts, SerializerOptions);
    }

    public static IReadOnlyList<LayoutPreset> CombineLayouts(
        IReadOnlyList<LayoutPreset> builtInLayouts,
        IReadOnlyList<LayoutPreset> customLayouts)
    {
        var layouts = builtInLayouts
            .Concat(customLayouts)
            .ToArray();

        Validate(layouts);

        return layouts;
    }

    public static string CreateCustomLayoutId(string name, IReadOnlyCollection<LayoutPreset> existingLayouts)
    {
        var normalizedCharacters = name.Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();
        var baseId = string.Join(
            "_",
            new string(normalizedCharacters).Split('_', StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "custom_layout";
        }
        else if (!baseId.StartsWith("custom_layout_", StringComparison.OrdinalIgnoreCase))
        {
            baseId = $"custom_layout_{baseId}";
        }

        var existingIds = existingLayouts
            .Select(layout => layout.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingIds.Contains(baseId))
        {
            return baseId;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseId}_{suffix}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }
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
        ValidateLayouts(layouts, requireAtLeastOne: true);
    }

    private static void ValidateLayouts(IReadOnlyList<LayoutPreset> layouts, bool requireAtLeastOne)
    {
        if (layouts.Count == 0)
        {
            if (requireAtLeastOne)
            {
                throw new InvalidOperationException("At least one layout preset is required.");
            }

            return;
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

        ValidateWeights(layout.Id, "column", layout.ColumnWeights, layout.GridColumns);
        ValidateWeights(layout.Id, "row", layout.RowWeights, layout.GridRows);

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

    private static void ValidateWeights(
        string layoutId,
        string axisName,
        IReadOnlyList<double>? weights,
        int expectedCount)
    {
        if (weights is null || weights.Count == 0)
        {
            return;
        }

        if (weights.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Layout {layoutId} has {weights.Count} {axisName} weight(s), expected {expectedCount}.");
        }

        if (weights.Any(weight => double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0))
        {
            throw new InvalidOperationException($"Layout {layoutId} has invalid {axisName} weights.");
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

        if (LayoutSlotBoundsCalculator.HasExplicitBounds(slot))
        {
            if (!LayoutSlotBoundsCalculator.IsValidExplicitBounds(slot))
            {
                throw new InvalidOperationException($"Layout {layout.Id} slot {slot.SlotId} has invalid bounds.");
            }

            return;
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
