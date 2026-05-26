using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class DiagnosticReportServiceTests : IDisposable
{
    private readonly string _rootFolder;
    private readonly string _profileFolder;
    private readonly string _dataFolder;

    public DiagnosticReportServiceTests()
    {
        _rootFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
        _profileFolder = Path.Combine(_rootFolder, "Profiles");
        _dataFolder = Path.Combine(_rootFolder, "Data");
        Directory.CreateDirectory(_rootFolder);
    }

    [Fact]
    public void CreateReport_IncludesProfilesDataFilesLatestResultAndDecision()
    {
        var browserPath = Path.Combine(_rootFolder, "msedge.exe");
        File.WriteAllText(browserPath, "");
        var profileService = new WebViewProfileService(_profileFolder);
        var presetStorage = new PresetStorageService(_dataFolder);
        var favoriteStorage = new FavoriteStorageService(_dataFolder);
        var feasibilityStorage = new FeasibilityResultStorageService(_dataFolder);
        presetStorage.SaveWorkspaces(
        [
            new WorkspacePreset
            {
                Id = "workspace_weekday",
                Name = "Weekday",
                LayoutId = LayoutPresetIds.Default,
                Slots =
                [
                    new WorkspaceSlot
                    {
                        SlotId = 1,
                        StreamName = "Preset Stream",
                        StreamUrl = "https://example.com/preset",
                        ProfileGroupId = "A"
                    }
                ]
            }
        ]);
        presetStorage.SaveAppState(new AppState
        {
            LastWorkspaceId = "workspace_weekday",
            SelectedSlotId = 2,
            LastSession = new WorkspacePreset
            {
                Id = "last_session",
                Name = "Last Session",
                LayoutId = LayoutPresetIds.Default,
                Slots =
                [
                    new WorkspaceSlot
                    {
                        SlotId = 1,
                        StreamName = "Active Stream",
                        StreamUrl = "https://example.com/live",
                        ProfileGroupId = "A"
                    },
                    new WorkspaceSlot
                    {
                        SlotId = 2,
                        StreamName = "Blank",
                        StreamUrl = "about:blank",
                        ProfileGroupId = "A"
                    }
                ]
            }
        });
        favoriteStorage.SaveFavorites(
        [
            new StreamEntry
            {
                Id = "favorite_stream",
                Name = "Favorite Stream",
                Url = "https://example.com/favorite"
            }
        ]);
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        feasibilityStorage.AppendResult(new FeasibilityTestResult
        {
            Id = "result_1",
            CapturedAt = capturedAt,
            PlaybackCount = 9,
            ScenarioId = "groups_a_b_c_9_slot_threshold",
            ScenarioName = "Groups A/B/C, 9-slot success threshold",
            Outcome = "success",
            Diagnostics = new RuntimeDiagnosticsSnapshot(
                capturedAt,
                WebViewProcessCount: 9,
                WebViewWorkingSetMegabytes: 512,
                WebViewPrivateMemoryMegabytes: 400,
                WebViewCpuPercent: 25),
            IsSameAccountSessionMaintained = true,
            AccountLabel = "main_soop",
            VerifiedProfileGroups = ["A", "B", "C"],
            IsRestartSessionMaintained = true,
            IsResourceUsageAcceptable = true,
            ObservedCpuPercent = 45.5,
            ObservedGpuPercent = 60,
            ObservedMemoryMegabytes = 12000
        });
        var decision = new FeasibilityDecision(
            "continue_webview2_experiments",
            "WebView2 추가 실험",
            "9개 재생은 가능하지만 A-D 계정 증거가 아직 완성되지 않았습니다.");
        var service = new DiagnosticReportService(new ExternalBrowserDiscoveryService(
        [
            new ExternalBrowserCandidate("edge", "Microsoft Edge", [browserPath])
        ]));
        var fallbackWorkspace = new WorkspacePreset
        {
            Id = "workspace_fallback",
            Name = "Fallback",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Stream 1",
                    StreamUrl = "https://example.com/1",
                    ProfileGroupId = "A"
                }
            ]
        };

        var report = service.CreateReport(
            profileService,
            presetStorage,
            favoriteStorage,
            feasibilityStorage,
            decision,
            fallbackWorkspace);

        Assert.Equal(_profileFolder, report.ProfileRootFolder);
        Assert.Contains(report.ProfileGroups, group => group.Id == "A");
        Assert.Contains(report.ProfileGroups, group => group.Id == "Explorer");
        Assert.Equal(_dataFolder, report.DataFolder);
        Assert.Equal(1, report.WorkspaceDiagnostics.SavedWorkspaceCount);
        Assert.Equal(1, report.WorkspaceDiagnostics.FavoriteCount);
        Assert.True(report.WorkspaceDiagnostics.HasLastSession);
        Assert.Equal("workspace_weekday", report.WorkspaceDiagnostics.LastWorkspaceId);
        Assert.Equal(2, report.WorkspaceDiagnostics.SelectedSlotId);
        Assert.Equal(LayoutPresetIds.Default, report.WorkspaceDiagnostics.LastSessionLayoutId);
        Assert.Equal(2, report.WorkspaceDiagnostics.LastSessionSlotCount);
        Assert.Equal(1, report.WorkspaceDiagnostics.LastSessionActiveStreamCount);
        Assert.Contains(report.DataFiles, file => file.Name == "appstate" && file.Exists && file.SizeBytes > 0);
        Assert.Contains(report.DataFiles, file => file.Name == "workspaces" && file.Exists && file.SizeBytes > 0);
        Assert.Contains(report.DataFiles, file => file.Name == "favorites" && file.Exists && file.SizeBytes > 0);
        Assert.Contains(report.DataFiles, file => file.Name == "feasibility-results" && file.Exists && file.SizeBytes > 0);
        Assert.Contains(report.ExternalBrowsers, browser => browser.Id == "edge" && browser.IsInstalled && browser.ExecutablePath == browserPath);
        Assert.NotNull(report.ExternalBrowserFallbackPlan);
        Assert.True(report.ExternalBrowserFallbackPlan.CanLaunch);
        Assert.Single(report.ExternalBrowserFallbackPlan.Slots);
        Assert.Equal(1, report.FeasibilityResultCount);
        Assert.Equal("result_1", report.LatestFeasibilityResult?.Id);
        Assert.Equal(["main_soop"], report.FeasibilitySameAccountLabels);
        Assert.False(report.HasConflictingFeasibilityAccountLabels);
        Assert.Equal("continue_webview2_experiments", report.FeasibilityDecision.Code);
        Assert.Contains(report.FeasibilityAudit, item => item.Id == "phase0_success_gate" && item.Status == "pending");
        Assert.Contains(report.FeasibilityAudit, item => item.Id == "nine_plus_playback" && item.Status == "pass");
        Assert.Contains(report.FeasibilitySuggestedRecordShapes, suggestion =>
            suggestion.Contains("record --count 8 --outcome partial --account --profile-groups A,B"));
    }

    [Fact]
    public void CreateReport_UsesCustomExternalBrowserCandidatesFromDataFolder()
    {
        var executableFolder = Path.Combine(_rootFolder, "PortableBrowser");
        Directory.CreateDirectory(executableFolder);
        var executablePath = Path.Combine(executableFolder, "browser.exe");
        File.WriteAllText(executablePath, "");
        new ExternalBrowserCandidateStorageService(_dataFolder).SaveCandidates(
        [
            new ExternalBrowserCandidate(
                "portable_browser",
                "Portable Browser",
                [executablePath])
        ]);
        var profileService = new WebViewProfileService(_profileFolder);
        var presetStorage = new PresetStorageService(_dataFolder);
        var favoriteStorage = new FavoriteStorageService(_dataFolder);
        var feasibilityStorage = new FeasibilityResultStorageService(_dataFolder);
        var service = new DiagnosticReportService();

        var report = service.CreateReport(
            profileService,
            presetStorage,
            favoriteStorage,
            feasibilityStorage,
            new FeasibilityDecision("pending", "Pending", "Pending"));

        Assert.Contains(report.ExternalBrowsers, browser => browser.Id == "portable_browser" && browser.IsInstalled);
        Assert.Contains(report.DataFiles, file => file.Name == "external-browsers" && file.Exists && file.SizeBytes > 0);
    }

    [Fact]
    public void CreateReport_SummarizesConflictingSameAccountLabels()
    {
        var profileService = new WebViewProfileService(_profileFolder);
        var presetStorage = new PresetStorageService(_dataFolder);
        var favoriteStorage = new FavoriteStorageService(_dataFolder);
        var feasibilityStorage = new FeasibilityResultStorageService(_dataFolder);
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        feasibilityStorage.SaveResults(
        [
            CreateAccountResult(
                "result_abc",
                capturedAt,
                playbackCount: 9,
                scenarioId: "groups_a_b_c_9_slot_threshold",
                profileGroups: ["A", "B", "C"],
                accountLabel: "main_soop"),
            CreateAccountResult(
                "result_d",
                capturedAt.AddMinutes(15),
                playbackCount: 4,
                scenarioId: "isolated_group_d",
                profileGroups: ["D"],
                accountLabel: "alt_soop")
        ]);
        var decision = new FeasibilityDecision("continue_webview2_experiments", "실험", "계정 라벨 충돌");

        var report = new DiagnosticReportService().CreateReport(
            profileService,
            presetStorage,
            favoriteStorage,
            feasibilityStorage,
            decision);

        Assert.Equal(["alt_soop", "main_soop"], report.FeasibilitySameAccountLabels);
        Assert.True(report.HasConflictingFeasibilityAccountLabels);
        Assert.Contains(report.FeasibilityAudit, item =>
            item.Id == "same_account_session" &&
            item.Status == "fail" &&
            item.Evidence.Contains("conflicting account labels"));
    }

    [Fact]
    public void CreateReport_ToleratesMalformedLastSessionSlots()
    {
        var profileService = new WebViewProfileService(_profileFolder);
        var presetStorage = new PresetStorageService(_dataFolder);
        var favoriteStorage = new FavoriteStorageService(_dataFolder);
        var feasibilityStorage = new FeasibilityResultStorageService(_dataFolder);
        presetStorage.SaveAppState(new AppState
        {
            LastWorkspaceId = "workspace_malformed",
            LastSession = new WorkspacePreset
            {
                Id = "last_session",
                Name = "Last Session",
                LayoutId = LayoutPresetIds.Default,
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
            }
        });
        var service = new DiagnosticReportService();

        var report = service.CreateReport(
            profileService,
            presetStorage,
            favoriteStorage,
            feasibilityStorage,
            new FeasibilityDecision("pending", "Pending", "Pending"));

        Assert.True(report.WorkspaceDiagnostics.HasLastSession);
        Assert.Equal(1, report.WorkspaceDiagnostics.LastSessionSlotCount);
        Assert.Equal(1, report.WorkspaceDiagnostics.LastSessionActiveStreamCount);
    }

    [Fact]
    public void CreateReport_TreatsNullLastSessionSlotsAsEmpty()
    {
        var profileService = new WebViewProfileService(_profileFolder);
        var presetStorage = new PresetStorageService(_dataFolder);
        var favoriteStorage = new FavoriteStorageService(_dataFolder);
        var feasibilityStorage = new FeasibilityResultStorageService(_dataFolder);
        presetStorage.SaveAppState(new AppState
        {
            LastWorkspaceId = "workspace_malformed",
            LastSession = new WorkspacePreset
            {
                Id = "last_session",
                Name = "Last Session",
                LayoutId = LayoutPresetIds.Default,
                Slots = null!
            }
        });
        var service = new DiagnosticReportService();

        var report = service.CreateReport(
            profileService,
            presetStorage,
            favoriteStorage,
            feasibilityStorage,
            new FeasibilityDecision("pending", "Pending", "Pending"));

        Assert.True(report.WorkspaceDiagnostics.HasLastSession);
        Assert.Equal(0, report.WorkspaceDiagnostics.LastSessionSlotCount);
        Assert.Equal(0, report.WorkspaceDiagnostics.LastSessionActiveStreamCount);
    }

    [Fact]
    public void SaveReport_WritesTimestampedJsonFile()
    {
        var service = new DiagnosticReportService();
        var generatedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        var report = new DiagnosticReport
        {
            GeneratedAt = generatedAt,
            DataFolder = _dataFolder,
            ProfileRootFolder = _profileFolder
        };

        var path = service.SaveReport(report, _dataFolder);

        Assert.Equal(Path.Combine(_dataFolder, "diagnostic-report-20260526-120000.json"), path);
        Assert.True(File.Exists(path));
        var reportText = File.ReadAllText(path);
        Assert.Contains("profileRootFolder", reportText);
        Assert.Contains("feasibilitySuggestedRecordShapes", reportText);
    }

    [Fact]
    public void SaveExternalBrowserFallbackScript_WritesScriptWhenReportHasLaunchPlan()
    {
        var generatedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        var report = new DiagnosticReport
        {
            GeneratedAt = generatedAt,
            ExternalBrowserFallbackPlan = new ExternalBrowserFallbackPlan(
                true,
                "Prepared 1 browser launch plan(s).",
                1,
                1,
                [
                    new ExternalBrowserSlotLaunchPlan(
                        1,
                        "Stream 1",
                        "https://example.com/1",
                        "edge",
                        "Edge",
                        "C:\\Browsers\\edge.exe",
                        "C:\\Data\\Slot1",
                        ["--user-data-dir=C:\\Data\\Slot1", "--new-window", "https://example.com/1"])
                ])
        };
        var service = new DiagnosticReportService();

        var path = service.SaveExternalBrowserFallbackScript(report, _dataFolder);

        Assert.Equal(Path.Combine(_dataFolder, "external-browser-fallback-20260526-120000.ps1"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveExternalBrowserFallbackScript_ReturnsNullWhenReportHasNoLaunchablePlan()
    {
        var service = new DiagnosticReportService();

        var path = service.SaveExternalBrowserFallbackScript(new DiagnosticReport(), _dataFolder);

        Assert.Null(path);
    }

    private static FeasibilityTestResult CreateAccountResult(
        string id,
        DateTimeOffset capturedAt,
        int playbackCount,
        string scenarioId,
        IReadOnlyList<string> profileGroups,
        string accountLabel)
    {
        return new FeasibilityTestResult
        {
            Id = id,
            CapturedAt = capturedAt,
            PlaybackCount = playbackCount,
            ScenarioId = scenarioId,
            ScenarioName = scenarioId,
            Outcome = playbackCount >= 9 ? "success" : "partial",
            IsSameAccountSessionMaintained = true,
            AccountLabel = accountLabel,
            VerifiedProfileGroups = profileGroups,
            IsRestartSessionMaintained = playbackCount >= 9,
            IsResourceUsageAcceptable = playbackCount >= 9,
            ObservedCpuPercent = playbackCount >= 9 ? 45 : null,
            ObservedGpuPercent = playbackCount >= 9 ? 60 : null,
            ObservedMemoryMegabytes = playbackCount >= 9 ? 12000 : null
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootFolder))
        {
            Directory.Delete(_rootFolder, recursive: true);
        }
    }
}
