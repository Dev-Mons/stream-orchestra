using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void Execute_StatusUsesLaterRecordedResultWhenTimestampsMatch()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreateSuccessfulResult());
        storage.AppendResult(CreateFailureResult());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["status", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Latest result: failure, 9 slot(s)", text);
        Assert.Contains("Notes: manual test failed", text);
        Assert.DoesNotContain("Notes: manual test passed", text);
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
    public void Execute_HistoryOrdersSameTimestampResultsByLaterRecordFirst()
    {
        var storage = new FeasibilityResultStorageService(_dataFolder);
        storage.AppendResult(CreateSuccessfulResult());
        storage.AppendResult(CreateFailureResult());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["history", "--data-folder", _dataFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.True(
            text.IndexOf("Id: result_failure", StringComparison.Ordinal) <
            text.IndexOf("Id: result_1", StringComparison.Ordinal));
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
        Assert.Contains("Run `handoff --output-folder <path>` and `validate-handoff --input-folder <path>`", text);
        Assert.Contains("`handoff`, `validate-handoff`", text);
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
        Assert.Contains("validate-handoff --input-folder <path>", fileText);
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
        Assert.Contains("Data storage: [ready]", text);
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
        Assert.Contains("Data storage: [ready]", fileText);
        Assert.Contains($"Profile root: {profileFolder}", fileText);
        Assert.Contains("WebView2 runtime: [", fileText);
        Assert.Contains("Layouts: [ready]", fileText);
        Assert.Contains("Evidence recorded: 0", fileText);
        Assert.Contains("Plan verification: [pending]", fileText);
        Assert.Contains("Suggested record shapes:", fileText);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_PreflightWithFileDataFolder_PrintsBlockedStorageStatus()
    {
        Directory.CreateDirectory(_dataFolder);
        var dataFolderFile = Path.Combine(_dataFolder, "data-folder-file");
        File.WriteAllText(dataFolderFile, "not a directory");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            ["preflight", "--data-folder", dataFolderFile],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("Stream Orchestra Feasibility Preflight", text);
        Assert.Contains($"Data folder: {dataFolderFile}", text);
        Assert.Contains($"Results file: {Path.Combine(dataFolderFile, "feasibility-results.json")}", text);
        Assert.Contains("Data storage: [blocked]", text);
        Assert.Contains("Evidence recorded: 0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Suggested record shapes:", text);
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
        Assert.Contains("\"dataStorageStatus\": \"[ready] data folder is writable for feasibility artifacts.\"", manifestText);
        Assert.Contains($"\"profileRootFolder\": \"{JsonEncodedText.Encode(profileFolder)}\"", manifestText);
        Assert.Contains("\"webView2RuntimeStatus\":", manifestText);
        Assert.Contains("\"playbackLayoutStatus\":", manifestText);
        Assert.Contains("\"profileGroups\":", manifestText);
        Assert.Contains("\"id\": \"A\"", manifestText);
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
    public void Execute_HandoffWithFileDataFolder_WritesBlockedStorageBundle()
    {
        Directory.CreateDirectory(_dataFolder);
        var dataFolderFile = Path.Combine(_dataFolder, "data-folder-file");
        var profileFolder = Path.Combine(_dataFolder, "Profiles");
        var handoffFolder = Path.Combine(_dataFolder, "handoff-blocked-storage");
        File.WriteAllText(dataFolderFile, "not a directory");
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var validationOutput = new StringWriter();
        using var validationError = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            [
                "handoff",
                "--data-folder",
                dataFolderFile,
                "--profile-folder",
                profileFolder,
                "--output-folder",
                handoffFolder
            ],
            output,
            error);
        var validationExitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            validationOutput,
            validationError);

        var preflightText = File.ReadAllText(Path.Combine(handoffFolder, "phase0-preflight.txt"));
        var manifestText = File.ReadAllText(Path.Combine(handoffFolder, "phase0-handoff-manifest.json"));
        var diagnosticReportText = File.ReadAllText(Path.Combine(handoffFolder, "phase0-diagnostic-report.json"));
        var validationText = validationOutput.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(0, validationExitCode);
        Assert.Contains("Preflight ready: False", output.ToString());
        Assert.Contains($"Results snapshot source: {Path.Combine(dataFolderFile, "feasibility-results.json")}", output.ToString());
        Assert.Contains($"Data folder: {dataFolderFile}", preflightText);
        Assert.Contains("Data storage: [blocked]", preflightText);
        Assert.Contains("Evidence recorded: 0", preflightText);
        Assert.Contains("\"dataStorageStatus\": \"[blocked]", manifestText);
        Assert.Contains($"\"dataFolder\": \"{JsonEncodedText.Encode(dataFolderFile)}\"", manifestText);
        Assert.Contains("\"resultCount\": 0", manifestText);
        Assert.Contains("\"isPreflightReady\": false", manifestText);
        Assert.Contains("\"feasibilityResultCount\": 0", diagnosticReportText);
        Assert.Contains($"\"dataFolder\": \"{JsonEncodedText.Encode(dataFolderFile)}\"", diagnosticReportText);
        Assert.Equal("[]" + Environment.NewLine, File.ReadAllText(Path.Combine(handoffFolder, "phase0-results.json")));
        Assert.Contains("- [pass] phase0-handoff-manifest.json data storage status: [blocked]", validationText);
        Assert.Contains("- [pass] phase0-preflight.txt data storage: [blocked]", validationText);
        Assert.Contains("Validation: pass", validationText);
        Assert.Equal("", error.ToString());
        Assert.Equal("", validationError.ToString());
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
        Assert.Contains("- [pass] phase0-handoff-manifest.json canonical content.", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json generatedAt:", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json data folder path:", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json results file path:", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json profile root path:", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json data storage status: [ready]", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json results file belongs to data folder.", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json artifactFiles standard order.", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json artifactDetails standard order.", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json profile groups: A, B, C, D", text);
        Assert.Contains("- [pass] handoff folder contains only standard artifacts.", text);
        Assert.Contains("- [pass] phase0-results.json:", text);
        Assert.Contains("- [pass] phase0-preflight.txt data folder:", text);
        Assert.Contains("- [pass] phase0-preflight.txt results file:", text);
        Assert.Contains("- [pass] phase0-preflight.txt data storage: [ready]", text);
        Assert.Contains("- [pass] phase0-preflight.txt profile root:", text);
        Assert.Contains("- [pass] phase0-preflight.txt WebView2 runtime:", text);
        Assert.Contains("- [pass] phase0-preflight.txt layouts:", text);
        Assert.Contains("- [pass] phase0-preflight.txt readiness:", text);
        Assert.Contains("- [pass] phase0-preflight.txt profile groups: 4", text);
        Assert.Contains("- [pass] phase0-preflight.txt content matches manifest and results snapshot.", text);
        Assert.Contains("- [pass] phase0-results.json normalized snapshot content.", text);
        Assert.Contains("- [pass] phase0-results.json result count: 0", text);
        Assert.Contains("- [pass] phase0-handoff-manifest.json isVerified: False", text);
        Assert.Contains("- [pass] phase0-checklist.txt content matches results snapshot.", text);
        Assert.Contains("- [pass] phase0-audit.txt content matches results snapshot.", text);
        Assert.Contains("- [pass] phase0-verification.txt plan status: pending", text);
        Assert.Contains("- [pass] phase0-verification.txt completion: False", text);
        Assert.Contains("- [pass] phase0-verification.txt content matches results snapshot.", text);
        Assert.Contains("- [pass] phase0-history.txt content matches results snapshot.", text);
        Assert.Contains("- [pass] diagnostic report result count: 0", text);
        Assert.Contains("- [pass] phase0-diagnostic-report.json generatedAt:", text);
        Assert.Contains("- [pass] diagnostic report data folder:", text);
        Assert.Contains("- [pass] diagnostic report data files standard entries.", text);
        Assert.Contains("- [pass] diagnostic report results file:", text);
        Assert.Contains("- [pass] diagnostic report profile root:", text);
        Assert.Contains("- [pass] diagnostic report profile groups: 4", text);
        Assert.Contains("- [pass] diagnostic report workspace diagnostics:", text);
        Assert.Contains("- [pass] diagnostic report external browser fallback plan:", text);
        Assert.Contains("- [pass] diagnostic report external browsers:", text);
        Assert.Contains("- [pass] diagnostic report decision: 검증 대기 (pending)", text);
        Assert.Contains("- [pass] diagnostic report plan gates: pass=0, pending=11, fail=0, outstanding=11, status=pending", text);
        Assert.Contains("- [pass] diagnostic report decision details: 검증 대기 (pending)", text);
        Assert.Contains("- [pass] diagnostic report audit items: 11", text);
        Assert.Contains("- [pass] diagnostic report latest result: n/a", text);
        Assert.Contains("- [pass] diagnostic report account labels: n/a", text);
        Assert.Contains("- [pass] diagnostic report account label conflict: False", text);
        Assert.Contains("- [pass] diagnostic report suggested records:", text);
        Assert.Contains("Validation: pass", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsDiagnosticGeneratedAtMismatch()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-diagnostic-generated-at-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var diagnosticReportPath = Path.Combine(handoffFolder, "phase0-diagnostic-report.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var diagnosticReport = JsonNode.Parse(File.ReadAllText(diagnosticReportPath))!.AsObject();
        diagnosticReport["generatedAt"] = JsonValue.Create("2000-01-01T00:00:00+00:00");
        File.WriteAllText(
            diagnosticReportPath,
            diagnosticReport.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-diagnostic-report.json");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-diagnostic-report.json generatedAt outside handoff window", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsDiagnosticWorkspaceAndExternalBrowserMismatches()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-diagnostic-workspace-browser-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var diagnosticReportPath = Path.Combine(handoffFolder, "phase0-diagnostic-report.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var diagnosticReport = JsonNode.Parse(File.ReadAllText(diagnosticReportPath))!.AsObject();
        diagnosticReport["workspaceDiagnostics"] = new JsonObject
        {
            ["savedWorkspaceCount"] = JsonValue.Create(-1),
            ["favoriteCount"] = JsonValue.Create(-2),
            ["hasLastSession"] = JsonValue.Create(false),
            ["lastWorkspaceId"] = JsonValue.Create("workspace_tampered"),
            ["selectedSlotId"] = JsonValue.Create(99),
            ["lastSessionLayoutId"] = JsonValue.Create("layout_4x4"),
            ["lastSessionSlotCount"] = JsonValue.Create(3),
            ["lastSessionActiveStreamCount"] = JsonValue.Create(4)
        };
        diagnosticReport["externalBrowsers"] = new JsonArray(
            new JsonObject
            {
                ["id"] = JsonValue.Create("edge"),
                ["name"] = JsonValue.Create(""),
                ["isInstalled"] = JsonValue.Create(true),
                ["executablePath"] = JsonValue.Create(""),
                ["candidatePaths"] = new JsonArray(JsonValue.Create(""))
            },
            new JsonObject
            {
                ["id"] = JsonValue.Create("edge"),
                ["name"] = JsonValue.Create("Duplicate Edge"),
                ["isInstalled"] = JsonValue.Create(false),
                ["executablePath"] = null,
                ["candidatePaths"] = null
            },
            new JsonObject
            {
                ["id"] = JsonValue.Create("chrome"),
                ["name"] = JsonValue.Create("Chrome"),
                ["isInstalled"] = JsonValue.Create(true),
                ["executablePath"] = JsonValue.Create(@"C:\Browsers\chrome.exe"),
                ["candidatePaths"] = new JsonArray(JsonValue.Create(@"C:\Browsers\chrome.exe"))
            });
        diagnosticReport["externalBrowserFallbackPlan"] = new JsonObject
        {
            ["canLaunch"] = JsonValue.Create(true),
            ["reason"] = JsonValue.Create(""),
            ["installedBrowserCount"] = JsonValue.Create(99),
            ["plannedSlotCount"] = JsonValue.Create(3),
            ["slots"] = new JsonArray(
                new JsonObject
                {
                    ["slotId"] = JsonValue.Create(1),
                    ["streamName"] = JsonValue.Create("Tampered Chrome"),
                    ["streamUrl"] = JsonValue.Create("https://example.com/live"),
                    ["browserId"] = JsonValue.Create("chrome"),
                    ["browserName"] = JsonValue.Create("Wrong Chrome"),
                    ["executablePath"] = JsonValue.Create(@"C:\Tampered\chrome.exe"),
                    ["userDataFolder"] = JsonValue.Create(@"C:\Tampered\Profile"),
                    ["arguments"] = new JsonArray(
                        JsonValue.Create("--new-window"),
                        JsonValue.Create("--mute-audio")),
                    ["windowLayout"] = null,
                    ["isMuted"] = JsonValue.Create(false)
                },
                new JsonObject
                {
                    ["slotId"] = JsonValue.Create(17),
                    ["streamName"] = JsonValue.Create("Tampered"),
                    ["streamUrl"] = JsonValue.Create("ftp://example.com/live"),
                    ["browserId"] = JsonValue.Create("missing"),
                    ["browserName"] = JsonValue.Create(""),
                    ["executablePath"] = JsonValue.Create(""),
                    ["userDataFolder"] = JsonValue.Create(""),
                    ["arguments"] = new JsonArray(JsonValue.Create("")),
                    ["windowLayout"] = new JsonObject
                    {
                        ["gridColumns"] = JsonValue.Create(4),
                        ["gridRows"] = JsonValue.Create(3),
                        ["x"] = JsonValue.Create(3),
                        ["y"] = JsonValue.Create(2),
                        ["w"] = JsonValue.Create(2),
                        ["h"] = JsonValue.Create(2)
                    },
                    ["isMuted"] = JsonValue.Create(false)
                })
        };
        File.WriteAllText(
            diagnosticReportPath,
            diagnosticReport.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-diagnostic-report.json");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("diagnostic report workspace saved workspace count is negative: -1.", text);
        Assert.Contains("diagnostic report workspace favorite count is negative: -2.", text);
        Assert.Contains("diagnostic report workspace selected slot is outside 1-16: 99.", text);
        Assert.Contains("diagnostic report workspace active stream count exceeds slot count: 4/3.", text);
        Assert.Contains("diagnostic report workspace says no last session but includes last-session details.", text);
        Assert.Contains("diagnostic report external browser edge name is missing.", text);
        Assert.Contains("diagnostic report external browser edge has a blank candidate path.", text);
        Assert.Contains("diagnostic report external browser edge is installed but executable path is missing.", text);
        Assert.Contains("diagnostic report external browser edge is duplicated.", text);
        Assert.Contains("diagnostic report external browser fallback reason is missing.", text);
        Assert.Contains("diagnostic report external browser fallback installed count mismatch, expected 2, actual 99.", text);
        Assert.Contains("diagnostic report external browser fallback planned slot count mismatch, expected 2, actual 3.", text);
        Assert.Contains("diagnostic report external browser fallback slot 1 browser name mismatch, expected Chrome, actual Wrong Chrome.", text);
        Assert.Contains("diagnostic report external browser fallback slot 1 executable path mismatch", text);
        Assert.Contains("diagnostic report external browser fallback slot 1 user data folder mismatch", text);
        Assert.Contains("diagnostic report external browser fallback slot 1 is missing its user-data-dir argument.", text);
        Assert.Contains("diagnostic report external browser fallback slot 1 is missing its stream URL argument.", text);
        Assert.Contains("diagnostic report external browser fallback slot 1 is not muted but includes the --mute-audio argument.", text);
        Assert.Contains("diagnostic report external browser fallback slot id is outside 1-16: 17.", text);
        Assert.Contains("diagnostic report external browser fallback slot 17 URL is not HTTP/HTTPS.", text);
        Assert.Contains("diagnostic report external browser fallback slot 17 browser missing is not installed in the diagnostic snapshot.", text);
        Assert.Contains("diagnostic report external browser fallback slot 17 browser name is missing.", text);
        Assert.Contains("diagnostic report external browser fallback slot 17 executable path is missing.", text);
        Assert.Contains("diagnostic report external browser fallback slot 17 user data folder is missing.", text);
        Assert.Contains("diagnostic report external browser fallback slot 17 has a blank argument.", text);
        Assert.Contains("diagnostic report external browser fallback slot 17 window layout is invalid.", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsDiagnosticDataFileMismatches()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-diagnostic-data-file-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var diagnosticReportPath = Path.Combine(handoffFolder, "phase0-diagnostic-report.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var diagnosticReport = JsonNode.Parse(File.ReadAllText(diagnosticReportPath))!.AsObject();
        var dataFiles = diagnosticReport["dataFiles"]!.AsArray();
        for (var index = dataFiles.Count - 1; index >= 0; index--)
        {
            var dataFileObject = dataFiles[index]!.AsObject();
            var name = dataFileObject["name"]?.GetValue<string>();
            if (name == "appstate")
            {
                dataFileObject["path"] = JsonValue.Create(@"C:\tampered-data\appstate.json");
                dataFileObject["sizeBytes"] = JsonValue.Create(-1);
                dataFiles.Add(new JsonObject
                {
                    ["name"] = "appstate",
                    ["path"] = JsonValue.Create(Path.Combine(_dataFolder, "duplicate-appstate.json")),
                    ["exists"] = JsonValue.Create(false),
                    ["sizeBytes"] = JsonValue.Create(0)
                });
            }
            else if (name == "favorites")
            {
                dataFiles.RemoveAt(index);
            }
        }

        dataFiles.Add(new JsonObject
        {
            ["name"] = "unexpected",
            ["path"] = JsonValue.Create(Path.Combine(_dataFolder, "unexpected.json")),
            ["exists"] = JsonValue.Create(false),
            ["sizeBytes"] = JsonValue.Create(0)
        });
        File.WriteAllText(
            diagnosticReportPath,
            diagnosticReport.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-diagnostic-report.json");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("diagnostic report data file appstate has negative size -1.", text);
        Assert.Contains("diagnostic report data file appstate is duplicated.", text);
        Assert.Contains("diagnostic report data file appstate path mismatch", text);
        Assert.Contains("diagnostic report data file favorites is missing.", text);
        Assert.Contains("diagnostic report data file unexpected is unexpected.", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsManifestContextMismatches()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-context-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["generatedAt"] = JsonValue.Create("0001-01-01T00:00:00+00:00");
        manifest["dataFolder"] = JsonValue.Create("relative-data");
        manifest["resultsFilePath"] = JsonValue.Create(Path.Combine(_dataFolder, "wrong-results.json"));
        File.WriteAllText(
            manifestPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-handoff-manifest.json generatedAt is missing or default.", text);
        Assert.Contains("phase0-handoff-manifest.json data folder path is missing or not fully qualified: relative-data.", text);
        Assert.Contains("phase0-handoff-manifest.json results file path mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsNonCanonicalManifestContent()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-noncanonical-manifest");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["extraHiddenField"] = JsonValue.Create("tampered");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-handoff-manifest.json canonical content mismatch.", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoffWithOutput_WritesValidationTextFile()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-validation-output");
        var validationOutputPath = Path.Combine(_dataFolder, "phase0-handoff-validation.txt");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder, "--output", validationOutputPath],
            output,
            error);

        var text = output.ToString();
        var fileText = File.ReadAllText(validationOutputPath);
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(0, exitCode);
        Assert.Contains($"Handoff validation saved: {validationOutputPath}", text);
        Assert.Contains("Stream Orchestra Phase 0 Handoff Validation", fileText);
        Assert.Contains("Validation: pass", fileText);
        Assert.DoesNotContain("Handoff validation saved:", fileText);
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
    public void Execute_ValidateHandoff_DetectsManifestSummaryMismatch()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-summary-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var manifestText = File.ReadAllText(manifestPath).Replace("\"resultCount\": 0", "\"resultCount\": 1");
        File.WriteAllText(manifestPath, manifestText);

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-results.json result count mismatch, expected 1, actual 0", text);
        Assert.Contains("diagnostic report result count mismatch, expected 1, actual 0", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_RecomputesSummaryFromResultsSnapshot()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-recomputed-summary-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var diagnosticReportPath = Path.Combine(handoffFolder, "phase0-diagnostic-report.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var diagnosticReport = JsonNode.Parse(File.ReadAllText(diagnosticReportPath))!.AsObject();

        manifest["decisionCode"] = JsonValue.Create("continue_webview2_mvp");
        manifest["decisionTitle"] = JsonValue.Create("WebView2 MVP 계속");
        manifest["planVerificationStatus"] = JsonValue.Create("pass");
        manifest["passingGateCount"] = JsonValue.Create(11);
        manifest["pendingGateCount"] = JsonValue.Create(0);
        manifest["failingGateCount"] = JsonValue.Create(0);
        manifest["outstandingGateCount"] = JsonValue.Create(0);
        diagnosticReport["feasibilityDecision"] = new JsonObject
        {
            ["code"] = "continue_webview2_mvp",
            ["title"] = "WebView2 MVP 계속",
            ["detail"] = "tampered summary",
            ["nextAction"] = "tampered summary"
        };

        foreach (var item in diagnosticReport["feasibilityAudit"]!.AsArray())
        {
            var itemObject = item!.AsObject();
            itemObject["status"] = JsonValue.Create("pass");
            itemObject["evidence"] = JsonValue.Create("tampered summary");
        }

        File.WriteAllText(
            diagnosticReportPath,
            diagnosticReport.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-diagnostic-report.json");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains(
            "phase0-results.json decision mismatch, expected 검증 대기 (pending), actual WebView2 MVP 계속 (continue_webview2_mvp).",
            text);
        Assert.Contains(
            "phase0-results.json plan gates mismatch, expected pass=0, pending=11, fail=0, outstanding=11, status=pending; actual pass=11, pending=0, fail=0, outstanding=0, status=pass.",
            text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsNonCanonicalResultsSnapshot()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-noncanonical-results");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var resultsPath = Path.Combine(handoffFolder, "phase0-results.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        File.WriteAllText(resultsPath, JsonNode.Parse(File.ReadAllText(resultsPath))!.ToJsonString());
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-results.json");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-results.json normalized snapshot content mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsPreflightContextMismatch()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-preflight-context-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var preflightPath = Path.Combine(handoffFolder, "phase0-preflight.txt");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        File.WriteAllText(
            preflightPath,
            File.ReadAllText(preflightPath)
                .Replace($"Data folder: {_dataFolder}", "Data folder: C:\\tampered-data")
                .Replace(
                    $"Results file: {Path.Combine(_dataFolder, "feasibility-results.json")}",
                    "Results file: C:\\tampered-data\\feasibility-results.json"));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-preflight.txt");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-preflight.txt data folder mismatch", text);
        Assert.Contains("phase0-preflight.txt results file mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsPreflightDataStorageMismatch()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-preflight-storage-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var preflightPath = Path.Combine(handoffFolder, "phase0-preflight.txt");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        File.WriteAllText(
            preflightPath,
            File.ReadAllText(preflightPath)
                .Replace(
                    "Data storage: [ready] data folder is writable for feasibility artifacts.",
                    "Data storage: [blocked] tampered storage status."));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-preflight.txt");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-preflight.txt data storage mismatch", text);
        Assert.Contains("phase0-preflight.txt readiness mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsPreflightContentMismatch()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-preflight-content-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var preflightPath = Path.Combine(handoffFolder, "phase0-preflight.txt");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        File.WriteAllText(
            preflightPath,
            File.ReadAllText(preflightPath)
                .Replace("Evidence recorded: 0", "Evidence recorded: 1"));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-preflight.txt");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-preflight.txt content mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsDiagnosticContextMismatches()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-diagnostic-context-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var diagnosticReportPath = Path.Combine(handoffFolder, "phase0-diagnostic-report.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var diagnosticReport = JsonNode.Parse(File.ReadAllText(diagnosticReportPath))!.AsObject();
        diagnosticReport["dataFolder"] = JsonValue.Create(@"C:\tampered-data");
        diagnosticReport["profileRootFolder"] = JsonValue.Create(@"C:\tampered-profiles");
        foreach (var profileGroup in diagnosticReport["profileGroups"]!.AsArray())
        {
            var profileGroupObject = profileGroup!.AsObject();
            if (profileGroupObject["id"]?.GetValue<string>() == "A")
            {
                profileGroupObject["userDataFolder"] = JsonValue.Create(@"C:\tampered-profiles\GroupA");
                break;
            }
        }

        foreach (var dataFile in diagnosticReport["dataFiles"]!.AsArray())
        {
            var dataFileObject = dataFile!.AsObject();
            if (dataFileObject["name"]?.GetValue<string>() == "feasibility-results")
            {
                dataFileObject["path"] = JsonValue.Create(@"C:\tampered-data\feasibility-results.json");
                break;
            }
        }

        File.WriteAllText(
            diagnosticReportPath,
            diagnosticReport.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-diagnostic-report.json");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("diagnostic report data folder mismatch", text);
        Assert.Contains("diagnostic report results file mismatch", text);
        Assert.Contains("diagnostic report profile root mismatch", text);
        Assert.Contains("diagnostic report profile group A mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsDiagnosticSnapshotMismatches()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-diagnostic-snapshot-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var diagnosticReportPath = Path.Combine(handoffFolder, "phase0-diagnostic-report.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var diagnosticReport = JsonNode.Parse(File.ReadAllText(diagnosticReportPath))!.AsObject();
        diagnosticReport["latestFeasibilityResult"] = new JsonObject
        {
            ["id"] = "tampered",
            ["capturedAt"] = "2026-05-26T00:00:00+00:00",
            ["playbackCount"] = 9,
            ["scenarioId"] = "groups_a_b_c_9_slot_threshold",
            ["scenarioName"] = "Groups A/B/C, 9-slot success threshold",
            ["outcome"] = "success"
        };
        diagnosticReport["feasibilitySameAccountLabels"] = new JsonArray(JsonValue.Create("tampered"));
        diagnosticReport["hasConflictingFeasibilityAccountLabels"] = JsonValue.Create(true);
        diagnosticReport["feasibilitySuggestedRecordShapes"] = new JsonArray();
        File.WriteAllText(
            diagnosticReportPath,
            diagnosticReport.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-diagnostic-report.json");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("diagnostic report latest result mismatch", text);
        Assert.Contains("diagnostic report account labels mismatch", text);
        Assert.Contains("diagnostic report account label conflict mismatch", text);
        Assert.Contains("diagnostic report suggested records mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsDiagnosticDecisionAndAuditMismatches()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-diagnostic-decision-audit-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var diagnosticReportPath = Path.Combine(handoffFolder, "phase0-diagnostic-report.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var diagnosticReport = JsonNode.Parse(File.ReadAllText(diagnosticReportPath))!.AsObject();
        diagnosticReport["feasibilityDecision"] = new JsonObject
        {
            ["code"] = "pending",
            ["title"] = "검증 대기",
            ["detail"] = "tampered detail",
            ["nextAction"] = "tampered next action"
        };
        diagnosticReport["feasibilityAudit"]!.AsArray()[0]!.AsObject()["evidence"] =
            JsonValue.Create("tampered audit evidence");
        File.WriteAllText(
            diagnosticReportPath,
            diagnosticReport.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-diagnostic-report.json");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("diagnostic report decision details mismatch", text);
        Assert.Contains("diagnostic report audit items mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsChecklistMismatch()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-checklist-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var checklistPath = Path.Combine(handoffFolder, "phase0-checklist.txt");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        File.WriteAllText(
            checklistPath,
            File.ReadAllText(checklistPath)
                .Replace("Results recorded: 0", "Results recorded: 1"));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-checklist.txt");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-checklist.txt content mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsAuditAndHistoryMismatches()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-audit-history-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var auditPath = Path.Combine(handoffFolder, "phase0-audit.txt");
        var historyPath = Path.Combine(handoffFolder, "phase0-history.txt");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        File.WriteAllText(
            auditPath,
            File.ReadAllText(auditPath)
                .Replace("Plan audit: pass=0, pending=11, fail=0", "Plan audit: pass=11, pending=0, fail=0"));
        File.WriteAllText(
            historyPath,
            File.ReadAllText(historyPath)
                .Replace("Results recorded: 0", "Results recorded: 1"));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-audit.txt");
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-history.txt");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-audit.txt content mismatch", text);
        Assert.Contains("phase0-history.txt content mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsVerificationContentMismatch()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-verification-content-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var verificationPath = Path.Combine(handoffFolder, "phase0-verification.txt");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        File.WriteAllText(
            verificationPath,
            File.ReadAllText(verificationPath)
                .Replace("Required evidence: record live SOOP", "Required evidence: tampered SOOP"));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-verification.txt");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-verification.txt content mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsReadinessAndVerificationMismatches()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-boolean-mismatch");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var verificationPath = Path.Combine(handoffFolder, "phase0-verification.txt");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var isPreflightReady = manifest["isPreflightReady"]!.GetValue<bool>();
        manifest["isPreflightReady"] = JsonValue.Create(!isPreflightReady);
        manifest["isVerified"] = JsonValue.Create(true);
        File.WriteAllText(
            verificationPath,
            File.ReadAllText(verificationPath)
                .Replace("Plan verification: [pending]", "Plan verification: [pass]")
                .Replace("Verification: not complete", "Verification: pass"));
        UpdateManifestArtifactMetadata(manifest, handoffFolder, "phase0-verification.txt");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-preflight.txt readiness mismatch", text);
        Assert.Contains("phase0-handoff-manifest.json isVerified mismatch, expected False from results, actual True.", text);
        Assert.Contains("phase0-verification.txt plan status mismatch, expected pending from results, actual pass.", text);
        Assert.Contains("phase0-verification.txt completion mismatch, expected False from results, actual True.", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsNonStandardManifestArtifactOrder()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-artifact-order");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var artifactFiles = manifest["artifactFiles"]!.AsArray();
        var firstArtifactFile = artifactFiles[0]!.GetValue<string>();
        artifactFiles[0] = JsonValue.Create(artifactFiles[1]!.GetValue<string>());
        artifactFiles[1] = JsonValue.Create(firstArtifactFile);
        var artifactDetails = manifest["artifactDetails"]!.AsArray();
        var firstArtifactDetail = artifactDetails[0]!.DeepClone();
        artifactDetails[0] = artifactDetails[1]!.DeepClone();
        artifactDetails[1] = firstArtifactDetail;
        File.WriteAllText(
            manifestPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-handoff-manifest.json artifactFiles order mismatch", text);
        Assert.Contains("phase0-handoff-manifest.json artifactDetails order mismatch", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsMissingRequiredArtifactListing()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-missing-artifact-listing");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var artifactFiles = manifest["artifactFiles"]!.AsArray();
        var resultsIndex = artifactFiles
            .Select((node, index) => new { FileName = node!.GetValue<string>(), Index = index })
            .Single(item => item.FileName == "phase0-results.json")
            .Index;
        artifactFiles.RemoveAt(resultsIndex);
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-results.json: missing from artifactFiles.", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsUnexpectedManifestArtifacts()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-unexpected-artifacts");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var extraArtifactPath = Path.Combine(handoffFolder, "phase0-extra.txt");
        File.WriteAllText(extraArtifactPath, "unexpected");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["artifactFiles"]!.AsArray().Add(JsonValue.Create("phase0-extra.txt"));
        manifest["artifactDetails"]!.AsArray().Add(new JsonObject
        {
            ["fileName"] = "phase0-extra.txt",
            ["sizeBytes"] = new FileInfo(extraArtifactPath).Length,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(extraArtifactPath))).ToLowerInvariant()
        });
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-extra.txt: unexpected artifactFiles entry.", text);
        Assert.Contains("phase0-extra.txt: unexpected artifactDetails entry.", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsUnexpectedFolderFiles()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-unexpected-folder-file");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        File.WriteAllText(Path.Combine(handoffFolder, "phase0-extra.txt"), "unexpected");

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-extra.txt: unexpected file in handoff folder.", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsDuplicateManifestEntries()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-duplicate-artifacts");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var artifactFiles = manifest["artifactFiles"]!.AsArray();
        artifactFiles.Add(JsonValue.Create("phase0-results.json"));
        var artifactDetails = manifest["artifactDetails"]!.AsArray();
        var resultsDetail = artifactDetails
            .Single(node => node!["fileName"]!.GetValue<string>() == "phase0-results.json")!
            .DeepClone();
        artifactDetails.Add(resultsDetail);
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-results.json: duplicate artifactFiles entry.", text);
        Assert.Contains("phase0-results.json: duplicate artifactDetails entry.", text);
        Assert.Contains("Validation: fail", text);
        Assert.Equal("", handoffError.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Execute_ValidateHandoff_DetectsInvalidManifestProfileGroups()
    {
        var handoffFolder = Path.Combine(_dataFolder, "handoff-invalid-profile-groups");
        using var handoffOutput = new StringWriter();
        using var handoffError = new StringWriter();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handoffExitCode = FeasibilityStatusCommand.Execute(
            ["handoff", "--data-folder", _dataFolder, "--output-folder", handoffFolder],
            handoffOutput,
            handoffError);
        var manifestPath = Path.Combine(handoffFolder, "phase0-handoff-manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var profileGroup = manifest["profileGroups"]!.AsArray()[0]!.AsObject();
        profileGroup["id"] = JsonValue.Create("X");
        profileGroup["userDataFolder"] = JsonValue.Create(Path.Combine(_dataFolder, "Profiles", "GroupX"));
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = FeasibilityStatusCommand.Execute(
            ["validate-handoff", "--input-folder", handoffFolder],
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, handoffExitCode);
        Assert.Equal(1, exitCode);
        Assert.Contains("phase0-handoff-manifest.json profile group X is unexpected.", text);
        Assert.Contains("phase0-handoff-manifest.json profile group A is missing.", text);
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
        Assert.Contains("Plan audit: pass=2, pending=9, fail=0", text);
        Assert.Contains("Plan verification: [pending]", text);
        Assert.Contains("Outstanding gates:", text);
        Assert.Contains("- [pending] App restart keeps login session", text);
        Assert.Contains("- [pending] CPU/GPU/memory acceptable", text);
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
    public void Execute_FallbackNormalizesBareDomainAndSkipsNonWebLastSessionUrls()
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
                        StreamName = " ",
                        StreamUrl = " example.com/live ",
                        ProfileGroupId = "A"
                    },
                    new WorkspaceSlot
                    {
                        SlotId = 2,
                        StreamName = "Script",
                        StreamUrl = "javascript:alert(1)",
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
        Assert.Contains("Planned slots: 1", text);
        Assert.Contains("[slot 1] live -> AAA Portable Browser (aaa_portable_browser), muted=False: https://example.com/live", text);
        Assert.DoesNotContain("javascript:alert", text);
        Assert.Contains("'\"https://example.com/live\"'", scriptText);
        Assert.Contains("'\"--user-data-dir=", scriptText);
        Assert.DoesNotContain("javascript:alert", scriptText);
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
    public void Execute_RecordDryRunWithFileDataFolder_ReturnsCommandFailure()
    {
        Directory.CreateDirectory(_dataFolder);
        var dataFolderFile = Path.Combine(_dataFolder, "data-folder-file");
        File.WriteAllText(dataFolderFile, "not a directory");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(
            [
                "record",
                "--count",
                "9",
                "--outcome",
                "partial",
                "--dry-run",
                "--data-folder",
                dataFolderFile
            ],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Equal("", output.ToString());
        Assert.Contains("Command failed:", error.ToString());
        Assert.Contains(dataFolderFile, error.ToString());
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
    [InlineData("record --count 9 --outcome success --account --profile-groups A,B,C --restart --resources --cpu-percent 45 --gpu-percent 60 --memory-mb 12000", "Same-account evidence requires an account label.")]
    [InlineData("record --count 9 --outcome partial --resources", "Resource OK requires CPU %, GPU %, and memory MB observations.")]
    [InlineData("record --count 9 --outcome failure --account --profile-groups A,B,C --account-label main_soop --restart", "Failure records cannot include restart evidence.")]
    [InlineData("record --count 9 --outcome failure --resources --cpu-percent 45 --gpu-percent 60 --memory-mb 12000", "Failure records cannot include resource OK evidence.")]
    [InlineData("record --count 9 --outcome success --account --restart --resources --cpu-percent 45 --gpu-percent 60 --memory-mb 12000", "Success requires same-account profile group evidence for groups A, B, C.")]
    [InlineData("record --count 9 --outcome partial --profile-groups A,Z", "Profile groups must be A, B, C, and/or D.")]
    [InlineData("record --count 17 --outcome success", "--count must be between 1 and 16.")]
    [InlineData("record --count 9 --outcome unknown", "--outcome must be success, partial, or failure.")]
    [InlineData("record --count 9 --outcome partial --cpu-percent 101", "--cpu-percent must be between 0 and 100.")]
    [InlineData("record --count 9 --outcome partial --cpu-percent NaN", "CPU % must be a finite number.")]
    [InlineData("record --count 9 --outcome partial --gpu-percent bad", "--gpu-percent requires a numeric value.")]
    [InlineData("record --count 9 --outcome partial --memory-mb -1", "--memory-mb must be 0 or higher.")]
    [InlineData("record --count 9 --outcome partial --account-label", "--account-label requires a value.")]
    [InlineData("record --count 9 --outcome partial --account-label main_soop", "Account label requires same-account evidence.")]
    [InlineData("record --count 9 --outcome partial --restart", "Restart evidence requires same-account evidence.")]
    [InlineData("record --count 9 --outcome partial --account --account-label main_soop", "Same-account evidence requires at least one verified profile group.")]
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
    public void Execute_ValidateHandoffOutputValidationErrors_ReturnUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = FeasibilityStatusCommand.Execute(["validate-handoff", "--input-folder", _dataFolder, "--output"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("--output requires a value.", error.ToString());
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

    private static void UpdateManifestArtifactMetadata(
        JsonObject manifest,
        string handoffFolder,
        string artifactFileName)
    {
        var artifactPath = Path.Combine(handoffFolder, artifactFileName);
        var fileInfo = new FileInfo(artifactPath);
        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifactPath))).ToLowerInvariant();
        var artifactDetail = manifest["artifactDetails"]!.AsArray()
            .Single(node => node!["fileName"]!.GetValue<string>() == artifactFileName)!
            .AsObject();

        artifactDetail["sizeBytes"] = JsonValue.Create(fileInfo.Length);
        artifactDetail["sha256"] = JsonValue.Create(sha256);
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
