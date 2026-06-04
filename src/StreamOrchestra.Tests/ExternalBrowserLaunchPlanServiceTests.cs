using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class ExternalBrowserLaunchPlanServiceTests
{
    [Fact]
    public void CreatePlan_ReturnsUnavailableWhenNoBrowserIsInstalled()
    {
        var service = new ExternalBrowserLaunchPlanService();

        var plan = service.CreatePlan(CreateWorkspace(), [], "C:\\Data");

        Assert.False(plan.CanLaunch);
        Assert.Equal(0, plan.InstalledBrowserCount);
        Assert.Empty(plan.Slots);
    }

    [Fact]
    public void CreatePlan_IgnoresNullAndIncompleteBrowserEntries()
    {
        var service = new ExternalBrowserLaunchPlanService();
        ExternalBrowserInfo[] browsers =
        {
            null!,
            new("missing_id", "Missing Executable", IsInstalled: true, ExecutablePath: " ", CandidatePaths: []),
            new(null!, "Missing Id", IsInstalled: true, ExecutablePath: "C:\\Browsers\\missing-id.exe", CandidatePaths: []),
            new("portable", " ", IsInstalled: true, ExecutablePath: " C:\\Browsers\\portable.exe ", CandidatePaths: [])
        };

        var plan = service.CreatePlan(CreateWorkspace(), browsers, "C:\\Data");

        Assert.True(plan.CanLaunch);
        Assert.Equal(1, plan.InstalledBrowserCount);
        Assert.All(plan.Slots, slot =>
        {
            Assert.Equal("portable", slot.BrowserId);
            Assert.Equal("portable", slot.BrowserName);
            Assert.Equal("C:\\Browsers\\portable.exe", slot.ExecutablePath);
        });
    }

    [Fact]
    public void CreatePlan_ReturnsUnavailableWhenNoActiveSlotsExist()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_blank",
            Name = "Blank",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Empty",
                    StreamUrl = "about:blank",
                    ProfileGroupId = "A"
                }
            ]
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        Assert.False(plan.CanLaunch);
        Assert.Equal(1, plan.InstalledBrowserCount);
        Assert.Empty(plan.Slots);
    }

    [Fact]
    public void CreatePlan_TreatsNullWorkspaceSlotsAsNoActiveSlots()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_malformed",
            Name = "Malformed",
            Slots = null!
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        Assert.False(plan.CanLaunch);
        Assert.Equal("No active stream URLs are available.", plan.Reason);
        Assert.Equal(1, plan.InstalledBrowserCount);
        Assert.Empty(plan.Slots);
    }

    [Fact]
    public void CreatePlan_IgnoresNullWorkspaceSlotEntries()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_malformed",
            Name = "Malformed",
            Slots =
            [
                null!,
                new WorkspaceSlot
                {
                    SlotId = 2,
                    StreamName = "Active",
                    StreamUrl = "https://example.com/active",
                    ProfileGroupId = "A"
                }
            ]
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        Assert.True(plan.CanLaunch);
        var slot = Assert.Single(plan.Slots);
        Assert.Equal(2, slot.SlotId);
        Assert.Equal("https://example.com/active", slot.StreamUrl);
    }

    [Fact]
    public void CreatePlan_IgnoresNullWhitespaceBlankMalformedAndNonWebStreamUrls()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_malformed",
            Name = "Malformed",
            Slots =
            [
                new WorkspaceSlot { SlotId = 1, StreamName = "Null", StreamUrl = null!, ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 2, StreamName = "Whitespace", StreamUrl = " ", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 3, StreamName = "Blank", StreamUrl = "about:blank", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 4, StreamName = "Malformed", StreamUrl = "not a url", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 5, StreamName = "Script", StreamUrl = "javascript:alert(1)", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 6, StreamName = "File", StreamUrl = "file:///C:/Temp/test.html", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 7, StreamName = "Ftp", StreamUrl = "ftp://example.com/stream", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 8, StreamName = "Active", StreamUrl = " https://example.com/8 ", ProfileGroupId = "A" }
            ]
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        Assert.True(plan.CanLaunch);
        var slot = Assert.Single(plan.Slots);
        Assert.Equal(8, slot.SlotId);
        Assert.Equal("https://example.com/8", slot.StreamUrl);
        Assert.Contains("https://example.com/8", slot.Arguments);
    }

    [Fact]
    public void CreatePlan_NormalizesBareDomainWorkspaceSlotUrls()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_bare_domain",
            Name = "Bare Domain",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 2,
                    StreamName = " ",
                    StreamUrl = " example.com/live ",
                    ProfileGroupId = "A"
                }
            ]
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        Assert.True(plan.CanLaunch);
        var slot = Assert.Single(plan.Slots);
        Assert.Equal(2, slot.SlotId);
        Assert.Equal("live", slot.StreamName);
        Assert.Equal("https://example.com/live", slot.StreamUrl);
        Assert.Contains("https://example.com/live", slot.Arguments);
    }

    [Fact]
    public void CreatePlan_IgnoresOutOfRangeWorkspaceSlots()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_out_of_range",
            Name = "Out Of Range",
            Slots =
            [
                new WorkspaceSlot { SlotId = 0, StreamName = "Zero", StreamUrl = "https://example.com/0", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 5, StreamName = "Five", StreamUrl = "https://example.com/5", ProfileGroupId = "B" },
                new WorkspaceSlot { SlotId = 17, StreamName = "Seventeen", StreamUrl = "https://example.com/17", ProfileGroupId = "D" }
            ]
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        Assert.True(plan.CanLaunch);
        var slot = Assert.Single(plan.Slots);
        Assert.Equal(5, slot.SlotId);
        Assert.Equal("https://example.com/5", slot.StreamUrl);
    }

    [Fact]
    public void CreatePlan_ReturnsUnavailableWhenOnlyOutOfRangeSlotsAreActive()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_out_of_range_only",
            Name = "Out Of Range Only",
            Slots =
            [
                new WorkspaceSlot { SlotId = 0, StreamName = "Zero", StreamUrl = "https://example.com/0", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 17, StreamName = "Seventeen", StreamUrl = "https://example.com/17", ProfileGroupId = "D" }
            ]
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        Assert.False(plan.CanLaunch);
        Assert.Equal("No active stream URLs are available.", plan.Reason);
        Assert.Empty(plan.Slots);
    }

    [Fact]
    public void CreatePlan_DerivesStreamNameWhenActiveSlotNameIsBlank()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_blank_name",
            Name = "Blank Name",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = " ",
                    StreamUrl = "https://example.com/streamer123",
                    ProfileGroupId = "A"
                }
            ]
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        var slot = Assert.Single(plan.Slots);
        Assert.Equal("streamer123", slot.StreamName);
    }

    [Fact]
    public void CreatePlan_ReturnsUnavailableWhenOnlyMalformedUrlsExist()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_malformed_only",
            Name = "Malformed Only",
            Slots =
            [
                new WorkspaceSlot { SlotId = 1, StreamName = "Malformed", StreamUrl = "not a url", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 2, StreamName = "Script", StreamUrl = "javascript:alert(1)", ProfileGroupId = "A" },
                new WorkspaceSlot { SlotId = 3, StreamName = "File", StreamUrl = "file:///C:/Temp/test.html", ProfileGroupId = "A" }
            ]
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        Assert.False(plan.CanLaunch);
        Assert.Equal("No active stream URLs are available.", plan.Reason);
        Assert.Empty(plan.Slots);
    }

    [Fact]
    public void CreatePlan_AssignsActiveSlotsRoundRobinAcrossInstalledBrowsers()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = CreateWorkspace();
        var browsers = new[]
        {
            CreateBrowser("edge", "Edge"),
            CreateBrowser("chrome", "Chrome")
        };

        var plan = service.CreatePlan(workspace, browsers, "C:\\Data");

        Assert.True(plan.CanLaunch);
        Assert.Equal(2, plan.InstalledBrowserCount);
        Assert.Equal(3, plan.PlannedSlotCount);
        Assert.Collection(
            plan.Slots,
            slot =>
            {
                Assert.Equal(1, slot.SlotId);
                Assert.Equal("chrome", slot.BrowserId);
                Assert.Equal("Stream 1", slot.StreamName);
                Assert.Contains("--new-window", slot.Arguments);
                Assert.Contains("https://example.com/1", slot.Arguments);
                Assert.Contains("ExternalBrowserProfiles\\chrome\\Slot1", slot.UserDataFolder);
            },
            slot =>
            {
                Assert.Equal(2, slot.SlotId);
                Assert.Equal("edge", slot.BrowserId);
                Assert.Contains("ExternalBrowserProfiles\\edge\\Slot2", slot.UserDataFolder);
            },
            slot =>
            {
                Assert.Equal(3, slot.SlotId);
                Assert.Equal("chrome", slot.BrowserId);
            });
    }

    [Fact]
    public void CreatePlan_IncludesWindowLayoutForMatchingWorkspaceLayout()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_default",
            Name = "Default",
            LayoutId = LayoutPresetIds.Default,
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 9,
                    StreamName = "Main",
                    StreamUrl = "https://example.com/main",
                    ProfileGroupId = "C"
                }
            ]
        };
        var layouts = new[]
        {
            new LayoutPreset
            {
                Id = LayoutPresetIds.Default,
                Name = "8 Small + 1 Main",
                GridColumns = 4,
                GridRows = 3,
                ColumnWeights = [1, 1, 2, 2],
                RowWeights = [1, 2, 2],
                Slots =
                [
                    new LayoutSlot { SlotId = 9, X = 2, Y = 1, W = 2, H = 2 }
                ]
            }
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data", layouts);

        var slot = Assert.Single(plan.Slots);
        Assert.NotNull(slot.WindowLayout);
        Assert.Equal(4, slot.WindowLayout.GridColumns);
        Assert.Equal(3, slot.WindowLayout.GridRows);
        Assert.Equal(2, slot.WindowLayout.X);
        Assert.Equal(1, slot.WindowLayout.Y);
        Assert.Equal(2, slot.WindowLayout.W);
        Assert.Equal(2, slot.WindowLayout.H);
        Assert.Equal([1, 1, 2, 2], slot.WindowLayout.ColumnWeights);
        Assert.Equal([1, 2, 2], slot.WindowLayout.RowWeights);
    }

    [Fact]
    public void CreatePlan_IncludesExplicitNormalizedWindowLayout()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_independent",
            Name = "Independent",
            LayoutId = "layout_independent",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 2,
                    StreamName = "Side",
                    StreamUrl = "https://example.com/side",
                    ProfileGroupId = "B"
                }
            ]
        };
        var layouts = new[]
        {
            new LayoutPreset
            {
                Id = "layout_independent",
                Name = "Independent",
                GridColumns = 1,
                GridRows = 1,
                Slots =
                [
                    new LayoutSlot
                    {
                        SlotId = 2,
                        X = 0,
                        Y = 0,
                        W = 1,
                        H = 1,
                        Left = 0.42,
                        Top = 0.16,
                        Width = 0.33,
                        Height = 0.71
                    }
                ]
            }
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data", layouts);

        var slot = Assert.Single(plan.Slots);
        Assert.NotNull(slot.WindowLayout);
        Assert.Equal(0.42, slot.WindowLayout.LeftRatio);
        Assert.Equal(0.16, slot.WindowLayout.TopRatio);
        Assert.Equal(0.33, slot.WindowLayout.WidthRatio);
        Assert.Equal(0.71, slot.WindowLayout.HeightRatio);
    }

    [Fact]
    public void CreatePlan_ToleratesMalformedLayoutEntries()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_default",
            Name = "Default",
            LayoutId = LayoutPresetIds.Default,
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 9,
                    StreamName = "Main",
                    StreamUrl = "https://example.com/main",
                    ProfileGroupId = "C"
                }
            ]
        };
        LayoutPreset[] layouts =
        [
            null!,
            new LayoutPreset
            {
                Id = LayoutPresetIds.Default,
                Name = "8 Small + 1 Main",
                GridColumns = 4,
                GridRows = 3,
                Slots =
                [
                    null!,
                    new LayoutSlot { SlotId = 0, X = 0, Y = 0, W = 1, H = 1 },
                    new LayoutSlot { SlotId = 9, X = 5, Y = 1, W = 1, H = 1 },
                    new LayoutSlot { SlotId = 9, X = 2, Y = 1, W = 2, H = 2 }
                ]
            }
        ];

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data", layouts);

        var slot = Assert.Single(plan.Slots);
        Assert.NotNull(slot.WindowLayout);
        Assert.Equal(2, slot.WindowLayout.X);
        Assert.Equal(1, slot.WindowLayout.Y);
        Assert.Equal(2, slot.WindowLayout.W);
        Assert.Equal(2, slot.WindowLayout.H);
    }

    [Fact]
    public void CreatePlan_IgnoresInvalidLayoutInsteadOfFailing()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_default",
            Name = "Default",
            LayoutId = LayoutPresetIds.Default,
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Visible",
                    StreamUrl = "https://example.com/visible",
                    ProfileGroupId = "A"
                }
            ]
        };
        var layouts = new[]
        {
            new LayoutPreset
            {
                Id = LayoutPresetIds.Default,
                Name = "Invalid",
                GridColumns = 0,
                GridRows = 3,
                Slots =
                [
                    new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 }
                ]
            }
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data", layouts);

        var slot = Assert.Single(plan.Slots);
        Assert.Equal(1, slot.SlotId);
        Assert.Null(slot.WindowLayout);
    }

    [Fact]
    public void CreatePlan_IgnoresActiveSlotsOutsideMatchingLayout()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_default",
            Name = "Default",
            LayoutId = LayoutPresetIds.Default,
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Visible",
                    StreamUrl = "https://example.com/visible",
                    ProfileGroupId = "A"
                },
                new WorkspaceSlot
                {
                    SlotId = 10,
                    StreamName = "Hidden",
                    StreamUrl = "https://example.com/hidden",
                    ProfileGroupId = "C"
                }
            ]
        };
        var layouts = new[]
        {
            new LayoutPreset
            {
                Id = LayoutPresetIds.Default,
                Name = "Default",
                GridColumns = 4,
                GridRows = 3,
                Slots =
                [
                    new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 }
                ]
            }
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data", layouts);

        var slot = Assert.Single(plan.Slots);
        Assert.Equal(1, slot.SlotId);
        Assert.Equal("https://example.com/visible", slot.StreamUrl);
    }

    [Fact]
    public void CreatePlan_UsesDefaultLayoutWhenWorkspaceLayoutIdIsMissing()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_missing_layout",
            Name = "Missing Layout",
            LayoutId = null!,
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Visible",
                    StreamUrl = "https://example.com/visible",
                    ProfileGroupId = "A"
                },
                new WorkspaceSlot
                {
                    SlotId = 10,
                    StreamName = "Hidden",
                    StreamUrl = "https://example.com/hidden",
                    ProfileGroupId = "C"
                }
            ]
        };
        var layouts = new[]
        {
            new LayoutPreset
            {
                Id = LayoutPresetIds.Default,
                Name = "Default",
                GridColumns = 4,
                GridRows = 3,
                Slots =
                [
                    new LayoutSlot { SlotId = 1, X = 0, Y = 0, W = 1, H = 1 }
                ]
            },
            new LayoutPreset
            {
                Id = LayoutPresetIds.Tournament,
                Name = "Tournament",
                GridColumns = 4,
                GridRows = 4,
                Slots =
                [
                    new LayoutSlot { SlotId = 10, X = 1, Y = 2, W = 1, H = 1 }
                ]
            }
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data", layouts);

        var slot = Assert.Single(plan.Slots);
        Assert.Equal(1, slot.SlotId);
        Assert.Equal("https://example.com/visible", slot.StreamUrl);
    }

    [Fact]
    public void CreatePlan_IncludesMuteArgumentForMutedSlots()
    {
        var service = new ExternalBrowserLaunchPlanService();
        var workspace = new WorkspacePreset
        {
            Id = "workspace_muted",
            Name = "Muted",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Muted Stream",
                    StreamUrl = "https://example.com/muted",
                    Muted = true,
                    ProfileGroupId = "A"
                }
            ]
        };

        var plan = service.CreatePlan(workspace, [CreateBrowser("edge", "Edge")], "C:\\Data");

        var slot = Assert.Single(plan.Slots);
        Assert.True(slot.IsMuted);
        Assert.Contains("--mute-audio", slot.Arguments);
        Assert.Contains("https://example.com/muted", slot.Arguments);
    }

    private static WorkspacePreset CreateWorkspace()
    {
        return new WorkspacePreset
        {
            Id = "workspace_test",
            Name = "Test",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Stream 1",
                    StreamUrl = "https://example.com/1",
                    ProfileGroupId = "A"
                },
                new WorkspaceSlot
                {
                    SlotId = 2,
                    StreamName = "Stream 2",
                    StreamUrl = "https://example.com/2",
                    ProfileGroupId = "A"
                },
                new WorkspaceSlot
                {
                    SlotId = 3,
                    StreamName = "Stream 3",
                    StreamUrl = "https://example.com/3",
                    ProfileGroupId = "A"
                },
                new WorkspaceSlot
                {
                    SlotId = 4,
                    StreamName = "Empty",
                    StreamUrl = "about:blank",
                    ProfileGroupId = "A"
                }
            ]
        };
    }

    private static ExternalBrowserInfo CreateBrowser(string id, string name)
    {
        return new ExternalBrowserInfo(
            id,
            name,
            IsInstalled: true,
            ExecutablePath: $"C:\\Browsers\\{id}.exe",
            CandidatePaths: [$"C:\\Browsers\\{id}.exe"]);
    }
}
