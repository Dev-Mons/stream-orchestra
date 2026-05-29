using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class LayoutTemplateCandidateServiceTests
{
    [Fact]
    public void GetCandidates_WithFourVisibleSlots_ReturnsOnlyFiveSlotTemplates()
    {
        var service = new LayoutTemplateCandidateService();
        var templates = CreateTemplates();

        var candidates = service.GetCandidates(templates, currentVisibleSlotCount: 4);

        Assert.NotEmpty(candidates);
        Assert.All(candidates, template => Assert.Equal(5, template.EffectiveSlotCount));
        Assert.DoesNotContain(candidates, template => template.EffectiveSlotCount != 5);
    }

    [Fact]
    public void GetCandidates_UsesSlotCountFieldOverSlotCollectionSize()
    {
        var service = new LayoutTemplateCandidateService();
        var templates = new[]
        {
            // SlotCount(=3)가 명시되면 Slots.Count(=1)보다 우선한다.
            new LayoutPreset
            {
                Id = "explicit_3",
                Name = "Explicit 3",
                SlotCount = 3,
                GridColumns = 3,
                GridRows = 1,
                Slots = [new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 }]
            }
        };

        var candidates = service.GetCandidates(templates, currentVisibleSlotCount: 2);

        Assert.Single(candidates);
        Assert.Equal("explicit_3", candidates[0].Id);
    }

    [Fact]
    public void GetCandidates_FallsBackToSlotCollectionSizeWhenSlotCountMissing()
    {
        var service = new LayoutTemplateCandidateService();
        var templates = new[]
        {
            new LayoutPreset
            {
                Id = "implicit_2",
                Name = "Implicit 2",
                GridColumns = 2,
                GridRows = 1,
                Slots =
                [
                    new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 },
                    new LayoutSlot { SlotId = 2, X = 1, Y = 0, W = 1, H = 1 }
                ]
            }
        };

        var candidates = service.GetCandidates(templates, currentVisibleSlotCount: 1);

        Assert.Single(candidates);
        Assert.Equal("implicit_2", candidates[0].Id);
    }

    [Fact]
    public void GetCandidates_ReturnsEmptyWhenNoTemplateMatchesNextSlotCount()
    {
        var service = new LayoutTemplateCandidateService();
        var templates = CreateTemplates();

        // 9개 화면 → 10슬롯 템플릿이 없으므로 후보 0개.
        var candidates = service.GetCandidates(templates, currentVisibleSlotCount: 9);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GetCandidates_ReturnsEmptyForEmptyTemplateList()
    {
        var service = new LayoutTemplateCandidateService();

        Assert.Empty(service.GetCandidates([], currentVisibleSlotCount: 3));
    }

    [Fact]
    public void GetTemplatesForSlotCount_ReturnsExactMatches_UsedForRemovalToPreviousCount()
    {
        var service = new LayoutTemplateCandidateService();
        var templates = CreateTemplates();

        // 5개 화면에서 한 개 제거 → 4슬롯 템플릿(N-1)을 찾는다.
        var previous = service.GetTemplatesForSlotCount(templates, 4);
        Assert.Single(previous);
        Assert.Equal("layout_4", previous[0].Id);
    }

    [Fact]
    public void GetTemplatesForSlotCount_ReturnsEmptyWhenExactCountMissing()
    {
        var service = new LayoutTemplateCandidateService();
        var templates = CreateTemplates();

        // 9 → 8슬롯 템플릿이 없으면 빈 결과(제거 버튼 비활성 처리에 사용).
        Assert.Empty(service.GetTemplatesForSlotCount(templates, 8));
        Assert.Empty(service.GetTemplatesForSlotCount(templates, 0));
    }

    private static LayoutPreset[] CreateTemplates()
    {
        return new[]
        {
            CreateTemplate("layout_4", 4),
            CreateTemplate("layout_5", 5),
            CreateTemplate("layout_5b", 5),
            CreateTemplate("layout_6", 6),
            CreateTemplate("layout_9", 9)
        };
    }

    private static LayoutPreset CreateTemplate(string id, int slotCount)
    {
        return new LayoutPreset
        {
            Id = id,
            Name = id,
            SlotCount = slotCount,
            GridColumns = Math.Max(1, slotCount),
            GridRows = 1,
            Slots = Enumerable.Range(1, slotCount)
                .Select(slotId => new LayoutSlot { SlotId = slotId, X = slotId - 1, Y = 0, W = 1, H = 1 })
                .ToArray()
        };
    }
}
