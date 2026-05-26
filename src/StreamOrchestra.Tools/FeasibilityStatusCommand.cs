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

    private static readonly string[] RequiredHandoffProfileGroupIds = ["A", "B", "C", "D"];

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

        try
        {
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
        catch (Exception ex) when (IsCommandEnvironmentException(ex))
        {
            error.WriteLine($"Command failed: {ex.Message}");
            return 1;
        }
    }

    private static bool IsCommandEnvironmentException(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
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
        var dataContext = CreateHandoffDataContext(parseResult.DataFolder);
        var outputFolder = ResolveHandoffOutputFolder(dataContext.DataFolder, parseResult.OutputPath);
        Directory.CreateDirectory(outputFolder);
        var generatedAt = DateTimeOffset.Now;
        var preflightSnapshot = CreateHandoffPreflightSnapshot(parseResult.ProfileFolder);
        var isPreflightReady = IsHandoffPreflightReady(dataContext.DataStorageStatus, preflightSnapshot);
        var isVerified = IsHandoffVerified(dataContext.Summary);
        var diagnosticReport = CreateHandoffDiagnosticReport(parseResult, dataContext, preflightSnapshot);
        var manifestContext = CreateHandoffManifest(
            generatedAt,
            dataContext,
            preflightSnapshot,
            isPreflightReady,
            isVerified,
            [],
            []);
        var preflightLines = CreateHandoffPreflightArtifactLines(manifestContext, dataContext.Summary);
        var checklistLines = CreateHandoffChecklistArtifactLines(manifestContext, dataContext.Summary);
        var auditLines = CreateHandoffAuditArtifactLines(manifestContext, dataContext.Summary);
        var verificationLines = CreateHandoffVerificationArtifactLines(manifestContext, dataContext.Summary);
        var historyLines = CreateHandoffHistoryArtifactLines(manifestContext, dataContext.Results);
        var artifacts = new[]
        {
            SaveHandoffArtifact(outputFolder, HandoffPreflightFileName, preflightLines),
            SaveHandoffArtifact(outputFolder, HandoffChecklistFileName, checklistLines),
            SaveHandoffArtifact(outputFolder, HandoffAuditFileName, auditLines),
            SaveHandoffArtifact(outputFolder, HandoffVerificationFileName, verificationLines),
            SaveHandoffArtifact(outputFolder, HandoffHistoryFileName, historyLines),
            SaveHandoffDiagnosticReport(outputFolder, diagnosticReport),
            SaveHandoffResultsSnapshot(outputFolder, dataContext.Results)
        };
        var artifactFileNames = artifacts
            .Select(Path.GetFileName)
            .Where(fileName => fileName is not null)
            .Select(fileName => fileName!)
            .ToArray();
        var artifactDetails = artifacts
            .Select(CreateHandoffArtifactMetadata)
            .ToArray();
        var manifest = manifestContext with
        {
            ArtifactFiles = artifactFileNames,
            ArtifactDetails = artifactDetails
        };
        var manifestPath = SaveHandoffManifest(outputFolder, manifest);

        output.WriteLine("Stream Orchestra Phase 0 Handoff");
        output.WriteLine($"Output folder: {outputFolder}");
        output.WriteLine($"Generated at: {generatedAt:O}");
        output.WriteLine($"Results snapshot source: {dataContext.ResultsFilePath}");
        output.WriteLine($"Results snapshot count: {dataContext.Results.Count}");
        foreach (var artifact in artifacts)
        {
            output.WriteLine($"Saved: {artifact}");
        }

        output.WriteLine($"Saved: {manifestPath}");
        output.WriteLine($"Preflight ready: {isPreflightReady}");
        output.WriteLine($"Verification complete: {isVerified}");
        output.WriteLine($"Plan verification: {dataContext.Summary.PlanVerificationStatus}");
        output.WriteLine($"Plan audit: {dataContext.Summary.AuditSummary.ToCompactText()}");
        output.WriteLine($"Outstanding gates: {dataContext.Summary.OutstandingGateCount}");
        output.WriteLine("Use the saved files as the setup, checklist, audit, and verification artifacts for the manual SOOP run.");
        return 0;
    }

    private static HandoffDataContext CreateHandoffDataContext(string? dataFolder)
    {
        var resolvedDataFolder = FeasibilityResultStorageService.ResolveDataFolder(dataFolder);
        var resultsFilePath = FeasibilityResultStorageService.GetResultsFilePath(resolvedDataFolder);
        var dataStorageStatus = GetDataStorageStatus(resolvedDataFolder);
        FeasibilityResultStorageService? storage = null;
        IReadOnlyList<FeasibilityTestResult> results = [];

        if (dataStorageStatus.StartsWith("[ready]", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                storage = new FeasibilityResultStorageService(resolvedDataFolder);
                results = storage.LoadResults();
                resolvedDataFolder = storage.DataFolder;
                resultsFilePath = storage.ResultsFilePath;
            }
            catch (Exception ex) when (IsCommandEnvironmentException(ex))
            {
                storage = null;
                dataStorageStatus = $"[blocked] {ex.Message}";
                results = [];
            }
        }

        return new HandoffDataContext(
            resolvedDataFolder,
            resultsFilePath,
            dataStorageStatus,
            results,
            CreateHandoffResultsSummary(results),
            storage);
    }

    private static bool IsHandoffPreflightReady(
        string dataStorageStatus,
        HandoffPreflightSnapshot preflightSnapshot)
    {
        return dataStorageStatus.StartsWith("[ready]", StringComparison.OrdinalIgnoreCase) &&
            preflightSnapshot.WebView2RuntimeStatus.StartsWith("[available]", StringComparison.OrdinalIgnoreCase) &&
            preflightSnapshot.PlaybackLayoutStatus.StartsWith("[ready]", StringComparison.OrdinalIgnoreCase) &&
            preflightSnapshot.ProfileGroups.Count == RequiredHandoffProfileGroupIds.Length &&
            preflightSnapshot.ProfileGroups.All(group => group.Status.Equals("ready", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHandoffVerified(HandoffResultsSummary summary)
    {
        return summary.AuditItems.Count > 0 &&
            summary.AuditItems.All(item => item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase));
    }

    private static DiagnosticReport CreateHandoffDiagnosticReport(
        ParseResult parseResult,
        HandoffDataContext dataContext,
        HandoffPreflightSnapshot preflightSnapshot)
    {
        return dataContext.Storage is null
            ? CreateBlockedHandoffDiagnosticReport(dataContext, preflightSnapshot)
            : CreateDiagnosticReport(parseResult, dataContext.Storage, dataContext.Results);
    }

    private static DiagnosticReport CreateBlockedHandoffDiagnosticReport(
        HandoffDataContext dataContext,
        HandoffPreflightSnapshot preflightSnapshot)
    {
        return new DiagnosticReport
        {
            GeneratedAt = DateTimeOffset.Now,
            ProfileRootFolder = preflightSnapshot.ProfileRootFolder,
            ProfileGroups = preflightSnapshot.ProfileGroups
                .Select(group => new ProfileGroup(group.Id, $"SOOP Group {group.Id}", group.UserDataFolder))
                .ToArray(),
            DataFolder = dataContext.DataFolder,
            DataFiles = CreateBlockedHandoffDiagnosticDataFiles(dataContext.DataFolder, dataContext.ResultsFilePath),
            WorkspaceDiagnostics = new WorkspaceDiagnostics(0, 0, false, null, null, null, 0, 0),
            ExternalBrowsers = [],
            FeasibilityResultCount = dataContext.Results.Count,
            LatestFeasibilityResult = dataContext.Results
                .OrderByDescending(result => result.CapturedAt)
                .FirstOrDefault(),
            FeasibilitySameAccountLabels =
                FeasibilityProfileGroupEvidenceService.GetLatestSameAccountAccountLabels(dataContext.Results),
            HasConflictingFeasibilityAccountLabels =
                FeasibilityProfileGroupEvidenceService.HasConflictingSameAccountLabels(dataContext.Results),
            FeasibilityDecision = dataContext.Summary.Decision,
            FeasibilityAudit = dataContext.Summary.AuditItems,
            FeasibilitySuggestedRecordShapes =
                new FeasibilityAuditService().CreateSuggestedRecordShapes(dataContext.Summary.AuditItems)
        };
    }

    private static IReadOnlyList<DiagnosticDataFile> CreateBlockedHandoffDiagnosticDataFiles(
        string dataFolder,
        string resultsFilePath)
    {
        return
        [
            CreateBlockedHandoffDiagnosticDataFile("appstate", Path.Combine(dataFolder, "appstate.json")),
            CreateBlockedHandoffDiagnosticDataFile("workspaces", Path.Combine(dataFolder, "workspaces.json")),
            CreateBlockedHandoffDiagnosticDataFile("favorites", Path.Combine(dataFolder, "favorites.json")),
            CreateBlockedHandoffDiagnosticDataFile("feasibility-results", resultsFilePath),
            CreateBlockedHandoffDiagnosticDataFile("external-browsers", Path.Combine(dataFolder, "external-browsers.json"))
        ];
    }

    private static DiagnosticDataFile CreateBlockedHandoffDiagnosticDataFile(string name, string path)
    {
        var fileInfo = new FileInfo(path);
        return new DiagnosticDataFile(
            name,
            path,
            fileInfo.Exists,
            fileInfo.Exists ? fileInfo.Length : 0);
    }

    private static HandoffManifest CreateHandoffManifest(
        DateTimeOffset generatedAt,
        HandoffDataContext dataContext,
        HandoffPreflightSnapshot preflightSnapshot,
        bool isPreflightReady,
        bool isVerified,
        IReadOnlyList<string> artifactFiles,
        IReadOnlyList<HandoffArtifactMetadata> artifactDetails)
    {
        return new HandoffManifest(
            generatedAt,
            dataContext.DataFolder,
            dataContext.ResultsFilePath,
            dataContext.DataStorageStatus,
            preflightSnapshot.ProfileRootFolder,
            preflightSnapshot.WebView2RuntimeStatus,
            preflightSnapshot.PlaybackLayoutStatus,
            preflightSnapshot.ProfileGroups,
            dataContext.Results.Count,
            isPreflightReady,
            isVerified,
            dataContext.Summary.Decision.Code,
            dataContext.Summary.Decision.Title,
            dataContext.Summary.PlanVerificationStatus,
            dataContext.Summary.AuditSummary.PassCount,
            dataContext.Summary.AuditSummary.PendingCount,
            dataContext.Summary.AuditSummary.FailCount,
            dataContext.Summary.OutstandingGateCount,
            artifactFiles,
            artifactDetails);
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

        var manifestText = File.ReadAllText(manifestPath);
        HandoffManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<HandoffManifest>(
                manifestText,
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
        isValid &= ValidateHandoffManifestText(manifestText, manifest, validationLines);
        isValid &= ValidateHandoffManifestContext(manifest, DateTimeOffset.Now, validationLines);
        isValid &= ValidateHandoffManifestProfileGroups(manifest, validationLines);

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

        isValid &= ValidateHandoffManifestArtifactOrder(
            manifestArtifactFiles,
            artifactDetails,
            validationLines);

        var detailedFiles = new HashSet<string>(
            artifactDetails.Select(detail => detail.FileName),
            StringComparer.OrdinalIgnoreCase);
        var artifactFiles = new HashSet<string>(
            manifestArtifactFiles,
            StringComparer.OrdinalIgnoreCase);
        var requiredFiles = new HashSet<string>(RequiredHandoffArtifactFiles, StringComparer.OrdinalIgnoreCase);
        isValid &= ValidateHandoffInputFolderContents(inputFolder, requiredFiles, validationLines);

        foreach (var artifactFile in manifestArtifactFiles)
        {
            if (!string.IsNullOrWhiteSpace(artifactFile) && !requiredFiles.Contains(artifactFile))
            {
                isValid = false;
                validationLines.Add($"- [fail] {artifactFile}: unexpected artifactFiles entry.");
            }
        }

        foreach (var detail in artifactDetails)
        {
            if (!string.IsNullOrWhiteSpace(detail.FileName) && !requiredFiles.Contains(detail.FileName))
            {
                isValid = false;
                validationLines.Add($"- [fail] {detail.FileName}: unexpected artifactDetails entry.");
            }
        }

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

        HandoffResultsSummary? resultsSummary = null;
        var resultsPath = Path.Combine(inputFolder, HandoffResultsFileName);
        if (File.Exists(resultsPath))
        {
            try
            {
                var resultsText = File.ReadAllText(resultsPath);
                var results = JsonSerializer.Deserialize<FeasibilityTestResult[]>(
                    resultsText,
                    HandoffJsonOptions) ?? [];
                var normalizedResults = FeasibilityResultStorageService.NormalizeResults(results);
                isValid &= ValidateHandoffResultsSnapshotText(resultsText, normalizedResults, validationLines);
                resultsSummary = CreateHandoffResultsSummary(normalizedResults);
                if (normalizedResults.Count == manifest.ResultCount)
                {
                    validationLines.Add($"- [pass] {HandoffResultsFileName} result count: {normalizedResults.Count}");
                }
                else
                {
                    isValid = false;
                    validationLines.Add(
                        $"- [fail] {HandoffResultsFileName} result count mismatch, expected {manifest.ResultCount}, actual {normalizedResults.Count}.");
                }
            }
            catch (JsonException ex)
            {
                isValid = false;
                validationLines.Add($"- [fail] {HandoffResultsFileName} JSON is invalid: {ex.Message}");
            }
        }

        isValid &= ValidateHandoffPreflightArtifact(inputFolder, manifest, resultsSummary, validationLines);

        if (resultsSummary is not null)
        {
            isValid &= ValidateHandoffResultsSummary(resultsSummary, manifest, validationLines);
            isValid &= ValidateHandoffChecklistArtifact(inputFolder, manifest, resultsSummary, validationLines);
            isValid &= ValidateHandoffAuditArtifact(inputFolder, manifest, resultsSummary, validationLines);
            isValid &= ValidateHandoffVerificationArtifact(inputFolder, manifest, resultsSummary, validationLines);
            isValid &= ValidateHandoffHistoryArtifact(inputFolder, manifest, resultsSummary, validationLines);
        }

        var diagnosticReportPath = Path.Combine(inputFolder, HandoffDiagnosticReportFileName);
        if (File.Exists(diagnosticReportPath))
        {
            try
            {
                var diagnosticReportText = File.ReadAllText(diagnosticReportPath);
                var report = JsonSerializer.Deserialize<DiagnosticReport>(
                    diagnosticReportText,
                    HandoffJsonOptions);
                if (report is null)
                {
                    isValid = false;
                    validationLines.Add($"- [fail] {HandoffDiagnosticReportFileName} is empty.");
                }
                else
                {
                    isValid &= ValidateHandoffDiagnosticReport(
                        diagnosticReportText,
                        report,
                        manifest,
                        resultsSummary,
                        validationLines);
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

    private static bool ValidateHandoffInputFolderContents(
        string inputFolder,
        IReadOnlySet<string> requiredFiles,
        List<string> validationLines)
    {
        var isValid = true;
        var allowedFiles = new HashSet<string>(requiredFiles, StringComparer.OrdinalIgnoreCase)
        {
            HandoffManifestFileName
        };
        foreach (var fileName in Directory.EnumerateFiles(inputFolder).Select(Path.GetFileName))
        {
            if (string.IsNullOrWhiteSpace(fileName) || allowedFiles.Contains(fileName))
            {
                continue;
            }

            isValid = false;
            validationLines.Add($"- [fail] {fileName}: unexpected file in handoff folder.");
        }

        foreach (var directoryName in Directory.EnumerateDirectories(inputFolder).Select(Path.GetFileName))
        {
            isValid = false;
            validationLines.Add($"- [fail] {directoryName}: unexpected directory in handoff folder.");
        }

        if (isValid)
        {
            validationLines.Add("- [pass] handoff folder contains only standard artifacts.");
        }

        return isValid;
    }

    private static bool ValidateHandoffManifestText(
        string actualText,
        HandoffManifest manifest,
        List<string> validationLines)
    {
        var expectedText = JsonSerializer.Serialize(manifest, HandoffJsonOptions) + Environment.NewLine;
        if (actualText == expectedText)
        {
            validationLines.Add($"- [pass] {HandoffManifestFileName} canonical content.");
            return true;
        }

        validationLines.Add($"- [fail] {HandoffManifestFileName} canonical content mismatch.");
        return false;
    }

    private static bool ValidateHandoffManifestArtifactOrder(
        IReadOnlyList<string> artifactFiles,
        IReadOnlyList<HandoffArtifactMetadata> artifactDetails,
        List<string> validationLines)
    {
        var isValid = true;
        if (HasRequiredHandoffArtifactOrder(artifactFiles))
        {
            validationLines.Add($"- [pass] {HandoffManifestFileName} artifactFiles standard order.");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffManifestFileName} artifactFiles order mismatch, expected {FormatArtifactOrder(RequiredHandoffArtifactFiles)}, actual {FormatArtifactOrder(artifactFiles)}.");
        }

        var detailFiles = artifactDetails.Select(detail => detail.FileName).ToArray();
        if (HasRequiredHandoffArtifactOrder(detailFiles))
        {
            validationLines.Add($"- [pass] {HandoffManifestFileName} artifactDetails standard order.");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffManifestFileName} artifactDetails order mismatch, expected {FormatArtifactOrder(RequiredHandoffArtifactFiles)}, actual {FormatArtifactOrder(detailFiles)}.");
        }

        return isValid;
    }

    private static bool HasRequiredHandoffArtifactOrder(IReadOnlyList<string> fileNames)
    {
        return fileNames.Count == RequiredHandoffArtifactFiles.Length &&
            fileNames.SequenceEqual(RequiredHandoffArtifactFiles, StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatArtifactOrder(IEnumerable<string?> fileNames)
    {
        return string.Join(", ", fileNames.Select(fileName => string.IsNullOrWhiteSpace(fileName) ? "<blank>" : fileName));
    }

    private static bool ValidateHandoffManifestContext(
        HandoffManifest manifest,
        DateTimeOffset now,
        List<string> validationLines)
    {
        var isValid = true;
        if (manifest.GeneratedAt == default)
        {
            isValid = false;
            validationLines.Add($"- [fail] {HandoffManifestFileName} generatedAt is missing or default.");
        }
        else if (manifest.GeneratedAt > now.AddDays(1))
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffManifestFileName} generatedAt is in the future, expected no later than {now.AddDays(1):O}, actual {manifest.GeneratedAt:O}.");
        }
        else
        {
            validationLines.Add($"- [pass] {HandoffManifestFileName} generatedAt: {manifest.GeneratedAt:O}");
        }

        isValid &= ValidateManifestFullPath("data folder", manifest.DataFolder, validationLines);
        isValid &= ValidateManifestFullPath("results file", manifest.ResultsFilePath, validationLines);
        isValid &= ValidateManifestFullPath("profile root", manifest.ProfileRootFolder, validationLines);
        if (!string.IsNullOrWhiteSpace(manifest.DataStorageStatus) &&
            (manifest.DataStorageStatus.StartsWith("[ready]", StringComparison.OrdinalIgnoreCase) ||
                manifest.DataStorageStatus.StartsWith("[blocked]", StringComparison.OrdinalIgnoreCase)))
        {
            validationLines.Add(
                $"- [pass] {HandoffManifestFileName} data storage status: {manifest.DataStorageStatus}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffManifestFileName} data storage status is invalid: {FormatMismatchLine(manifest.DataStorageStatus)}.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.DataFolder) && !string.IsNullOrWhiteSpace(manifest.ResultsFilePath))
        {
            var expectedResultsFilePath = Path.Combine(manifest.DataFolder.Trim(), "feasibility-results.json");
            if (AreEquivalentPaths(manifest.ResultsFilePath, expectedResultsFilePath))
            {
                validationLines.Add(
                    $"- [pass] {HandoffManifestFileName} results file belongs to data folder.");
            }
            else
            {
                isValid = false;
                validationLines.Add(
                    $"- [fail] {HandoffManifestFileName} results file path mismatch, expected {FormatPathForValidation(expectedResultsFilePath)}, actual {FormatPathForValidation(manifest.ResultsFilePath)}.");
            }
        }

        return isValid;
    }

    private static bool ValidateManifestFullPath(
        string fieldName,
        string? path,
        List<string> validationLines)
    {
        if (!IsFullyQualifiedPath(path))
        {
            validationLines.Add(
                $"- [fail] {HandoffManifestFileName} {fieldName} path is missing or not fully qualified: {FormatPathForValidation(path)}.");
            return false;
        }

        validationLines.Add(
            $"- [pass] {HandoffManifestFileName} {fieldName} path: {FormatPathForValidation(path)}");
        return true;
    }

    private static bool IsFullyQualifiedPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path.Trim());
    }

    private static bool ValidateHandoffManifestProfileGroups(
        HandoffManifest manifest,
        List<string> validationLines)
    {
        var isValid = true;
        if (string.IsNullOrWhiteSpace(manifest.ProfileRootFolder))
        {
            isValid = false;
            validationLines.Add($"- [fail] {HandoffManifestFileName} profile root is missing.");
        }

        var profileGroups = (manifest.ProfileGroups ?? Array.Empty<HandoffProfileGroupMetadata>())
            .Where(group => group is not null)
            .Select(group => group!)
            .ToArray();
        if (profileGroups.Length == 0)
        {
            validationLines.Add($"- [fail] {HandoffManifestFileName} profile groups are missing.");
            return false;
        }

        foreach (var duplicateGroupId in FindDuplicateFileNames(profileGroups.Select(group => group.Id)))
        {
            isValid = false;
            validationLines.Add($"- [fail] {HandoffManifestFileName} profile group {duplicateGroupId} is duplicated.");
        }

        var requiredGroupIds = new HashSet<string>(RequiredHandoffProfileGroupIds, StringComparer.OrdinalIgnoreCase);
        var actualGroupIds = new HashSet<string>(
            profileGroups
                .Where(group => !string.IsNullOrWhiteSpace(group.Id))
                .Select(group => group.Id),
            StringComparer.OrdinalIgnoreCase);
        foreach (var group in profileGroups)
        {
            if (string.IsNullOrWhiteSpace(group.Id))
            {
                isValid = false;
                validationLines.Add($"- [fail] {HandoffManifestFileName} profile group id is missing.");
                continue;
            }

            if (!requiredGroupIds.Contains(group.Id))
            {
                isValid = false;
                validationLines.Add($"- [fail] {HandoffManifestFileName} profile group {group.Id} is unexpected.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(group.UserDataFolder))
            {
                isValid = false;
                validationLines.Add($"- [fail] {HandoffManifestFileName} profile group {group.Id} folder is missing.");
            }
            else
            {
                var expectedFolder = Path.Combine(manifest.ProfileRootFolder, $"Group{group.Id.ToUpperInvariant()}");
                if (!AreEquivalentPaths(group.UserDataFolder, expectedFolder))
                {
                    isValid = false;
                    validationLines.Add(
                        $"- [fail] {HandoffManifestFileName} profile group {group.Id} folder mismatch, expected {FormatPathForValidation(expectedFolder)}, actual {FormatPathForValidation(group.UserDataFolder)}.");
                }
            }

            if (!string.Equals(group.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(group.Status, "missing", StringComparison.OrdinalIgnoreCase))
            {
                isValid = false;
                validationLines.Add(
                    $"- [fail] {HandoffManifestFileName} profile group {group.Id} status is invalid: {FormatMismatchLine(group.Status)}.");
            }
        }

        foreach (var requiredGroupId in RequiredHandoffProfileGroupIds)
        {
            if (!actualGroupIds.Contains(requiredGroupId))
            {
                isValid = false;
                validationLines.Add($"- [fail] {HandoffManifestFileName} profile group {requiredGroupId} is missing.");
            }
        }

        if (isValid)
        {
            validationLines.Add(
                $"- [pass] {HandoffManifestFileName} profile groups: {string.Join(", ", RequiredHandoffProfileGroupIds)}");
        }

        return isValid;
    }

    private static bool ValidateHandoffResultsSnapshotText(
        string actualText,
        IReadOnlyList<FeasibilityTestResult> normalizedResults,
        List<string> validationLines)
    {
        var expectedText = JsonSerializer.Serialize(normalizedResults, HandoffJsonOptions) + Environment.NewLine;
        if (string.Equals(actualText, expectedText, StringComparison.Ordinal))
        {
            validationLines.Add($"- [pass] {HandoffResultsFileName} normalized snapshot content.");
            return true;
        }

        validationLines.Add($"- [fail] {HandoffResultsFileName} normalized snapshot content mismatch.");
        return false;
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
        var expectedLines = CreateHandoffChecklistArtifactLines(manifest, summary);
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
        var expectedLines = CreateHandoffAuditArtifactLines(manifest, summary);
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
        HandoffResultsSummary? summary,
        List<string> validationLines)
    {
        var preflightPath = Path.Combine(inputFolder, HandoffPreflightFileName);
        if (!File.Exists(preflightPath))
        {
            return true;
        }

        var lines = File.ReadAllLines(preflightPath);
        var isValid = true;
        var dataFolder = ReadLineValue(lines, "Data folder:");
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            isValid = false;
            validationLines.Add($"- [fail] {HandoffPreflightFileName} data folder line is missing.");
        }
        else if (AreEquivalentPaths(dataFolder, manifest.DataFolder))
        {
            validationLines.Add($"- [pass] {HandoffPreflightFileName} data folder: {FormatPathForValidation(dataFolder)}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffPreflightFileName} data folder mismatch, expected {FormatPathForValidation(manifest.DataFolder)}, actual {FormatPathForValidation(dataFolder)}.");
        }

        var resultsFile = ReadLineValue(lines, "Results file:");
        if (string.IsNullOrWhiteSpace(resultsFile))
        {
            isValid = false;
            validationLines.Add($"- [fail] {HandoffPreflightFileName} results file line is missing.");
        }
        else if (AreEquivalentPaths(resultsFile, manifest.ResultsFilePath))
        {
            validationLines.Add($"- [pass] {HandoffPreflightFileName} results file: {FormatPathForValidation(resultsFile)}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffPreflightFileName} results file mismatch, expected {FormatPathForValidation(manifest.ResultsFilePath)}, actual {FormatPathForValidation(resultsFile)}.");
        }

        var dataStorageStatus = ReadLineValue(lines, "Data storage:");
        if (string.IsNullOrWhiteSpace(dataStorageStatus))
        {
            isValid = false;
            validationLines.Add($"- [fail] {HandoffPreflightFileName} data storage line is missing.");
        }
        else if (string.Equals(dataStorageStatus, manifest.DataStorageStatus, StringComparison.Ordinal))
        {
            validationLines.Add($"- [pass] {HandoffPreflightFileName} data storage: {dataStorageStatus}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffPreflightFileName} data storage mismatch, expected {FormatMismatchLine(manifest.DataStorageStatus)}, actual {FormatMismatchLine(dataStorageStatus)}.");
        }

        var profileRoot = ReadLineValue(lines, "Profile root:");
        if (string.IsNullOrWhiteSpace(profileRoot))
        {
            isValid = false;
            validationLines.Add($"- [fail] {HandoffPreflightFileName} profile root line is missing.");
        }
        else if (AreEquivalentPaths(profileRoot, manifest.ProfileRootFolder))
        {
            validationLines.Add($"- [pass] {HandoffPreflightFileName} profile root: {FormatPathForValidation(profileRoot)}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffPreflightFileName} profile root mismatch, expected {FormatPathForValidation(manifest.ProfileRootFolder)}, actual {FormatPathForValidation(profileRoot)}.");
        }

        var dataStorageLine = lines.FirstOrDefault(
            line => line.StartsWith("Data storage:", StringComparison.OrdinalIgnoreCase));
        var runtimeLine = lines.FirstOrDefault(
            line => line.StartsWith("WebView2 runtime:", StringComparison.OrdinalIgnoreCase));
        var layoutsLine = lines.FirstOrDefault(
            line => line.StartsWith("Layouts:", StringComparison.OrdinalIgnoreCase));
        if (dataStorageLine is null || runtimeLine is null || layoutsLine is null)
        {
            isValid = false;
            validationLines.Add($"- [fail] {HandoffPreflightFileName} readiness lines are missing.");
        }
        else
        {
            var runtimeStatus = ReadLineValue(lines, "WebView2 runtime:");
            if (string.Equals(runtimeStatus, manifest.WebView2RuntimeStatus, StringComparison.Ordinal))
            {
                validationLines.Add($"- [pass] {HandoffPreflightFileName} WebView2 runtime: {runtimeStatus}");
            }
            else
            {
                isValid = false;
                validationLines.Add(
                    $"- [fail] {HandoffPreflightFileName} WebView2 runtime mismatch, expected {FormatMismatchLine(manifest.WebView2RuntimeStatus)}, actual {FormatMismatchLine(runtimeStatus)}.");
            }

            var layoutStatus = ReadLineValue(lines, "Layouts:");
            if (string.Equals(layoutStatus, manifest.PlaybackLayoutStatus, StringComparison.Ordinal))
            {
                validationLines.Add($"- [pass] {HandoffPreflightFileName} layouts: {layoutStatus}");
            }
            else
            {
                isValid = false;
                validationLines.Add(
                    $"- [fail] {HandoffPreflightFileName} layout status mismatch, expected {FormatMismatchLine(manifest.PlaybackLayoutStatus)}, actual {FormatMismatchLine(layoutStatus)}.");
            }

            var profileGroupLines = ReadPreflightProfileGroupLines(lines);
            var isReady = dataStorageLine.Contains("[ready]", StringComparison.OrdinalIgnoreCase) &&
                runtimeLine.Contains("[available]", StringComparison.OrdinalIgnoreCase) &&
                layoutsLine.Contains("[ready]", StringComparison.OrdinalIgnoreCase) &&
                profileGroupLines.Count == RequiredHandoffProfileGroupIds.Length &&
                profileGroupLines.All(line => line.Contains("[ready]", StringComparison.OrdinalIgnoreCase));
            if (manifest.IsPreflightReady == isReady)
            {
                validationLines.Add($"- [pass] {HandoffPreflightFileName} readiness: {isReady}");
            }
            else
            {
                isValid = false;
                validationLines.Add(
                    $"- [fail] {HandoffPreflightFileName} readiness mismatch, expected {isReady} from artifact, actual {manifest.IsPreflightReady} in manifest.");
            }
        }

        var expectedProfileGroupLines = CreateExpectedHandoffProfileGroupLines(manifest.ProfileGroups);
        var actualProfileGroupLines = ReadPreflightProfileGroupLines(lines);
        if (actualProfileGroupLines.SequenceEqual(expectedProfileGroupLines, StringComparer.Ordinal))
        {
            validationLines.Add($"- [pass] {HandoffPreflightFileName} profile groups: {expectedProfileGroupLines.Count}");
        }
        else
        {
            isValid = false;
            var lineNumber = FindFirstLineMismatch(expectedProfileGroupLines, actualProfileGroupLines) + 1;
            var expectedLine = lineNumber <= expectedProfileGroupLines.Count ? expectedProfileGroupLines[lineNumber - 1] : "<missing>";
            var actualLine = lineNumber <= actualProfileGroupLines.Count ? actualProfileGroupLines[lineNumber - 1] : "<missing>";
            validationLines.Add(
                $"- [fail] {HandoffPreflightFileName} profile groups mismatch at item {lineNumber}, expected {FormatMismatchLine(expectedLine)}, actual {FormatMismatchLine(actualLine)}.");
        }

        if (summary is null)
        {
            return isValid;
        }

        var expectedLines = CreateHandoffPreflightArtifactLines(manifest, summary);
        if (lines.SequenceEqual(expectedLines, StringComparer.Ordinal))
        {
            validationLines.Add($"- [pass] {HandoffPreflightFileName} content matches manifest and results snapshot.");
            return isValid;
        }

        var mismatchLineNumber = FindFirstLineMismatch(expectedLines, lines) + 1;
        var expectedMismatchLine = mismatchLineNumber <= expectedLines.Count
            ? expectedLines[mismatchLineNumber - 1]
            : "<missing>";
        var actualMismatchLine = mismatchLineNumber <= lines.Length
            ? lines[mismatchLineNumber - 1]
            : "<missing>";
        isValid = false;
        validationLines.Add(
            $"- [fail] {HandoffPreflightFileName} content mismatch at line {mismatchLineNumber}, expected {FormatMismatchLine(expectedMismatchLine)}, actual {FormatMismatchLine(actualMismatchLine)}.");
        return isValid;
    }

    private static bool ValidateHandoffVerificationArtifact(
        string inputFolder,
        HandoffManifest manifest,
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
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] {HandoffVerificationFileName} completion mismatch, expected {isExpectedVerified} from results, actual {isArtifactVerified}.");
        }

        var expectedLines = CreateHandoffVerificationArtifactLines(manifest, summary);
        if (lines.SequenceEqual(expectedLines, StringComparer.Ordinal))
        {
            validationLines.Add($"- [pass] {HandoffVerificationFileName} content matches results snapshot.");
            return isValid;
        }

        var lineNumber = FindFirstLineMismatch(expectedLines, lines) + 1;
        var expectedLine = lineNumber <= expectedLines.Count ? expectedLines[lineNumber - 1] : "<missing>";
        var actualLine = lineNumber <= lines.Length ? lines[lineNumber - 1] : "<missing>";
        validationLines.Add(
            $"- [fail] {HandoffVerificationFileName} content mismatch at line {lineNumber}, expected {FormatMismatchLine(expectedLine)}, actual {FormatMismatchLine(actualLine)}.");
        return false;
    }

    private static bool ValidateHandoffHistoryArtifact(
        string inputFolder,
        HandoffManifest manifest,
        HandoffResultsSummary summary,
        List<string> validationLines)
    {
        var expectedLines = CreateHandoffHistoryArtifactLines(manifest, summary.Results);
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

    private static IReadOnlyList<string> CreateHandoffPreflightArtifactLines(
        HandoffManifest manifest,
        HandoffResultsSummary summary)
    {
        var lines = new List<string>
        {
            "Stream Orchestra Feasibility Preflight",
            $"Data folder: {manifest.DataFolder}",
            $"Results file: {manifest.ResultsFilePath}",
            $"Data storage: {manifest.DataStorageStatus}",
            $"Profile root: {manifest.ProfileRootFolder}",
            $"WebView2 runtime: {manifest.WebView2RuntimeStatus}",
            "Profile groups:"
        };

        lines.AddRange(CreateExpectedHandoffProfileGroupLines(manifest.ProfileGroups));
        lines.Add($"Layouts: {manifest.PlaybackLayoutStatus}");
        lines.Add($"Evidence recorded: {summary.Results.Count}");
        lines.Add($"Decision: {summary.Decision.Title} ({summary.Decision.Code})");
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

        var suggestions = new FeasibilityAuditService().CreateSuggestedRecordShapes(summary.AuditItems);
        if (suggestions.Count > 0)
        {
            lines.Add("Suggested record shapes:");
            lines.AddRange(suggestions.Select(suggestion => $"- {suggestion}"));
        }

        return lines;
    }

    private static IReadOnlyList<string> CreateExpectedHandoffProfileGroupLines(
        IReadOnlyList<HandoffProfileGroupMetadata>? profileGroups)
    {
        return (profileGroups ?? Array.Empty<HandoffProfileGroupMetadata>())
            .Select(FormatHandoffProfileGroupLine)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadPreflightProfileGroupLines(IReadOnlyList<string> lines)
    {
        var startIndex = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Equals("Profile groups:", StringComparison.OrdinalIgnoreCase))
            {
                startIndex = index;
                break;
            }
        }

        if (startIndex < 0)
        {
            return [];
        }

        var endIndex = lines.Count;
        for (var index = startIndex + 1; index < lines.Count; index++)
        {
            if (lines[index].StartsWith("Layouts:", StringComparison.OrdinalIgnoreCase))
            {
                endIndex = index;
                break;
            }
        }

        return lines.Skip(startIndex + 1).Take(endIndex - startIndex - 1).ToArray();
    }

    private static string FormatHandoffProfileGroupLine(HandoffProfileGroupMetadata group)
    {
        return $"- [{group.Status}] Group {group.Id}: {group.UserDataFolder}";
    }

    private static IReadOnlyList<string> CreateHandoffChecklistArtifactLines(
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

    private static IReadOnlyList<string> CreateHandoffAuditArtifactLines(
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

    private static IReadOnlyList<string> CreateHandoffHistoryArtifactLines(
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

    private static IReadOnlyList<string> CreateHandoffVerificationArtifactLines(
        HandoffManifest manifest,
        HandoffResultsSummary summary)
    {
        var lines = new List<string>
        {
            "Stream Orchestra Plan Verification",
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

        if (summary.PlanVerificationStatus.Equals("pass", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("Verification: pass");
            return lines;
        }

        lines.Add("Verification: not complete");
        var outstandingItems = summary.AuditItems
            .Where(item => !item.Status.Equals("pass", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (outstandingItems.Length > 0)
        {
            lines.Add("Outstanding gates:");
            lines.AddRange(outstandingItems.Select(item => $"- [{item.Status}] {item.Title}: {item.Evidence}"));
        }

        lines.Add("Required evidence: record live SOOP 4-slot Group A, 8-slot, 9-slot threshold, 12-slot, and 16-slot playback evidence plus A-D account-label, restart, resource, CPU, GPU, and memory evidence.");
        var suggestions = new FeasibilityAuditService().CreateSuggestedRecordShapes(summary.AuditItems);
        if (suggestions.Count > 0)
        {
            lines.Add("Suggested record shapes:");
            lines.AddRange(suggestions.Select(suggestion => $"- {suggestion}"));
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

    private static string? ReadLineValue(IEnumerable<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(
            candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return line is null ? null : line[prefix.Length..].Trim();
    }

    private static bool ValidateHandoffDiagnosticReport(
        string reportText,
        DiagnosticReport report,
        HandoffManifest manifest,
        HandoffResultsSummary? summary,
        List<string> validationLines)
    {
        var isValid = true;
        isValid &= ValidateHandoffDiagnosticReportGeneratedAt(reportText, report, manifest, validationLines);
        isValid &= ValidateHandoffDiagnosticReportContext(report, manifest, validationLines);

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

    private static bool ValidateHandoffDiagnosticReportGeneratedAt(
        string reportText,
        DiagnosticReport report,
        HandoffManifest manifest,
        List<string> validationLines)
    {
        using var document = JsonDocument.Parse(reportText);
        if (!document.RootElement.TryGetProperty("generatedAt", out var generatedAtElement) ||
            generatedAtElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            validationLines.Add($"- [fail] {HandoffDiagnosticReportFileName} generatedAt is missing.");
            return false;
        }

        if (generatedAtElement.ValueKind != JsonValueKind.String ||
            !generatedAtElement.TryGetDateTimeOffset(out var generatedAt))
        {
            validationLines.Add($"- [fail] {HandoffDiagnosticReportFileName} generatedAt is invalid.");
            return false;
        }

        if (report.GeneratedAt != generatedAt)
        {
            validationLines.Add(
                $"- [fail] {HandoffDiagnosticReportFileName} generatedAt parse mismatch, expected {generatedAt:O}, actual {report.GeneratedAt:O}.");
            return false;
        }

        if (generatedAt == default)
        {
            validationLines.Add($"- [fail] {HandoffDiagnosticReportFileName} generatedAt is missing or default.");
            return false;
        }

        if (manifest.GeneratedAt == default)
        {
            validationLines.Add(
                $"- [fail] {HandoffDiagnosticReportFileName} generatedAt cannot be checked because manifest generatedAt is missing or default.");
            return false;
        }

        DateTimeOffset earliestExpected;
        DateTimeOffset latestExpected;
        try
        {
            earliestExpected = manifest.GeneratedAt.AddMinutes(-1);
            latestExpected = manifest.GeneratedAt.AddMinutes(5);
        }
        catch (ArgumentOutOfRangeException)
        {
            validationLines.Add(
                $"- [fail] {HandoffDiagnosticReportFileName} generatedAt cannot be checked because manifest generatedAt is out of range: {manifest.GeneratedAt:O}.");
            return false;
        }

        if (generatedAt < earliestExpected || generatedAt > latestExpected)
        {
            validationLines.Add(
                $"- [fail] {HandoffDiagnosticReportFileName} generatedAt outside handoff window, expected between {earliestExpected:O} and {latestExpected:O}, actual {generatedAt:O}.");
            return false;
        }

        validationLines.Add($"- [pass] {HandoffDiagnosticReportFileName} generatedAt: {generatedAt:O}");
        return true;
    }

    private static bool ValidateHandoffDiagnosticReportContext(
        DiagnosticReport report,
        HandoffManifest manifest,
        List<string> validationLines)
    {
        var isValid = true;
        if (AreEquivalentPaths(report.DataFolder, manifest.DataFolder))
        {
            validationLines.Add($"- [pass] diagnostic report data folder: {FormatPathForValidation(report.DataFolder)}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report data folder mismatch, expected {FormatPathForValidation(manifest.DataFolder)}, actual {FormatPathForValidation(report.DataFolder)}.");
        }

        var dataFiles = report.DataFiles?
            .Where(file => file is not null)
            .Select(file => file!)
            .ToArray() ?? [];
        isValid &= ValidateHandoffDiagnosticDataFiles(dataFiles, manifest, validationLines);
        var resultsFile = dataFiles.FirstOrDefault(
            file => string.Equals(file.Name, "feasibility-results", StringComparison.OrdinalIgnoreCase));
        if (resultsFile is null)
        {
            isValid = false;
            validationLines.Add("- [fail] diagnostic report results file entry is missing.");
        }
        else if (AreEquivalentPaths(resultsFile.Path, manifest.ResultsFilePath))
        {
            validationLines.Add(
                $"- [pass] diagnostic report results file: {FormatPathForValidation(resultsFile.Path)} (exists={resultsFile.Exists}, size={resultsFile.SizeBytes})");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report results file mismatch, expected {FormatPathForValidation(manifest.ResultsFilePath)}, actual {FormatPathForValidation(resultsFile.Path)}.");
        }

        if (AreEquivalentPaths(report.ProfileRootFolder, manifest.ProfileRootFolder))
        {
            validationLines.Add($"- [pass] diagnostic report profile root: {FormatPathForValidation(report.ProfileRootFolder)}");
        }
        else
        {
            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report profile root mismatch, expected {FormatPathForValidation(manifest.ProfileRootFolder)}, actual {FormatPathForValidation(report.ProfileRootFolder)}.");
        }

        isValid &= ValidateHandoffDiagnosticProfileGroups(report, manifest, validationLines);
        return isValid;
    }

    private static bool ValidateHandoffDiagnosticDataFiles(
        IReadOnlyList<DiagnosticDataFile> dataFiles,
        HandoffManifest manifest,
        List<string> validationLines)
    {
        if (string.IsNullOrWhiteSpace(manifest.DataFolder) ||
            string.IsNullOrWhiteSpace(manifest.ResultsFilePath))
        {
            validationLines.Add("- [fail] diagnostic report data files cannot be checked because manifest data paths are missing.");
            return false;
        }

        var expectedDataFiles = new[]
        {
            new ExpectedDiagnosticDataFile("appstate", Path.Combine(manifest.DataFolder, "appstate.json")),
            new ExpectedDiagnosticDataFile("workspaces", Path.Combine(manifest.DataFolder, "workspaces.json")),
            new ExpectedDiagnosticDataFile("favorites", Path.Combine(manifest.DataFolder, "favorites.json")),
            new ExpectedDiagnosticDataFile("feasibility-results", manifest.ResultsFilePath),
            new ExpectedDiagnosticDataFile("external-browsers", Path.Combine(manifest.DataFolder, "external-browsers.json"))
        };
        var expectedNames = expectedDataFiles
            .Select(file => file.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filesByName = new Dictionary<string, List<DiagnosticDataFile>>(StringComparer.OrdinalIgnoreCase);
        var isValid = true;
        foreach (var dataFile in dataFiles)
        {
            var name = dataFile.Name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                isValid = false;
                validationLines.Add("- [fail] diagnostic report data file name is missing.");
                continue;
            }

            if (!expectedNames.Contains(name))
            {
                isValid = false;
                validationLines.Add($"- [fail] diagnostic report data file {name} is unexpected.");
            }

            if (dataFile.SizeBytes < 0)
            {
                isValid = false;
                validationLines.Add($"- [fail] diagnostic report data file {name} has negative size {dataFile.SizeBytes}.");
            }

            if (!filesByName.TryGetValue(name, out var matchingFiles))
            {
                matchingFiles = [];
                filesByName[name] = matchingFiles;
            }

            matchingFiles.Add(dataFile);
        }

        foreach (var duplicateName in filesByName
            .Where(item => item.Value.Count > 1)
            .Select(item => item.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            isValid = false;
            validationLines.Add($"- [fail] diagnostic report data file {duplicateName} is duplicated.");
        }

        foreach (var expectedDataFile in expectedDataFiles)
        {
            if (!filesByName.TryGetValue(expectedDataFile.Name, out var matchingFiles) ||
                matchingFiles.Count == 0)
            {
                isValid = false;
                validationLines.Add($"- [fail] diagnostic report data file {expectedDataFile.Name} is missing.");
                continue;
            }

            var dataFile = matchingFiles[0];
            if (AreEquivalentPaths(dataFile.Path, expectedDataFile.Path))
            {
                validationLines.Add(
                    $"- [pass] diagnostic report data file {expectedDataFile.Name}: {FormatPathForValidation(dataFile.Path)}");
                continue;
            }

            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report data file {expectedDataFile.Name} path mismatch, expected {FormatPathForValidation(expectedDataFile.Path)}, actual {FormatPathForValidation(dataFile.Path)}.");
        }

        if (isValid)
        {
            validationLines.Add("- [pass] diagnostic report data files standard entries.");
        }

        return isValid;
    }

    private static bool ValidateHandoffDiagnosticProfileGroups(
        DiagnosticReport report,
        HandoffManifest manifest,
        List<string> validationLines)
    {
        var expectedGroups = (manifest.ProfileGroups ?? Array.Empty<HandoffProfileGroupMetadata>())
            .OrderBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (expectedGroups.Length == 0)
        {
            validationLines.Add("- [fail] diagnostic report profile groups cannot be checked because manifest profile groups are missing.");
            return false;
        }

        var reportGroups = (report.ProfileGroups ?? Array.Empty<ProfileGroup>())
            .Where(group => group is not null)
            .Select(group => group!)
            .ToArray();
        var expectedIds = new HashSet<string>(
            expectedGroups.Select(group => group.Id),
            StringComparer.OrdinalIgnoreCase);
        var duplicateIds = reportGroups
            .Where(group => expectedIds.Contains(group.Id))
            .GroupBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            foreach (var duplicateId in duplicateIds)
            {
                validationLines.Add($"- [fail] diagnostic report profile group {duplicateId} is duplicated.");
            }

            return false;
        }

        var isValid = true;
        foreach (var expectedGroup in expectedGroups)
        {
            var reportGroup = reportGroups.FirstOrDefault(
                group => string.Equals(group.Id, expectedGroup.Id, StringComparison.OrdinalIgnoreCase));
            if (reportGroup is null)
            {
                isValid = false;
                validationLines.Add($"- [fail] diagnostic report profile group {expectedGroup.Id} is missing.");
                continue;
            }

            if (AreEquivalentPaths(reportGroup.UserDataFolder, expectedGroup.UserDataFolder))
            {
                continue;
            }

            isValid = false;
            validationLines.Add(
                $"- [fail] diagnostic report profile group {expectedGroup.Id} mismatch, expected {FormatPathForValidation(expectedGroup.UserDataFolder)}, actual {FormatPathForValidation(reportGroup.UserDataFolder)}.");
        }

        if (isValid)
        {
            validationLines.Add($"- [pass] diagnostic report profile groups: {expectedGroups.Length}");
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

    private static bool AreEquivalentPaths(string? actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected))
        {
            return string.IsNullOrWhiteSpace(actual) && string.IsNullOrWhiteSpace(expected);
        }

        return string.Equals(
            NormalizePathForComparison(actual),
            NormalizePathForComparison(expected),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForComparison(string path)
    {
        var trimmedPath = path.Trim();
        try
        {
            return Path.GetFullPath(trimmedPath);
        }
        catch (ArgumentException)
        {
            return trimmedPath;
        }
        catch (NotSupportedException)
        {
            return trimmedPath;
        }
        catch (PathTooLongException)
        {
            return trimmedPath;
        }
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

    private static string FormatPathForValidation(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "n/a" : path.Trim();
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
        var (lines, isReady, _) = CreatePreflightLines(parseResult.DataFolder, parseResult.ProfileFolder);
        WriteLines(lines, output);

        if (!string.IsNullOrWhiteSpace(parseResult.OutputPath))
        {
            SaveTextFile(parseResult.OutputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            output.WriteLine($"Preflight saved: {parseResult.OutputPath}");
        }

        return isReady ? 0 : 1;
    }

    private static (IReadOnlyList<string> Lines, bool IsReady, string DataStorageStatus) CreatePreflightLines(
        string? dataFolder,
        string? profileFolder)
    {
        return CreatePreflightLines(dataFolder, CreateHandoffPreflightSnapshot(profileFolder));
    }

    private static HandoffPreflightSnapshot CreateHandoffPreflightSnapshot(string? profileFolder)
    {
        var profileService = new WebViewProfileService(profileFolder);
        var profileGroups = profileService.Groups
            .OrderBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HandoffProfileGroupMetadata(
                group.Id,
                group.UserDataFolder,
                Directory.Exists(group.UserDataFolder) ? "ready" : "missing"))
            .ToArray();

        return new HandoffPreflightSnapshot(
            profileService.BaseProfileFolder,
            GetWebView2RuntimeStatus(),
            GetPlaybackLayoutStatus(),
            profileGroups);
    }

    private static (IReadOnlyList<string> Lines, bool IsReady, string DataStorageStatus) CreatePreflightLines(
        string? dataFolder,
        HandoffPreflightSnapshot preflightSnapshot)
    {
        var resolvedDataFolder = FeasibilityResultStorageService.ResolveDataFolder(dataFolder);
        var resultsFilePath = FeasibilityResultStorageService.GetResultsFilePath(resolvedDataFolder);
        var dataStorageStatus = GetDataStorageStatus(resolvedDataFolder);
        IReadOnlyList<FeasibilityTestResult> results = [];
        if (dataStorageStatus.StartsWith("[ready]", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var feasibilityStorage = new FeasibilityResultStorageService(resolvedDataFolder);
                results = feasibilityStorage.LoadResults();
            }
            catch (Exception ex)
            {
                dataStorageStatus = $"[blocked] {ex.Message}";
            }
        }

        var decision = new FeasibilityDecisionService().Decide(results);
        var auditService = new FeasibilityAuditService();
        var auditItems = auditService.CreateAudit(results, decision);
        var lines = new List<string>
        {
            "Stream Orchestra Feasibility Preflight",
            $"Data folder: {resolvedDataFolder}",
            $"Results file: {resultsFilePath}",
            $"Data storage: {dataStorageStatus}",
            $"Profile root: {preflightSnapshot.ProfileRootFolder}",
            $"WebView2 runtime: {preflightSnapshot.WebView2RuntimeStatus}",
            "Profile groups:"
        };

        foreach (var group in preflightSnapshot.ProfileGroups)
        {
            lines.Add(FormatHandoffProfileGroupLine(group));
        }

        lines.Add($"Layouts: {preflightSnapshot.PlaybackLayoutStatus}");
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

        var isReady = dataStorageStatus.StartsWith("[ready]", StringComparison.OrdinalIgnoreCase) &&
            preflightSnapshot.WebView2RuntimeStatus.StartsWith("[available]", StringComparison.OrdinalIgnoreCase) &&
            preflightSnapshot.PlaybackLayoutStatus.StartsWith("[ready]", StringComparison.OrdinalIgnoreCase) &&
            preflightSnapshot.ProfileGroups.Count == RequiredHandoffProfileGroupIds.Length &&
            preflightSnapshot.ProfileGroups.All(group => group.Status.Equals("ready", StringComparison.OrdinalIgnoreCase));

        return (lines, isReady, dataStorageStatus);
    }

    private static string GetDataStorageStatus(string dataFolder)
    {
        var probePath = Path.Combine(
            dataFolder,
            $"feasibility-preflight-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(dataFolder);
            File.WriteAllText(probePath, "preflight");
            File.Delete(probePath);
            return "[ready] data folder is writable for feasibility artifacts.";
        }
        catch (Exception ex)
        {
            TryDeleteFile(probePath);
            return $"[blocked] {ex.Message}";
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for the preflight probe file.
        }
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
            AccountLabel = parseResult.SameAccountSession ? parseResult.AccountLabel ?? "" : "",
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

        var resolvedDataFolder = FeasibilityResultStorageService.ResolveDataFolder(dataFolder);
        return Path.Combine(resolvedDataFolder, $"phase0-handoff-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
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
        HandoffManifest manifest)
    {
        var path = Path.Combine(outputFolder, HandoffManifestFileName);
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

    private sealed record HandoffDataContext(
        string DataFolder,
        string ResultsFilePath,
        string DataStorageStatus,
        IReadOnlyList<FeasibilityTestResult> Results,
        HandoffResultsSummary Summary,
        FeasibilityResultStorageService? Storage);

    private sealed record HandoffManifest(
        DateTimeOffset GeneratedAt,
        string DataFolder,
        string ResultsFilePath,
        string DataStorageStatus,
        string ProfileRootFolder,
        string WebView2RuntimeStatus,
        string PlaybackLayoutStatus,
        IReadOnlyList<HandoffProfileGroupMetadata> ProfileGroups,
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

    private sealed record HandoffPreflightSnapshot(
        string ProfileRootFolder,
        string WebView2RuntimeStatus,
        string PlaybackLayoutStatus,
        IReadOnlyList<HandoffProfileGroupMetadata> ProfileGroups);

    private sealed record HandoffProfileGroupMetadata(
        string Id,
        string UserDataFolder,
        string Status);

    private sealed record HandoffArtifactMetadata(
        string FileName,
        long SizeBytes,
        string Sha256);

    private sealed record ExpectedDiagnosticDataFile(
        string Name,
        string Path);

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
