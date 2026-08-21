namespace StreamOrchestra.App.Models;

/// <summary>레이아웃의 한 대상 슬롯에 배치할 방송 상태.</summary>
public sealed record LayoutTransitionAssignment(
    int TargetSlotId,
    int? SourceSlotId,
    string StreamName,
    string StreamUrl);

/// <summary>
/// 직접 레이아웃 전환 전에 계산한 불변 계획. UI 변경 전에 유지·이동·종료 대상을 확정해
/// 전환 도중 선택 상태나 슬롯 상태가 바뀌어도 일관된 결과를 적용한다.
/// </summary>
public sealed record LayoutTransitionPlan(
    LayoutPreset TargetLayout,
    IReadOnlyList<LayoutTransitionAssignment> Assignments,
    IReadOnlyList<int> ClosedSourceSlotIds,
    int? PreferredSourceSlotId)
{
    public int TargetSlotCount => Assignments.Count;

    public int RetainedStreamCount => Assignments.Count(assignment => assignment.SourceSlotId is not null);

    public int ClosedStreamCount => ClosedSourceSlotIds.Count;

    public int? PreferredTargetSlotId => Assignments
        .FirstOrDefault(assignment => assignment.SourceSlotId == PreferredSourceSlotId)
        ?.TargetSlotId;
}
