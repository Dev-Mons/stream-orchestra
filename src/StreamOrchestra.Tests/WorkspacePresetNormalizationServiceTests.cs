using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class WorkspacePresetNormalizationServiceTests
{
    private static readonly LayoutPreset[] Layouts =
    [
        new LayoutPreset
        {
            Id = "layout_4x4",
            Name = "4x4",
            GridColumns = 4,
            GridRows = 4,
            Slots = []
        },
        new LayoutPreset
        {
            Id = "layout_8_small_1_main",
            Name = "8 Small + 1 Main",
            GridColumns = 4,
            GridRows = 3,
            Slots = []
        }
    ];

    [Fact]
    public void Normalize_FillsMissingSlotsWithBlankAndSlotProfileGroups()
    {
        var service = new WorkspacePresetNormalizationService(new StreamNavigationService());
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = "layout_8_small_1_main",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 2,
                    StreamName = "Saved Stream",
                    StreamUrl = "example.com/stream",
                    Muted = true,
                    ProfileGroupId = "Wrong"
                }
            ]
        };

        var normalized = service.Normalize(workspace, Layouts);

        Assert.Equal("layout_8_small_1_main", normalized.LayoutId);
        Assert.Equal(16, normalized.Slots.Count);
        Assert.Contains(normalized.Slots, slot => slot.SlotId == 2 && slot.StreamName == "Saved Stream" && slot.StreamUrl == "https://example.com/stream" && slot.Muted && slot.ProfileGroupId == "A");
        Assert.Contains(normalized.Slots, slot => slot.SlotId == 9 && slot.StreamName == "Empty" && slot.StreamUrl == "about:blank" && !slot.Muted && slot.ProfileGroupId == "C");
        Assert.Contains(normalized.Slots, slot => slot.SlotId == 16 && slot.ProfileGroupId == "D");
    }

    [Fact]
    public void Normalize_PreservesVolumePercentAndClampsOutOfRange()
    {
        var service = new WorkspacePresetNormalizationService(new StreamNavigationService());
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = "layout_8_small_1_main",
            Slots =
            [
                new WorkspaceSlot { SlotId = 1, StreamUrl = "example.com/one", VolumePercent = 40 },
                new WorkspaceSlot { SlotId = 2, StreamUrl = "example.com/two", VolumePercent = 250 },
                new WorkspaceSlot { SlotId = 3, StreamUrl = "example.com/three", VolumePercent = -30 }
            ]
        };

        var normalized = service.Normalize(workspace, Layouts);

        Assert.Contains(normalized.Slots, slot => slot.SlotId == 1 && slot.VolumePercent == 40);
        Assert.Contains(normalized.Slots, slot => slot.SlotId == 2 && slot.VolumePercent == 100);
        Assert.Contains(normalized.Slots, slot => slot.SlotId == 3 && slot.VolumePercent == 0);
        // 저장된 값이 없는 슬롯은 기본 100%로 복원된다.
        Assert.Contains(normalized.Slots, slot => slot.SlotId == 9 && slot.VolumePercent == 100);
    }

    [Fact]
    public void Normalize_DropsOutOfRangeSlotsAndUsesLastDuplicateSlot()
    {
        var service = new WorkspacePresetNormalizationService(new StreamNavigationService());
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = "missing_layout",
            Slots =
            [
                new WorkspaceSlot { SlotId = 0, StreamUrl = "bad.example" },
                new WorkspaceSlot { SlotId = 2, StreamUrl = "first.example" },
                new WorkspaceSlot { SlotId = 2, StreamName = "Second", StreamUrl = "second.example", Muted = true },
                new WorkspaceSlot { SlotId = 17, StreamUrl = "bad.example" }
            ]
        };

        var normalized = service.Normalize(workspace, Layouts);

        Assert.Equal("layout_8_small_1_main", normalized.LayoutId);
        Assert.Equal(16, normalized.Slots.Count);
        Assert.Contains(normalized.Slots, slot => slot.SlotId == 2 && slot.StreamName == "Second" && slot.StreamUrl == "https://second.example" && slot.Muted);
        Assert.DoesNotContain(normalized.Slots, slot => slot.SlotId is 0 or 17);
    }

    [Fact]
    public void Normalize_DefaultsBlankIdNameLayoutAndNullUrl()
    {
        var service = new WorkspacePresetNormalizationService(new StreamNavigationService());
        var workspace = new WorkspacePreset
        {
            Id = " ",
            Name = " ",
            LayoutId = "",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamUrl = null!
                }
            ]
        };

        var normalized = service.Normalize(workspace, []);

        Assert.Equal("workspace_imported", normalized.Id);
        Assert.Equal("Imported Workspace", normalized.Name);
        Assert.Equal("layout_8_small_1_main", normalized.LayoutId);
        Assert.Contains(normalized.Slots, slot => slot.SlotId == 1 && slot.StreamName == "Empty" && slot.StreamUrl == "about:blank");
    }

    [Fact]
    public void Normalize_BlankUrlForcesEmptyStreamName()
    {
        var service = new WorkspacePresetNormalizationService(new StreamNavigationService());
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = "layout_8_small_1_main",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 3,
                    StreamName = "Stale Stream Name",
                    StreamUrl = "about:blank",
                    Muted = true
                },
                new WorkspaceSlot
                {
                    SlotId = 4,
                    StreamName = "Whitespace URL Name",
                    StreamUrl = " "
                }
            ]
        };

        var normalized = service.Normalize(workspace, Layouts);

        Assert.Contains(normalized.Slots, slot =>
            slot.SlotId == 3 &&
            slot.StreamName == "Empty" &&
            slot.StreamUrl == "about:blank" &&
            slot.Muted);
        Assert.Contains(normalized.Slots, slot =>
            slot.SlotId == 4 &&
            slot.StreamName == "Empty" &&
            slot.StreamUrl == "about:blank");
    }

    [Fact]
    public void Normalize_NonWebUrlForcesBlankStream()
    {
        var service = new WorkspacePresetNormalizationService(new StreamNavigationService());
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = "layout_8_small_1_main",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 6,
                    StreamName = "Stale Script Name",
                    StreamUrl = "javascript:alert(1)",
                    Muted = true
                },
                new WorkspaceSlot
                {
                    SlotId = 7,
                    StreamName = "Stale File Name",
                    StreamUrl = "file:///C:/Temp/test.html"
                },
                new WorkspaceSlot
                {
                    SlotId = 8,
                    StreamName = "Stale FTP Name",
                    StreamUrl = "ftp://example.com/stream"
                }
            ]
        };

        var normalized = service.Normalize(workspace, Layouts);

        Assert.Contains(normalized.Slots, slot =>
            slot.SlotId == 6 &&
            slot.StreamName == "Empty" &&
            slot.StreamUrl == "about:blank" &&
            slot.Muted);
        Assert.Contains(normalized.Slots, slot =>
            slot.SlotId == 7 &&
            slot.StreamName == "Empty" &&
            slot.StreamUrl == "about:blank");
        Assert.Contains(normalized.Slots, slot =>
            slot.SlotId == 8 &&
            slot.StreamName == "Empty" &&
            slot.StreamUrl == "about:blank");
    }

    [Fact]
    public void Normalize_TreatsNullSlotCollectionAsEmptySlots()
    {
        var service = new WorkspacePresetNormalizationService(new StreamNavigationService());
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = "layout_8_small_1_main",
            Slots = null!
        };

        var normalized = service.Normalize(workspace, Layouts);

        Assert.Equal(16, normalized.Slots.Count);
        Assert.All(normalized.Slots, slot => Assert.Equal("about:blank", slot.StreamUrl));
        Assert.Contains(normalized.Slots, slot =>
            slot.SlotId == 13 &&
            slot.StreamName == "Empty" &&
            slot.ProfileGroupId == "D");
    }

    [Fact]
    public void Normalize_IgnoresNullSlotEntries()
    {
        var service = new WorkspacePresetNormalizationService(new StreamNavigationService());
        var workspace = new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            LayoutId = "layout_8_small_1_main",
            Slots =
            [
                null!,
                new WorkspaceSlot
                {
                    SlotId = 5,
                    StreamName = "Five",
                    StreamUrl = "example.com/five",
                    Muted = true
                }
            ]
        };

        var normalized = service.Normalize(workspace, Layouts);

        Assert.Equal(16, normalized.Slots.Count);
        Assert.Contains(normalized.Slots, slot =>
            slot.SlotId == 5 &&
            slot.StreamName == "Five" &&
            slot.StreamUrl == "https://example.com/five" &&
            slot.Muted &&
            slot.ProfileGroupId == "B");
        Assert.Contains(normalized.Slots, slot =>
            slot.SlotId == 6 &&
            slot.StreamName == "Empty" &&
            slot.StreamUrl == "about:blank" &&
            slot.ProfileGroupId == "B");
    }
}
