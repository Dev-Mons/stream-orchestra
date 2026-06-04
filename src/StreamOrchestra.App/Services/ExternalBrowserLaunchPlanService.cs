using System.IO;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class ExternalBrowserLaunchPlanService
{
    private static readonly StreamNavigationService NavigationService = new();

    public ExternalBrowserFallbackPlan CreatePlan(
        WorkspacePreset workspace,
        IReadOnlyList<ExternalBrowserInfo> browsers,
        string dataFolder,
        IReadOnlyList<LayoutPreset>? layouts = null)
    {
        IEnumerable<ExternalBrowserInfo?> sourceBrowsers = browsers ?? [];
        var installedBrowsers = sourceBrowsers
            .Select(NormalizeInstalledBrowser)
            .Where(browser => browser is not null)
            .Select(browser => browser!)
            .OrderBy(browser => browser.Id)
            .ToArray();
        var layoutSlots = CreateLayoutSlotsById(workspace.LayoutId, layouts);
        IEnumerable<WorkspaceSlot?> sourceSlots = workspace.Slots ?? [];
        var activeSlots = sourceSlots
            .Select(CreateLaunchableSlot)
            .Where(slot => slot is not null)
            .Select(slot => slot!)
            .Where(slot => layoutSlots.Count == 0 || layoutSlots.ContainsKey(slot.SlotId))
            .OrderBy(slot => slot.SlotId)
            .ToArray();

        if (installedBrowsers.Length == 0)
        {
            return new ExternalBrowserFallbackPlan(
                CanLaunch: false,
                Reason: "No external browser executable was found.",
                InstalledBrowserCount: 0,
                PlannedSlotCount: 0,
                Slots: []);
        }

        if (activeSlots.Length == 0)
        {
            return new ExternalBrowserFallbackPlan(
                CanLaunch: false,
                Reason: "No active stream URLs are available.",
                InstalledBrowserCount: installedBrowsers.Length,
                PlannedSlotCount: 0,
                Slots: []);
        }

        var planSlots = activeSlots
            .Select((slot, index) => CreateSlotPlan(
                slot,
                installedBrowsers[index % installedBrowsers.Length],
                dataFolder,
                layoutSlots.TryGetValue(slot.SlotId, out var layoutSlot) ? layoutSlot : null))
            .ToArray();

        return new ExternalBrowserFallbackPlan(
            CanLaunch: true,
            Reason: $"Prepared {planSlots.Length} browser launch plan(s).",
            InstalledBrowserCount: installedBrowsers.Length,
            PlannedSlotCount: planSlots.Length,
            Slots: planSlots);
    }

    private static ExternalBrowserInfo? NormalizeInstalledBrowser(ExternalBrowserInfo? browser)
    {
        if (browser is null ||
            !browser.IsInstalled ||
            string.IsNullOrWhiteSpace(browser.Id) ||
            string.IsNullOrWhiteSpace(browser.ExecutablePath))
        {
            return null;
        }

        var id = browser.Id.Trim();
        var name = string.IsNullOrWhiteSpace(browser.Name)
            ? id
            : browser.Name.Trim();
        var executablePath = browser.ExecutablePath.Trim();
        var candidatePaths = (browser.CandidatePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExternalBrowserInfo(
            id,
            name,
            IsInstalled: true,
            executablePath,
            candidatePaths);
    }

    private static ExternalBrowserSlotLaunchPlan CreateSlotPlan(
        WorkspaceSlot slot,
        ExternalBrowserInfo browser,
        string dataFolder,
        ExternalBrowserWindowLayout? windowLayout)
    {
        var userDataFolder = Path.Combine(
            dataFolder,
            "ExternalBrowserProfiles",
            browser.Id,
            $"Slot{slot.SlotId}");
        var arguments = new List<string>
        {
            $"--user-data-dir={userDataFolder}",
            "--new-window"
        };
        if (slot.Muted)
        {
            arguments.Add("--mute-audio");
        }

        arguments.Add(slot.StreamUrl);

        return new ExternalBrowserSlotLaunchPlan(
            slot.SlotId,
            slot.StreamName,
            slot.StreamUrl,
            browser.Id,
            browser.Name,
            browser.ExecutablePath!,
            userDataFolder,
            arguments,
            windowLayout,
            slot.Muted);
    }

    private static IReadOnlyDictionary<int, ExternalBrowserWindowLayout> CreateLayoutSlotsById(
        string? layoutId,
        IReadOnlyList<LayoutPreset>? layouts)
    {
        IEnumerable<LayoutPreset?> sourceLayouts = layouts ?? [];
        var availableLayouts = sourceLayouts
            .OfType<LayoutPreset>()
            .ToArray();

        if (availableLayouts.Length == 0)
        {
            return new Dictionary<int, ExternalBrowserWindowLayout>();
        }

        var layout = string.IsNullOrWhiteSpace(layoutId)
            ? null
            : availableLayouts.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, layoutId, StringComparison.OrdinalIgnoreCase));
        layout ??= availableLayouts.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, LayoutPresetIds.Default, StringComparison.OrdinalIgnoreCase));
        layout ??= availableLayouts.FirstOrDefault();
        if (layout is null || layout.GridColumns <= 0 || layout.GridRows <= 0)
        {
            return new Dictionary<int, ExternalBrowserWindowLayout>();
        }

        IEnumerable<LayoutSlot?> sourceSlots = layout.Slots ?? [];
        return sourceSlots
            .OfType<LayoutSlot>()
            .Where(slot => IsValidLayoutSlot(layout, slot))
            .GroupBy(slot => slot.SlotId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var slot = group.Last();
                    var hasExplicitBounds = LayoutSlotBoundsCalculator.HasExplicitBounds(slot);
                    var bounds = hasExplicitBounds
                        ? LayoutSlotBoundsCalculator.GetBounds(layout, slot)
                        : null;
                    return new ExternalBrowserWindowLayout(
                        layout.GridColumns,
                        layout.GridRows,
                        slot.X,
                        slot.Y,
                        slot.W,
                        slot.H,
                        layout.ColumnWeights,
                        layout.RowWeights,
                        bounds?.Left,
                        bounds?.Top,
                        bounds?.Width,
                        bounds?.Height);
                });
    }

    private static bool IsValidLayoutSlot(LayoutPreset layout, LayoutSlot slot)
    {
        if (slot.SlotId is < 1 or > PlaybackTestPlanService.MaxSlotCount)
        {
            return false;
        }

        if (LayoutSlotBoundsCalculator.HasExplicitBounds(slot))
        {
            return LayoutSlotBoundsCalculator.IsValidExplicitBounds(slot);
        }

        return slot.X >= 0
               && slot.Y >= 0
               && slot.W > 0
               && slot.H > 0
               && slot.X + slot.W <= layout.GridColumns
               && slot.Y + slot.H <= layout.GridRows;
    }

    private static WorkspaceSlot? CreateLaunchableSlot(WorkspaceSlot? slot)
    {
        if (slot is null)
        {
            return null;
        }

        if (slot.SlotId is < 1 or > PlaybackTestPlanService.MaxSlotCount)
        {
            return null;
        }

        if (!TryNormalizeLaunchableStreamUrl(slot.StreamUrl, out var streamUrl))
        {
            return null;
        }

        return new WorkspaceSlot
        {
            SlotId = slot.SlotId,
            StreamName = string.IsNullOrWhiteSpace(slot.StreamName)
                ? NavigationService.CreateDisplayName(streamUrl)
                : slot.StreamName.Trim(),
            StreamUrl = streamUrl,
            Muted = slot.Muted,
            ProfileGroupId = slot.ProfileGroupId
        };
    }

    private static bool TryNormalizeLaunchableStreamUrl(string? streamUrl, out string normalizedUrl)
    {
        normalizedUrl = "";
        var normalizedStreamUrl = NavigationService.NormalizeUrl(streamUrl ?? "");
        if (normalizedStreamUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(normalizedStreamUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        normalizedUrl = normalizedStreamUrl;
        return true;
    }
}
