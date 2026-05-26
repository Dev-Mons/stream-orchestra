using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tools;

public static class FeasibilityStatusCommand
{
    private const string HandoffPreflightFileName = "phase0-preflight.txt";
    private const string HandoffChecklistFileName = "phase0-checklist.txt";
    private const string HandoffAuditFileName = "phase0-audit.txt";
    private const string HandoffVerificationFileName = "phase0-verification.txt";
    private const string HandoffHistoryFileName = "phase0-history.txt";
    private const string HandoffDiagnosticReportFileName = "phase0-diagnostic-report.json";
    private const string HandoffResultsFileName = "phase0-results.json";
    private const string HandoffManifestFileName = "phase0-handoff-manifest.json";

    private static readonly JsonSerializerOptions HandoffJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly string[] RequiredHandoffArtifactFiles =
    [
        HandoffPreflightFileName,
        HandoffChecklistFileName,
        HandoffAuditFileName,
        HandoffVerificationFileName,
        HandoffHistoryFileName,
        HandoffDiagnosticReportFileName,
        HandoffResultsFileName
    ];

    public static int Execute(string[] args, TextWriter output, TextWriter error)
    {
        var parseResult = ParseArgs(args);
        if (!parseResult.IsValid)
        {
            error.WriteLine(parseResult.ErrorMessage);
            WriteUsage(error);
            return 2;
        }

        if (parseResult.ShowHelp)
        {
            WriteUsage(output);
            return 0;
        }

        return parseResult.Command switch
        {
            "audit" => PrintAudit(parseResult, output),
            "browsers" => PrintBrowsers(parseResult.DataFolder, output),
            "checklist" => PrintChecklist(parseResult, output),
            "fallback" => SaveFallbackScript(parseResult.DataFolder, output),
            "handoff" => SaveHandoff(parseResult, output),
            "history" => PrintHistory(parseResult.DataFolder, output),
            "preflight" => PrintPreflight(parseResult, output),
            "record" => RecordResult(parseResult, output),
            "report" => SaveReport(parseResult, output),
            "scenarios" => PrintScenarios(output),
            "validate-handoff" => ValidateHandoff(parseResult, output),
            "verify" => VerifyPlan(parseResult, output),
            _ => PrintStatus(parseResult.DataFolder, output)
        };
    }

    private static int PrintStatus(string? dataFolder, TextWriter output)
    {
        var storage = new FeasibilityResultStorageService(dataFolder);
        var results = storage.LoadResults();
        var decision = new FeasibilityDecisionService().Decide(results);
        var latest = results.OrderByDescending(result => result.CapturedAt).FirstOrDefault();
        var auditService = new FeasibilityAuditService();
        var auditItems = auditService.CreateAudit(results, decision);

        output.WriteLine("Stream Orchestra Feasibility Status");
        output.WriteLine($"Data folder: {storage.DataFolder}");
        output.WriteLine($"Results file: {storage.ResultsFilePath}");
        output.WriteLine($"Results recorded: {results.Count}");
        output.WriteLine($"Decision: {decision.Title} ({decision.Code})");
        output.WriteLine($"Detail: {decision.Detail}");
        WriteNextAction(decision, output);
        WriteAuditSummary(auditItems, output);
        WritePlanVerificationStatus(auditItems, output);
        WritePhase0SuccessGate(auditItems, output);
        WriteSuggestedRecordShapes(auditItems, output);

        if (latest is null)
        {
            output.WriteLine("Latest result: none");
            return 0;
        }

        output.WriteLine(
            $"Latest result: {latest.Outcome}, {latest.PlaybackCount} slot(s), {latest.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        output.WriteLine($"Scenario: {latest.ScenarioName} ({latest.ScenarioId})");
        output.WriteLine(
            $"Criteria: account={latest.IsSameAccountSessionMaintained}, restart={latest.IsRestartSessionMaintained}, resources={latest.IsResourceUsageAcceptable}");
        output.WriteLine($"Account label: {FormatAccountLabel(latest.AccountLabel)}");
        output.WriteLine($"Profile groups: {FeasibilityProfileGroupEvidenceService.FormatGroups(latest.VerifiedProfileGroups)}");
        output.WriteLine(
            $"Observed resources: cpu={FormatNullable(latest.ObservedCpuPercent)}%, gpu={FormatNullable(latest.ObservedGpuPercent)}%, memory={FormatNullable(latest.ObservedMemoryMegabytes)} MB");
        if (!string.IsNullOrWhiteSpace(latest.DecisionCode))
        {
            output.WriteLine($"Recorded decision: {latest.DecisionTitle} ({latest.DecisionCode})");
        }

        if (!string.IsNullOrWhiteSpace(latest.Notes))
        {
            output.WriteLine($"Notes: {latest.Notes}");
        }

        return 0;
    }

    private static int PrintHistory(string? dataFolder, TextWriter output)
    {
        WriteLines(CreateHistoryLines(dataFolder), output);
        return 0;
    }

    private static IReadOnlyList<string> CreateHistoryLines(string? dataFolder)
    {
        var storage = new FeasibilityResultStorageService(dataFolder);
        var results = storage.LoadResults()
            .OrderByDescending(result => result.CapturedAt)
            .ToArray();

        var lines = new List<string>
        {
            "Stream Orchestra Feasibility History",
            $"Data folder: {storage.DataFolder}",
            $"Results file: {storage.ResultsFilePath}",
            $"Results recorded: {results.Length}"
        };

        if (results.Length == 0)
        {
            lines.Add("No feasibility results recorded.");
            return lines;
        }

        foreach (var result in results)
        {
            lines.Add(
                $"[{result.CapturedAt:yyyy-MM-dd HH:mm:ss}] {result.Outcome}, {result.PlaybackCount} slot(s), {result.ScenarioName} ({result.ScenarioId})");
            lines.Add($"  Id: {result.Id}");
            lines.Add(
                $"  Criteria: account={result.IsSameAccountSessionMaintained}, restart={result.IsRestartSessionMaintained}, resources={result.IsResourceUsageAcceptable}");
            lines.Add($"  Account label: {FormatAccountLabel(result.AccountLabel)}");
            lines.Add($"  Profile groups: {FeasibilityProfileGroupEvidenceService.FormatGroups(result.VerifiedProfileGroups)}");
            lines.Add(
                $"  Observed resources: cpu={FormatNullable(result.ObservedCpuPercent)}%, gpu={FormatNullable(result.ObservedGpuPercent)}%, memory={FormatNullable(result.ObservedMemoryMegabytes)} MB");
            lines.Add(
                string.IsNullOrWhiteSpace(result.DecisionCode)
                    ? "  Recorded decision: n/a"
                    : $"  Recorded decision: {result.DecisionTitle} ({result.DecisionCode})");

            if (!string.IsNullOrWhiteSpace(result.DecisionNextAction))
            {
                lines.Add($"  Next action at record time: {result.DecisionNextAction}");
            }

            if (!string.IsNullOrWhiteSpace(result.Notes))
            {
                lines.Add($"  Notes: {result.Notes}");
            }
        }

        return lines;
    }

    private static int PrintAudit(ParseResult parseResult, TextWriter output)
    {
        var lines = CreateAuditLines(parseResult.DataFolder);
        WriteLines(lines, output);

        if (!string.IsNullOrWhiteSpace(parseResult.OutputPath))
        {
            SaveTextFile(parseResult.OutputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            output.WriteLine($"Audit saved: {parseResult.OutputPath}");
        }

        return 0;
    }

    private static IReadOnlyList<string> CreateAuditLines(string? dataFolder)
    {
        var storage = new FeasibilityResultStorageService(dataFolder);
        var results = storage.LoadResults();
        var decision = new FeasibilityDecisionService().Decide(results);
        var auditService = new FeasibilityAuditService();
        var auditItems = auditService.CreateAudit(results, decision);
        var summary = auditService.CreateSummary(auditItems);
        var successGate = auditItems.FirstOrDefault(item => item.Id == "phase0_success_gate");
        var lines = new List<string>
        {
            "Stream Orchestra Plan Audit",
            $"Data folder: {storage.DataFolder}",
            $"Results file: {storage.ResultsFilePath}",
            $"Results recorded: {results.Count}",
            $"Decision: {decision.Title} ({decision.Code})",
            $"Plan audit: {summary.ToCompactText()}",
            $"Plan verification: [{auditService.CreatePlanVerificationStatus(auditItems)}]"
        };
        if (!string.IsNullOrWhiteSpace(decision.NextAction))
        {
            lines.Add($"Next action: {decision.NextAction}");
        }

        if (successGate is not null)
        {
            lines.Add($"Success gate: [{successGate.Status}] {successGate.Evidence}");
        }

        lines.AddRange(auditItems.Select(item => $"[{item.Status}] {item.Title}: {item.Evidence}"));
        var suggestedRecordShapes = auditService.CreateSuggestedRecordShapes(auditItems);
        if (suggestedRecordShapes.Count > 0)
        {
            lines.Add("Suggested record shapes:");
            lines.AddRange(suggestedRecordShapes.Select(suggestion => $"- {suggestion}"));
        }

        return lines;
    }

    private static int SaveFallbackScript(string? dataFolder, TextWriter output)
    {
        var presetStorage = new PresetStorageService(dataFolder);
        var appState = presetStorage.LoadAppState();
        var exportService = new ExternalBrowserFallbackExportService();
        var exportResult = exportService.SaveScript(
            appState?.LastSession,
            presetStorage.DataFolder,
            DateTimeOffset.Now,
            layouts: TryLoadLayouts());

        output.WriteLine("Stream Orchestra External Browser Fallback");
        output.WriteLine($"Data folder: {presetStorage.DataFolder}");
        output.WriteLine($"App state file: {presetStorage.AppStateFilePath}");

        if (appState?.LastSession is null)
        {
            output.WriteLine("Last session: none");
            output.WriteLine($"External browser fallback script: not available ({exportResult.Reason})");
            return 1;
        }

        var plan = exportResult.Plan;

        output.WriteLine($"Last session: {appState.LastSession.Name} ({appState.LastSession.Id})");
        output.WriteLine($"Installed browsers: {plan?.InstalledBrowserCount ?? 0}");
        output.WriteLine($"Planned slots: {plan?.PlannedSlotCount ?? 0}");
        output.WriteLine($"Plan: {exportResult.Reason}");

        var planSlots = plan?.Slots.OrderBy(slot => slot.SlotId) ??
            Enumerable.Empty<ExternalBrowserSlotLaunchPlan>();
        foreach (var slot in planSlots)
        {
            output.WriteLine(
                $"[slot {slot.SlotId}] {slot.StreamName} -> {slot.BrowserName} ({slot.BrowserId}), muted={slot.IsMuted}: {slot.StreamUrl}");
        }

        if (!exportResult.ScriptSaved)
        {
            output.WriteLine($"External browser fallback script: not available ({exportResult.Reason})");
            return 1;
        }

        output.WriteLine($"External browser fallback script: {exportResult.ScriptPath}");
        output.WriteLine("Review the script before running it. This command does not launch browsers.");
        return 0;
    }

    private static int SaveHandoff(ParseResult parseResult, TextWriter output)
    {
        var outputFolder = ResolveHandoffOutputFolder(parseResult.DataFolder, parseResult.OutputPath);
        Directory.CreateDirectory(outputFolder);
        var generatedAt = DateTimeOffset.Now;
        var feasibilityStorage = new FeasibilityResultStorageService(parseResult.DataFolder);
        var results = feasibilityStorage.LoadResults();
        var diagnosticReport = CreateDiagnosticReport(parseResult, feasibilityStorage, results);
        var auditService = new FeasibilityAuditService();
        var auditSummary = auditService.CreateSummary(diagnosticReport.FeasibilityAudit);
        var planVerificationStatus = auditService.CreatePlanVerificationStatus(diagnosticReport.FeasibilityAudit);
        var outstandingGateCount = diagnosticReport.FeasibilityAudit.Count(
            item => !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase));

        var (preflightLines, isPreflightReady) = CreatePreflightLines(
            parseResult.DataFolder,
            parseResult.ProfileFolder);
        var checklistLines = CreateChecklistLines(parseResult.DataFolder);
        var auditLines = CreateAuditLines(parseResult.DataFolder);
        var (verificationLines, isVerified) = CreateVerificationLines(parseResult.DataFolder);
        var historyLines = CreateHistoryLines(parseResult.DataFolder);
        var artifacts = new[]
        {
            SaveHandoffArtifact(outputFolder, HandoffPreflightFileName, preflightLines),
            SaveHandoffArtifact(outputFolder, HandoffChecklistFileName, checklistLines),
            SaveHandoffArtifact(outputFolder, HandoffAuditFileName, auditLines),
            SaveHandoffArtifact(outputFolder, HandoffVerificationFileName, verificationLines),
            SaveHandoffArtifact(outputFolder, HandoffHistoryFileName, historyLines),
            SaveHandoffDiagnosticReport(outputFolder, diagnosticReport),
            SaveHandoffResultsSnapshot(outputFolder, results)
        };
        var artifactFileNames = artifacts
            .Select(Path.GetFileName)
            .Where(fileName => fileName is not null)
            .Select(fileName => fileName!)
            .ToArray();
        var artifactDetails = artifacts
            .Select(CreateHandoffArtifactMetadata)
            .ToArray();
        var manifestPath = SaveHandoffManifest(
            outputFolder,
            generatedAt,
            feasibilityStorage.DataFolder,
            feasibilityStorage.ResultsFilePath,
            results.Count,
            isPreflightReady,
            isVerified,
            diagnosticReport.FeasibilityDecision.Code,
            diagnosticReport.FeasibilityDecision.Title,
            planVerificationStatus,
            auditSummary.PassCount,
            auditSummary.PendingCount,
            auditSummary.FailCount,
            outstandingGateCount,
            artifactFileNames,
            artifactDetails);

        output.WriteLine("Stream Orchestra Phase 0 Handoff");
        output.WriteLine($"Output folder: {outputFolder}");
        output.WriteLine($"Generated at: {generatedAt:O}");
        output.WriteLine($"Results snapshot source: {feasibilityStorage.ResultsFilePath}");
        output.WriteLine($"Results snapshot count: {results.Count}");
        foreach (var artifact in artifacts)
        {
            output.WriteLine($"Saved: {artifact}");
        }

        output.WriteLine($"Saved: {manifestPath}");
        output.WriteLine($"Preflight ready: {isPreflightReady}");
        output.WriteLine($"Verification complete: {isVerified}");
        output.WriteLine($"Plan verification: {planVerificationStatus}");
        output.WriteLine($"Plan audit: {auditSummary.ToCompactText()}");
        output.WriteLine($"Outstanding gates: {outstandingGateCount}");
        output.WriteLine("Use the saved files as the setup, checklist, audit, and verification artifacts for the manual SOOP run.");
        return 0;
    }

    private static int ValidateHandoff(ParseResult parseResult, TextWriter output)
    {
        var inputFolder = parseResult.DataFolder ?? "";
        var manifestPath = Path.Combine(inputFolder, HandoffManifestFileName);
        var validationLines = new List<string>
        {
            "Stream Orchestra Phase 0 Handoff Validation",
            $"Input folder: {inputFolder}",
            $"Manifest: {manifestPath}"
        };
        var isValid = true;

        if (!Directory.Exists(inputFolder))
        {
            validationLines.Add("Validation: fail");
            validationLines.Add("- [fail] Input folder does not exist.");
            return WriteHandoffValidationResult(validationLines, output, parseResult.OutputPath, exitCode: 1);
        }

        if (!File.Exists(manifestPath))
        {
            validationLines.Add("Validation: fail");
            validationLines.Add($"- [fail] {HandoffManifestFileName} is missing.");
            return WriteHandoffValidationResult(validationLines, output, parseResult.OutputPath, exitCode: 1);
        }

        HandoffManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<HandoffManifest>(
                File.ReadAllText(manifestPath),
                HandoffJsonOptions);
        }
        catch (JsonException ex)
        {
            validationLines.Add("Validation: fail");
            validationLines.Add($"- [fail] Manifest JSON is invalid: {ex.Message}");
            return WriteHandoffValidationResult(validationLines, output, parseResult.OutputPath, exitCode: 1);
        }

        if (manifest is null)
        {
            validationLines.Add("Validation: fail");
            validationLines.Add("- [fail] Manifest JSON is empty.");
            return WriteHandoffValidationResult(validationLines, output, parseResult.OutputPath, exitCode: 1);
        }

        validationLines.Add($"Generated at: {manifest.GeneratedAt:O}");
        validationLines.Add($"Decision: {manifest.DecisionTitle} ({manifest.DecisionCode})");
        validationLines.Add($"Plan verification: {manifest.PlanVerificationStatus}");
        validationLines.Add(
            $"Plan audit: pass={manifest.PassingGateCount}, pending={manifest.PendingGateCount}, fail={manifest.FailingGateCount}");
        validationLines.Add($"Outstanding gates: {manifest.OutstandingGateCount}");

        var artifactDetails = manifest.ArtifactDetails?.ToArray() ?? Array.Empty<HandoffArtifactMetadata>();
        var manifestArtifactFiles = manifest.ArtifactFiles ?? Array.Empty<string>();
        if (artifactDetails.Length == 0)
        {
            validationLines.Add("Validation: fail");
            validationLines.Add("- [fail] Manifest has no artifactDetails entries.");
            return WriteHandoffValidationResult(validationLines, output, parseResult.OutputPath, exitCode: 1);
        }

        foreach (var duplicateFileName in FindDuplicateFileNames(manifestArtifactFiles))
        {
            isValid = false;
            validationLines.Add($"- [fail] {duplicateFileName}: duplicate artifactFiles entry.");
        }

        foreach (var duplicateFileName in FindDuplicateFileNames(artifactDetails.Select(detail => detail.FileName)))
        {
            isValid = false;
            validationLines.Add($"- [fail] {duplicateFileName}: duplicate artifactDetails entry.");
        }

        var detailedFiles = new HashSet<string>(
            artifactDetails.Select(detail => detail.FileName),
            StringComparer.OrdinalIgnoreCase);
        var artifactFiles = new HashSet<string>(
            manifestArtifactFiles,
            StringComparer.OrdinalIgnoreCase);
        var requiredFiles = new HashSet<string>(RequiredHandoffArtifactFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var requiredFile in requiredFiles)
        {
            if (!artifactFiles.Contains(requiredFile))
            {
                isValid = false;
                validationLines.Add($"- [fail] {requiredFile}: missing from artifactFiles.");
            }

            if (!detailedFiles.Contains(requiredFile))
            {
                isValid = false;
                validationLines.Add($"- [fail] {requiredFile}: missing artifactDetails entry.");
            }
        }

        foreach (var artifactFile in manifestArtifactFiles)
        {
            if (!detailedFiles.Contains(artifactFile))
            {
                isValid = false;
                validationLines.Add($"- [fail] {artifactFile}: missing artifactDetails entry.");
            }
        }

        foreach (var detail in artifactDetails)
        {
            if (!artifactFiles.Contains(detail.FileName) && !requiredFiles.Contains(detail.FileName))
            {
                isValid = false;
                validationLines.Add($"- [fail] {detail.FileName}: missing from artifactFiles.");
            }
        }

        foreach (var detail in artifactDetails)
        {
            if (string.IsNullOrWhiteSpace(detail.FileName) || detail.FileName != Path.GetFileName(detail.FileName))
            {
                isValid = false;
                validationLines.Add($"- [fail] {detail.FileName}: invalid artifact file name.");
                continue;
            }

            var artifactPath = Path.Combine(inputFolder, detail.FileName);
            if (!File.Exists(artifactPath))
            {
                isValid = false;
                validationLines.Add($"- [fail] {detail.FileName}: missing.");
                continue;
            }

            var fileInfo = new FileInfo(artifactPath);
            var actualSha256 = ComputeFileSha256(artifactPath);
            var hasExpectedSize = fileInfo.Length == detail.SizeBytes;
            var hasExpectedSha = actualSha256.Equals(detail.Sha256, StringComparison.OrdinalIgnoreCase);
            if (hasExpectedSize && hasExpectedSha)
            {
                validationLines.Add($"- [pass] {detail.FileName}: {fileInfo.Length} byte(s), sha256={actualSha256}");
                continue;
            }

            isValid = false;
            if (!hasExpectedSize)
            {
                validationLines.Add(
                    $"- [fail] {detail.FileName}: size mismatch, expected {detail.SizeBytes} byte(s), actual {fileInfo.Length} byte(s).");
            }

            if (!hasExpectedSha)
            {
                validationLines.Add(
                    $"- [fail] {detail.FileName}: sha256 mismatch, expected {detail.Sha256}, actual {actualSha256}.");
            }
        }

        isValid &= ValidateHandoffPreflightArtifact(inputFolder, manifest, validationLines);

        HandoffResultsSummary? resultsSummary = null;
        var resultsPath = Path.Combine(inputFolder, HandoffResultsFileName);
        if (File.Exists(resultsPath))
        {
            try
            {
                var results = JsonSerializer.Deserialize<FeasibilityTestResult[]>(
                    File.ReadAllText(resultsPath),
                    HandoffJsonOptions) ?? [];
                resultsSummary = CreateHandoffResultsSummary(results);
                if (results.Length == manifest.ResultCount)
                {
                    validationLines.Add($"- [pass] {HandoffResultsFileName} result count: {results.Length}");
                }
                else
                {
                    isValid = false;
                    validationLines.Add(
                        $"- [fail] {HandoffResultsFileName} result count mismatch, expected {manifest.ResultCount}, actual {results.Length}.");
                }
            }
            catch (JsonException ex)
            {
                isValid = false;
                validationLines.Add($"- [fail] {HandoffResultsFileName} JSON is invalid: {ex.Message}");
            }
        }

        if (resultsSummary is not null)
        {
            isValid &= ValidateHandoffResultsSummary(resultsSummary, manifest, validationLines);
            isValid &= ValidateHandoffChecklistArtifact(inputFolder, manifest, resultsSummary, validationLines);
            isValid &= ValidateHandoffAuditArtifact(inputFolder, manifest, resultsSummary, validationLines);
            isValid &= ValidateHandoffVerificationArtifact(inputFolder, resultsSummary, validationLines);
            isValid &= ValidateHandoffHistoryArtifact(inputFolder, manifest, resultsSummary, validationLines);
        }

        var diagnosticReportPath = Path.Combine(inputFolder, HandoffDiagnosticReportFileName);
        if (File.Exists(diagnosticReportPath))
        {
            try
            {
                var report = JsonSerializer.Deserialize<DiagnosticReport>(
                    File.ReadAllText(diagnosticReportPath),
                    HandoffJsonOptions);
                if (report is null)
                {
                    isValid = false;
                    validationLines.Add($"- [fail] {HandoffDiagnosticReportFileName} is empty.");
                }
                else
                {
                    isValid &= ValidateHandoffDiagnosticReport(report, manifest, resultsSummary, validationLines);
                }
            }
            catch (JsonException ex)
            {
                isValid = false;
                validationLines.Add($"- [fail] {HandoffDiagnosticReportFileName} JSON is invalid: {ex.Message}");
            }
        }

        validationLines.Add(isValid ? "Validation: pass" : "Validation: fail");
        return WriteHandoffValidationResult(
            validationLines,
            output,
            parseResult.OutputPath,
            isValid ? 0 : 1);
    }

    private static int WriteHandoffValidationResult(
        IReadOnlyList<string> validationLines,
        TextWriter output,
        string? outputPath,
        int exitCode)
    {
        WriteLines(validationLines, output);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            SaveTextFile(outputPath, string.Join(Environment.NewLine, validationLines) + Environment.NewLine);
            output.WriteLine($"Handoff validation saved: {outputPath}");
        }

        return exitCode;
    }

    private static IReadOnlyList<string> FindDuplicateFileNames(IEnumerable<string?> fileNames)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();

        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            if (!seen.Add(fileName) && reported.Add(fileName))
            {
                duplicates.Add(fileName);
            }
        }

        return duplicates;
    }

    private static HandoffResultsSummary CreateHandoffResultsSummary(IReadOnlyList<FeasibilityTestResult> results)
    {
        var decision = new FeasibilityDecisionService().Decide(results);
        var auditService = new FeasibilityAuditService();
        var auditItems = auditService.CreateAudit(results, decision);
        var auditSummary = auditService.CreateSummary(auditItems);
        var planVerificationStatus = auditService.CreatePlanVerificationStatus(auditItems);
        var outstandingGateCount = auditItems.Count(
            item => !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase));

        return new HandoffResultsSummary(
            results.ToArray(),
            decision,
            auditItems,
            auditSummary,
            planVerificationStatus,
            outstandingGateCount);
    }

    private static bool ValidateHandoffResultsSummary(
        HandoffResultsSummary summary,
        HandoffManifest manifest,
        List<string> validationLines)
    {
        var isValid = true;
        var isExpectedVerified = summary.PlanVerificationStatus.Equals("pass", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(summary.Decision.Code, manifest.DecisionCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(summary.Decision.Title, manifest.DecisionTitle, StringComparison.Ordinal))
        {
            validationLines.Add(
                $"- [pass] {HandoffResultsFileName} decision: {summary.Decision.Title} ({summary.Decision.Code})");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffResultsFileName} decision mismatch, expected {summary.Decision.Title} ({summary.Decision.Code}), actual {manifest.DecisionTitle} ({manifest.DecisionCode}).");
        }

        if (manifest.IsVerified == isExpectedVerified)
        {
            validationLines.Add($"- [pass] {HandoffManifestFileName} isVerified: {manifest.IsVerified}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffManifestFileName} isVerified mismatch, expected {isExpectedVerified} from results, actual {manifest.IsVerified}.");
        }

        if (summary.AuditSummary.PassCount == manifest.PassingGateCount &&
            summary.AuditSummary.PendingCount == manifest.PendingGateCount &&
            summary.AuditSummary.FailCount == manifest.FailingGateCount &&
            summary.OutstandingGateCount == manifest.OutstandingGateCount &&
            summary.PlanVerificationStatus.Equals(manifest.PlanVerificationStatus, StringComparison.OrdinalIgnoreCase))
        {
            validationLines.Add(
                $"- [pass] {HandoffResultsFileName} plan gates: pass={summary.AuditSummary.PassCount}, pending={summary.AuditSummary.PendingCount}, fail={summary.AuditSummary.FailCount}, outstanding={summary.OutstandingGateCount}, status={summary.PlanVerificationStatus}");
            return isValid;
        }

        validationLines.Add(
            $"- [fail] {HandoffResultsFileName} plan gates mismatch, expected pass={summary.AuditSummary.PassCount}, pending={summary.AuditSummary.PendingCount}, fail={summary.AuditSummary.FailCount}, outstanding={summary.OutstandingGateCount}, status={summary.PlanVerificationStatus}; actual pass={manifest.PassingGateCount}, pending={manifest.PendingGateCount}, fail={manifest.FailingGateCount}, outstanding={manifest.OutstandingGateCount}, status={manifest.PlanVerificationStatus}.");
        return false;
    }

    private static bool ValidateHandoffChecklistArtifact(
        string inputFolder,
        HandoffManifest manifest,
        HandoffResultsSummary summary,
        List<string> validationLines)
    {
        var expectedLines = CreateExpectedHandoffChecklistLines(manifest, summary);
        return ValidateHandoffTextArtifact(
            inputFolder,
            HandoffChecklistFileName,
            expectedLines,
            "content",
            validationLines);
    }

    private static bool ValidateHandoffAuditArtifact(
        string inputFolder,
        HandoffManifest manifest,
        HandoffResultsSummary summary,
        List<string> validationLines)
    {
        var expectedLines = CreateExpectedHandoffAuditLines(manifest, summary);
        return ValidateHandoffTextArtifact(
            inputFolder,
            HandoffAuditFileName,
            expectedLines,
            "content",
            validationLines);
    }

    private static bool ValidateHandoffPreflightArtifact(
        string inputFolder,
        HandoffManifest manifest,
        List<string> validationLines)
    {
        var preflightPath = Path.Combine(inputFolder, HandoffPreflightFileName);
        if (!File.Exists(preflightPath))
        {
            return true;
        }

        var lines = File.ReadAllLines(preflightPath);
        var runtimeLine = lines.FirstOrDefault(
            line => line.StartsWith("WebView2 runtime:", StringComparison.OrdinalIgnoreCase));
        var layoutsLine = lines.FirstOrDefault(
            line => line.StartsWith("Layouts:", StringComparison.OrdinalIgnoreCase));
        if (runtimeLine is null || layoutsLine is null)
        {
            validationLines.Add($"- [fail] {HandoffPreflightFileName} readiness lines are missing.");
            return false;
        }

        var isReady = runtimeLine.Contains("[available]", StringComparison.OrdinalIgnoreCase) &&
            layoutsLine.Contains("[ready]", StringComparison.OrdinalIgnoreCase);
        if (manifest.IsPreflightReady == isReady)
        {
            validationLines.Add($"- [pass] {HandoffPreflightFileName} readiness: {isReady}");
            return true;
        }

        validationLines.Add(
            $"- [fail] {HandoffPreflightFileName} readiness mismatch, expected {isReady} from artifact, actual {manifest.IsPreflightReady} in manifest.");
        return false;
    }

    private static bool ValidateHandoffVerificationArtifact(
        string inputFolder,
        HandoffResultsSummary summary,
        List<string> validationLines)
    {
        var verificationPath = Path.Combine(inputFolder, HandoffVerificationFileName);
        if (!File.Exists(verificationPath))
        {
            return true;
        }

        var lines = File.ReadAllLines(verificationPath);
        var planVerificationStatus = ReadBracketedStatus(lines, "Plan verification:");
        if (string.IsNullOrWhiteSpace(planVerificationStatus))
        {
            validationLines.Add($"- [fail] {HandoffVerificationFileName} plan verification line is missing.");
            return false;
        }

        var isValid = true;
        if (planVerificationStatus.Equals(summary.PlanVerificationStatus, StringComparison.OrdinalIgnoreCase))
        {
            validationLines.Add($"- [pass] {HandoffVerificationFileName} plan status: {planVerificationStatus}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffVerificationFileName} plan status mismatch, expected {summary.PlanVerificationStatus} from results, actual {planVerificationStatus}.");
        }

        var isExpectedVerified = summary.PlanVerificationStatus.Equals("pass", StringComparison.OrdinalIgnoreCase);
        var isArtifactVerified = lines.Any(line => line.Equals("Verification: pass", StringComparison.OrdinalIgnoreCase));
        var isArtifactIncomplete = lines.Any(line => line.Equals("Verification: not complete", StringComparison.OrdinalIgnoreCase));
        if (!isArtifactVerified && !isArtifactIncomplete)
        {
            validationLines.Add($"- [fail] {HandoffVerificationFileName} completion line is missing.");
            return false;
        }

        if (isArtifactVerified == isExpectedVerified)
        {
            validationLines.Add($"- [pass] {HandoffVerificationFileName} completion: {isArtifactVerified}");
            return isValid;
        }

        validationLines.Add(
            $"- [fail] {HandoffVerificationFileName} completion mismatch, expected {isExpectedVerified} from results, actual {isArtifactVerified}.");
        return false;
    }

    private static bool ValidateHandoffHistoryArtifact(
        string inputFolder,
        HandoffManifest manifest,
        HandoffResultsSummary summary,
        List<string> validationLines)
    {
        var expectedLines = CreateExpectedHandoffHistoryLines(manifest, summary.Results);
        return ValidateHandoffTextArtifact(
            inputFolder,
            HandoffHistoryFileName,
            expectedLines,
            "content",
            validationLines);
    }

    private static bool ValidateHandoffTextArtifact(
        string inputFolder,
        string fileName,
        IReadOnlyList<string> expectedLines,
        string description,
        List<string> validationLines)
    {
        var path = Path.Combine(inputFolder, fileName);
        if (!File.Exists(path))
        {
            return true;
        }

        var actualLines = File.ReadAllLines(path);
        if (actualLines.SequenceEqual(expectedLines, StringComparer.Ordinal))
        {
            validationLines.Add($"- [pass] {fileName} {description} matches results snapshot.");
            return true;
        }

        var lineNumber = FindFirstLineMismatch(expectedLines, actualLines) + 1;
        var expectedLine = lineNumber <= expectedLines.Count ? expectedLines[lineNumber - 1] : "<missing>";
        var actualLine = lineNumber <= actualLines.Length ? actualLines[lineNumber - 1] : "<missing>";
        validationLines.Add(
            $"- [fail] {fileName} {description} mismatch at line {lineNumber}, expected {FormatMismatchLine(expectedLine)}, actual {FormatMismatchLine(actualLine)}.");
        return false;
    }

    private static int FindFirstLineMismatch(IReadOnlyList<string> expectedLines, IReadOnlyList<string> actualLines)
    {
        var sharedLength = Math.Min(expectedLines.Count, actualLines.Count);
        for (var index = 0; index < sharedLength; index++)
        {
            if (!string.Equals(expectedLines[index], actualLines[index], StringComparison.Ordinal))
            {
                return index;
            }
        }

        return sharedLength;
    }

    private static string FormatMismatchLine(string? line)
    {
        if (line is null)
        {
            return "<null>";
        }

        const int maxLength = 160;
        return line.Length <= maxLength
            ? $"\"{line}\""
            : $"\"{line[..maxLength]}...\"";
    }

    private static IReadOnlyList<string> CreateExpectedHandoffChecklistLines(
        HandoffManifest manifest,
        HandoffResultsSummary summary)
    {
        var lines = new List<string>
        {
            "Stream Orchestra Phase 0 Manual Checklist",
            $"Data folder: {manifest.DataFolder}",
            $"Results file: {manifest.ResultsFilePath}",
            $"Results recorded: {summary.Results.Count}",
            $"Decision: {summary.Decision.Title} ({summary.Decision.Code})"
        };

        if (!string.IsNullOrWhiteSpace(summary.Decision.NextAction))
        {
            lines.Add($"Next action: {summary.Decision.NextAction}");
        }

        lines.Add($"Plan audit: {summary.AuditSummary.ToCompactText()}");
        lines.Add($"Plan verification: [{summary.PlanVerificationStatus}]");

        var successGate = summary.AuditItems.FirstOrDefault(item => item.Id == "phase0_success_gate");
        if (successGate is not null)
        {
            lines.Add($"Success gate: [{successGate.Status}] {successGate.Evidence}");
        }

        var outstandingItems = summary.AuditItems
            .Where(item => !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (outstandingItems.Length > 0)
        {
            lines.Add("Outstanding gates:");
            lines.AddRange(outstandingItems.Select(item => $"- [{item.Status}] {item.Title}: {item.Evidence}"));
        }

        lines.Add("Safety: use normal SOOP login/player behavior only; do not bypass DRM, authentication, or security behavior.");
        lines.Add("1. Run `preflight` and confirm WebView2 Runtime, A-D profile folders, and 4/8/9/12/16 layout coverage are ready.");
        lines.Add("2. Open the WPF app, load SOOP, and sign into the same SOOP account in profile groups A, B, C, and D.");
        lines.Add("3. Restart the app and confirm the SOOP login session persists in the required profile groups.");
        lines.Add("4. Run the isolated Group A test and record whether slots 1-4 visibly play.");
        lines.Add("5. Run the 8-slot, 9-slot threshold, 12-slot, and 16-slot playback tests and record each visible playback result.");
        lines.Add("6. After playback stabilizes, record Task Manager CPU %, GPU %, and memory MB, plus whether resource usage is acceptable.");
        lines.Add("7. Use one shared non-sensitive account label for every same-account evidence record across A-D.");
        lines.Add("8. Record lower-count playback evidence as `partial` when the requested slots visibly play but success-only evidence is incomplete, or `failure` when they do not work.");
        lines.Add("9. Run each intended `record` command with `--dry-run` first to preview validation, decision, and audit output without saving.");
        lines.Add("10. Record the final 9+ `success` evidence last, only when playback, account, restart, resource, CPU, GPU, and memory evidence is complete.");
        lines.Add("11. Run `verify`; Phase 0 is not complete until every plan gate passes.");
        lines.Add("Helpful commands: `scenarios`, `record --dry-run`, `audit`, `verify`.");

        var suggestions = new FeasibilityAuditService().CreateSuggestedRecordShapes(summary.AuditItems);
        if (suggestions.Count > 0)
        {
            lines.Add("Suggested record shapes:");
            lines.AddRange(suggestions.Select(suggestion => $"- {suggestion}"));
        }

        return lines;
    }

    private static IReadOnlyList<string> CreateExpectedHandoffAuditLines(
        HandoffManifest manifest,
        HandoffResultsSummary summary)
    {
        var lines = new List<string>
        {
            "Stream Orchestra Plan Audit",
            $"Data folder: {manifest.DataFolder}",
            $"Results file: {manifest.ResultsFilePath}",
            $"Results recorded: {summary.Results.Count}",
            $"Decision: {summary.Decision.Title} ({summary.Decision.Code})",
            $"Plan audit: {summary.AuditSummary.ToCompactText()}",
            $"Plan verification: [{summary.PlanVerificationStatus}]"
        };

        if (!string.IsNullOrWhiteSpace(summary.Decision.NextAction))
        {
            lines.Add($"Next action: {summary.Decision.NextAction}");
        }

        var successGate = summary.AuditItems.FirstOrDefault(item => item.Id == "phase0_success_gate");
        if (successGate is not null)
        {
            lines.Add($"Success gate: [{successGate.Status}] {successGate.Evidence}");
        }

        lines.AddRange(summary.AuditItems.Select(item => $"[{item.Status}] {item.Title}: {item.Evidence}"));
        var suggestedRecordShapes = new FeasibilityAuditService().CreateSuggestedRecordShapes(summary.AuditItems);
        if (suggestedRecordShapes.Count > 0)
        {
            lines.Add("Suggested record shapes:");
            lines.AddRange(suggestedRecordShapes.Select(suggestion => $"- {suggestion}"));
        }

        return lines;
    }

    private static IReadOnlyList<string> CreateExpectedHandoffHistoryLines(
        HandoffManifest manifest,
        IReadOnlyList<FeasibilityTestResult> results)
    {
        var orderedResults = results
            .OrderByDescending(result => result.CapturedAt)
            .ToArray();
        var lines = new List<string>
        {
            "Stream Orchestra Feasibility History",
            $"Data folder: {manifest.DataFolder}",
            $"Results file: {manifest.ResultsFilePath}",
            $"Results recorded: {orderedResults.Length}"
        };

        if (orderedResults.Length == 0)
        {
            lines.Add("No feasibility results recorded.");
            return lines;
        }

        foreach (var result in orderedResults)
        {
            lines.Add(
                $"[{result.CapturedAt:yyyy-MM-dd HH:mm:ss}] {result.Outcome}, {result.PlaybackCount} slot(s), {result.ScenarioName} ({result.ScenarioId})");
            lines.Add($"  Id: {result.Id}");
            lines.Add(
                $"  Criteria: account={result.IsSameAccountSessionMaintained}, restart={result.IsRestartSessionMaintained}, resources={result.IsResourceUsageAcceptable}");
            lines.Add($"  Account label: {FormatAccountLabel(result.AccountLabel)}");
            lines.Add($"  Profile groups: {FeasibilityProfileGroupEvidenceService.FormatGroups(result.VerifiedProfileGroups)}");
            lines.Add(
                $"  Observed resources: cpu={FormatNullable(result.ObservedCpuPercent)}%, gpu={FormatNullable(result.ObservedGpuPercent)}%, memory={FormatNullable(result.ObservedMemoryMegabytes)} MB");
            lines.Add(
                string.IsNullOrWhiteSpace(result.DecisionCode)
                    ? "  Recorded decision: n/a"
                    : $"  Recorded decision: {result.DecisionTitle} ({result.DecisionCode})");

            if (!string.IsNullOrWhiteSpace(result.DecisionNextAction))
            {
                lines.Add($"  Next action at record time: {result.DecisionNextAction}");
            }

            if (!string.IsNullOrWhiteSpace(result.Notes))
            {
                lines.Add($"  Notes: {result.Notes}");
            }
        }

        return lines;
    }

    private static string? ReadBracketedStatus(IEnumerable<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(
            candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            return null;
        }

        var openBracketIndex = line.IndexOf('[', StringComparison.Ordinal);
        var closeBracketIndex = line.IndexOf(']', StringComparison.Ordinal);
        return openBracketIndex >= 0 && closeBracketIndex > openBracketIndex
            ? line[(openBracketIndex + 1)..closeBracketIndex]
            : null;
    }

    private static bool ValidateHandoffDiagnosticReport(
        DiagnosticReport report,
        HandoffManifest manifest,
        HandoffResultsSummary? summary,
        List<string> validationLines)
    {
        var isValid = true;
        if (report.FeasibilityResultCount == manifest.ResultCount)
        {
            validationLines.Add($"- [pass] diagnostic report result count: {report.FeasibilityResultCount}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report result count mismatch, expected {manifest.ResultCount}, actual {report.FeasibilityResultCount}.");
        }

        var decision = report.FeasibilityDecision;
        if (decision is null)
        {
            isValid = false;
            validationLines.Add("- [fail] diagnostic report decision is missing.");
        }
        else if (string.Equals(decision.Code, manifest.DecisionCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(decision.Title, manifest.DecisionTitle, StringComparison.Ordinal))
        {
            validationLines.Add(
                $"- [pass] diagnostic report decision: {decision.Title} ({decision.Code})");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report decision mismatch, expected {manifest.DecisionTitle} ({manifest.DecisionCode}), actual {decision.Title} ({decision.Code}).");
        }

        var auditService = new FeasibilityAuditService();
        var auditItems = report.FeasibilityAudit ?? Array.Empty<FeasibilityAuditItem>();
        var auditSummary = auditService.CreateSummary(auditItems);
        var planVerificationStatus = auditService.CreatePlanVerificationStatus(auditItems);
        var outstandingGateCount = auditItems.Count(
            item => !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase));
        if (auditSummary.PassCount == manifest.PassingGateCount &&
            auditSummary.PendingCount == manifest.PendingGateCount &&
            auditSummary.FailCount == manifest.FailingGateCount &&
            outstandingGateCount == manifest.OutstandingGateCount &&
            planVerificationStatus.Equals(manifest.PlanVerificationStatus, StringComparison.OrdinalIgnoreCase))
        {
            validationLines.Add(
                $"- [pass] diagnostic report plan gates: pass={auditSummary.PassCount}, pending={auditSummary.PendingCount}, fail={auditSummary.FailCount}, outstanding={outstandingGateCount}, status={planVerificationStatus}");
        }
        else
        {
            validationLines.Add(
                $"- [fail] diagnostic report plan gates mismatch, expected pass={manifest.PassingGateCount}, pending={manifest.PendingGateCount}, fail={manifest.FailingGateCount}, outstanding={manifest.OutstandingGateCount}, status={manifest.PlanVerificationStatus}; actual pass={auditSummary.PassCount}, pending={auditSummary.PendingCount}, fail={auditSummary.FailCount}, outstanding={outstandingGateCount}, status={planVerificationStatus}.");
            isValid = false;
        }

        if (summary is not null)
        {
            isValid &= ValidateHandoffDiagnosticReportSnapshot(report, summary, validationLines);
        }

        return isValid;
    }

    private static bool ValidateHandoffDiagnosticReportSnapshot(
        DiagnosticReport report,
        HandoffResultsSummary summary,
        List<string> validationLines)
    {
        var isValid = true;
        if (AreEquivalentDecisions(report.FeasibilityDecision, summary.Decision))
        {
            validationLines.Add(
                $"- [pass] diagnostic report decision details: {summary.Decision.Title} ({summary.Decision.Code})");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report decision details mismatch, expected {FormatDecisionIdentity(summary.Decision)}, actual {FormatDecisionIdentity(report.FeasibilityDecision)}.");
        }

        var actualAuditItems = report.FeasibilityAudit ?? Array.Empty<FeasibilityAuditItem>();
        if (actualAuditItems.SequenceEqual(summary.AuditItems))
        {
            validationLines.Add($"- [pass] diagnostic report audit items: {summary.AuditItems.Count}");
        }
        else
        {
            isValid = false;
            var mismatchIndex = FindFirstAuditItemMismatch(summary.AuditItems, actualAuditItems);
            validationLines.Add(
                $"- [fail] diagnostic report audit items mismatch at item {mismatchIndex + 1}, expected {FormatAuditItem(summary.AuditItems, mismatchIndex)}, actual {FormatAuditItem(actualAuditItems, mismatchIndex)}.");
        }

        var expectedLatestResult = summary.Results
            .OrderByDescending(result => result.CapturedAt)
            .FirstOrDefault();
        if (AreEquivalentResults(report.LatestFeasibilityResult, expectedLatestResult))
        {
            validationLines.Add(
                $"- [pass] diagnostic report latest result: {FormatResultIdentity(expectedLatestResult)}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report latest result mismatch, expected {FormatResultIdentity(expectedLatestResult)}, actual {FormatResultIdentity(report.LatestFeasibilityResult)}.");
        }

        var expectedLabels = FeasibilityProfileGroupEvidenceService.GetLatestSameAccountAccountLabels(summary.Results);
        var actualLabels = report.FeasibilitySameAccountLabels ?? Array.Empty<string>();
        if (actualLabels.SequenceEqual(expectedLabels, StringComparer.OrdinalIgnoreCase))
        {
            validationLines.Add($"- [pass] diagnostic report account labels: {FormatStringList(expectedLabels)}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report account labels mismatch, expected {FormatStringList(expectedLabels)}, actual {FormatStringList(actualLabels)}.");
        }

        var expectedConflictStatus = FeasibilityProfileGroupEvidenceService.HasConflictingSameAccountLabels(summary.Results);
        if (report.HasConflictingFeasibilityAccountLabels == expectedConflictStatus)
        {
            validationLines.Add($"- [pass] diagnostic report account label conflict: {expectedConflictStatus}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report account label conflict mismatch, expected {expectedConflictStatus}, actual {report.HasConflictingFeasibilityAccountLabels}.");
        }

        var expectedSuggestions = new FeasibilityAuditService().CreateSuggestedRecordShapes(summary.AuditItems);
        var actualSuggestions = report.FeasibilitySuggestedRecordShapes ?? Array.Empty<string>();
        if (actualSuggestions.SequenceEqual(expectedSuggestions, StringComparer.Ordinal))
        {
            validationLines.Add($"- [pass] diagnostic report suggested records: {expectedSuggestions.Count}");
            return isValid;
        }

        isValid = false;
        validationLines.Add(
            $"- [fail] diagnostic report suggested records mismatch, expected {expectedSuggestions.Count}, actual {actualSuggestions.Count}.");
        return isValid;
    }

    private static bool AreEquivalentDecisions(FeasibilityDecision? actual, FeasibilityDecision? expected)
    {
        if (actual is null || expected is null)
        {
            return actual is null && expected is null;
        }

        return string.Equals(actual.Code, expected.Code, StringComparison.Ordinal) &&
            string.Equals(actual.Title, expected.Title, StringComparison.Ordinal) &&
            string.Equals(actual.Detail, expected.Detail, StringComparison.Ordinal) &&
            string.Equals(actual.NextAction, expected.NextAction, StringComparison.Ordinal);
    }

    private static bool AreEquivalentResults(FeasibilityTestResult? actual, FeasibilityTestResult? expected)
    {
        if (actual is null || expected is null)
        {
            return actual is null && expected is null;
        }

        return string.Equals(actual.Id, expected.Id, StringComparison.Ordinal) &&
            actual.CapturedAt.Equals(expected.CapturedAt) &&
            actual.PlaybackCount == expected.PlaybackCount &&
            string.Equals(actual.ScenarioId, expected.ScenarioId, StringComparison.Ordinal) &&
            string.Equals(actual.ScenarioName, expected.ScenarioName, StringComparison.Ordinal) &&
            string.Equals(actual.Outcome, expected.Outcome, StringComparison.Ordinal) &&
            AreEquivalentDiagnostics(actual.Diagnostics, expected.Diagnostics) &&
            actual.IsSameAccountSessionMaintained == expected.IsSameAccountSessionMaintained &&
            string.Equals(actual.AccountLabel, expected.AccountLabel, StringComparison.Ordinal) &&
            actual.IsRestartSessionMaintained == expected.IsRestartSessionMaintained &&
            actual.IsResourceUsageAcceptable == expected.IsResourceUsageAcceptable &&
            actual.VerifiedProfileGroups.SequenceEqual(expected.VerifiedProfileGroups, StringComparer.Ordinal) &&
            AreEquivalentNullableDouble(actual.ObservedCpuPercent, expected.ObservedCpuPercent) &&
            AreEquivalentNullableDouble(actual.ObservedGpuPercent, expected.ObservedGpuPercent) &&
            AreEquivalentNullableDouble(actual.ObservedMemoryMegabytes, expected.ObservedMemoryMegabytes) &&
            string.Equals(actual.DecisionCode, expected.DecisionCode, StringComparison.Ordinal) &&
            string.Equals(actual.DecisionTitle, expected.DecisionTitle, StringComparison.Ordinal) &&
            string.Equals(actual.DecisionDetail, expected.DecisionDetail, StringComparison.Ordinal) &&
            string.Equals(actual.DecisionNextAction, expected.DecisionNextAction, StringComparison.Ordinal) &&
            string.Equals(actual.Notes, expected.Notes, StringComparison.Ordinal);
    }

    private static bool AreEquivalentDiagnostics(
        RuntimeDiagnosticsSnapshot actual,
        RuntimeDiagnosticsSnapshot expected)
    {
        return actual.CapturedAt.Equals(expected.CapturedAt) &&
            actual.WebViewProcessCount == expected.WebViewProcessCount &&
            AreEquivalentNullableDouble(actual.WebViewWorkingSetMegabytes, expected.WebViewWorkingSetMegabytes) &&
            AreEquivalentNullableDouble(actual.WebViewPrivateMemoryMegabytes, expected.WebViewPrivateMemoryMegabytes) &&
            AreEquivalentNullableDouble(actual.WebViewCpuPercent, expected.WebViewCpuPercent);
    }

    private static bool AreEquivalentNullableDouble(double? actual, double? expected)
    {
        return actual.HasValue == expected.HasValue &&
            (!actual.HasValue || actual.Value.Equals(expected!.Value));
    }

    private static int FindFirstAuditItemMismatch(
        IReadOnlyList<FeasibilityAuditItem> expected,
        IReadOnlyList<FeasibilityAuditItem> actual)
    {
        var sharedLength = Math.Min(expected.Count, actual.Count);
        for (var index = 0; index < sharedLength; index++)
        {
            if (!expected[index].Equals(actual[index]))
            {
                return index;
            }
        }

        return sharedLength;
    }

    private static string FormatDecisionIdentity(FeasibilityDecision? decision)
    {
        return decision is null
            ? "n/a"
            : $"{decision.Title} ({decision.Code}), detail={FormatMismatchLine(decision.Detail)}, next={FormatMismatchLine(decision.NextAction)}";
    }

    private static string FormatResultIdentity(FeasibilityTestResult? result)
    {
        return result is null
            ? "n/a"
            : $"{result.Id} ({result.Outcome}, {result.PlaybackCount} slot(s), {result.CapturedAt:yyyy-MM-dd HH:mm:ss})";
    }

    private static string FormatAuditItem(IReadOnlyList<FeasibilityAuditItem> auditItems, int index)
    {
        if (index >= auditItems.Count)
        {
            return "<missing>";
        }

        var item = auditItems[index];
        return $"{item.Id}/{item.Status}/{FormatMismatchLine(item.Evidence)}";
    }

    private static string FormatStringList(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "n/a" : string.Join(", ", values);
    }

    private static int PrintChecklist(ParseResult parseResult, TextWriter output)
    {
        var lines = CreateChecklistLines(parseResult.DataFolder);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        if (!string.IsNullOrWhiteSpace(parseResult.OutputPath))
        {
            SaveTextFile(parseResult.OutputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            output.WriteLine($"Checklist saved: {parseResult.OutputPath}");
        }

        return 0;
    }

    private static IReadOnlyList<string> CreateChecklistLines(string? dataFolder)
    {
        var storage = new FeasibilityResultStorageService(dataFolder);
        var results = storage.LoadResults();
        var decision = new FeasibilityDecisionService().Decide(results);
        var auditService = new FeasibilityAuditService();
        var auditItems = auditService.CreateAudit(results, decision);
        var lines = new List<string>
        {
            "Stream Orchestra Phase 0 Manual Checklist",
            $"Data folder: {storage.DataFolder}",
            $"Results file: {storage.ResultsFilePath}",
            $"Results recorded: {results.Count}",
            $"Decision: {decision.Title} ({decision.Code})"
        };

        if (!string.IsNullOrWhiteSpace(decision.NextAction))
        {
            lines.Add($"Next action: {decision.NextAction}");
        }

        var summary = auditService.CreateSummary(auditItems);
        lines.Add($"Plan audit: {summary.ToCompactText()}");
        lines.Add($"Plan verification: [{auditService.CreatePlanVerificationStatus(auditItems)}]");

        var successGate = auditItems.FirstOrDefault(item => item.Id == "phase0_success_gate");
        if (successGate is not null)
        {
            lines.Add($"Success gate: [{successGate.Status}] {successGate.Evidence}");
        }

        var outstandingItems = auditItems
            .Where(item => !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (outstandingItems.Length > 0)
        {
            lines.Add("Outstanding gates:");
            lines.AddRange(outstandingItems.Select(item => $"- [{item.Status}] {item.Title}: {item.Evidence}"));
        }

        lines.Add("Safety: use normal SOOP login/player behavior only; do not bypass DRM, authentication, or security behavior.");
        lines.Add("1. Run `preflight` and confirm WebView2 Runtime, A-D profile folders, and 4/8/9/12/16 layout coverage are ready.");
        lines.Add("2. Open the WPF app, load SOOP, and sign into the same SOOP account in profile groups A, B, C, and D.");
        lines.Add("3. Restart the app and confirm the SOOP login session persists in the required profile groups.");
        lines.Add("4. Run the isolated Group A test and record whether slots 1-4 visibly play.");
        lines.Add("5. Run the 8-slot, 9-slot threshold, 12-slot, and 16-slot playback tests and record each visible playback result.");
        lines.Add("6. After playback stabilizes, record Task Manager CPU %, GPU %, and memory MB, plus whether resource usage is acceptable.");
        lines.Add("7. Use one shared non-sensitive account label for every same-account evidence record across A-D.");
        lines.Add("8. Record lower-count playback evidence as `partial` when the requested slots visibly play but success-only evidence is incomplete, or `failure` when they do not work.");
        lines.Add("9. Run each intended `record` command with `--dry-run` first to preview validation, decision, and audit output without saving.");
        lines.Add("10. Record the final 9+ `success` evidence last, only when playback, account, restart, resource, CPU, GPU, and memory evidence is complete.");
        lines.Add("11. Run `verify`; Phase 0 is not complete until every plan gate passes.");
        lines.Add("Helpful commands: `scenarios`, `record --dry-run`, `audit`, `verify`.");

        var suggestions = auditService.CreateSuggestedRecordShapes(auditItems);
        if (suggestions.Count > 0)
        {
            lines.Add("Suggested record shapes:");
            lines.AddRange(suggestions.Select(suggestion => $"- {suggestion}"));
        }

        return lines;
    }

    private static int PrintScenarios(TextWriter output)
    {
        var scenarioService = new FeasibilityScenarioService();
        var playbackTestCounts = new[] { 4, 8, 9, 12, 16 };

        output.WriteLine("Stream Orchestra Feasibility Scenarios");
        output.WriteLine("Playback test scenarios:");
        foreach (var count in playbackTestCounts)
        {
            var plan = new PlaybackTestPlanService().CreatePlan(count);
            var scenario = scenarioService.CreateFirstSlotsScenario(plan);
            output.WriteLine($"- {count} slot(s): {scenario.Name} ({scenario.Id})");
            output.WriteLine($"  WPF: click `{count}개`");
            foreach (var recordShape in CreatePlaybackScenarioRecordShapes(count))
            {
                output.WriteLine($"  CLI: {recordShape}");
            }
        }

        output.WriteLine("Isolated profile-group scenarios:");
        foreach (var groupId in new[] { "A", "B", "C", "D" })
        {
            var scenario = scenarioService.CreateIsolatedGroupScenario(groupId, targetSlotCount: 4);
            output.WriteLine($"- Group {groupId}: {scenario.Name} ({scenario.Id})");
            output.WriteLine($"  WPF: select Group {groupId}, then click `그룹 단독`");
            output.WriteLine($"  CLI: record --group {groupId} --outcome <partial|failure> --account --profile-groups {groupId} --account-label <label>");
        }

        output.WriteLine("Use `partial` when the requested slots visibly play but success-only evidence is incomplete.");
        output.WriteLine("Use `failure` when the requested playback count or isolated group does not work.");
        output.WriteLine("Record `success` only when the 9+ playback, required profile-group account, restart, resource, CPU, GPU, and memory evidence is complete.");
        return 0;
    }

    private static int PrintPreflight(ParseResult parseResult, TextWriter output)
    {
        var (lines, isReady) = CreatePreflightLines(parseResult.DataFolder, parseResult.ProfileFolder);
        WriteLines(lines, output);

        if (!string.IsNullOrWhiteSpace(parseResult.OutputPath))
        {
            SaveTextFile(parseResult.OutputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            output.WriteLine($"Preflight saved: {parseResult.OutputPath}");
        }

        return isReady ? 0 : 1;
    }

    private static (IReadOnlyList<string> Lines, bool IsReady) CreatePreflightLines(
        string? dataFolder,
        string? profileFolder)
    {
        var profileService = new WebViewProfileService(profileFolder);
        var feasibilityStorage = new FeasibilityResultStorageService(dataFolder);
        var results = feasibilityStorage.LoadResults();
        var decision = new FeasibilityDecisionService().Decide(results);
        var auditService = new FeasibilityAuditService();
        var auditItems = auditService.CreateAudit(results, decision);
        var runtimeStatus = GetWebView2RuntimeStatus();
        var layoutStatus = GetPlaybackLayoutStatus();
        var lines = new List<string>
        {
            "Stream Orchestra Feasibility Preflight",
            $"Data folder: {feasibilityStorage.DataFolder}",
            $"Results file: {feasibilityStorage.ResultsFilePath}",
            $"Profile root: {profileService.BaseProfileFolder}",
            $"WebView2 runtime: {runtimeStatus}",
            "Profile groups:"
        };

        foreach (var group in profileService.Groups.OrderBy(group => group.Id, StringComparer.OrdinalIgnoreCase))
        {
            var status = Directory.Exists(group.UserDataFolder) ? "ready" : "missing";
            lines.Add($"- [{status}] Group {group.Id}: {group.UserDataFolder}");
        }

        lines.Add($"Layouts: {layoutStatus}");
        lines.Add($"Evidence recorded: {results.Count}");
        lines.Add($"Decision: {decision.Title} ({decision.Code})");
        if (!string.IsNullOrWhiteSpace(decision.NextAction))
        {
            lines.Add($"Next action: {decision.NextAction}");
        }

        var summary = auditService.CreateSummary(auditItems);
        lines.Add($"Plan audit: {summary.ToCompactText()}");
        lines.Add($"Plan verification: [{auditService.CreatePlanVerificationStatus(auditItems)}]");
        var successGate = auditItems.FirstOrDefault(item => item.Id == "phase0_success_gate");
        if (successGate is not null)
        {
            lines.Add($"Success gate: [{successGate.Status}] {successGate.Evidence}");
        }

        var suggestions = auditService.CreateSuggestedRecordShapes(auditItems);
        if (suggestions.Count > 0)
        {
            lines.Add("Suggested record shapes:");
            lines.AddRange(suggestions.Select(suggestion => $"- {suggestion}"));
        }

        var isReady = runtimeStatus.StartsWith("[available]", StringComparison.OrdinalIgnoreCase) &&
            layoutStatus.StartsWith("[ready]", StringComparison.OrdinalIgnoreCase);

        return (lines, isReady);
    }

    private static string GetWebView2RuntimeStatus()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version)
                ? "[missing] Runtime version was not reported."
                : $"[available] {version}";
        }
        catch (Exception ex)
        {
            return $"[missing] {ex.Message}";
        }
    }

    private static string GetPlaybackLayoutStatus()
    {
        try
        {
            var layouts = new LayoutPresetService().LoadFromDefaultLocation();
            var defaultLayout = LayoutPresetService.SelectDefaultLayout(layouts);
            var playableCounts = new[] { 4, 8, 9, 12, 16 }
                .Select(count => LayoutPresetService.SelectPlaybackTestLayout(layouts, defaultLayout, count).Id)
                .ToArray();

            var layoutCoverage = string.Join(", ", playableCounts.Distinct(StringComparer.OrdinalIgnoreCase));
            return $"[ready] {layouts.Count} layout(s), playback test counts 4/8/9/12/16 are visible via {layoutCoverage}.";
        }
        catch (Exception ex)
        {
            return $"[missing] {ex.Message}";
        }
    }

    private static IReadOnlyList<string> CreatePlaybackScenarioRecordShapes(int playbackCount)
    {
        var requiredGroups = string.Join(
            ",",
            FeasibilityProfileGroupEvidenceService.GetRequiredGroupsForPlaybackCount(playbackCount));
        var partialOrFailureShape =
            $"record --count {playbackCount} --outcome <partial|failure> --account --profile-groups {requiredGroups} --account-label <label>";

        if (playbackCount < 9)
        {
            return [partialOrFailureShape];
        }

        return
        [
            partialOrFailureShape,
            $"record --count {playbackCount} --outcome success --account --profile-groups {requiredGroups} --restart --resources --cpu-percent <0-100> --gpu-percent <0-100> --memory-mb <value> --account-label <label>"
        ];
    }

    private static int VerifyPlan(ParseResult parseResult, TextWriter output)
    {
        var (lines, isVerified) = CreateVerificationLines(parseResult.DataFolder);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        if (!string.IsNullOrWhiteSpace(parseResult.OutputPath))
        {
            SaveTextFile(parseResult.OutputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            output.WriteLine($"Verification saved: {parseResult.OutputPath}");
        }

        return isVerified ? 0 : 1;
    }

    private static (IReadOnlyList<string> Lines, bool IsVerified) CreateVerificationLines(string? dataFolder)
    {
        var storage = new FeasibilityResultStorageService(dataFolder);
        var results = storage.LoadResults();
        var decision = new FeasibilityDecisionService().Decide(results);
        var auditService = new FeasibilityAuditService();
        var auditItems = auditService.CreateAudit(results, decision);
        var summary = auditService.CreateSummary(auditItems);
        var successGate = auditItems.FirstOrDefault(item => item.Id == "phase0_success_gate");
        var isVerified = auditItems.Count > 0 &&
            auditItems.All(item => item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase));
        var lines = new List<string>
        {
            "Stream Orchestra Plan Verification",
            $"Data folder: {storage.DataFolder}",
            $"Results file: {storage.ResultsFilePath}",
            $"Results recorded: {results.Count}",
            $"Decision: {decision.Title} ({decision.Code})"
        };

        if (!string.IsNullOrWhiteSpace(decision.NextAction))
        {
            lines.Add($"Next action: {decision.NextAction}");
        }

        lines.Add($"Plan audit: {summary.ToCompactText()}");
        lines.Add($"Plan verification: [{auditService.CreatePlanVerificationStatus(auditItems)}]");

        if (successGate is not null)
        {
            lines.Add($"Success gate: [{successGate.Status}] {successGate.Evidence}");
        }

        if (isVerified)
        {
            lines.Add("Verification: pass");
            return (lines, true);
        }

        lines.Add("Verification: not complete");
        var outstandingItems = auditItems
            .Where(item => !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (outstandingItems.Length > 0)
        {
            lines.Add("Outstanding gates:");
            lines.AddRange(outstandingItems.Select(item => $"- [{item.Status}] {item.Title}: {item.Evidence}"));
        }

        lines.Add("Required evidence: record live SOOP 4-slot Group A, 8-slot, 9-slot threshold, 12-slot, and 16-slot playback evidence plus A-D account-label, restart, resource, CPU, GPU, and memory evidence.");
        var suggestions = auditService.CreateSuggestedRecordShapes(auditItems);
        if (suggestions.Count > 0)
        {
            lines.Add("Suggested record shapes:");
            lines.AddRange(suggestions.Select(suggestion => $"- {suggestion}"));
        }

        return (lines, false);
    }

    private static int PrintBrowsers(string? dataFolder, TextWriter output)
    {
        var candidateStorage = new ExternalBrowserCandidateStorageService(dataFolder);
        var browsers = new ExternalBrowserDiscoveryService(candidateStorage.DataFolder)
            .Discover()
            .OrderBy(browser => browser.Id)
            .ToArray();
        var installedCount = browsers.Count(browser => browser.IsInstalled);

        output.WriteLine("Stream Orchestra External Browsers");
        output.WriteLine($"Data folder: {candidateStorage.DataFolder}");
        output.WriteLine($"Custom candidates file: {candidateStorage.CandidatesFilePath}");
        output.WriteLine($"Installed browsers: {installedCount}/{browsers.Length}");

        foreach (var browser in browsers)
        {
            output.WriteLine(
                browser.IsInstalled
                    ? $"[installed] {browser.Name} ({browser.Id}): {browser.ExecutablePath}"
                    : $"[missing] {browser.Name} ({browser.Id}): {browser.CandidatePaths.Count} candidate path(s)");
        }

        return 0;
    }

    private static int RecordResult(ParseResult parseResult, TextWriter output)
    {
        var storage = new FeasibilityResultStorageService(parseResult.DataFolder);
        var capturedAt = DateTimeOffset.Now;
        var diagnostics = new WebViewRuntimeDiagnosticsService().Capture();
        var result = new FeasibilityTestResult
        {
            Id = FeasibilityResultStorageService.CreateResultId(
                capturedAt,
                parseResult.PlaybackCount!.Value,
                parseResult.Outcome!),
            CapturedAt = capturedAt,
            PlaybackCount = parseResult.PlaybackCount.Value,
            ScenarioId = parseResult.ScenarioId,
            ScenarioName = parseResult.ScenarioName,
            Outcome = parseResult.Outcome!,
            Diagnostics = diagnostics,
            IsSameAccountSessionMaintained = parseResult.SameAccountSession,
            AccountLabel = parseResult.AccountLabel ?? "",
            VerifiedProfileGroups = parseResult.VerifiedProfileGroups,
            IsRestartSessionMaintained = parseResult.RestartSession,
            IsResourceUsageAcceptable = parseResult.ResourceUsageAcceptable,
            ObservedCpuPercent = parseResult.ObservedCpuPercent,
            ObservedGpuPercent = parseResult.ObservedGpuPercent,
            ObservedMemoryMegabytes = parseResult.ObservedMemoryMegabytes,
            Notes = parseResult.Notes ?? ""
        };

        var existingResults = storage.LoadResults();
        var previewResults = existingResults.Append(result).ToArray();
        var decision = new FeasibilityDecisionService().Decide(previewResults);
        FeasibilityResultStorageService.ApplyDecisionSnapshot(result, decision);
        IReadOnlyList<FeasibilityTestResult> results = previewResults;
        if (!parseResult.DryRun)
        {
            storage.AppendResult(result);
            results = storage.LoadResults().ToArray();
        }

        var auditItems = new FeasibilityAuditService().CreateAudit(results, decision);

        output.WriteLine(parseResult.DryRun
            ? "Dry run: feasibility result was not recorded."
            : "Recorded feasibility result.");
        output.WriteLine($"Data folder: {storage.DataFolder}");
        output.WriteLine($"Stored results before command: {existingResults.Count}");
        output.WriteLine($"Stored results after command: {(parseResult.DryRun ? existingResults.Count : results.Count)}");
        output.WriteLine($"Result: {result.Outcome}, {result.PlaybackCount} slot(s), {result.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        output.WriteLine($"Scenario: {result.ScenarioName} ({result.ScenarioId})");
        output.WriteLine($"Criteria: account={result.IsSameAccountSessionMaintained}, restart={result.IsRestartSessionMaintained}, resources={result.IsResourceUsageAcceptable}");
        output.WriteLine($"Account label: {FormatAccountLabel(result.AccountLabel)}");
        output.WriteLine($"Profile groups: {FeasibilityProfileGroupEvidenceService.FormatGroups(result.VerifiedProfileGroups)}");
        output.WriteLine(
            $"Observed resources: cpu={FormatNullable(result.ObservedCpuPercent)}%, gpu={FormatNullable(result.ObservedGpuPercent)}%, memory={FormatNullable(result.ObservedMemoryMegabytes)} MB");
        output.WriteLine($"Decision: {decision.Title} ({decision.Code})");
        WriteNextAction(decision, output);
        WriteAuditSummary(auditItems, output);
        WritePlanVerificationStatus(auditItems, output);
        WritePhase0SuccessGate(auditItems, output);
        WriteSuggestedRecordShapes(auditItems, output);

        return 0;
    }

    private static int SaveReport(ParseResult parseResult, TextWriter output)
    {
        var feasibilityStorage = new FeasibilityResultStorageService(parseResult.DataFolder);
        var results = feasibilityStorage.LoadResults();
        var decision = new FeasibilityDecisionService().Decide(results);
        var presetStorage = new PresetStorageService(parseResult.DataFolder);
        var reportService = new DiagnosticReportService();
        var report = CreateDiagnosticReport(parseResult, feasibilityStorage, results);
        var path = reportService.SaveReport(report, presetStorage.DataFolder);
        var fallbackScriptPath = reportService.SaveExternalBrowserFallbackScript(report, presetStorage.DataFolder);

        output.WriteLine("Diagnostic report saved.");
        output.WriteLine($"Path: {path}");
        output.WriteLine(FormatExternalBrowserFallbackScriptStatus(report, fallbackScriptPath));
        output.WriteLine($"Decision: {decision.Title} ({decision.Code})");
        WriteNextAction(decision, output);
        output.WriteLine($"Results recorded: {results.Count}");
        WriteAuditSummary(report.FeasibilityAudit, output);
        WritePlanVerificationStatus(report.FeasibilityAudit, output);
        WritePhase0SuccessGate(report.FeasibilityAudit, output);
        WriteSuggestedRecordShapes(report.FeasibilityAudit, output);

        return 0;
    }

    private static DiagnosticReport CreateDiagnosticReport(
        ParseResult parseResult,
        FeasibilityResultStorageService feasibilityStorage,
        IReadOnlyList<FeasibilityTestResult> results)
    {
        var profileService = new WebViewProfileService(parseResult.ProfileFolder);
        var presetStorage = new PresetStorageService(parseResult.DataFolder);
        var favoriteStorage = new FavoriteStorageService(parseResult.DataFolder);
        var decision = new FeasibilityDecisionService().Decide(results);
        var appState = presetStorage.LoadAppState();

        return new DiagnosticReportService().CreateReport(
            profileService,
            presetStorage,
            favoriteStorage,
            feasibilityStorage,
            decision,
            appState?.LastSession,
            TryLoadLayouts());
    }

    private static ParseResult ParseArgs(string[] args)
    {
        if (args.Length == 0)
        {
            return ParseResult.Valid("status", dataFolder: null);
        }

        var command = args[0];
        if (command is "-h" or "--help" or "help")
        {
            return ParseResult.Help();
        }

        if (command.Equals("record", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRecordArgs(args);
        }

        if (command.Equals("report", StringComparison.OrdinalIgnoreCase))
        {
            return ParseReportArgs(args);
        }

        if (command.Equals("preflight", StringComparison.OrdinalIgnoreCase))
        {
            return ParsePreflightArgs(args);
        }

        if (command.Equals("audit", StringComparison.OrdinalIgnoreCase))
        {
            return ParseTextOutputArgs("audit", args);
        }

        if (command.Equals("browsers", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDataFolderOnlyArgs("browsers", args);
        }

        if (command.Equals("checklist", StringComparison.OrdinalIgnoreCase))
        {
            return ParseTextOutputArgs("checklist", args);
        }

        if (command.Equals("fallback", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDataFolderOnlyArgs("fallback", args);
        }

        if (command.Equals("handoff", StringComparison.OrdinalIgnoreCase))
        {
            return ParseHandoffArgs(args);
        }

        if (command.Equals("history", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDataFolderOnlyArgs("history", args);
        }

        if (command.Equals("scenarios", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDataFolderOnlyArgs("scenarios", args);
        }

        if (command.Equals("verify", StringComparison.OrdinalIgnoreCase))
        {
            return ParseTextOutputArgs("verify", args);
        }

        if (command.Equals("validate-handoff", StringComparison.OrdinalIgnoreCase))
        {
            return ParseValidateHandoffArgs(args);
        }

        if (command.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDataFolderOnlyArgs("status", args);
        }

        return ParseResult.Invalid($"Unknown command: {command}");
    }

    private static ParseResult ParseDataFolderOnlyArgs(string command, string[] args)
    {
        string? dataFolder = null;
        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.Equals("--data-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--data-folder requires a value.");
                }

                dataFolder = args[++index];
                continue;
            }

            return ParseResult.Invalid($"Unknown option: {arg}");
        }

        return ParseResult.Valid(command, dataFolder);
    }

    private static ParseResult ParseTextOutputArgs(string command, string[] args)
    {
        string? dataFolder = null;
        string? outputPath = null;

        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.Equals("--data-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--data-folder requires a value.");
                }

                dataFolder = args[++index];
                continue;
            }

            if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--output requires a value.");
                }

                outputPath = args[++index];
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return ParseResult.Invalid("--output requires a value.");
                }

                continue;
            }

            return ParseResult.Invalid($"Unknown option: {arg}");
        }

        return ParseResult.TextOutput(command, dataFolder, outputPath);
    }

    private static ParseResult ParseRecordArgs(string[] args)
    {
        string? dataFolder = null;
        int? playbackCount = null;
        string? outcome = null;
        var sameAccountSession = false;
        var restartSession = false;
        var resources = false;
        string? isolatedGroupId = null;
        string? scenarioId = null;
        string? scenarioName = null;
        double? observedCpuPercent = null;
        double? observedGpuPercent = null;
        double? observedMemoryMegabytes = null;
        IReadOnlyList<string> verifiedProfileGroups = [];
        string? accountLabel = null;
        string? notes = null;
        var dryRun = false;

        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.Equals("--data-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--data-folder requires a value.");
                }

                dataFolder = args[++index];
                continue;
            }

            if (arg.Equals("--count", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || !int.TryParse(args[++index], out var count))
                {
                    return ParseResult.Invalid("--count requires an integer value.");
                }

                if (count is < 1 or > PlaybackTestPlanService.MaxSlotCount)
                {
                    return ParseResult.Invalid("--count must be between 1 and 16.");
                }

                playbackCount = count;
                continue;
            }

            if (arg.Equals("--outcome", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--outcome requires a value.");
                }

                outcome = args[++index].ToLowerInvariant();
                if (outcome is not ("success" or "partial" or "failure"))
                {
                    return ParseResult.Invalid("--outcome must be success, partial, or failure.");
                }

                continue;
            }

            if (arg.Equals("--account", StringComparison.OrdinalIgnoreCase))
            {
                sameAccountSession = true;
                continue;
            }

            if (arg.Equals("--account-label", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--account-label requires a value.");
                }

                accountLabel = args[++index].Trim();
                if (string.IsNullOrWhiteSpace(accountLabel))
                {
                    return ParseResult.Invalid("--account-label requires a value.");
                }

                continue;
            }

            if (arg.Equals("--restart", StringComparison.OrdinalIgnoreCase))
            {
                restartSession = true;
                continue;
            }

            if (arg.Equals("--resources", StringComparison.OrdinalIgnoreCase))
            {
                resources = true;
                continue;
            }

            if (arg.Equals("--profile-groups", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--profile-groups requires a value.");
                }

                verifiedProfileGroups = ParseProfileGroups(args[++index]);
                if (verifiedProfileGroups.Count == 0)
                {
                    return ParseResult.Invalid("--profile-groups requires a value.");
                }

                continue;
            }

            if (arg.Equals("--group", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--group requires a value.");
                }

                var normalizedGroup = FeasibilityProfileGroupEvidenceService.Normalize([args[++index]]);
                if (normalizedGroup.Count != 1 ||
                    FeasibilityProfileGroupEvidenceService.ValidateValues(normalizedGroup) is not null)
                {
                    return ParseResult.Invalid("--group must be A, B, C, or D.");
                }

                isolatedGroupId = normalizedGroup[0];
                continue;
            }

            if (arg.Equals("--notes", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--notes requires a value.");
                }

                notes = args[++index];
                continue;
            }

            if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            if (arg.Equals("--cpu-percent", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseOptionDouble(args, ref index, "--cpu-percent", 0, 100, out observedCpuPercent, out var errorMessage))
                {
                    return ParseResult.Invalid(errorMessage);
                }

                continue;
            }

            if (arg.Equals("--gpu-percent", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseOptionDouble(args, ref index, "--gpu-percent", 0, 100, out observedGpuPercent, out var errorMessage))
                {
                    return ParseResult.Invalid(errorMessage);
                }

                continue;
            }

            if (arg.Equals("--memory-mb", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseOptionDouble(args, ref index, "--memory-mb", 0, double.MaxValue, out observedMemoryMegabytes, out var errorMessage))
                {
                    return ParseResult.Invalid(errorMessage);
                }

                continue;
            }

            if (arg.Equals("--scenario", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--scenario requires a value.");
                }

                scenarioId = args[++index];
                if (string.IsNullOrWhiteSpace(scenarioId))
                {
                    return ParseResult.Invalid("--scenario requires a value.");
                }

                continue;
            }

            if (arg.Equals("--scenario-name", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--scenario-name requires a value.");
                }

                scenarioName = args[++index];
                if (string.IsNullOrWhiteSpace(scenarioName))
                {
                    return ParseResult.Invalid("--scenario-name requires a value.");
                }

                continue;
            }

            return ParseResult.Invalid($"Unknown option: {arg}");
        }

        if (isolatedGroupId is not null && (!string.IsNullOrWhiteSpace(scenarioId) || !string.IsNullOrWhiteSpace(scenarioName)))
        {
            return ParseResult.Invalid("--group cannot be combined with --scenario or --scenario-name.");
        }

        if (playbackCount is null && isolatedGroupId is not null)
        {
            playbackCount = 4;
        }

        if (playbackCount is null)
        {
            return ParseResult.Invalid("record requires --count.");
        }

        if (isolatedGroupId is not null && playbackCount is > 4)
        {
            return ParseResult.Invalid("--group can only be used with --count 1-4.");
        }

        if (string.IsNullOrWhiteSpace(outcome))
        {
            return ParseResult.Invalid("record requires --outcome.");
        }

        var validationError = new FeasibilityResultValidationService().Validate(
            playbackCount.Value,
            outcome,
            sameAccountSession,
            restartSession,
            resources,
            observedCpuPercent,
            observedGpuPercent,
            observedMemoryMegabytes,
            verifiedProfileGroups,
            accountLabel);
        if (validationError is not null)
        {
            return ParseResult.Invalid(validationError);
        }

        var defaultScenario = isolatedGroupId is null
            ? CreateDefaultRecordScenario(playbackCount.Value)
            : new FeasibilityScenarioService().CreateIsolatedGroupScenario(isolatedGroupId, playbackCount.Value);
        scenarioId ??= defaultScenario.Id;
        scenarioName ??= scenarioId.Equals(defaultScenario.Id, StringComparison.OrdinalIgnoreCase)
            ? defaultScenario.Name
            : scenarioId;

        var scenarioPlaybackCountError = FeasibilityScenarioService.ValidatePlaybackCountConsistency(
            playbackCount.Value,
            scenarioId);
        if (scenarioPlaybackCountError is not null)
        {
            return ParseResult.Invalid(scenarioPlaybackCountError);
        }

        var scenarioProfileGroupError = FeasibilityProfileGroupEvidenceService.ValidateScenarioConsistency(
            playbackCount.Value,
            scenarioId,
            verifiedProfileGroups);
        if (scenarioProfileGroupError is not null)
        {
            return ParseResult.Invalid(scenarioProfileGroupError);
        }

        return ParseResult.Record(
            dataFolder,
            playbackCount.Value,
            outcome,
            sameAccountSession,
            restartSession,
            resources,
            scenarioId,
            scenarioName,
            verifiedProfileGroups,
            observedCpuPercent,
            observedGpuPercent,
            observedMemoryMegabytes,
            accountLabel,
            notes,
            dryRun);
    }

    private static FeasibilityScenario CreateDefaultRecordScenario(int playbackCount)
    {
        var plan = new PlaybackTestPlanService().CreatePlan(playbackCount);
        return new FeasibilityScenarioService().CreateFirstSlotsScenario(plan);
    }

    private static ParseResult ParseReportArgs(string[] args)
    {
        string? dataFolder = null;
        string? profileFolder = null;

        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.Equals("--data-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--data-folder requires a value.");
                }

                dataFolder = args[++index];
                continue;
            }

            if (arg.Equals("--profile-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--profile-folder requires a value.");
                }

                profileFolder = args[++index];
                continue;
            }

            return ParseResult.Invalid($"Unknown option: {arg}");
        }

        return ParseResult.Report(dataFolder, profileFolder);
    }

    private static ParseResult ParsePreflightArgs(string[] args)
    {
        string? dataFolder = null;
        string? profileFolder = null;
        string? outputPath = null;

        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.Equals("--data-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--data-folder requires a value.");
                }

                dataFolder = args[++index];
                continue;
            }

            if (arg.Equals("--profile-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--profile-folder requires a value.");
                }

                profileFolder = args[++index];
                continue;
            }

            if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--output requires a value.");
                }

                outputPath = args[++index];
                continue;
            }

            return ParseResult.Invalid($"Unknown option: {arg}");
        }

        return ParseResult.Preflight(dataFolder, profileFolder, outputPath);
    }

    private static ParseResult ParseHandoffArgs(string[] args)
    {
        string? dataFolder = null;
        string? profileFolder = null;
        string? outputFolder = null;

        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.Equals("--data-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--data-folder requires a value.");
                }

                dataFolder = args[++index];
                continue;
            }

            if (arg.Equals("--profile-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--profile-folder requires a value.");
                }

                profileFolder = args[++index];
                continue;
            }

            if (arg.Equals("--output-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--output-folder requires a value.");
                }

                outputFolder = args[++index];
                if (string.IsNullOrWhiteSpace(outputFolder))
                {
                    return ParseResult.Invalid("--output-folder requires a value.");
                }

                continue;
            }

            return ParseResult.Invalid($"Unknown option: {arg}");
        }

        return ParseResult.Handoff(dataFolder, profileFolder, outputFolder);
    }

    private static ParseResult ParseValidateHandoffArgs(string[] args)
    {
        string? inputFolder = null;
        string? outputPath = null;

        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.Equals("--input-folder", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--input-folder requires a value.");
                }

                inputFolder = args[++index];
                if (string.IsNullOrWhiteSpace(inputFolder))
                {
                    return ParseResult.Invalid("--input-folder requires a value.");
                }

                continue;
            }

            if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseResult.Invalid("--output requires a value.");
                }

                outputPath = args[++index];
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return ParseResult.Invalid("--output requires a value.");
                }

                continue;
            }

            return ParseResult.Invalid($"Unknown option: {arg}");
        }

        if (string.IsNullOrWhiteSpace(inputFolder))
        {
            return ParseResult.Invalid("validate-handoff requires --input-folder.");
        }

        return ParseResult.TextOutput("validate-handoff", inputFolder, outputPath);
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  StreamOrchestra.Tools status [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools audit [--data-folder <path>] [--output <path>]");
        writer.WriteLine("  StreamOrchestra.Tools browsers [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools checklist [--data-folder <path>] [--output <path>]");
        writer.WriteLine("  StreamOrchestra.Tools fallback [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools handoff [--data-folder <path>] [--profile-folder <path>] [--output-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools history [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools preflight [--data-folder <path>] [--profile-folder <path>] [--output <path>]");
        writer.WriteLine("  StreamOrchestra.Tools record [--count <1-16>] [--group <A-D>] --outcome <success|partial|failure> [--account] [--account-label <text>] [--profile-groups <A,B,C,D>] [--restart] [--resources] [--cpu-percent <0-100>] [--gpu-percent <0-100>] [--memory-mb <value>] [--scenario <id>] [--scenario-name <text>] [--notes <text>] [--dry-run] [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools report [--data-folder <path>] [--profile-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools scenarios");
        writer.WriteLine("  StreamOrchestra.Tools validate-handoff --input-folder <path> [--output <path>]");
        writer.WriteLine("  StreamOrchestra.Tools verify [--data-folder <path>] [--output <path>]");
        writer.WriteLine("  StreamOrchestra.Tools --help");
    }

    private static void WriteAuditSummary(IReadOnlyList<FeasibilityAuditItem> auditItems, TextWriter output)
    {
        if (auditItems.Count == 0)
        {
            return;
        }

        var summary = new FeasibilityAuditService().CreateSummary(auditItems);

        output.WriteLine($"Plan audit: {summary.ToCompactText()}");
    }

    private static void WritePhase0SuccessGate(IReadOnlyList<FeasibilityAuditItem> auditItems, TextWriter output)
    {
        var successGate = auditItems.FirstOrDefault(item => item.Id == "phase0_success_gate");
        if (successGate is null)
        {
            return;
        }

        output.WriteLine($"Success gate: [{successGate.Status}] {successGate.Evidence}");
    }

    private static void WritePlanVerificationStatus(IReadOnlyList<FeasibilityAuditItem> auditItems, TextWriter output)
    {
        if (auditItems.Count == 0)
        {
            return;
        }

        var status = new FeasibilityAuditService().CreatePlanVerificationStatus(auditItems);

        output.WriteLine($"Plan verification: [{status}]");
    }

    private static void WriteOutstandingGates(IReadOnlyList<FeasibilityAuditItem> auditItems, TextWriter output)
    {
        var outstandingItems = auditItems
            .Where(item => !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (outstandingItems.Length == 0)
        {
            return;
        }

        output.WriteLine("Outstanding gates:");
        foreach (var item in outstandingItems)
        {
            output.WriteLine($"- [{item.Status}] {item.Title}: {item.Evidence}");
        }
    }

    private static void WriteSuggestedRecordShapes(IReadOnlyList<FeasibilityAuditItem> auditItems, TextWriter output)
    {
        var suggestions = new FeasibilityAuditService().CreateSuggestedRecordShapes(auditItems);
        if (suggestions.Count == 0)
        {
            return;
        }

        output.WriteLine("Suggested record shapes:");
        foreach (var suggestion in suggestions)
        {
            output.WriteLine($"- {suggestion}");
        }
    }

    private static void WriteNextAction(FeasibilityDecision decision, TextWriter output)
    {
        if (!string.IsNullOrWhiteSpace(decision.NextAction))
        {
            output.WriteLine($"Next action: {decision.NextAction}");
        }
    }

    private static IReadOnlyList<LayoutPreset>? TryLoadLayouts()
    {
        try
        {
            return new LayoutPresetService().LoadFromDefaultLocation();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatExternalBrowserFallbackScriptStatus(
        DiagnosticReport report,
        string? fallbackScriptPath)
    {
        if (fallbackScriptPath is not null)
        {
            return $"External browser fallback script: {fallbackScriptPath}";
        }

        var reason = report.ExternalBrowserFallbackPlan?.Reason ?? "No last saved session is available.";
        return $"External browser fallback script: not available ({reason})";
    }

    private static void SaveTextFile(string path, string text)
    {
        var directoryName = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryName))
        {
            Directory.CreateDirectory(directoryName);
        }

        File.WriteAllText(path, text);
    }

    private static string ResolveHandoffOutputFolder(string? dataFolder, string? outputFolder)
    {
        if (!string.IsNullOrWhiteSpace(outputFolder))
        {
            return outputFolder;
        }

        var storage = new FeasibilityResultStorageService(dataFolder);
        return Path.Combine(storage.DataFolder, $"phase0-handoff-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
    }

    private static string SaveHandoffArtifact(
        string outputFolder,
        string fileName,
        IReadOnlyList<string> lines)
    {
        var path = Path.Combine(outputFolder, fileName);
        SaveTextFile(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        return path;
    }

    private static string SaveHandoffResultsSnapshot(
        string outputFolder,
        IReadOnlyList<FeasibilityTestResult> results)
    {
        var path = Path.Combine(outputFolder, HandoffResultsFileName);
        SaveTextFile(path, JsonSerializer.Serialize(results, HandoffJsonOptions) + Environment.NewLine);
        return path;
    }

    private static string SaveHandoffDiagnosticReport(
        string outputFolder,
        DiagnosticReport report)
    {
        var path = Path.Combine(outputFolder, HandoffDiagnosticReportFileName);
        SaveTextFile(path, JsonSerializer.Serialize(report, HandoffJsonOptions) + Environment.NewLine);
        return path;
    }

    private static string SaveHandoffManifest(
        string outputFolder,
        DateTimeOffset generatedAt,
        string dataFolder,
        string resultsFilePath,
        int resultCount,
        bool isPreflightReady,
        bool isVerified,
        string decisionCode,
        string decisionTitle,
        string planVerificationStatus,
        int passingGateCount,
        int pendingGateCount,
        int failingGateCount,
        int outstandingGateCount,
        IReadOnlyList<string> artifactFiles,
        IReadOnlyList<HandoffArtifactMetadata> artifactDetails)
    {
        var path = Path.Combine(outputFolder, HandoffManifestFileName);
        var manifest = new HandoffManifest(
            generatedAt,
            dataFolder,
            resultsFilePath,
            resultCount,
            isPreflightReady,
            isVerified,
            decisionCode,
            decisionTitle,
            planVerificationStatus,
            passingGateCount,
            pendingGateCount,
            failingGateCount,
            outstandingGateCount,
            artifactFiles,
            artifactDetails);
        SaveTextFile(path, JsonSerializer.Serialize(manifest, HandoffJsonOptions) + Environment.NewLine);
        return path;
    }

    private static HandoffArtifactMetadata CreateHandoffArtifactMetadata(string path)
    {
        var fileInfo = new FileInfo(path);

        return new HandoffArtifactMetadata(
            fileInfo.Name,
            fileInfo.Length,
            ComputeFileSha256(path));
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteLines(IReadOnlyList<string> lines, TextWriter output)
    {
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }
    }

    private static bool TryParseOptionDouble(
        string[] args,
        ref int index,
        string optionName,
        double minValue,
        double maxValue,
        out double? value,
        out string errorMessage)
    {
        value = null;
        errorMessage = "";
        if (index + 1 >= args.Length)
        {
            errorMessage = $"{optionName} requires a value.";
            return false;
        }

        var rawValue = args[++index];
        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
        {
            errorMessage = $"{optionName} requires a numeric value.";
            return false;
        }

        if (parsedValue < minValue || parsedValue > maxValue)
        {
            errorMessage = maxValue == double.MaxValue
                ? $"{optionName} must be {minValue} or higher."
                : $"{optionName} must be between {minValue} and {maxValue}.";
            return false;
        }

        value = parsedValue;
        return true;
    }

    private static string FormatNullable(double? value)
    {
        return value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "n/a";
    }

    private static string FormatAccountLabel(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "n/a" : value.Trim();
    }

    private sealed record ParseResult(
        bool IsValid,
        bool ShowHelp,
        string Command,
        string? DataFolder,
        string? ProfileFolder,
        string? OutputPath,
        int? PlaybackCount,
        string? Outcome,
        bool SameAccountSession,
        bool RestartSession,
        bool ResourceUsageAcceptable,
        string ScenarioId,
        string ScenarioName,
        IReadOnlyList<string> VerifiedProfileGroups,
        double? ObservedCpuPercent,
        double? ObservedGpuPercent,
        double? ObservedMemoryMegabytes,
        string? AccountLabel,
        string? Notes,
        bool DryRun,
        string ErrorMessage)
    {
        public static ParseResult Valid(string command, string? dataFolder)
        {
            return new ParseResult(true, false, command, dataFolder, null, null, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, false, "");
        }

        public static ParseResult TextOutput(string command, string? dataFolder, string? outputPath)
        {
            return new ParseResult(true, false, command, dataFolder, null, outputPath, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, false, "");
        }

        public static ParseResult Record(
            string? dataFolder,
            int playbackCount,
            string outcome,
            bool sameAccountSession,
            bool restartSession,
            bool resources,
            string scenarioId,
            string scenarioName,
            IReadOnlyList<string> verifiedProfileGroups,
            double? observedCpuPercent,
            double? observedGpuPercent,
            double? observedMemoryMegabytes,
            string? accountLabel,
            string? notes,
            bool dryRun)
        {
            return new ParseResult(
                true,
                false,
                "record",
                dataFolder,
                null,
                null,
                playbackCount,
                outcome,
                sameAccountSession,
                restartSession,
                resources,
                scenarioId,
                scenarioName,
                verifiedProfileGroups,
                observedCpuPercent,
                observedGpuPercent,
                observedMemoryMegabytes,
                accountLabel,
                notes,
                dryRun,
                "");
        }

        public static ParseResult Report(string? dataFolder, string? profileFolder)
        {
            return new ParseResult(true, false, "report", dataFolder, profileFolder, null, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, false, "");
        }

        public static ParseResult Preflight(string? dataFolder, string? profileFolder, string? outputPath)
        {
            return new ParseResult(true, false, "preflight", dataFolder, profileFolder, outputPath, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, false, "");
        }

        public static ParseResult Handoff(string? dataFolder, string? profileFolder, string? outputFolder)
        {
            return new ParseResult(true, false, "handoff", dataFolder, profileFolder, outputFolder, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, false, "");
        }

        public static ParseResult Help()
        {
            return new ParseResult(true, true, "help", null, null, null, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, false, "");
        }

        public static ParseResult Invalid(string errorMessage)
        {
            return new ParseResult(false, false, "", null, null, null, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, false, errorMessage);
        }
    }

    private sealed record HandoffManifest(
        DateTimeOffset GeneratedAt,
        string DataFolder,
        string ResultsFilePath,
        int ResultCount,
        bool IsPreflightReady,
        bool IsVerified,
        string DecisionCode,
        string DecisionTitle,
        string PlanVerificationStatus,
        int PassingGateCount,
        int PendingGateCount,
        int FailingGateCount,
        int OutstandingGateCount,
        IReadOnlyList<string> ArtifactFiles,
        IReadOnlyList<HandoffArtifactMetadata> ArtifactDetails);

    private sealed record HandoffArtifactMetadata(
        string FileName,
        long SizeBytes,
        string Sha256);

    private sealed record HandoffResultsSummary(
        IReadOnlyList<FeasibilityTestResult> Results,
        FeasibilityDecision Decision,
        IReadOnlyList<FeasibilityAuditItem> AuditItems,
        FeasibilityAuditSummary AuditSummary,
        string PlanVerificationStatus,
        int OutstandingGateCount);

    private static IReadOnlyList<string> ParseProfileGroups(string rawValue)
    {
        return FeasibilityProfileGroupEvidenceService.Normalize(
            rawValue.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
