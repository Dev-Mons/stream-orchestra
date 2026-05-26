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
