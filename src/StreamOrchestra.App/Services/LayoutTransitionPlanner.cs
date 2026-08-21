using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

/// <summary>
/// 현재 방송 상태를 목표 레이아웃에 배치하는 순수 계획기.
/// 축소할 때는 선택 방송을 가장 먼저 남기고, 나머지는 현재 화면의 공간 순서를 따른다.
/// 동일 크기·확대 전환에서는 가능한 한 같은 SlotId를 유지해 WebView 재탐색을 피한다.
/// </summary>
public sealed class LayoutTransitionPlanner
{
    public LayoutTransitionPlan CreatePlan(
        LayoutPreset currentLayout,
        LayoutPreset targetLayout,
        IReadOnlyCollection<SlotRuntimeState> currentSlots,
        int? preferredSlotId)
    {
        ArgumentNullException.ThrowIfNull(currentLayout);
        ArgumentNullException.ThrowIfNull(targetLayout);
        ArgumentNullException.ThrowIfNull(currentSlots);

        var statesById = currentSlots
            .GroupBy(slot => slot.SlotId)
            .ToDictionary(group => group.Key, group => group.First());
        var playingSources = OrderSlotsSpatially(currentLayout)
            .Where(slot => statesById.TryGetValue(slot.SlotId, out var state) && !IsBlank(state.StreamUrl))
            .Select(slot => statesById[slot.SlotId])
            .ToArray();
        var targetSlots = OrderSlotsSpatially(targetLayout).ToArray();
        var preferredSource = preferredSlotId is { } requested
            ? playingSources.FirstOrDefault(source => source.SlotId == requested)
            : null;

        var survivorPriority = preferredSource is null
            ? playingSources
            : [preferredSource, .. playingSources.Where(source => source.SlotId != preferredSource.SlotId)];
        var survivors = survivorPriority
            .Take(targetSlots.Length)
            .ToArray();
        var closedSourceSlotIds = playingSources
            .Where(source => survivors.All(survivor => survivor.SlotId != source.SlotId))
            .Select(source => source.SlotId)
            .ToArray();

        var assignments = playingSources.Length > targetSlots.Length
            ? AssignForShrink(targetSlots, survivors, preferredSource)
            : AssignPreservingSlotIdentity(targetSlots, survivors);

        return new LayoutTransitionPlan(
            targetLayout,
            assignments,
            closedSourceSlotIds,
            preferredSource?.SlotId);
    }

    private static IReadOnlyList<LayoutTransitionAssignment> AssignForShrink(
        IReadOnlyList<LayoutSlot> targetSlots,
        IReadOnlyList<SlotRuntimeState> survivors,
        SlotRuntimeState? preferredSource)
    {
        var assignments = new Dictionary<int, SlotRuntimeState>();
        var assignedSourceIds = new HashSet<int>();

        // 축소의 첫 영역은 사용자가 명시적으로 선택한 방송에 예약한다.
        if (preferredSource is not null && targetSlots.Count > 0)
        {
            assignments[targetSlots[0].SlotId] = preferredSource;
            assignedSourceIds.Add(preferredSource.SlotId);
        }

        // 나머지는 동일 SlotId를 우선 유지하여 불필요한 WebView 교체를 줄인다.
        foreach (var targetSlot in targetSlots)
        {
            if (assignments.ContainsKey(targetSlot.SlotId))
            {
                continue;
            }

            var sameSlotSource = survivors.FirstOrDefault(source =>
                source.SlotId == targetSlot.SlotId && !assignedSourceIds.Contains(source.SlotId));
            if (sameSlotSource is null)
            {
                continue;
            }

            assignments[targetSlot.SlotId] = sameSlotSource;
            assignedSourceIds.Add(sameSlotSource.SlotId);
        }

        FillRemainingAssignments(targetSlots, survivors, assignments, assignedSourceIds);
        return CreateAssignments(targetSlots, assignments);
    }

    private static IReadOnlyList<LayoutTransitionAssignment> AssignPreservingSlotIdentity(
        IReadOnlyList<LayoutSlot> targetSlots,
        IReadOnlyList<SlotRuntimeState> survivors)
    {
        var assignments = new Dictionary<int, SlotRuntimeState>();
        var assignedSourceIds = new HashSet<int>();

        foreach (var targetSlot in targetSlots)
        {
            var sameSlotSource = survivors.FirstOrDefault(source => source.SlotId == targetSlot.SlotId);
            if (sameSlotSource is null)
            {
                continue;
            }

            assignments[targetSlot.SlotId] = sameSlotSource;
            assignedSourceIds.Add(sameSlotSource.SlotId);
        }

        FillRemainingAssignments(targetSlots, survivors, assignments, assignedSourceIds);
        return CreateAssignments(targetSlots, assignments);
    }

    private static void FillRemainingAssignments(
        IReadOnlyList<LayoutSlot> targetSlots,
        IReadOnlyList<SlotRuntimeState> survivors,
        IDictionary<int, SlotRuntimeState> assignments,
        ISet<int> assignedSourceIds)
    {
        var remainingSources = new Queue<SlotRuntimeState>(
            survivors.Where(source => !assignedSourceIds.Contains(source.SlotId)));

        foreach (var targetSlot in targetSlots.Where(slot => !assignments.ContainsKey(slot.SlotId)))
        {
            if (remainingSources.Count == 0)
            {
                break;
            }

            var source = remainingSources.Dequeue();
            assignments[targetSlot.SlotId] = source;
            assignedSourceIds.Add(source.SlotId);
        }
    }

    private static IReadOnlyList<LayoutTransitionAssignment> CreateAssignments(
        IReadOnlyList<LayoutSlot> targetSlots,
        IReadOnlyDictionary<int, SlotRuntimeState> sourcesByTargetSlotId)
    {
        return targetSlots
            .Select(targetSlot => sourcesByTargetSlotId.TryGetValue(targetSlot.SlotId, out var source)
                ? new LayoutTransitionAssignment(
                    targetSlot.SlotId,
                    source.SlotId,
                    source.StreamName,
                    source.StreamUrl)
                : new LayoutTransitionAssignment(
                    targetSlot.SlotId,
                    null,
                    string.Empty,
                    "about:blank"))
            .ToArray();
    }

    private static IEnumerable<LayoutSlot> OrderSlotsSpatially(LayoutPreset layout)
    {
        return layout.Slots
            .Select(slot => (Slot: slot, Bounds: LayoutSlotBoundsCalculator.GetBounds(layout, slot)))
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .ThenBy(item => item.Slot.SlotId)
            .Select(item => item.Slot);
    }

    private static bool IsBlank(string? streamUrl) =>
        string.IsNullOrWhiteSpace(streamUrl) ||
        streamUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
}
