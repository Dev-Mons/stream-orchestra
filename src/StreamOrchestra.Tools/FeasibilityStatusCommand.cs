using System.Globalization;
using Microsoft.Web.WebView2.Core;
using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tools;

public static class FeasibilityStatusCommand
{
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
            "fallback" => SaveFallbackScript(parseResult.DataFolder, output),
            "history" => PrintHistory(parseResult.DataFolder, output),
            "preflight" => PrintPreflight(parseResult, output),
            "record" => RecordResult(parseResult, output),
            "report" => SaveReport(parseResult, output),
            "scenarios" => PrintScenarios(output),
            "verify" => VerifyPlan(parseResult.DataFolder, output),
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
        var storage = new FeasibilityResultStorageService(dataFolder);
        var results = storage.LoadResults()
            .OrderByDescending(result => result.CapturedAt)
            .ToArray();

        output.WriteLine("Stream Orchestra Feasibility History");
        output.WriteLine($"Data folder: {storage.DataFolder}");
        output.WriteLine($"Results file: {storage.ResultsFilePath}");
        output.WriteLine($"Results recorded: {results.Length}");

        if (results.Length == 0)
        {
            output.WriteLine("No feasibility results recorded.");
            return 0;
        }

        foreach (var result in results)
        {
            output.WriteLine(
                $"[{result.CapturedAt:yyyy-MM-dd HH:mm:ss}] {result.Outcome}, {result.PlaybackCount} slot(s), {result.ScenarioName} ({result.ScenarioId})");
            output.WriteLine($"  Id: {result.Id}");
            output.WriteLine(
                $"  Criteria: account={result.IsSameAccountSessionMaintained}, restart={result.IsRestartSessionMaintained}, resources={result.IsResourceUsageAcceptable}");
            output.WriteLine($"  Account label: {FormatAccountLabel(result.AccountLabel)}");
            output.WriteLine($"  Profile groups: {FeasibilityProfileGroupEvidenceService.FormatGroups(result.VerifiedProfileGroups)}");
            output.WriteLine(
                $"  Observed resources: cpu={FormatNullable(result.ObservedCpuPercent)}%, gpu={FormatNullable(result.ObservedGpuPercent)}%, memory={FormatNullable(result.ObservedMemoryMegabytes)} MB");
            output.WriteLine(
                string.IsNullOrWhiteSpace(result.DecisionCode)
                    ? "  Recorded decision: n/a"
                    : $"  Recorded decision: {result.DecisionTitle} ({result.DecisionCode})");

            if (!string.IsNullOrWhiteSpace(result.DecisionNextAction))
            {
                output.WriteLine($"  Next action at record time: {result.DecisionNextAction}");
            }

            if (!string.IsNullOrWhiteSpace(result.Notes))
            {
                output.WriteLine($"  Notes: {result.Notes}");
            }
        }

        return 0;
    }

    private static int PrintAudit(ParseResult parseResult, TextWriter output)
    {
        var storage = new FeasibilityResultStorageService(parseResult.DataFolder);
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

        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        if (!string.IsNullOrWhiteSpace(parseResult.AuditOutputPath))
        {
            SaveTextFile(parseResult.AuditOutputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            output.WriteLine($"Audit saved: {parseResult.AuditOutputPath}");
        }

        return 0;
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

        output.WriteLine("Record `success` only when the 9+ playback, required profile-group account, restart, resource, CPU, GPU, and memory evidence is complete.");
        return 0;
    }

    private static int PrintPreflight(ParseResult parseResult, TextWriter output)
    {
        var profileService = new WebViewProfileService(parseResult.ProfileFolder);
        var feasibilityStorage = new FeasibilityResultStorageService(parseResult.DataFolder);
        var results = feasibilityStorage.LoadResults();
        var decision = new FeasibilityDecisionService().Decide(results);
        var auditService = new FeasibilityAuditService();
        var auditItems = auditService.CreateAudit(results, decision);
        var runtimeStatus = GetWebView2RuntimeStatus();
        var layoutStatus = GetPlaybackLayoutStatus();

        output.WriteLine("Stream Orchestra Feasibility Preflight");
        output.WriteLine($"Data folder: {feasibilityStorage.DataFolder}");
        output.WriteLine($"Results file: {feasibilityStorage.ResultsFilePath}");
        output.WriteLine($"Profile root: {profileService.BaseProfileFolder}");
        output.WriteLine($"WebView2 runtime: {runtimeStatus}");
        output.WriteLine("Profile groups:");

        foreach (var group in profileService.Groups.OrderBy(group => group.Id, StringComparer.OrdinalIgnoreCase))
        {
            var status = Directory.Exists(group.UserDataFolder) ? "ready" : "missing";
            output.WriteLine($"- [{status}] Group {group.Id}: {group.UserDataFolder}");
        }

        output.WriteLine($"Layouts: {layoutStatus}");
        output.WriteLine($"Evidence recorded: {results.Count}");
        output.WriteLine($"Decision: {decision.Title} ({decision.Code})");
        WriteNextAction(decision, output);
        WriteAuditSummary(auditItems, output);
        WritePlanVerificationStatus(auditItems, output);
        WritePhase0SuccessGate(auditItems, output);
        WriteSuggestedRecordShapes(auditItems, output);

        return runtimeStatus.StartsWith("[available]", StringComparison.OrdinalIgnoreCase) &&
            layoutStatus.StartsWith("[ready]", StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
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

    private static int VerifyPlan(string? dataFolder, TextWriter output)
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

        output.WriteLine("Stream Orchestra Plan Verification");
        output.WriteLine($"Data folder: {storage.DataFolder}");
        output.WriteLine($"Results file: {storage.ResultsFilePath}");
        output.WriteLine($"Results recorded: {results.Count}");
        output.WriteLine($"Decision: {decision.Title} ({decision.Code})");
        WriteNextAction(decision, output);
        output.WriteLine($"Plan audit: {summary.ToCompactText()}");
        WritePlanVerificationStatus(auditItems, output);
        WritePhase0SuccessGate(auditItems, output);

        if (isVerified)
        {
            output.WriteLine("Verification: pass");
            return 0;
        }

        output.WriteLine("Verification: not complete");
        WriteOutstandingGates(auditItems, output);
        output.WriteLine("Required evidence: record live SOOP 4-slot Group A, 8-slot, 9-slot threshold, 12-slot, and 16-slot playback evidence plus A-D account, restart, resource, CPU, GPU, and memory evidence.");
        WriteSuggestedRecordShapes(auditItems, output);
        return 1;
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
        var decision = new FeasibilityDecisionService().Decide(existingResults.Append(result).ToArray());
        FeasibilityResultStorageService.ApplyDecisionSnapshot(result, decision);
        storage.AppendResult(result);
        var results = storage.LoadResults();
        var auditItems = new FeasibilityAuditService().CreateAudit(results, decision);

        output.WriteLine("Recorded feasibility result.");
        output.WriteLine($"Data folder: {storage.DataFolder}");
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
        var profileService = new WebViewProfileService(parseResult.ProfileFolder);
        var presetStorage = new PresetStorageService(parseResult.DataFolder);
        var favoriteStorage = new FavoriteStorageService(parseResult.DataFolder);
        var feasibilityStorage = new FeasibilityResultStorageService(parseResult.DataFolder);
        var results = feasibilityStorage.LoadResults();
        var decision = new FeasibilityDecisionService().Decide(results);
        var appState = presetStorage.LoadAppState();
        var reportService = new DiagnosticReportService();
        var report = reportService.CreateReport(
            profileService,
            presetStorage,
            favoriteStorage,
            feasibilityStorage,
            decision,
            appState?.LastSession,
            TryLoadLayouts());
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
            return ParseAuditArgs(args);
        }

        if (command.Equals("browsers", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDataFolderOnlyArgs("browsers", args);
        }

        if (command.Equals("fallback", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDataFolderOnlyArgs("fallback", args);
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
            return ParseDataFolderOnlyArgs("verify", args);
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

    private static ParseResult ParseAuditArgs(string[] args)
    {
        string? dataFolder = null;
        string? auditOutputPath = null;

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

                auditOutputPath = args[++index];
                if (string.IsNullOrWhiteSpace(auditOutputPath))
                {
                    return ParseResult.Invalid("--output requires a value.");
                }

                continue;
            }

            return ParseResult.Invalid($"Unknown option: {arg}");
        }

        return ParseResult.Audit(dataFolder, auditOutputPath);
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
            verifiedProfileGroups);
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
            notes);
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

        return ParseResult.Preflight(dataFolder, profileFolder);
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  StreamOrchestra.Tools status [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools audit [--data-folder <path>] [--output <path>]");
        writer.WriteLine("  StreamOrchestra.Tools browsers [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools fallback [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools history [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools preflight [--data-folder <path>] [--profile-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools record [--count <1-16>] [--group <A-D>] --outcome <success|partial|failure> [--account] [--account-label <text>] [--profile-groups <A,B,C,D>] [--restart] [--resources] [--cpu-percent <0-100>] [--gpu-percent <0-100>] [--memory-mb <value>] [--scenario <id>] [--scenario-name <text>] [--notes <text>] [--data-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools report [--data-folder <path>] [--profile-folder <path>]");
        writer.WriteLine("  StreamOrchestra.Tools scenarios");
        writer.WriteLine("  StreamOrchestra.Tools verify [--data-folder <path>]");
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
        string? AuditOutputPath,
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
        string ErrorMessage)
    {
        public static ParseResult Valid(string command, string? dataFolder)
        {
            return new ParseResult(true, false, command, dataFolder, null, null, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, "");
        }

        public static ParseResult Audit(string? dataFolder, string? auditOutputPath)
        {
            return new ParseResult(true, false, "audit", dataFolder, null, auditOutputPath, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, "");
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
            string? notes)
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
                "");
        }

        public static ParseResult Report(string? dataFolder, string? profileFolder)
        {
            return new ParseResult(true, false, "report", dataFolder, profileFolder, null, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, "");
        }

        public static ParseResult Preflight(string? dataFolder, string? profileFolder)
        {
            return new ParseResult(true, false, "preflight", dataFolder, profileFolder, null, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, "");
        }

        public static ParseResult Help()
        {
            return new ParseResult(true, true, "help", null, null, null, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, "");
        }

        public static ParseResult Invalid(string errorMessage)
        {
            return new ParseResult(false, false, "", null, null, null, null, null, false, false, false, "unspecified", "Unspecified", [], null, null, null, null, null, errorMessage);
        }
    }

    private static IReadOnlyList<string> ParseProfileGroups(string rawValue)
    {
        return FeasibilityProfileGroupEvidenceService.Normalize(
            rawValue.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
