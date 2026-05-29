using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

/// <summary>
/// 카드형 레이아웃 선택을 위한 템플릿 후보 계산기.
/// 동적 레이아웃 계산을 대체하며, 후보는 오직 정적 템플릿 목록에서만 선택된다.
/// 규칙: candidates = templates.Where(t => t.SlotCount == currentVisibleSlotCount + 1)
/// </summary>
public sealed class LayoutTemplateCandidateService
{
    /// <summary>화면 추가용 후보: 현재 보이는 슬롯 수 + 1개짜리 템플릿.</summary>
    public IReadOnlyList<LayoutPreset> GetCandidates(
        IReadOnlyList<LayoutPreset> templates,
        int currentVisibleSlotCount)
    {
        return GetTemplatesForSlotCount(templates, currentVisibleSlotCount + 1);
    }

    /// <summary>지정한 슬롯 수와 정확히 일치하는 템플릿. 화면 제거(N-1) 시에도 동일 규칙을 재사용한다.</summary>
    public IReadOnlyList<LayoutPreset> GetTemplatesForSlotCount(
        IReadOnlyList<LayoutPreset> templates,
        int targetSlotCount)
    {
        if (templates.Count == 0 || targetSlotCount <= 0)
        {
            return [];
        }

        return templates
            .Where(template => template.EffectiveSlotCount == targetSlotCount)
            .OrderBy(template => template.EffectiveSlotCount)
            .ThenBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
