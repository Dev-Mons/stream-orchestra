using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class ExternalBrowserLaunchScriptServiceTests : IDisposable
{
    private readonly string _dataFolder;

    public ExternalBrowserLaunchScriptServiceTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void CreateScript_WritesPerSlotProfileFoldersAndStartProcessCommands()
    {
        var service = new ExternalBrowserLaunchScriptService();
        var plan = CreateLaunchPlan();

        var script = service.CreateScript(plan);

        Assert.Contains("Review before running", script);
        Assert.Contains("# Plan: Prepared 1 browser launch plan(s).", script);
        Assert.Contains("# Installed browser candidates: 1", script);
        Assert.Contains("# Planned slots: 1", script);
        Assert.Contains("# Slot 1: Stream 1", script);
        Assert.Contains("# Browser: Edge (edge)", script);
        Assert.Contains("# Executable: C:\\Program Files\\Browser\\browser.exe", script);
        Assert.Contains("# Profile: C:\\Data Root\\Profiles\\Slot1", script);
        Assert.Contains("# URL: https://example.com/watch?stream=1", script);
        Assert.Contains("# Muted: False", script);
        Assert.Contains("New-Item -ItemType Directory -Force -Path 'C:\\Data Root\\Profiles\\Slot1'", script);
        Assert.Contains("$process = Start-Process -FilePath 'C:\\Program Files\\Browser\\browser.exe' -ArgumentList $arguments -PassThru", script);
        Assert.Contains("'\"--user-data-dir=C:\\Data Root\\Profiles\\Slot1\"'", script);
        Assert.Contains("'--new-window'", script);
        Assert.Contains("'\"https://example.com/watch?stream=1\"'", script);
    }

    [Fact]
    public void CreateScript_MovesBrowserWindowsWhenLayoutIsAvailable()
    {
        var service = new ExternalBrowserLaunchScriptService();
        var plan = new ExternalBrowserFallbackPlan(
            true,
            "Prepared 1 browser launch plan(s).",
            1,
            1,
            [
                new ExternalBrowserSlotLaunchPlan(
                    9,
                    "Main",
                    "https://example.com/main",
                    "edge",
                    "Edge",
                    "C:\\Program Files\\Browser\\browser.exe",
                    "C:\\Data Root\\Profiles\\Slot9",
                    [
                        "--user-data-dir=C:\\Data Root\\Profiles\\Slot9",
                        "--new-window",
                        "https://example.com/main"
                    ],
                    new ExternalBrowserWindowLayout(4, 3, 2, 1, 2, 2))
            ]);

        var script = service.CreateScript(plan);

        Assert.Contains("Move-StreamOrchestraBrowserWindow", script);
        Assert.Contains("Get-StreamOrchestraSlotWindowBounds", script);
        Assert.Contains("SetWindowPos", script);
        Assert.Contains("$slotWindow = Get-StreamOrchestraSlotWindowBounds -GridColumns 4 -GridRows 3 -CellX 2 -CellY 1 -CellWidth 2 -CellHeight 2", script);
        Assert.Contains("\"--window-position=$($slotWindow.Left),$($slotWindow.Top)\"", script);
        Assert.Contains("\"--window-size=$($slotWindow.Width),$($slotWindow.Height)\"", script);
        Assert.Contains("Move-StreamOrchestraBrowserWindow -Process $process -Bounds $slotWindow", script);
    }

    [Fact]
    public void CreateScript_WritesMuteArgumentForMutedSlots()
    {
        var service = new ExternalBrowserLaunchScriptService();
        var plan = new ExternalBrowserFallbackPlan(
            true,
            "Prepared 1 browser launch plan(s).",
            1,
            1,
            [
                new ExternalBrowserSlotLaunchPlan(
                    1,
                    "Muted Stream",
                    "https://example.com/muted",
                    "edge",
                    "Edge",
                    "C:\\Program Files\\Browser\\browser.exe",
                    "C:\\Data Root\\Profiles\\Slot1",
                    [
                        "--user-data-dir=C:\\Data Root\\Profiles\\Slot1",
                        "--new-window",
                        "--mute-audio",
                        "https://example.com/muted"
                    ],
                    IsMuted: true)
            ]);

        var script = service.CreateScript(plan);

        Assert.Contains("# Muted: True", script);
        Assert.Contains("'--mute-audio'", script);
    }

    [Fact]
    public void CreateScript_GuardsWindowToolsTypeForRepeatedPowerShellSessions()
    {
        var service = new ExternalBrowserLaunchScriptService();
        var plan = new ExternalBrowserFallbackPlan(
            true,
            "Prepared 1 browser launch plan(s).",
            1,
            1,
            [
                new ExternalBrowserSlotLaunchPlan(
                    1,
                    "Stream",
                    "https://example.com/stream",
                    "edge",
                    "Edge",
                    "C:\\Program Files\\Browser\\browser.exe",
                    "C:\\Data Root\\Profiles\\Slot1",
                    [
                        "--user-data-dir=C:\\Data Root\\Profiles\\Slot1",
                        "--new-window",
                        "https://example.com/stream"
                    ],
                    new ExternalBrowserWindowLayout(4, 4, 0, 0, 1, 1))
            ]);

        var script = service.CreateScript(plan);

        Assert.Contains("if (-not ('StreamOrchestraWindowTools' -as [type])) {", script);
        Assert.Contains("Add-Type @'", script);
    }

    [Fact]
    public void CreateScript_SkipsIncompleteSlotPlansAndNormalizesValidSlots()
    {
        var service = new ExternalBrowserLaunchScriptService();
        var plan = new ExternalBrowserFallbackPlan(
            true,
            "Prepared 2 browser launch plan(s).",
            1,
            2,
            [
                null!,
                new ExternalBrowserSlotLaunchPlan(
                    0,
                    "Invalid Slot",
                    "https://example.com/invalid",
                    "edge",
                    "Edge",
                    "C:\\Program Files\\Browser\\browser.exe",
                    "C:\\Data Root\\Profiles\\Slot0",
                    ["https://example.com/invalid"]),
                new ExternalBrowserSlotLaunchPlan(
                    1,
                    " ",
                    " https://example.com/valid ",
                    " ",
                    " ",
                    " C:\\Program Files\\Browser\\browser.exe ",
                    " C:\\Data Root\\Profiles\\Slot1 ",
                    [
                        " ",
                        " --new-window ",
                        " https://example.com/valid "
                    ],
                    new ExternalBrowserWindowLayout(0, 3, 0, 0, 1, 1))
            ]);

        var script = service.CreateScript(plan);

        Assert.Contains("# Planned slots: 1", script);
        Assert.Contains("# Slot 1: valid", script);
        Assert.Contains("# Browser: browser (browser)", script);
        Assert.Contains("# Executable: C:\\Program Files\\Browser\\browser.exe", script);
        Assert.Contains("# Profile: C:\\Data Root\\Profiles\\Slot1", script);
        Assert.Contains("'--new-window'", script);
        Assert.Contains("'\"https://example.com/valid\"'", script);
        Assert.DoesNotContain("Invalid Slot", script);
        Assert.DoesNotContain("Get-StreamOrchestraSlotWindowBounds", script);
    }

    [Fact]
    public void CreateScript_RebuildsArgumentsWhenValidSlotHasNoArguments()
    {
        var service = new ExternalBrowserLaunchScriptService();
        var plan = new ExternalBrowserFallbackPlan(
            true,
            "Prepared 1 browser launch plan(s).",
            1,
            1,
            [
                new ExternalBrowserSlotLaunchPlan(
                    1,
                    "Muted Stream",
                    "https://example.com/muted",
                    "edge",
                    "Edge",
                    "C:\\Program Files\\Browser\\browser.exe",
                    "C:\\Data Root\\Profiles\\Slot1",
                    null!,
                    IsMuted: true)
            ]);

        var script = service.CreateScript(plan);

        Assert.Contains("'\"--user-data-dir=C:\\Data Root\\Profiles\\Slot1\"'", script);
        Assert.Contains("'--new-window'", script);
        Assert.Contains("'--mute-audio'", script);
        Assert.Contains("'\"https://example.com/muted\"'", script);
    }

    [Fact]
    public void CreateScript_RejectsLaunchablePlanWhenNoSlotCanBeScripted()
    {
        var service = new ExternalBrowserLaunchScriptService();
        var plan = new ExternalBrowserFallbackPlan(
            true,
            "Prepared 1 browser launch plan(s).",
            1,
            1,
            [
                null!,
                new ExternalBrowserSlotLaunchPlan(
                    1,
                    "Missing Executable",
                    "https://example.com/stream",
                    "edge",
                    "Edge",
                    " ",
                    "C:\\Data Root\\Profiles\\Slot1",
                    ["https://example.com/stream"])
            ]);

        var exception = Assert.Throws<InvalidOperationException>(() => service.CreateScript(plan));

        Assert.Contains("no launchable slot plan is available", exception.Message);
    }

    [Fact]
    public void SaveScript_WritesTimestampedPowerShellFile()
    {
        var service = new ExternalBrowserLaunchScriptService();
        var generatedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);

        var path = service.SaveScript(CreateLaunchPlan(), _dataFolder, generatedAt);

        Assert.Equal(Path.Combine(_dataFolder, "external-browser-fallback-20260526-120000.ps1"), path);
        Assert.True(File.Exists(path));
        Assert.Contains("Start-Process", File.ReadAllText(path));
    }

    [Fact]
    public void CreateScript_RejectsUnavailablePlan()
    {
        var service = new ExternalBrowserLaunchScriptService();
        var plan = new ExternalBrowserFallbackPlan(false, "No browser.", 0, 0, []);

        var exception = Assert.Throws<InvalidOperationException>(() => service.CreateScript(plan));

        Assert.Contains("No browser.", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }

    private static ExternalBrowserFallbackPlan CreateLaunchPlan()
    {
        return new ExternalBrowserFallbackPlan(
            true,
            "Prepared 1 browser launch plan(s).",
            1,
            1,
            [
                new ExternalBrowserSlotLaunchPlan(
                    1,
                    "Stream 1",
                    "https://example.com/watch?stream=1",
                    "edge",
                    "Edge",
                    "C:\\Program Files\\Browser\\browser.exe",
                    "C:\\Data Root\\Profiles\\Slot1",
                    [
                        "--user-data-dir=C:\\Data Root\\Profiles\\Slot1",
                        "--new-window",
                        "https://example.com/watch?stream=1"
                    ])
            ]);
    }
}
