namespace StreamOrchestra.App.Models;

public sealed class LayoutPreset
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public int GridColumns { get; init; }

    public int GridRows { get; init; }

    /// <summary>
    /// 이 템플릿이 채우는 레이아웃 슬롯(영상 화면) 개수. 카드 후보 계산의 기준 필드.
    /// 값이 지정되지 않으면 <see cref="Slots"/> 개수를 사용한다.
    /// </summary>
    public int SlotCount { get; init; }

    public IReadOnlyList<double> ColumnWeights { get; init; } = [];

    public IReadOnlyList<double> RowWeights { get; init; } = [];

    public IReadOnlyList<LayoutSlot> Slots { get; init; } = [];

    /// <summary>
    /// 명시적 <see cref="SlotCount"/>가 있으면 그 값을, 없으면 슬롯 개수를 반환한다.
    /// </summary>
    public int EffectiveSlotCount => SlotCount > 0 ? SlotCount : Slots.Count;
}
