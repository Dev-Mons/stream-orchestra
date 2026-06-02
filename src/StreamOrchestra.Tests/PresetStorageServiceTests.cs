using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class PresetStorageServiceTests : IDisposable
{
    private readonly string _dataFolder;

    public PresetStorageServiceTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "StreamOrchestra.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void LoadWorkspaces_ReturnsEmptyListWhenFileDoesNotExist()
    {
        var service = new PresetStorageService(_dataFolder);

        var workspaces = service.LoadWorkspaces();

        Assert.Empty(workspaces);
    }

    [Fact]
    public void SaveWorkspaces_AndLoadWorkspaces_RoundTripsPresets()
    {
        var service = new PresetStorageService(_dataFolder);
        var workspace = CreateWorkspace("workspace_weekday", "Weekday");

        service.SaveWorkspaces([workspace]);
        var loadedWorkspaces = service.LoadWorkspaces();

        var loadedWorkspace = Assert.Single(loadedWorkspaces);
        Assert.Equal(workspace.Id, loadedWorkspace.Id);
        Assert.Equal(workspace.Name, loadedWorkspace.Name);
        Assert.Equal(workspace.LayoutId, loadedWorkspace.LayoutId);
        Assert.Equal(2, loadedWorkspace.Slots.Count);
        Assert.Contains(loadedWorkspace.Slots, slot => slot.SlotId == 2 && slot.StreamName == "Stream B" && slot.StreamUrl == "https://example.com/b" && slot.Muted);
    }

    [Fact]
    public void SaveWorkspaces_OverwritesExistingFileWithoutLeavingTemporaryFiles()
    {
        var service = new PresetStorageService(_dataFolder);
        service.SaveWorkspaces([CreateWorkspace("workspace_old", "Old")]);

        service.SaveWorkspaces([CreateWorkspace("workspace_new", "New")]);

        var loadedWorkspace = Assert.Single(service.LoadWorkspaces());
        Assert.Equal("workspace_new", loadedWorkspace.Id);
        Assert.Empty(Directory.GetFiles(_dataFolder, "workspaces.json.tmp.*"));
    }

    [Fact]
    public void LoadWorkspaces_QuarantinesCorruptJsonAndReturnsEmptyList()
    {
        var service = new PresetStorageService(_dataFolder);
        File.WriteAllText(service.WorkspacesFilePath, "{ invalid json");

        var workspaces = service.LoadWorkspaces();

        Assert.Empty(workspaces);
        Assert.False(File.Exists(service.WorkspacesFilePath));
        Assert.Single(Directory.GetFiles(_dataFolder, "workspaces.json.corrupt.*"));
    }

    [Fact]
    public void LoadWorkspaces_IgnoresNullEntries()
    {
        var service = new PresetStorageService(_dataFolder);
        File.WriteAllText(
            service.WorkspacesFilePath,
            """
            [
              null,
              {
                "id": "workspace_weekday",
                "name": "Weekday",
                "layoutId": "layout_8_small_1_main",
                "slots": []
              }
            ]
            """);

        var workspaces = service.LoadWorkspaces();

        var workspace = Assert.Single(workspaces);
        Assert.Equal("workspace_weekday", workspace.Id);
        Assert.Equal("Weekday", workspace.Name);
    }

    [Fact]
    public void LoadWorkspaces_NormalizesBlankAndDuplicateWorkspaceMetadata()
    {
        var service = new PresetStorageService(_dataFolder);
        File.WriteAllText(
            service.WorkspacesFilePath,
            """
            [
              {
                "id": " duplicate ",
                "name": " First ",
                "layoutId": " layout_4x4 ",
                "slots": []
              },
              {
                "id": "duplicate",
                "name": "Second",
                "layoutId": null,
                "slots": null
              },
              {
                "id": " ",
                "name": " ",
                "layoutId": "layout_8_small_1_main",
                "slots": []
              }
            ]
            """);

        var workspaces = service.LoadWorkspaces();

        Assert.Equal(["duplicate", "duplicate_2", "workspace_imported"], workspaces.Select(workspace => workspace.Id));
        Assert.Equal(["First", "Second", "Imported Workspace"], workspaces.Select(workspace => workspace.Name));
        Assert.Equal("layout_4x4", workspaces[0].LayoutId);
        Assert.Equal("", workspaces[1].LayoutId);
        Assert.Empty(workspaces[1].Slots);
    }

    [Fact]
    public void SaveWorkspaces_NormalizesDuplicateWorkspaceMetadataBeforeWriting()
    {
        var service = new PresetStorageService(_dataFolder);

        service.SaveWorkspaces(
        [
            CreateWorkspace(" duplicate ", " First "),
            CreateWorkspace("duplicate", "Second"),
            CreateWorkspace(" ", " ")
        ]);

        var loadedWorkspaces = service.LoadWorkspaces();

        Assert.Equal(["duplicate", "duplicate_2", "workspace_imported"], loadedWorkspaces.Select(workspace => workspace.Id));
        Assert.Equal(["First", "Second", "Imported Workspace"], loadedWorkspaces.Select(workspace => workspace.Name));
        Assert.Contains("\"id\": \"duplicate_2\"", File.ReadAllText(service.WorkspacesFilePath));
    }

    [Fact]
    public void SaveAppState_AndLoadAppState_RoundTripsLastSessionWithoutUsingWorkspacesFile()
    {
        var service = new PresetStorageService(_dataFolder);
        var appState = new AppState
        {
            LastWorkspaceId = "workspace_weekday",
            SelectedSlotId = 2,
            LastSession = CreateWorkspace("last_session", "Last Session"),
            IsExplorerPanelVisible = false,
            AreSlotUrlEditorsVisible = false,
            AreSlotControlBarsAlwaysVisible = false,
            AudibleQualityKey = "hd4k",
            Window = new AppWindowState
            {
                X = 10,
                Y = 20,
                Width = 1280,
                Height = 720,
                IsMaximized = true
            }
        };

        service.SaveAppState(appState);
        var loadedAppState = service.LoadAppState();

        Assert.NotNull(loadedAppState);
        Assert.Equal("workspace_weekday", loadedAppState.LastWorkspaceId);
        Assert.Equal(2, loadedAppState.SelectedSlotId);
        Assert.Equal("last_session", loadedAppState.LastSession?.Id);
        Assert.False(loadedAppState.IsExplorerPanelVisible);
        Assert.False(loadedAppState.AreSlotUrlEditorsVisible);
        Assert.False(loadedAppState.AreSlotControlBarsAlwaysVisible);
        Assert.Equal("hd4k", loadedAppState.AudibleQualityKey);
        Assert.Equal(1280, loadedAppState.Window.Width);
        Assert.False(File.Exists(service.WorkspacesFilePath));
    }

    [Fact]
    public void SaveAppState_AndLoadAppState_RoundTripsRemappedArbitraryShortcutKeys()
    {
        var service = new PresetStorageService(_dataFolder);
        var appState = new AppState
        {
            Shortcuts = new ShortcutSettings
            {
                RemoveKey = ShortcutKey.Create(0x12, "Alt"),
                SwapKey = ShortcutKey.Create(0x41, "A"),
                SwitchKey = ShortcutKey.Create(0x70, "F1")
            }
        };

        service.SaveAppState(appState);
        var loadedAppState = service.LoadAppState();

        Assert.NotNull(loadedAppState);
        Assert.Equal(0x12, loadedAppState.Shortcuts.RemoveKey.VirtualKey);
        Assert.Equal(0x41, loadedAppState.Shortcuts.SwapKey.VirtualKey);
        Assert.Equal(0x70, loadedAppState.Shortcuts.SwitchKey.VirtualKey);
        Assert.Equal("A", loadedAppState.Shortcuts.SwapKey.Name);
        Assert.Equal("F1", loadedAppState.Shortcuts.SwitchKey.Name);
        // 사이드바 토글 키는 지정하지 않았으므로 기본값 Tab(0x09)을 유지한다.
        Assert.Equal(0x09, loadedAppState.Shortcuts.ToggleExplorerKey.VirtualKey);
        // 가상 키 코드와 표시 이름이 함께 저장된다.
        var json = File.ReadAllText(service.AppStateFilePath);
        Assert.Contains("\"virtualKey\": 65", json);
        Assert.Contains("\"name\": \"A\"", json);
    }

    [Fact]
    public void LoadAppState_ResetsShortcutsToDefaultsWhenKeysCollide()
    {
        var service = new PresetStorageService(_dataFolder);
        File.WriteAllText(
            service.AppStateFilePath,
            """
            {
              "shortcuts": {
                "removeKey": { "virtualKey": 17, "name": "Ctrl" },
                "swapKey": { "virtualKey": 17, "name": "Ctrl" },
                "switchKey": { "virtualKey": 18, "name": "Alt" }
              }
            }
            """);

        var appState = service.LoadAppState();

        Assert.NotNull(appState);
        Assert.Equal(0x11, appState.Shortcuts.RemoveKey.VirtualKey);
        Assert.Equal(0x10, appState.Shortcuts.SwapKey.VirtualKey);
        Assert.Equal(0x12, appState.Shortcuts.SwitchKey.VirtualKey);
    }

    [Fact]
    public void LoadAppState_ResetsShortcutsToDefaultsWhenEscapeIsBound()
    {
        var service = new PresetStorageService(_dataFolder);
        File.WriteAllText(
            service.AppStateFilePath,
            """
            {
              "shortcuts": {
                "removeKey": { "virtualKey": 27, "name": "Esc" },
                "swapKey": { "virtualKey": 16, "name": "Shift" },
                "switchKey": { "virtualKey": 18, "name": "Alt" }
              }
            }
            """);

        var appState = service.LoadAppState();

        Assert.NotNull(appState);
        Assert.Equal(0x11, appState.Shortcuts.RemoveKey.VirtualKey);
        Assert.Equal("Ctrl", appState.Shortcuts.RemoveKey.Name);
    }

    [Fact]
    public void LoadAppState_DefaultsShortcutsWhenMissing()
    {
        var service = new PresetStorageService(_dataFolder);
        File.WriteAllText(service.AppStateFilePath, "{ \"audibleQualityKey\": \"original\" }");

        var appState = service.LoadAppState();

        Assert.NotNull(appState);
        Assert.Equal(0x11, appState.Shortcuts.RemoveKey.VirtualKey);
        Assert.Equal(0x10, appState.Shortcuts.SwapKey.VirtualKey);
        Assert.Equal(0x12, appState.Shortcuts.SwitchKey.VirtualKey);
        Assert.Equal(0x09, appState.Shortcuts.ToggleExplorerKey.VirtualKey);
    }

    [Fact]
    public void LoadAppState_NormalizesHandEditedStateMetadata()
    {
        var service = new PresetStorageService(_dataFolder);
        File.WriteAllText(
            service.AppStateFilePath,
            """
            {
              "lastWorkspaceId": " workspace_weekday ",
              "window": null,
              "selectedSlotId": 17,
              "lastSession": {
                "id": " ",
                "name": " ",
                "layoutId": null,
                "slots": null
              },
              "audibleQualityKey": "  bad_key  "
            }
            """);

        var appState = service.LoadAppState();

        Assert.NotNull(appState);
        Assert.Equal("workspace_weekday", appState.LastWorkspaceId);
        Assert.NotNull(appState.Window);
        Assert.Equal(1600, appState.Window.Width);
        Assert.Null(appState.SelectedSlotId);
        Assert.NotNull(appState.LastSession);
        Assert.Equal("workspace_imported", appState.LastSession.Id);
        Assert.Equal("Imported Workspace", appState.LastSession.Name);
        Assert.Equal("", appState.LastSession.LayoutId);
        Assert.Empty(appState.LastSession.Slots);
        Assert.Equal("original", appState.AudibleQualityKey);
    }

    [Fact]
    public void SaveAppState_NormalizesStateMetadataBeforeWriting()
    {
        var service = new PresetStorageService(_dataFolder);

        service.SaveAppState(new AppState
        {
            LastWorkspaceId = " ",
            Window = null!,
            SelectedSlotId = 99,
            LastSession = new WorkspacePreset
            {
                Id = " last_session ",
                Name = " Last Session ",
                LayoutId = " layout_4x4 ",
                Slots = null!
            }
        });

        var loadedAppState = service.LoadAppState();

        Assert.NotNull(loadedAppState);
        Assert.Null(loadedAppState.LastWorkspaceId);
        Assert.NotNull(loadedAppState.Window);
        Assert.Null(loadedAppState.SelectedSlotId);
        Assert.NotNull(loadedAppState.LastSession);
        Assert.Equal("last_session", loadedAppState.LastSession.Id);
        Assert.Equal("Last Session", loadedAppState.LastSession.Name);
        Assert.Equal("layout_4x4", loadedAppState.LastSession.LayoutId);
        Assert.Empty(loadedAppState.LastSession.Slots);
        Assert.Contains("\"selectedSlotId\": null", File.ReadAllText(service.AppStateFilePath));
    }

    [Fact]
    public void SaveAppState_OverwritesExistingStateWithoutLeavingTemporaryFiles()
    {
        var service = new PresetStorageService(_dataFolder);
        service.SaveAppState(new AppState
        {
            LastWorkspaceId = "workspace_old",
            SelectedSlotId = 1,
            LastSession = CreateWorkspace("last_session_old", "Old Session")
        });

        service.SaveAppState(new AppState
        {
            LastWorkspaceId = "workspace_new",
            SelectedSlotId = 9,
            LastSession = CreateWorkspace("last_session_new", "New Session")
        });

        var loadedAppState = service.LoadAppState();

        Assert.NotNull(loadedAppState);
        Assert.Equal("workspace_new", loadedAppState.LastWorkspaceId);
        Assert.Equal(9, loadedAppState.SelectedSlotId);
        Assert.Equal("last_session_new", loadedAppState.LastSession?.Id);
        Assert.Empty(Directory.GetFiles(_dataFolder, "appstate.json.tmp.*"));
    }

    [Fact]
    public void SaveAppState_DoesNotModifySavedWorkspacePresets()
    {
        var service = new PresetStorageService(_dataFolder);
        var savedWorkspace = CreateWorkspace("workspace_weekday", "Weekday");
        service.SaveWorkspaces([savedWorkspace]);
        var transientSession = CreateWorkspace("last_session", "Last Session");
        transientSession = new WorkspacePreset
        {
            Id = transientSession.Id,
            Name = transientSession.Name,
            LayoutId = transientSession.LayoutId,
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Temporary Stream",
                    StreamUrl = "https://example.com/temporary",
                    Muted = true,
                    ProfileGroupId = "A"
                }
            ]
        };

        service.SaveAppState(new AppState
        {
            LastWorkspaceId = savedWorkspace.Id,
            LastSession = transientSession
        });

        var loadedWorkspace = Assert.Single(service.LoadWorkspaces());
        Assert.Equal(savedWorkspace.Id, loadedWorkspace.Id);
        Assert.Contains(loadedWorkspace.Slots, slot =>
            slot.SlotId == 1 &&
            slot.StreamName == "Stream A" &&
            slot.StreamUrl == "https://example.com/a" &&
            !slot.Muted);
        Assert.DoesNotContain(loadedWorkspace.Slots, slot => slot.StreamName == "Temporary Stream");
    }

    [Fact]
    public void LoadAppState_QuarantinesCorruptJsonAndReturnsNull()
    {
        var service = new PresetStorageService(_dataFolder);
        File.WriteAllText(service.AppStateFilePath, "{ invalid json");

        var appState = service.LoadAppState();

        Assert.Null(appState);
        Assert.False(File.Exists(service.AppStateFilePath));
        Assert.Single(Directory.GetFiles(_dataFolder, "appstate.json.corrupt.*"));
    }

    [Fact]
    public void CreateWorkspaceId_CreatesUniqueStableIds()
    {
        var existingWorkspace = CreateWorkspace("workspace_weekday_default", "Weekday Default");

        var firstId = PresetStorageService.CreateWorkspaceId("Weekday Default", []);
        var secondId = PresetStorageService.CreateWorkspaceId("Weekday Default", [existingWorkspace]);

        Assert.Equal("workspace_weekday_default", firstId);
        Assert.Equal("workspace_weekday_default_2", secondId);
    }

    [Fact]
    public void CreateWorkspaceId_NormalizesNamesAndFallsBackForBlankIds()
    {
        var existingWorkspace = CreateWorkspace("workspace", "Existing");

        var normalizedId = PresetStorageService.CreateWorkspaceId("  Weekday / Main!  ", []);
        var blankFallbackId = PresetStorageService.CreateWorkspaceId("!!!", [existingWorkspace]);

        Assert.Equal("workspace_weekday_main", normalizedId);
        Assert.Equal("workspace_2", blankFallbackId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }

    private static WorkspacePreset CreateWorkspace(string id, string name)
    {
        return new WorkspacePreset
        {
            Id = id,
            Name = name,
            LayoutId = "layout_4x4",
            Slots =
            [
                new WorkspaceSlot
                {
                    SlotId = 1,
                    StreamName = "Stream A",
                    StreamUrl = "https://example.com/a",
                    Muted = false,
                    ProfileGroupId = "A"
                },
                new WorkspaceSlot
                {
                    SlotId = 2,
                    StreamName = "Stream B",
                    StreamUrl = "https://example.com/b",
                    Muted = true,
                    ProfileGroupId = "A"
                }
            ]
        };
    }
}
