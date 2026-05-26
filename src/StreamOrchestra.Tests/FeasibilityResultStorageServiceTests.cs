using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class FeasibilityResultStorageServiceTests : IDisposable
{
    private readonly string _dataFolder;

    public FeasibilityResultStorageServiceTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void LoadResults_ReturnsEmptyListWhenFileDoesNotExist()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);

        var results = service.LoadResults();

        Assert.Empty(results);
    }

    [Fact]
    public void AppendResult_AndLoadResults_RoundTripsResults()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero);
        var result = new FeasibilityTestResult
        {
            Id = "feasibility_test",
            CapturedAt = capturedAt,
            PlaybackCount = 12,
            ScenarioId = "groups_a_b_c_12_slots",
            ScenarioName = "Groups A/B/C, 12 slots",
            Outcome = "partial",
            Diagnostics = new RuntimeDiagnosticsSnapshot(
                capturedAt,
                WebViewProcessCount: 9,
                WebViewWorkingSetMegabytes: 1024,
                WebViewPrivateMemoryMegabytes: 800,
                WebViewCpuPercent: 42.5),
            IsSameAccountSessionMaintained = true,
            AccountLabel = " main_soop ",
            VerifiedProfileGroups = ["A", "B", "C"],
            IsRestartSessionMaintained = true,
            IsResourceUsageAcceptable = false,
            ObservedCpuPercent = 55.5,
            ObservedGpuPercent = 72.25,
            ObservedMemoryMegabytes = 16384,
            DecisionCode = "continue_webview2_experiments",
            DecisionTitle = "WebView2 추가 실험",
            DecisionDetail = "12개까지 가능성이 있으나 성공 기준 전체가 충족되지 않았습니다.",
            DecisionNextAction = "9개 이상 테스트를 다시 실행하세요.",
            Notes = "12 streams played, 16 did not."
        };

        service.AppendResult(result);
        var loadedResults = service.LoadResults();

        var loadedResult = Assert.Single(loadedResults);
        Assert.Equal(result.Id, loadedResult.Id);
        Assert.Equal(result.CapturedAt, loadedResult.CapturedAt);
        Assert.Equal(12, loadedResult.PlaybackCount);
        Assert.Equal("groups_a_b_c_12_slots", loadedResult.ScenarioId);
        Assert.Equal("Groups A/B/C, 12 slots", loadedResult.ScenarioName);
        Assert.Equal("partial", loadedResult.Outcome);
        Assert.Equal(9, loadedResult.Diagnostics.WebViewProcessCount);
        Assert.Equal(42.5, loadedResult.Diagnostics.WebViewCpuPercent);
        Assert.True(loadedResult.IsSameAccountSessionMaintained);
        Assert.Equal("main_soop", loadedResult.AccountLabel);
        Assert.Equal(["A", "B", "C"], loadedResult.VerifiedProfileGroups);
        Assert.True(loadedResult.IsRestartSessionMaintained);
        Assert.False(loadedResult.IsResourceUsageAcceptable);
        Assert.Equal(55.5, loadedResult.ObservedCpuPercent);
        Assert.Equal(72.25, loadedResult.ObservedGpuPercent);
        Assert.Equal(16384, loadedResult.ObservedMemoryMegabytes);
        Assert.Equal("continue_webview2_experiments", loadedResult.DecisionCode);
        Assert.Equal("WebView2 추가 실험", loadedResult.DecisionTitle);
        Assert.Equal("12개까지 가능성이 있으나 성공 기준 전체가 충족되지 않았습니다.", loadedResult.DecisionDetail);
        Assert.Equal("9개 이상 테스트를 다시 실행하세요.", loadedResult.DecisionNextAction);
        Assert.Equal(result.Notes, loadedResult.Notes);
    }

    [Fact]
    public void AppendResult_PreservesExistingResultsWithoutLeavingTemporaryFiles()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);

        service.AppendResult(CreateResult("result_first", new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero)));
        service.AppendResult(CreateResult("result_second", new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero)));

        var loadedResults = service.LoadResults();

        Assert.Equal(["result_first", "result_second"], loadedResults.Select(result => result.Id));
        Assert.Empty(Directory.GetFiles(_dataFolder, "feasibility-results.json.tmp.*"));
    }

    [Fact]
    public void LoadResults_QuarantinesCorruptJsonAndReturnsEmptyList()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(service.ResultsFilePath, "{ invalid json");

        var results = service.LoadResults();

        Assert.Empty(results);
        Assert.False(File.Exists(service.ResultsFilePath));
        Assert.Single(Directory.GetFiles(_dataFolder, "feasibility-results.json.corrupt.*"));
    }

    [Fact]
    public void LoadResults_IgnoresNullEntries()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(
            service.ResultsFilePath,
            """
            [
              null,
              {
                "id": "result_1",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial"
              }
            ]
            """);

        var results = service.LoadResults();

        var result = Assert.Single(results);
        Assert.Equal("result_1", result.Id);
        Assert.Equal(9, result.PlaybackCount);
    }

    [Fact]
    public void LoadResults_NormalizesHandEditedEntriesAndDropsInvalidPlaybackCounts()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(
            service.ResultsFilePath,
            """
            [
              {
                "id": " ",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": " groups_a_b_c_9_slot_threshold ",
                "scenarioName": " Groups A/B/C, 9-slot success threshold ",
                "outcome": " partial ",
                "diagnostics": null,
                "verifiedProfileGroups": ["c", "A", null, " ", "a"],
                "decisionCode": " pending ",
                "decisionTitle": " 검증 대기 ",
                "decisionDetail": null,
                "decisionNextAction": " next ",
                "notes": " note "
              },
              {
                "id": "invalid_count",
                "capturedAt": "2026-05-26T12:30:00+00:00",
                "playbackCount": 17,
                "outcome": "partial"
              }
            ]
            """);

        var results = service.LoadResults();

        var result = Assert.Single(results);
        Assert.Equal("feasibility_20260526_120000_9_partial", result.Id);
        Assert.Equal("groups_a_b_c_9_slot_threshold", result.ScenarioId);
        Assert.Equal("Groups A/B/C, 9-slot success threshold", result.ScenarioName);
        Assert.Equal("partial", result.Outcome);
        Assert.Equal(0, result.Diagnostics.WebViewProcessCount);
        Assert.Equal(result.CapturedAt, result.Diagnostics.CapturedAt);
        Assert.Equal(["A", "C"], result.VerifiedProfileGroups);
        Assert.Equal("pending", result.DecisionCode);
        Assert.Equal("검증 대기", result.DecisionTitle);
        Assert.Equal("", result.DecisionDetail);
        Assert.Equal("next", result.DecisionNextAction);
        Assert.Equal("note", result.Notes);
    }

    [Fact]
    public void LoadResults_DropsAccountLabelWhenSameAccountEvidenceIsFalse()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(
            service.ResultsFilePath,
            """
            [
              {
                "id": "playback_only",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": false,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["A", "B", "C"]
              }
            ]
            """);

        var results = service.LoadResults();

        var result = Assert.Single(results);
        Assert.False(result.IsSameAccountSessionMaintained);
        Assert.Equal("", result.AccountLabel);
    }

    [Fact]
    public void LoadResults_DropsSameAccountEvidenceWhenLabelOrGroupsAreMissing()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(
            service.ResultsFilePath,
            """
            [
              {
                "id": "account_without_label",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "accountLabel": " ",
                "verifiedProfileGroups": ["A", "B", "C"]
              },
              {
                "id": "account_without_groups",
                "capturedAt": "2026-05-26T12:05:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": []
              },
              {
                "id": "account_with_evidence",
                "capturedAt": "2026-05-26T12:10:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["A", "B", "C"]
              }
            ]
            """);

        var results = service.LoadResults();

        Assert.Equal(3, results.Count);
        Assert.False(results[0].IsSameAccountSessionMaintained);
        Assert.Equal("", results[0].AccountLabel);
        Assert.False(results[1].IsSameAccountSessionMaintained);
        Assert.Equal("", results[1].AccountLabel);
        Assert.True(results[2].IsSameAccountSessionMaintained);
        Assert.Equal("main_soop", results[2].AccountLabel);
    }

    [Fact]
    public void LoadResults_DropsProfileGroupsOutsideScenarioAndAccountEvidenceWhenNoneRemain()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(
            service.ResultsFilePath,
            """
            [
              {
                "id": "mixed_groups",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["D", "x", "b", "A"]
              },
              {
                "id": "mismatched_group_only",
                "capturedAt": "2026-05-26T12:05:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["D"]
              }
            ]
            """);

        var results = service.LoadResults();

        Assert.Equal(2, results.Count);
        Assert.Equal(["A", "B"], results[0].VerifiedProfileGroups);
        Assert.True(results[0].IsSameAccountSessionMaintained);
        Assert.Empty(results[1].VerifiedProfileGroups);
        Assert.False(results[1].IsSameAccountSessionMaintained);
        Assert.Equal("", results[1].AccountLabel);
    }

    [Fact]
    public void LoadResults_DropsRestartEvidenceWhenSameAccountEvidenceIsIncomplete()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(
            service.ResultsFilePath,
            """
            [
              {
                "id": "restart_without_account",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": false,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["A", "B", "C"],
                "isRestartSessionMaintained": true
              },
              {
                "id": "restart_without_label",
                "capturedAt": "2026-05-26T12:05:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "accountLabel": " ",
                "verifiedProfileGroups": ["A", "B", "C"],
                "isRestartSessionMaintained": true
              },
              {
                "id": "restart_without_groups",
                "capturedAt": "2026-05-26T12:10:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": [],
                "isRestartSessionMaintained": true
              },
              {
                "id": "restart_with_evidence",
                "capturedAt": "2026-05-26T12:15:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["A", "B", "C"],
                "isRestartSessionMaintained": true
              }
            ]
            """);

        var results = service.LoadResults();

        Assert.Equal(4, results.Count);
        Assert.All(results.Take(3), result => Assert.False(result.IsRestartSessionMaintained));
        Assert.True(results[3].IsRestartSessionMaintained);
    }

    [Fact]
    public void LoadResults_DropsResourceFlagWhenStructuredObservationsAreIncompleteOrInvalid()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(
            service.ResultsFilePath,
            """
            [
              {
                "id": "resource_missing_observation",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isResourceUsageAcceptable": true,
                "observedCpuPercent": 45.5,
                "observedMemoryMegabytes": 12000
              },
              {
                "id": "resource_invalid_observation",
                "capturedAt": "2026-05-26T12:05:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isResourceUsageAcceptable": true,
                "observedCpuPercent": 45.5,
                "observedGpuPercent": 101,
                "observedMemoryMegabytes": 12000
              },
              {
                "id": "resource_with_observations",
                "capturedAt": "2026-05-26T12:10:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "partial",
                "isResourceUsageAcceptable": true,
                "observedCpuPercent": 45.5,
                "observedGpuPercent": 60,
                "observedMemoryMegabytes": 12000
              }
            ]
            """);

        var results = service.LoadResults();

        Assert.Equal(3, results.Count);
        Assert.False(results[0].IsResourceUsageAcceptable);
        Assert.False(results[1].IsResourceUsageAcceptable);
        Assert.True(results[2].IsResourceUsageAcceptable);
    }

    [Fact]
    public void LoadResults_DropsRestartAndResourceFlagsFromFailureOutcome()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(
            service.ResultsFilePath,
            """
            [
              {
                "id": "failed_attempt_with_criteria_flags",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": " failure ",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["A", "B", "C"],
                "isRestartSessionMaintained": true,
                "isResourceUsageAcceptable": true,
                "observedCpuPercent": 45.5,
                "observedGpuPercent": 60,
                "observedMemoryMegabytes": 12000
              }
            ]
            """);

        var result = Assert.Single(service.LoadResults());

        Assert.Equal("failure", result.Outcome);
        Assert.True(result.IsSameAccountSessionMaintained);
        Assert.Equal("main_soop", result.AccountLabel);
        Assert.Equal(["A", "B", "C"], result.VerifiedProfileGroups);
        Assert.False(result.IsRestartSessionMaintained);
        Assert.False(result.IsResourceUsageAcceptable);
        Assert.Equal(45.5, result.ObservedCpuPercent);
        Assert.Equal(60, result.ObservedGpuPercent);
        Assert.Equal(12000, result.ObservedMemoryMegabytes);
    }

    [Fact]
    public void LoadResults_DowngradesSuccessOutcomeWhenSuccessEvidenceIsIncomplete()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);
        File.WriteAllText(
            service.ResultsFilePath,
            """
            [
              {
                "id": "success_without_restart",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "success",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["A", "B", "C"],
                "isRestartSessionMaintained": false,
                "isResourceUsageAcceptable": true,
                "observedCpuPercent": 45.5,
                "observedGpuPercent": 60,
                "observedMemoryMegabytes": 12000
              },
              {
                "id": "success_without_required_groups",
                "capturedAt": "2026-05-26T12:05:00+00:00",
                "playbackCount": 16,
                "scenarioId": "groups_a_b_c_d_16_slots",
                "scenarioName": "Groups A/B/C/D, 16 slots",
                "outcome": "success",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["A", "B", "C"],
                "isRestartSessionMaintained": true,
                "isResourceUsageAcceptable": true,
                "observedCpuPercent": 45.5,
                "observedGpuPercent": 60,
                "observedMemoryMegabytes": 12000
              },
              {
                "id": "success_with_complete_evidence",
                "capturedAt": "2026-05-26T12:10:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": "success",
                "isSameAccountSessionMaintained": true,
                "accountLabel": "main_soop",
                "verifiedProfileGroups": ["A", "B", "C"],
                "isRestartSessionMaintained": true,
                "isResourceUsageAcceptable": true,
                "observedCpuPercent": 45.5,
                "observedGpuPercent": 60,
                "observedMemoryMegabytes": 12000
              }
            ]
            """);

        var results = service.LoadResults();

        Assert.Equal(3, results.Count);
        Assert.Equal("partial", results[0].Outcome);
        Assert.Equal("partial", results[1].Outcome);
        Assert.Equal("success", results[2].Outcome);
    }

    [Fact]
    public void SaveResults_NormalizesResultsBeforeWriting()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);

        service.SaveResults(
        [
            new FeasibilityTestResult
            {
                Id = " ",
                CapturedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
                PlaybackCount = 4,
                ScenarioId = " isolated_group_a ",
                ScenarioName = " Isolated Group A ",
                Outcome = " partial ",
                Diagnostics = null!,
                VerifiedProfileGroups = ["a", "A", " "],
                DecisionCode = null!,
                DecisionTitle = " title ",
                DecisionDetail = " detail ",
                DecisionNextAction = null!,
                Notes = null!
            }
        ]);

        var savedJson = File.ReadAllText(service.ResultsFilePath);

        Assert.Contains("\"id\": \"feasibility_20260526_120000_4_partial\"", savedJson);
        Assert.Contains("\"scenarioId\": \"isolated_group_a\"", savedJson);
        Assert.Contains("\"scenarioName\": \"Isolated Group A\"", savedJson);
        Assert.Contains("\"outcome\": \"partial\"", savedJson);
        Assert.Contains("\"verifiedProfileGroups\": [\r\n      \"A\"\r\n    ]", savedJson);
        Assert.Contains("\"decisionCode\": \"\"", savedJson);
        Assert.Contains("\"decisionTitle\": \"title\"", savedJson);
        Assert.Contains("\"decisionDetail\": \"detail\"", savedJson);
        Assert.Contains("\"decisionNextAction\": \"\"", savedJson);
        Assert.Contains("\"notes\": \"\"", savedJson);
    }

    [Fact]
    public void AppendResult_RejectsNullResult()
    {
        var service = new FeasibilityResultStorageService(_dataFolder);

        Assert.Throws<ArgumentNullException>(() => service.AppendResult(null!));
    }

    [Fact]
    public void ApplyDecisionSnapshot_RejectsNullArguments()
    {
        var result = CreateResult("result", new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero));
        var decision = new FeasibilityDecision("pending", "Pending", "Detail", "Next");

        Assert.Throws<ArgumentNullException>(() =>
            FeasibilityResultStorageService.ApplyDecisionSnapshot(null!, decision));
        Assert.Throws<ArgumentNullException>(() =>
            FeasibilityResultStorageService.ApplyDecisionSnapshot(result, null!));
    }

    [Fact]
    public void CreateResultId_NormalizesOutcome()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero);

        var id = FeasibilityResultStorageService.CreateResultId(capturedAt, 16, "Partial Success");

        Assert.Equal("feasibility_20260526_123000_16_partial_success", id);
    }

    [Fact]
    public void CreateResultId_FallsBackForBlankOutcome()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero);

        var id = FeasibilityResultStorageService.CreateResultId(capturedAt, 16, null);

        Assert.Equal("feasibility_20260526_123000_16_unknown", id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }

    private static FeasibilityTestResult CreateResult(string id, DateTimeOffset capturedAt)
    {
        return new FeasibilityTestResult
        {
            Id = id,
            CapturedAt = capturedAt,
            PlaybackCount = 9,
            ScenarioId = "groups_a_b_c_9_slot_threshold",
            ScenarioName = "Groups A/B/C, 9-slot success threshold",
            Outcome = "partial",
            Diagnostics = new RuntimeDiagnosticsSnapshot(
                capturedAt,
                WebViewProcessCount: 9,
                WebViewWorkingSetMegabytes: 1024,
                WebViewPrivateMemoryMegabytes: 800,
                WebViewCpuPercent: 42.5),
            IsSameAccountSessionMaintained = true,
            VerifiedProfileGroups = ["A", "B", "C"],
            IsRestartSessionMaintained = false,
            IsResourceUsageAcceptable = false,
            Notes = "manual evidence"
        };
    }
}
