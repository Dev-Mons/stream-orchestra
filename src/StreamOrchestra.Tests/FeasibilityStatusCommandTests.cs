using System.Security.Cryptography;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;
using StreamOrchestra.Tools;

namespace StreamOrchestra.Tests;

public sealed class FeasibilityStatusCommandTests : IDisposable
{
    private readonly string _dataFolder;

    public FeasibilityStatusCommandTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void Execute_StatusWithNoResults_PrintsPendingDecision()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["status", "--data-folder", _dataFolder],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Results recorded: 0", output.ToString());
        Assert.Contains("pending", output.ToString());
        Assert.Contains("Next action:", output.ToString());
        Assert.Contains("Plan audit: pass=0, pending=11, fail=0", output.ToString());
        Assert.Contains("Plan verification: [pending]", output.ToString());
        Assert.Contains("Success gate: [pending]", output.ToString());
        Assert.Contains("Suggested record shapes:", output.ToString());
        Assert.Contains("record --count 9 --outcome success --account --profile-groups A,B,C", output.ToString());
        Assert.Contains("Latest result: none", output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_StatusWithSuccessfulResultMissingGroupD_PrintsExperimentDecisionAndLatestCriteria()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreateSuccessfulResult());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["status", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Results recorded: 1", text);
        Assert.Contains("continue_webview2_experiments", text);
        Assert.Contains("Next action:", text);
        Assert.Contains("Plan audit: pass=5, pending=6, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Success gate: [pending]", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Contains("record --count 8 --outcome partial --account --profile-groups A,B", text);
        Assert.Contains("Latest result: success, 9 slot(s)", text);
        Assert.Contains("Scenario: Groups A/B/C, 9-slot success threshold (groups_a_b_c_9_slot_threshold)", text);
        Assert.Contains("Criteria: account=True, restart=True, resources=True", text);
        Assert.Contains("Profile groups: A/B/C", text);
        Assert.Contains("Observed resources: cpu=45.5%, gpu=60%, memory=12000 MB", text);
        Assert.Contains("Notes: manual test passed", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_StatusWithFailedNinePlusResult_PrintsFallbackNextAction()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreateFailureResult());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["status", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("switch_external_browser", text);
        Assert.Contains("Next action:", text);
        Assert.Contains("fallback", text);
        Assert.Contains("Success gate: [fail]", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_HistoryWithNoResults_PrintsEmptyHistory()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["history", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Stream Orchestra Feasibility History", text);
        Assert.Contains("Results recorded: 0", text);
        Assert.Contains("No feasibility results recorded.", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_HistoryWithResults_PrintsRecordedDecisionSnapshots()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        var result = CreateSuccessfulResult();
        FeasibilityResultStorageService.ApplyDecisionSnapshot(
            result,
            new FeasibilityDecisionService().Decide([result]));
        storage.AppendResult(result);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["history", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Stream Orchestra Feasibility History", text);
        Assert.Contains("Results recorded: 1", text);
        Assert.Contains("success, 9 slot(s), Groups A/B/C, 9-slot success threshold", text);
        Assert.Contains("Recorded decision: WebView2 추가 실험 (continue_webview2_experiments)", text);
        Assert.Contains("Next action at record time:", text);
        Assert.Contains("Notes: manual test passed", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_Scenarios_PrintsPlaybackAndIsolatedGroupScenarioIds()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["scenarios"], output, error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Stream Orchestra Feasibility Scenarios", text);
        Assert.Contains("groups_a_b_c_9_slot_threshold", text);
        Assert.Contains("groups_a_b_c_d_16_slots", text);
        Assert.Contains("isolated_group_a", text);
        Assert.Contains("record --count 4 --outcome <partial|failure> --account --profile-groups A", text);
        Assert.Contains("record --count 8 --outcome <partial|failure> --account --profile-groups A,B", text);
        Assert.Contains("record --count 9 --outcome <partial|failure> --account --profile-groups A,B,C", text);
        Assert.Contains("record --count 9 --outcome success --account --profile-groups A,B,C --restart --resources --cpu-percent <0-100> --gpu-percent <0-100> --memory-mb <value>", text);
        Assert.Contains("--account-label <label>", text);
        Assert.Contains("record --count 16 --outcome success --account --profile-groups A,B,C,D --restart --resources --cpu-percent <0-100> --gpu-percent <0-100> --memory-mb <value>", text);
        Assert.Contains("record --group A --outcome <partial|failure> --account --profile-groups A", text);
        Assert.DoesNotContain("record --count 8 --outcome <success|partial|failure>", text);
        Assert.DoesNotContain("record --count 9 --outcome <success|partial|failure>", text);
        Assert.Contains("Use `partial` when the requested slots visibly play but success-only evidence is incomplete.", text);
        Assert.Contains("Use `failure` when the requested playback count or isolated group does not work.", text);
        Assert.Contains("Record `success` only when the 9+ playback", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_Checklist_PrintsPlanManualVerificationSteps()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["checklist", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Stream Orchestra Phase 0 Manual Checklist", text);
        Assert.Contains($"Data folder: {_dataFolder}", text);
        Assert.Contains("Results recorded: 0", text);
        Assert.Contains("Decision: 검증 대기 (pending)", text);
        Assert.Contains("Plan audit: pass=0, pending=11, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Success gate: [pending]", text);
        Assert.Contains("Outstanding gates:", text);
        Assert.Contains("- [pending] Manual feasibility result recorded", text);
        Assert.Contains("do not bypass DRM, authentication, or security behavior", text);
        Assert.Contains("Run `preflight`", text);
        Assert.Contains("same SOOP account in profile groups A, B, C, and D", text);
        Assert.Contains("Restart the app", text);
        Assert.Contains("isolated Group A test", text);
        Assert.Contains("8-slot, 9-slot threshold, 12-slot, and 16-slot playback tests", text);
        Assert.Contains("CPU %, GPU %, and memory MB", text);
        Assert.Contains("one shared non-sensitive account label", text);
        Assert.Contains("Run each intended `record` command with `--dry-run` first", text);
        Assert.Contains("Record the final 9+ `success` evidence last", text);
        Assert.Contains("Run `verify`", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Contains("record --count 9 --outcome success --account --profile-groups A,B,C", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ChecklistWithOutput_WritesChecklistTextFile()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreateSuccessfulResult());
        var checklistOutputPath = Path.Combine(_dataFolder, "phase0-checklist.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["checklist", "--data-folder", _dataFolder, "--output", checklistOutputPath],
            output,
            error);

        var fileText = File.ReadAllText(checklistOutputPath);
        Assert.Equal(0, exitCode);
        Assert.Contains($"Checklist saved: {checklistOutputPath}", output.ToString());
        Assert.Contains("Stream Orchestra Phase 0 Manual Checklist", fileText);
        Assert.Contains("Results recorded: 1", fileText);
        Assert.Contains("Plan audit: pass=5, pending=6, fail=0", fileText);
        Assert.Contains("Plan verification: [pending]", fileText);
        Assert.Contains("Outstanding gates:", fileText);
        Assert.Contains("- [pending] Phase 0 WebView2 success gate", fileText);
        Assert.Contains("Safety: use normal SOOP login/player behavior only", fileText);
        Assert.Contains("Suggested record shapes:", fileText);
        Assert.Contains("record --count 8 --outcome partial --account --profile-groups A,B", fileText);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_Preflight_PrintsRuntimeProfilesLayoutsAndEvidenceStatus()
    {
        var profileFolder = Path.Combine(_dataFolder, "Profiles");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["preflight", "--data-folder", _dataFolder, "--profile-folder", profileFolder],
            output,
            error);

        var text = output.ToString();
        Assert.True(exitCode is 0 or 1);
        Assert.Contains("Stream Orchestra Feasibility Preflight", text);
        Assert.Contains($"Data folder: {_dataFolder}", text);
        Assert.Contains($"Profile root: {profileFolder}", text);
        Assert.Contains("WebView2 runtime: [", text);
        Assert.Contains("Layouts: [ready]", text);
        Assert.Contains("- [ready] Group A:", text);
        Assert.Contains("- [ready] Group B:", text);
        Assert.Contains("- [ready] Group C:", text);
        Assert.Contains("- [ready] Group D:", text);
        Assert.Contains("Evidence recorded: 0", text);
        Assert.Contains("Plan audit: pass=0, pending=11, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Success gate: [pending]", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_PreflightWithOutput_WritesPreflightTextFile()
    {
        var profileFolder = Path.Combine(_dataFolder, "Profiles");
        var preflightOutputPath = Path.Combine(_dataFolder, "phase0-preflight.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            [
                "preflight",
                "--data-folder",
                _dataFolder,
                "--profile-folder",
                profileFolder,
                "--output",
                preflightOutputPath
            ],
            output,
            error);

        var fileText = File.ReadAllText(preflightOutputPath);
        Assert.True(exitCode is 0 or 1);
        Assert.Contains($"Preflight saved: {preflightOutputPath}", output.ToString());
        Assert.Contains("Stream Orchestra Feasibility Preflight", fileText);
        Assert.Contains($"Data folder: {_dataFolder}", fileText);
        Assert.Contains($"Profile root: {profileFolder}", fileText);
        Assert.Contains("WebView2 runtime: [", fileText);
        Assert.Contains("Layouts: [ready]", fileText);
        Assert.Contains("Evidence recorded: 0", fileText);
        Assert.Contains("Plan verification: [pending]", fileText);
        Assert.Contains("Suggested record shapes:", fileText);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_Handoff_WritesPhase0ArtifactBundle()
    {
        var profileFolder = Path.Combine(_dataFolder, "Profiles");
        var handoffFolder = Path.Combine(_dataFolder, "handoff");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            [
                "handoff",
                "--data-folder",
                _dataFolder,
                "--profile-folder",
                profileFolder,
                "--output-folder",
                handoffFolder
            ],
            output,
            error);

        var preflightPath = Path.Combine(handoffFolder, "phase0-preflight.txt");
        var checklistPath = Path.Combine(handoffFolder, "phase0-checklist.txt");
        var auditPath = Path.Combine(handoffFolder, "phase0-audit.txt");
        var verificationPath = Path.Combine(handoffFolder, "phase0-verification.txt");
        var historyPath = Path.Combine(handoffFolder, "phase0-history.txt");
        var diagnosticReportPath = Path.Combine(handoffFolder, "phase0-diagnostic-report.json");
        var resultsPath = Path.Combine(handoffFolder, "phase0-results.json");
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Stream Orchestra Phase 0 Handoff", text);
        Assert.Contains($"Output folder: {handoffFolder}", text);
        Assert.Contains("Generated at:", text);
        Assert.Contains($"Results snapshot source: {Path.Combine(_dataFolder, "feasibility-results.json")}", text);
        Assert.Contains("Results snapshot count: 0", text);
        Assert.Contains($"Saved: {preflightPath}", text);
        Assert.Contains($"Saved: {checklistPath}", text);
        Assert.Contains($"Saved: {auditPath}", text);
        Assert.Contains($"Saved: {verificationPath}", text);
        Assert.Contains($"Saved: {historyPath}", text);
        Assert.Contains($"Saved: {diagnosticReportPath}", text);
        Assert.Contains($"Saved: {resultsPath}", text);
        Assert.Contains($"Saved: {manifestPath}", text);
        Assert.Contains("Preflight ready:", text);
        Assert.Contains("Verification complete: False", text);
        Assert.Contains("Plan verification: pending", text);
        Assert.Contains("Plan audit: pass=0, pending=11, fail=0", text);
        Assert.Contains("Outstanding gates: 11", text);
        Assert.Contains("Stream Orchestra Feasibility Preflight", File.ReadAllText(preflightPath));
        Assert.Contains("Stream Orchestra Phase 0 Manual Checklist", File.ReadAllText(checklistPath));
        Assert.Contains("Stream Orchestra Plan Audit", File.ReadAllText(auditPath));
        Assert.Contains("Verification: not complete", File.ReadAllText(verificationPath));
        Assert.Contains("Stream Orchestra Feasibility History", File.ReadAllText(historyPath));
        var diagnosticReportText = File.ReadAllText(diagnosticReportPath);
        Assert.Contains("\"profileRootFolder\":", diagnosticReportText);
        Assert.Contains("\"feasibilityResultCount\": 0", diagnosticReportText);
        Assert.Equal("[]" + Environment.NewLine, File.ReadAllText(resultsPath));
        var manifestText = File.ReadAllText(manifestPath);
        Assert.Contains("\"resultCount\": 0", manifestText);
        Assert.Contains("\"isPreflightReady\":", manifestText);
        Assert.Contains("\"isVerified\": false", manifestText);
        Assert.Contains("\"decisionCode\": \"pending\"", manifestText);
        Assert.Contains("\"planVerificationStatus\": \"pending\"", manifestText);
        Assert.Contains("\"passingGateCount\": 0", manifestText);
        Assert.Contains("\"pendingGateCount\": 11", manifestText);
        Assert.Contains("\"failingGateCount\": 0", manifestText);
        Assert.Contains("\"outstandingGateCount\": 11", manifestText);
        Assert.Contains("\"phase0-results.json\"", manifestText);
        Assert.Contains("\"phase0-history.txt\"", manifestText);
        Assert.Contains("\"phase0-diagnostic-report.json\"", manifestText);
        Assert.Contains("\"phase0-verification.txt\"", manifestText);
        Assert.Contains("\"artifactDetails\":", manifestText);
        Assert.Contains("\"fileName\": \"phase0-preflight.txt\"", manifestText);
        Assert.Contains("\"sizeBytes\":", manifestText);
        Assert.Contains("\"sha256\":", manifestText);
        var resultsHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(resultsPath))).ToLowerInvariant();
        Assert.Contains($"\"sha256\": \"{resultsHash}\"", manifestText);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_HandoffWithResults_WritesNormalizedResultsSnapshot()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreateSuccessfulResult());
        var handoffFolder = Path.Combine(_dataFolder, "handoff-with-results");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            output,
            error);

        var resultsText = File.ReadAllText(Path.Combine(handoffFolder, "phase0-results.json"));
        var historyText = File.ReadAllText(Path.Combine(handoffFolder, "phase0-history.txt"));
        var diagnosticReportText = File.ReadAllText(Path.Combine(handoffFolder, "phase0-diagnostic-report.json"));
        Assert.Equal(0, exitCode);
        Assert.Contains("Results snapshot count: 1", output.ToString());
        Assert.Contains("\"id\": \"result_1\"", resultsText);
        Assert.Contains("\"scenarioId\": \"groups_a_b_c_9_slot_threshold\"", resultsText);
        Assert.Contains("\"accountLabel\": \"main_soop\"", resultsText);
        Assert.Contains("success, 9 slot(s)", historyText);
        Assert.Contains("\"feasibilityResultCount\": 1", diagnosticReportText);
        Assert.Contains("\"feasibilityDecision\":", diagnosticReportText);
        Assert.Contains("\"planVerificationStatus\": \"pending\"", File.ReadAllText(Path.Combine(handoffFolder, "phase0-handoff-manifest.json")));
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_ReturnsPassForGeneratedBundle()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-validation");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(0, exitCode);
        Assert.Contains("Stream Orchestra Phase 0 Handoff Validation", text);
        Assert.Contains($"Input folder: {handoffFolder}", text);
        Assert.Contains("Plan verification: pending", text);
        Assert.Contains("- [pass] phase0-results.json:", text);
        Assert.Contains("Validation: pass", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsTamperedArtifact()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-tampered");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        File.AppendAllText(Path.Combine(handoffFolder, "phase0-results.json"), "tampered");

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-results.json: size mismatch", text);
        Assert.Contains("phase0-results.json: sha256 mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_VerifyWithNoResults_ReturnsFailureAndPendingGate()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["verify", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();

        Assert.Equal(1, exitCode);
        Assert.Contains("Stream Orchestra Plan Verification", text);
        Assert.Contains("Results recorded: 0", text);
        Assert.Contains("Plan audit: pass=0, pending=11, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Success gate: [pending]", text);
        Assert.Contains("Verification: not complete", text);
        Assert.Contains("Outstanding gates:", text);
        Assert.Contains("- [pending] Manual feasibility result recorded", text);
        Assert.Contains("- [pending] SOOP 8-slot split-profile playback", text);
        Assert.Contains("- [pending] Phase 0 WebView2 success gate", text);
        Assert.Contains("Required evidence: record live SOOP 4-slot Group A, 8-slot, 9-slot threshold, 12-slot, and 16-slot playback evidence plus A-D account-label", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Contains("record --group A --outcome partial --account --profile-groups A", text);
        Assert.Contains("record --count 8 --outcome partial --account --profile-groups A,B", text);
        Assert.Contains("record --count 9 --outcome success --account --profile-groups A,B,C --restart --resources --cpu-percent <0-100>", text);
        Assert.Contains("--account-label <label>", text);
        Assert.Contains("record --count 16 --outcome partial --account --profile-groups A,B,C,D", text);
        Assert.True(
            text.IndexOf("record --count 16 --outcome partial", StringComparison.Ordinal) <
            text.IndexOf("record --count 9 --outcome success", StringComparison.Ordinal));
        Assert.DoesNotContain("record --count 16 --outcome <success|partial|failure>", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_VerifyWithOutput_WritesVerificationTextFile()
    {
        var verificationOutputPath = Path.Combine(_dataFolder, "phase0-verification.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["verify", "--data-folder", _dataFolder, "--output", verificationOutputPath],
            output,
            error);

        var fileText = File.ReadAllText(verificationOutputPath);
        Assert.Equal(1, exitCode);
        Assert.Contains($"Verification saved: {verificationOutputPath}", output.ToString());
        Assert.Contains("Stream Orchestra Plan Verification", fileText);
        Assert.Contains("Results recorded: 0", fileText);
        Assert.Contains("Plan audit: pass=0, pending=11, fail=0", fileText);
        Assert.Contains("Plan verification: [pending]", fileText);
        Assert.Contains("Verification: not complete", fileText);
        Assert.Contains("Outstanding gates:", fileText);
        Assert.Contains("- [pending] Manual feasibility result recorded", fileText);
        Assert.Contains("Required evidence: record live SOOP 4-slot Group A", fileText);
        Assert.Contains("Suggested record shapes:", fileText);
        Assert.Contains("record --count 9 --outcome success --account --profile-groups A,B,C", fileText);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_VerifyWithNullProfileGroupEntries_DoesNotCrash()
    {
        Directory.CreateDirectory(_dataFolder);
        File.WriteAllText(
            Path.Combine(_dataFolder, "feasibility-results.json"),
            """
            [
              {
                "id": "result_group_a",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 4,
                "scenarioId": "isolated_group_a",
                "scenarioName": "Isolated Group A",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "verifiedProfileGroups": [null, "a"],
                "isRestartSessionMaintained": false,
                "isResourceUsageAcceptable": false
              }
            ]
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["verify", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();

        Assert.Equal(1, exitCode);
        Assert.Contains("Results recorded: 1", text);
        Assert.Contains("Plan audit: pass=2, pending=9, fail=0", text);
        Assert.Contains("Verification: not complete", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_VerifyWithNullOutcome_DoesNotCrash()
    {
        Directory.CreateDirectory(_dataFolder);
        File.WriteAllText(
            Path.Combine(_dataFolder, "feasibility-results.json"),
            """
            [
              {
                "id": "result_null_outcome",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 9,
                "scenarioId": "groups_a_b_c_9_slot_threshold",
                "scenarioName": "Groups A/B/C, 9-slot success threshold",
                "outcome": null,
                "isSameAccountSessionMaintained": true,
                "verifiedProfileGroups": ["A", "B", "C"],
                "isRestartSessionMaintained": true,
                "isResourceUsageAcceptable": true,
                "observedCpuPercent": 45,
                "observedGpuPercent": 60,
                "observedMemoryMegabytes": 12000
              }
            ]
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["verify", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();

        Assert.Equal(1, exitCode);
        Assert.Contains("Results recorded: 1", text);
        Assert.Contains("SOOP 9-slot threshold playback", text);
        Assert.Contains("Verification: not complete", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_VerifyWithNullScenarioId_DoesNotCrash()
    {
        Directory.CreateDirectory(_dataFolder);
        File.WriteAllText(
            Path.Combine(_dataFolder, "feasibility-results.json"),
            """
            [
              {
                "id": "result_null_scenario",
                "capturedAt": "2026-05-26T12:00:00+00:00",
                "playbackCount": 4,
                "scenarioId": null,
                "scenarioName": "Missing scenario",
                "outcome": "partial",
                "isSameAccountSessionMaintained": true,
                "verifiedProfileGroups": ["A"],
                "isRestartSessionMaintained": false,
                "isResourceUsageAcceptable": false
              }
            ]
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["verify", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();

        Assert.Equal(1, exitCode);
        Assert.Contains("Results recorded: 1", text);
        Assert.Contains("Plan audit: pass=1, pending=10, fail=0", text);
        Assert.Contains("Verification: not complete", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_VerifyWithSuccessfulResult_ReturnsSuccessAndPassedGate()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        foreach (var result in CreateCompletePlanResults())
        {
            storage.AppendResult(result);
        }
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["verify", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Stream Orchestra Plan Verification", text);
        Assert.Contains("Results recorded: 5", text);
        Assert.Contains("continue_webview2_mvp", text);
        Assert.Contains("Plan audit: pass=11, pending=0, fail=0", text);
        Assert.Contains("Plan verification: [pass]", text);
        Assert.Contains("Success gate: [pass]", text);
        Assert.Contains("Verification: pass", text);
        Assert.DoesNotContain("Outstanding gates:", text);
        Assert.DoesNotContain("Suggested record shapes:", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_VerifyWithSuccessfulResultAndOutput_WritesPassedVerificationTextFile()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        foreach (var result in CreateCompletePlanResults())
        {
            storage.AppendResult(result);
        }

        var verificationOutputPath = Path.Combine(_dataFolder, "phase0-verification-pass.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["verify", "--data-folder", _dataFolder, "--output", verificationOutputPath],
            output,
            error);

        var fileText = File.ReadAllText(verificationOutputPath);
        Assert.Equal(0, exitCode);
        Assert.Contains($"Verification saved: {verificationOutputPath}", output.ToString());
        Assert.Contains("Stream Orchestra Plan Verification", fileText);
        Assert.Contains("Results recorded: 5", fileText);
        Assert.Contains("continue_webview2_mvp", fileText);
        Assert.Contains("Plan audit: pass=11, pending=0, fail=0", fileText);
        Assert.Contains("Plan verification: [pass]", fileText);
        Assert.Contains("Success gate: [pass]", fileText);
        Assert.Contains("Verification: pass", fileText);
        Assert.DoesNotContain("Outstanding gates:", fileText);
        Assert.DoesNotContain("Suggested record shapes:", fileText);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_VerifyWithSuggestedOrderResults_ReturnsSuccessAndPassedGate()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        foreach (var result in CreateSuggestedOrderPlanResults())
        {
            storage.AppendResult(result);
        }
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["verify", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Results recorded: 5", text);
        Assert.Contains("continue_webview2_mvp", text);
        Assert.Contains("Plan audit: pass=11, pending=0, fail=0", text);
        Assert.Contains("Verification: pass", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_VerifyWithPartialNinePlusResult_PrintsOutstandingGateDetails()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreatePartialResult());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["verify", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();

        Assert.Equal(1, exitCode);
        Assert.Contains("Plan audit: pass=3, pending=7, fail=1", text);
        Assert.Contains("Plan verification: [fail]", text);
        Assert.Contains("Outstanding gates:", text);
        Assert.Contains("- [fail] App restart keeps login session", text);
        Assert.Contains("- [pending] Structured resource observations captured", text);
        Assert.Contains("- [pending] Phase 0 WebView2 success gate", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Contains("record --count 9 --outcome success --account --profile-groups A,B,C --restart --resources", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_AuditWithNoResults_PrintsPendingGate()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["audit", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Stream Orchestra Plan Audit", text);
        Assert.Contains("Results recorded: 0", text);
        Assert.Contains("Plan audit: pass=0, pending=11, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Success gate: [pending]", text);
        Assert.Contains("[pending] Manual feasibility result recorded", text);
        Assert.Contains("[pending] Phase 0 WebView2 success gate", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Contains("record --group A --outcome partial --account --profile-groups A", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_AuditWithSuccessfulResultMissingGroupD_PrintsPendingSuccessGate()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreateSuccessfulResult());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["audit", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("continue_webview2_experiments", text);
        Assert.Contains("Plan audit: pass=5, pending=6, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Success gate: [pending]", text);
        Assert.Contains("[pass] SOOP 9-slot threshold playback", text);
        Assert.Contains("[pending] Same SOOP account session persists across A-D", text);
        Assert.Contains("[pass] App restart keeps login session", text);
        Assert.Contains("[pass] CPU/GPU/memory acceptable", text);
        Assert.Contains("[pass] Structured resource observations captured", text);
        Assert.Contains("[pending] Phase 0 WebView2 success gate", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Contains("record --count 8 --outcome partial --account --profile-groups A,B", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_AuditWithOutput_WritesAuditTextFile()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreateSuccessfulResult());
        var auditOutputPath = Path.Combine(_dataFolder, "manual-audit.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["audit", "--data-folder", _dataFolder, "--output", auditOutputPath],
            output,
            error);

        var fileText = File.ReadAllText(auditOutputPath);
        Assert.Equal(0, exitCode);
        Assert.Contains($"Audit saved: {auditOutputPath}", output.ToString());
        Assert.Contains("Stream Orchestra Plan Audit", fileText);
        Assert.Contains("Plan audit: pass=5, pending=6, fail=0", fileText);
        Assert.Contains("Plan verification: [pending]", fileText);
        Assert.Contains("Success gate: [pending]", fileText);
        Assert.Contains("[pending] Phase 0 WebView2 success gate", fileText);
        Assert.Contains("Suggested record shapes:", fileText);
        Assert.Contains("record --count 8 --outcome partial --account --profile-groups A,B", fileText);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_AuditWithOutputFileNameOnly_WritesAuditTextFile()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreateSuccessfulResult());
        var auditOutputPath = $"manual-audit-{Guid.NewGuid():N}.txt";
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            var exitCode = FeasibilityStatusCommand.Execute(
                ["audit", "--data-folder", _dataFolder, "--output", auditOutputPath],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(auditOutputPath));
            var fileText = File.ReadAllText(auditOutputPath);
            Assert.Contains("Plan audit: pass=5, pending=6, fail=0", fileText);
            Assert.Contains("Plan verification: [pending]", fileText);
            Assert.Contains("Success gate: [pending]", fileText);
            Assert.Contains("[pending] Phase 0 WebView2 success gate", fileText);
            Assert.Contains("Suggested record shapes:", fileText);
            Assert.Equal("", error.ToString());
        }
        finally
        {
            if (File.Exists(auditOutputPath))
            {
                File.Delete(auditOutputPath);
            }
        }
    }

    [Fact]
    public void Execute_Browsers_PrintsInstalledCustomBrowserCandidate()
    {
        var browserFolder = Path.Combine(_dataFolder, "Browser");
        Directory.CreateDirectory(browserFolder);
        var executablePath = Path.Combine(browserFolder, "browser.exe");
        File.WriteAllText(executablePath, "");
        new ExternalBrowserCandidateStorageService(_dataFolder).SaveCandidates(
        [
            new ExternalBrowserCandidate(
                "portable_browser",
                "Portable Browser",
                [executablePath])
        ]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["browsers", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Stream Orchestra External Browsers", text);
        Assert.Contains($"Custom candidates file: {Path.Combine(_dataFolder, "external-browsers.json")}", text);
        Assert.Contains("[installed] Portable Browser (portable_browser)", text);
        Assert.Contains(executablePath, text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_FallbackWithNoLastSession_ReturnsUnavailable()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["fallback", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("Stream Orchestra External Browser Fallback", text);
        Assert.Contains("Last session: none", text);
        Assert.Contains("External browser fallback script: not available (No last saved session is available.)", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_FallbackWithLaunchableLastSession_WritesScript()
    {
        var browserFolder = Path.Combine(_dataFolder, "Browser");
        Directory.CreateDirectory(browserFolder);
        var executablePath = Path.Combine(browserFolder, "browser.exe");
        File.WriteAllText(executablePath, "");
        new ExternalBrowserCandidateStorageService(_dataFolder).SaveCandidates(
        [
            new ExternalBrowserCandidate(
                "aaa_portable_browser",
                "AAA Portable Browser",
                [executablePath])
        ]);
        new PresetStorageService(_dataFolder).SaveAppState(new AppState
        {
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
                        StreamName = "Streamer A",
                        StreamUrl = "https://example.com/live/a",
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
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["fallback", "--data-folder", _dataFolder],
            output,
            error);

        var scripts = Directory.GetFiles(_dataFolder, "external-browser-fallback-*.ps1");
        var text = output.ToString();
        var scriptText = File.ReadAllText(Assert.Single(scripts));
        Assert.Equal(0, exitCode);
        Assert.Contains("Last session: Last Session (last_session)", text);
        Assert.Contains("Planned slots: 1", text);
        Assert.Contains("[slot 1] Streamer A -> AAA Portable Browser (aaa_portable_browser), muted=False: https://example.com/live/a", text);
        Assert.Contains("External browser fallback script:", text);
        Assert.Contains("Review the script before running it.", text);
        Assert.Contains(executablePath, scriptText);
        Assert.Contains("https://example.com/live/a", scriptText);
        Assert.Contains("Start-Process", scriptText);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_UnknownCommand_ReturnsUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["unknown"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown command", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void Execute_Record_AppendsResultAndPrintsDecision()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            [
                "record",
                "--count",
                "9",
                "--outcome",
                "success",
                "--account",
                "--account-label",
                "main_soop",
                "--profile-groups",
                "A,B,C",
                "--restart",
                "--resources",
                "--cpu-percent",
                "45.5",
                "--gpu-percent",
                "60",
                "--memory-mb",
                "12000",
                "--scenario",
                "groups_a_b_c_9_slot_threshold",
                "--scenario-name",
                "Groups A/B/C, 9-slot success threshold",
                "--notes",
                "manual cli record",
                "--data-folder",
                _dataFolder
            ],
            output,
            error);

        var storage = new FeasibilityResultStorageService(_dataFolder);
        var result = Assert.Single(storage.LoadResults());
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(9, result.PlaybackCount);
        Assert.Equal("groups_a_b_c_9_slot_threshold", result.ScenarioId);
        Assert.Equal("Groups A/B/C, 9-slot success threshold", result.ScenarioName);
        Assert.Equal("success", result.Outcome);
        Assert.True(result.IsSameAccountSessionMaintained);
        Assert.Equal("main_soop", result.AccountLabel);
        Assert.Equal(["A", "B", "C"], result.VerifiedProfileGroups);
        Assert.True(result.IsRestartSessionMaintained);
        Assert.True(result.IsResourceUsageAcceptable);
        Assert.Equal(45.5, result.ObservedCpuPercent);
        Assert.Equal(60, result.ObservedGpuPercent);
        Assert.Equal(12000, result.ObservedMemoryMegabytes);
        Assert.Equal("manual cli record", result.Notes);
        Assert.Equal("continue_webview2_experiments", result.DecisionCode);
        Assert.Equal("WebView2 추가 실험", result.DecisionTitle);
        Assert.Contains("프로필", result.DecisionNextAction);
        Assert.Contains("Recorded feasibility result.", text);
        Assert.Contains("Scenario: Groups A/B/C, 9-slot success threshold (groups_a_b_c_9_slot_threshold)", text);
        Assert.Contains("Account label: main_soop", text);
        Assert.Contains("Profile groups: A/B/C", text);
        Assert.Contains("Observed resources: cpu=45.5%, gpu=60%, memory=12000 MB", text);
        Assert.Contains("continue_webview2_experiments", text);
        Assert.Contains("Plan audit: pass=5, pending=6, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Success gate: [pending]", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Contains("record --count 8 --outcome partial --account --profile-groups A,B", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_RecordDryRun_PrintsPreviewWithoutAppendingResult()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            [
                "record",
                "--count",
                "9",
                "--outcome",
                "success",
                "--account",
                "--account-label",
                "main_soop",
                "--profile-groups",
                "A,B,C",
                "--restart",
                "--resources",
                "--cpu-percent",
                "45.5",
                "--gpu-percent",
                "60",
                "--memory-mb",
                "12000",
                "--dry-run",
                "--data-folder",
                _dataFolder
            ],
            output,
            error);

        var storage = new FeasibilityResultStorageService(_dataFolder);
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Empty(storage.LoadResults());
        Assert.Contains("Dry run: feasibility result was not recorded.", text);
        Assert.Contains("Stored results before command: 0", text);
        Assert.Contains("Stored results after command: 0", text);
        Assert.Contains("Result: success, 9 slot(s)", text);
        Assert.Contains("Scenario: Groups A/B/C, 9-slot success threshold (groups_a_b_c_9_slot_threshold)", text);
        Assert.Contains("Account label: main_soop", text);
        Assert.Contains("Profile groups: A/B/C", text);
        Assert.Contains("continue_webview2_experiments", text);
        Assert.Contains("Plan audit: pass=5, pending=6, fail=0", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_RecordWithGroup_DerivesIsolatedGroupScenarioAndDefaultCount()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            [
                "record",
                "--group",
                "D",
                "--outcome",
                "partial",
                "--account",
                "--profile-groups",
                "D",
                "--account-label",
                "main_soop",
                "--data-folder",
                _dataFolder
            ],
            output,
            error);

        var storage = new FeasibilityResultStorageService(_dataFolder);
        var result = Assert.Single(storage.LoadResults());
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(4, result.PlaybackCount);
        Assert.Equal("isolated_group_d", result.ScenarioId);
        Assert.Equal("Isolated Group D test (4 slot(s))", result.ScenarioName);
        Assert.Equal("partial", result.Outcome);
        Assert.True(result.IsSameAccountSessionMaintained);
        Assert.Equal("main_soop", result.AccountLabel);
        Assert.Equal(["D"], result.VerifiedProfileGroups);
        Assert.Contains("Scenario: Isolated Group D test (4 slot(s)) (isolated_group_d)", text);
        Assert.Contains("Account label: main_soop", text);
        Assert.Contains("Profile groups: D", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_RecordWithoutScenario_UsesPlaybackCountScenario()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            [
                "record",
                "--count",
                "12",
                "--outcome",
                "partial",
                "--data-folder",
                _dataFolder
            ],
            output,
            error);

        var storage = new FeasibilityResultStorageService(_dataFolder);
        var result = Assert.Single(storage.LoadResults());
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(12, result.PlaybackCount);
        Assert.Equal("groups_a_b_c_12_slots", result.ScenarioId);
        Assert.Equal("Groups A/B/C, 12 slots", result.ScenarioName);
        Assert.Contains("Scenario: Groups A/B/C, 12 slots (groups_a_b_c_12_slots)", text);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_Report_WritesDiagnosticReport()
    {
        var profileFolder = Path.Combine(_dataFolder, "Profiles");
        new FeasibilityResultStorageService(_dataFolder).AppendResult(CreateSuccessfulResult());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["report", "--data-folder", _dataFolder, "--profile-folder", profileFolder],
            output,
            error);

        var reports = Directory.GetFiles(_dataFolder, "diagnostic-report-*.json");
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Single(reports);
        Assert.Contains("Diagnostic report saved.", text);
        Assert.Contains("External browser fallback script: not available (No last saved session is available.)", text);
        Assert.Contains("continue_webview2_experiments", text);
        Assert.Contains("Results recorded: 1", text);
        Assert.Contains("Plan audit: pass=5, pending=6, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Success gate: [pending]", text);
        Assert.Contains("Suggested record shapes:", text);
        Assert.Contains("record --count 8 --outcome partial --account --profile-groups A,B", text);
        Assert.Equal("", error.ToString());
    }

    [Theory]
    [InlineData("record --outcome success", "record requires --count.")]
    [InlineData("record --count 9", "record requires --outcome.")]
    [InlineData("record --count 8 --outcome success --account --restart --resources", "Success requires at least 9 simultaneous streams.")]
    [InlineData("record --count 9 --outcome success --restart --resources", "Success requires same-account session persistence.")]
    [InlineData("record --count 9 --outcome success --account --profile-groups A,B,C --resources", "Success requires restart session persistence.")]
    [InlineData("record --count 9 --outcome success --account --profile-groups A,B,C --restart", "Success requires acceptable resource usage.")]
    [InlineData("record --count 9 --outcome success --account --profile-groups A,B,C --restart --resources", "Resource OK requires CPU %, GPU %, and memory MB observations.")]
    [InlineData("record --count 9 --outcome partial --resources", "Resource OK requires CPU %, GPU %, and memory MB observations.")]
    [InlineData("record --count 9 --outcome success --account --restart --resources --cpu-percent 45 --gpu-percent 60 --memory-mb 12000", "Success requires same-account profile group evidence for groups A, B, C.")]
    [InlineData("record --count 9 --outcome partial --profile-groups A,Z", "Profile groups must be A, B, C, and/or D.")]
    [InlineData("record --count 17 --outcome success", "--count must be between 1 and 16.")]
    [InlineData("record --count 9 --outcome unknown", "--outcome must be success, partial, or failure.")]
    [InlineData("record --count 9 --outcome partial --cpu-percent 101", "--cpu-percent must be between 0 and 100.")]
    [InlineData("record --count 9 --outcome partial --cpu-percent NaN", "CPU % must be a finite number.")]
    [InlineData("record --count 9 --outcome partial --gpu-percent bad", "--gpu-percent requires a numeric value.")]
    [InlineData("record --count 9 --outcome partial --memory-mb -1", "--memory-mb must be 0 or higher.")]
    [InlineData("record --count 9 --outcome partial --account-label", "--account-label requires a value.")]
    [InlineData("record --group D --outcome partial --account --profile-groups D", "Same-account evidence requires an account label.")]
    [InlineData("record --group Z --outcome partial", "--group must be A, B, C, or D.")]
    [InlineData("record --group A --count 5 --outcome partial", "--group can only be used with --count 1-4.")]
    [InlineData("record --group A --outcome partial --scenario manual_group_a", "--group cannot be combined with --scenario or --scenario-name.")]
    [InlineData("record --group A --outcome partial --profile-groups D", "Profile groups must match scenario groups: A.")]
    [InlineData("record --count 16 --outcome partial --scenario manual_group_a --profile-groups A", "Scenario manual_group_a requires 1-4 slot(s).")]
    [InlineData("record --count 12 --outcome partial --scenario groups_a_b_8_slots --profile-groups A,B", "Scenario groups_a_b_8_slots requires 8 slot(s).")]
    [InlineData("record --count 9 --outcome partial --scenario groups_a_b_c_9_slot_threshold --profile-groups D", "Profile groups must match scenario groups: A, B, C.")]
    [InlineData("record --count 9 --outcome partial --profile-folder C:\\Temp", "Unknown option: --profile-folder")]
    public void Execute_RecordValidationErrors_ReturnUsageError(string commandLine, string expectedError)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(commandLine.Split(' '), output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains(expectedError, error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Theory]
    [InlineData("--scenario")]
    [InlineData("--scenario-name")]
    public void Execute_RecordWithBlankScenarioText_ReturnsUsageError(string option)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["record", "--count", "9", "--outcome", "partial", option, " ", "--data-folder", _dataFolder],
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Contains($"{option} requires a value.", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void Execute_ReportValidationErrors_ReturnUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["report", "--data-folder"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("--data-folder requires a value.", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Theory]
    [InlineData("--profile-folder", "--profile-folder requires a value.")]
    [InlineData("--output", "--output requires a value.")]
    public void Execute_PreflightValidationErrors_ReturnUsageError(string option, string expectedError)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["preflight", option], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains(expectedError, error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void Execute_HandoffValidationErrors_ReturnUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["handoff", "--output-folder"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("--output-folder requires a value.", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoffValidationErrors_ReturnUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["validate-handoff"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("validate-handoff requires --input-folder.", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void Execute_AuditValidationErrors_ReturnUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["audit", "--output"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("--output requires a value.", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void Execute_ChecklistValidationErrors_ReturnUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["checklist", "--output"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("--output requires a value.", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void Execute_VerifyValidationErrors_ReturnUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["verify", "--output"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("--output requires a value.", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void Execute_Help_PrintsUsage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["--help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output.ToString());
        Assert.Equal("", error.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }

    private static FeasibilityTestResult CreateSuccessfulResult()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        return new FeasibilityTestResult
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
                WebViewWorkingSetMegabytes: 1024,
                WebViewPrivateMemoryMegabytes: 800,
                WebViewCpuPercent: 30),
            IsSameAccountSessionMaintained = true,
            AccountLabel = "main_soop",
            VerifiedProfileGroups = ["A", "B", "C"],
            IsRestartSessionMaintained = true,
            IsResourceUsageAcceptable = true,
            ObservedCpuPercent = 45.5,
            ObservedGpuPercent = 60,
            ObservedMemoryMegabytes = 12000,
            Notes = "manual test passed"
        };
    }

    private static IReadOnlyList<FeasibilityTestResult> CreateCompletePlanResults()
    {
        return
        [
            CreatePassingScenarioResult(
                "result_group_a",
                4,
                "group_a_first_slots",
                "Group A only (4 slot(s))",
                new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero)),
            CreatePassingScenarioResult(
                "result_8",
                8,
                "groups_a_b_8_slots",
                "Groups A/B split, 8 slots",
                new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero)),
            CreatePassingScenarioResult(
                "result_9",
                9,
                "groups_a_b_c_9_slot_threshold",
                "Groups A/B/C, 9-slot success threshold",
                new DateTimeOffset(2026, 5, 26, 12, 45, 0, TimeSpan.Zero)),
            CreatePassingScenarioResult(
                "result_12",
                12,
                "groups_a_b_c_12_slots",
                "Groups A/B/C, 12 slots",
                new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero)),
            CreatePassingScenarioResult(
                "result_16",
                16,
                "groups_a_b_c_d_16_slots",
                "Groups A/B/C/D, 16 slots",
                new DateTimeOffset(2026, 5, 26, 13, 30, 0, TimeSpan.Zero))
        ];
    }

    private static IReadOnlyList<FeasibilityTestResult> CreateSuggestedOrderPlanResults()
    {
        return
        [
            CreatePassingScenarioResult(
                "result_group_a",
                4,
                "group_a_first_slots",
                "Group A only (4 slot(s))",
                new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero)),
            CreatePassingScenarioResult(
                "result_8",
                8,
                "groups_a_b_8_slots",
                "Groups A/B split, 8 slots",
                new DateTimeOffset(2026, 5, 26, 12, 15, 0, TimeSpan.Zero)),
            CreatePassingScenarioResult(
                "result_12",
                12,
                "groups_a_b_c_12_slots",
                "Groups A/B/C, 12 slots",
                new DateTimeOffset(2026, 5, 26, 12, 30, 0, TimeSpan.Zero),
                "partial"),
            CreatePassingScenarioResult(
                "result_16",
                16,
                "groups_a_b_c_d_16_slots",
                "Groups A/B/C/D, 16 slots",
                new DateTimeOffset(2026, 5, 26, 12, 45, 0, TimeSpan.Zero),
                "partial"),
            CreatePassingScenarioResult(
                "result_9",
                9,
                "groups_a_b_c_9_slot_threshold",
                "Groups A/B/C, 9-slot success threshold",
                new DateTimeOffset(2026, 5, 26, 13, 0, 0, TimeSpan.Zero))
        ];
    }

    private static FeasibilityTestResult CreatePassingScenarioResult(
        string id,
        int playbackCount,
        string scenarioId,
        string scenarioName,
        DateTimeOffset capturedAt,
        string? outcome = null)
    {
        return new FeasibilityTestResult
        {
            Id = id,
            CapturedAt = capturedAt,
            PlaybackCount = playbackCount,
            ScenarioId = scenarioId,
            ScenarioName = scenarioName,
            Outcome = outcome ?? (playbackCount >= 9 ? "success" : "partial"),
            Diagnostics = new RuntimeDiagnosticsSnapshot(
                capturedAt,
                WebViewProcessCount: playbackCount,
                WebViewWorkingSetMegabytes: 1024,
                WebViewPrivateMemoryMegabytes: 800,
                WebViewCpuPercent: 30),
            IsSameAccountSessionMaintained = true,
            AccountLabel = "main_soop",
            VerifiedProfileGroups = FeasibilityProfileGroupEvidenceService.GetRequiredGroupsForPlaybackCount(playbackCount),
            IsRestartSessionMaintained = true,
            IsResourceUsageAcceptable = true,
            ObservedCpuPercent = 45.5,
            ObservedGpuPercent = 60,
            ObservedMemoryMegabytes = 12000,
            Notes = "manual test passed"
        };
    }

    private static FeasibilityTestResult CreateFailureResult()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        return new FeasibilityTestResult
        {
            Id = "result_failure",
            CapturedAt = capturedAt,
            PlaybackCount = 9,
            ScenarioId = "groups_a_b_c_9_slot_threshold",
            ScenarioName = "Groups A/B/C, 9-slot success threshold",
            Outcome = "failure",
            Diagnostics = new RuntimeDiagnosticsSnapshot(
                capturedAt,
                WebViewProcessCount: 9,
                WebViewWorkingSetMegabytes: 1024,
                WebViewPrivateMemoryMegabytes: 800,
                WebViewCpuPercent: 30),
            IsSameAccountSessionMaintained = false,
            IsRestartSessionMaintained = false,
            IsResourceUsageAcceptable = false,
            ObservedCpuPercent = 45.5,
            ObservedGpuPercent = 60,
            ObservedMemoryMegabytes = 12000,
            Notes = "manual test failed"
        };
    }

    private static FeasibilityTestResult CreatePartialResult()
    {
        var capturedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
        return new FeasibilityTestResult
        {
            Id = "result_partial",
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
                WebViewCpuPercent: 30),
            IsSameAccountSessionMaintained = true,
            AccountLabel = "main_soop",
            VerifiedProfileGroups = ["A", "B", "C"],
            IsRestartSessionMaintained = false,
            IsResourceUsageAcceptable = true,
            ObservedCpuPercent = 45.5,
            ObservedGpuPercent = null,
            ObservedMemoryMegabytes = 12000,
            Notes = "manual test needs restart verification"
        };
    }
}
