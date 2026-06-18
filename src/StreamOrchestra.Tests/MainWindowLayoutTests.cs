using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void TopToolbar_RemovesManualLoadAndVerificationControls()
    {
        var document = LoadMainWindowDocument();

        Assert.Equal("EditLayoutsButton_Click", GetAttribute(FindMenuItem(document, "레이아웃"), "Click"));
        Assert.NotNull(FindElementByName(document, "LayoutMenuItem"));
        Assert.Null(FindMenuItemOrDefault(document, "보기"));
        Assert.Null(FindMenuItemOrDefault(document, "레이아웃 편집"));
        Assert.Null(FindElementByNameOrDefault(document, "LayoutComboBox"));
        Assert.Null(FindElementByNameOrDefault(document, "LayoutSelectorPanel"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value is "GlobalUrlTextBox" or "LoadScopeComboBox" or "FeasibilityResultTextBlock" or "CurrentFeasibilityScenarioTextBlock"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            GetAttribute(element, "Content") is "선택 그룹 로드" or "그룹 단독" or "전체 로드" or "빈 화면" or "성공" or "부분" or "실패" or "리포트 저장" or "브라우저 스크립트" or "감사 복사");
    }

    [Fact]
    public void ExplorerSidebar_ProvidesExplorerToggleInTopToolbarLeftOfSettings()
    {
        var document = LoadMainWindowDocument();
        var button = FindElementByName(document, "ToggleExplorerButton");
        var icon = FindElementByName(document, "ToggleExplorerIcon");

        Assert.Equal("ToggleExplorerButton_Click", GetAttribute(button, "Click"));
        Assert.Equal("Left", GetAttribute(button, "DockPanel.Dock"));
        Assert.Equal("SidebarToggleButton", GetAttribute(button, "Style")?.Trim('{', '}').Replace("StaticResource ", ""));
        Assert.NotEqual("Collapsed", GetAttribute(button, "Visibility"));

        // 탐색 토글은 더 이상 사이드바 내부가 아니라 최상단 툴바에 있고, 설정 메뉴보다 앞(왼쪽)에 온다.
        Assert.DoesNotContain(button.Ancestors(), ancestor => GetAttribute(ancestor, "Name") == "ExplorerBorder");
        var settingsMenu = FindMenuItem(document, "설정");
        var toolbar = button.Ancestors().First(ancestor => ancestor.Name.LocalName == "DockPanel");
        Assert.Contains(settingsMenu.Ancestors(), ancestor => ancestor == toolbar);
        var toolbarChildren = toolbar.Elements().ToList();
        var buttonIndex = toolbarChildren.IndexOf(button);
        var menuIndex = toolbarChildren.IndexOf(settingsMenu.Ancestors().First(a => a.Parent == toolbar));
        Assert.True(buttonIndex >= 0 && buttonIndex < menuIndex);

        // 토글 버튼은 사이드탭 열기/닫기 아이콘(sidebar-close.png 기본)을 사용한다.
        Assert.Equal("/Assets/sidebar-close.png", GetAttribute(icon, "Source"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value is "ToggleSlotUrlEditorsButton" or "ToggleSlotControlBarsButton"));
    }

    [Fact]
    public void TopToolbar_ProvidesRefreshButtonLeftOfMuteAllButton()
    {
        var document = LoadMainWindowDocument();
        var refreshButton = FindElementByName(document, "RefreshPlayingScreensButton");
        var refreshIcon = FindElementByName(document, "RefreshPlayingScreensIcon");
        var muteAllButton = FindElementByName(document, "MuteAllButton");
        var codeBehind = File.ReadAllText(GetMainWindowCodeBehindPath());
        var slotCodeBehind = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "StreamSlotView.xaml.cs")));

        Assert.Equal("RefreshPlayingScreensButton_Click", GetAttribute(refreshButton, "Click"));
        Assert.Equal("Right", GetAttribute(refreshButton, "DockPanel.Dock"));
        Assert.Equal("SidebarToggleButton", GetAttribute(refreshButton, "Style")?.Trim('{', '}').Replace("StaticResource ", ""));
        Assert.Equal("현재 재생 중인 화면 전체 새로고침", GetAttribute(refreshButton, "ToolTip"));
        Assert.Equal("/Assets/refresh.png", GetAttribute(refreshIcon, "Source"));

        var toolbar = refreshButton.Ancestors().First(ancestor => ancestor.Name.LocalName == "DockPanel");
        var toolbarChildren = toolbar.Elements().ToList();
        Assert.True(toolbarChildren.IndexOf(refreshButton) > toolbarChildren.IndexOf(muteAllButton));

        Assert.Contains("ReloadPlayingScreensAsync", codeBehind);
        Assert.Contains("GetVisibleNonBlankSlots()", codeBehind);
        Assert.Contains("await slot.ReloadAsync();", codeBehind);
        Assert.Contains("public async Task ReloadAsync()", slotCodeBehind);
        Assert.Contains("Browser.CoreWebView2.Reload();", slotCodeBehind);
    }

    [Fact]
    public void HiddenExplorer_RemovesLegacyEdgeToggleAndDrivesToolbarIcon()
    {
        var document = LoadMainWindowDocument();
        var codeBehindPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml.cs"));
        var codeBehind = File.ReadAllText(codeBehindPath);

        // 좌측 가장자리 호버 기반 숨김 토글(AutoShowExplorer*)이 XAML·코드비하인드에서 완전히 제거되었다.
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value is "AutoShowExplorerHitTarget" or "AutoShowExplorerPopup" or "AutoShowExplorerButton"));
        Assert.DoesNotContain("AutoShowExplorer", codeBehind);
        Assert.DoesNotContain("RefreshAutoShowExplorerEdgeVisibility", codeBehind);
        Assert.DoesNotContain("GetCursorPos", codeBehind);

        // 단일 최상단 토글 버튼이 사이드탭 열림 상태에 따라 열기/닫기 아이콘을 전환한다.
        Assert.Contains("ToggleExplorerIcon.Source = isVisible ? SidebarCloseIcon : SidebarOpenIcon", codeBehind);
        Assert.Contains("/Assets/sidebar-close.png", codeBehind);
        Assert.Contains("/Assets/sidebar-open.png", codeBehind);
    }

    [Fact]
    public void PlaybackGrid_AcceptsDroppedStreamsOverTheScreenArea()
    {
        var document = LoadMainWindowDocument();
        var slotsGrid = FindElementByName(document, "SlotsGrid");
        var codeBehindPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml.cs"));
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.Equal("True", GetAttribute(slotsGrid, "AllowDrop"));
        Assert.Equal("SlotsGrid_DragOver", GetAttribute(slotsGrid, "DragOver"));
        Assert.Equal("SlotsGrid_Drop", GetAttribute(slotsGrid, "Drop"));
        Assert.Equal("#05070A", GetAttribute(slotsGrid, "Background"));
        Assert.Contains("TryGetDropTargetSlot", codeBehind);
        Assert.Contains("LoadDroppedStreamIntoSlotAsync", codeBehind);
        Assert.Contains("StreamDropDataReader.TryGetDroppedStream", codeBehind);
        Assert.Contains("LayoutGridRenderer.Build", codeBehind);
    }

    [Fact]
    public void PlaybackGrid_WiresCardBasedLayoutSelectionAndRemovesDynamicDocking()
    {
        var document = LoadMainWindowDocument();
        var slotsGrid = FindElementByName(document, "SlotsGrid");
        var codeBehindPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml.cs"));
        var codeBehind = File.ReadAllText(codeBehindPath);

        // 카드 기반 레이아웃 전환 흐름이 배선되어 있다.
        Assert.Contains("LayoutCardPresenter", codeBehind);
        Assert.Contains("LayoutTemplateCandidateService", codeBehind);
        Assert.Contains("ShowLayoutCards", codeBehind);
        Assert.Contains("HideLayoutCards", codeBehind);
        Assert.Contains("ApplyTemplateFromCardAsync", codeBehind);
        Assert.Contains("CardChosen += LayoutCardPresenter_CardChosen", codeBehind);
        Assert.Contains("SlotSwapRequested += SlotView_SlotSwapRequested", codeBehind);

        // 키 홀드 기반 화면 제거/교체/전환 흐름이 단축키 설정을 통해 배선되어 있다.
        Assert.Contains("RemoveSlotRequested += SlotView_RemoveSlotRequested", codeBehind);
        Assert.Contains("KeyStateChanged += OnSlotKeyStateChanged", codeBehind);
        Assert.Contains("_shortcutSettings.GetAction(virtualKey)", codeBehind);
        // 텍스트 입력란 포커스 시에는 임의 키 단축키를 무시한다.
        Assert.Contains("Keyboard.FocusedElement is TextBoxBase", codeBehind);
        // 사이드바 토글 단축키(기본 Tab)는 누를 때마다 탐색 패널을 토글한다.
        Assert.Contains("ShortcutAction.ToggleExplorer", codeBehind);
        Assert.Contains("SetExplorerPanelVisible(!_isExplorerPanelVisible)", codeBehind);
        // 제거 버튼 클릭 → 삭제 예정 슬롯 토글 후 남은 슬롯 수에 맞는 레이아웃 카드를 갱신한다.
        Assert.Contains("SlotRemovalSelectionService", codeBehind);
        Assert.Contains("ToggleRemoveScreen", codeBehind);
        Assert.Contains("UpdateRemovalLayoutCards", codeBehind);
        Assert.Contains("ApplyRemovalAsync", codeBehind);
        Assert.Contains("LayoutCardMode.Remove", codeBehind);
        Assert.Contains("GetTemplatesForSlotCount", codeBehind);
        Assert.Contains("MainWindow_PreviewKeyDown", codeBehind);
        Assert.DoesNotContain("_pendingRemovalSlot", codeBehind);

        // 동적 도킹/트리 레이아웃 경로가 완전히 제거되었다.
        Assert.Null(GetAttribute(slotsGrid, "DragLeave"));
        Assert.DoesNotContain("DockingOverlayPresenter", codeBehind);
        Assert.DoesNotContain("LayoutTreeMutationService", codeBehind);
        Assert.DoesNotContain("LayoutTreePresetConverter", codeBehind);
        Assert.DoesNotContain("LayoutTreeRenderer", codeBehind);
        Assert.DoesNotContain("ApplyLayoutTree", codeBehind);
        Assert.DoesNotContain("CreateDockedSlotFromDropAsync", codeBehind);
        Assert.DoesNotContain("RemoveSlotFromDynamicLayoutAsync", codeBehind);
        Assert.DoesNotContain("_currentLayoutTree", codeBehind);
        Assert.DoesNotContain("DockingInputOverlay", codeBehind);
        Assert.DoesNotContain("_lastDockDirection", codeBehind);
        Assert.DoesNotContain("SetRemoveSlotActionAvailable", codeBehind);
    }

    [Fact]
    public void PlaybackToolbar_RemovesPlanPlaybackTestShortcutsAfterVerification()
    {
        var document = LoadMainWindowDocument();
        var audibleQualityComboBox = FindElementByName(document, "AudibleQualityComboBox");
        var qualityMenuItem = FindElementByName(document, "QualityMenuItem");

        var playbackButtonTags = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Button" &&
                GetAttribute(element, "Click") == "LoadFirstCountButton_Click")
            .Select(element => GetAttribute(element, "Tag"))
            .ToArray();
        var audibleQualityOptions = audibleQualityComboBox
            .Elements()
            .Where(element => element.Name.LocalName == "ComboBoxItem")
            .Select(element => $"{GetAttribute(element, "Tag")}:{GetAttribute(element, "Content")}")
            .ToArray();

        Assert.Empty(playbackButtonTags);
        Assert.Equal(["q1440:1440p", "original:1080p", "hd4k:720p", "hd:540p", "sd:360p"], audibleQualityOptions);
        Assert.Equal("화질", GetAttribute(qualityMenuItem, "Header"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            GetAttribute(element, "Content") == "화질 적용");
        Assert.Empty(document.Descendants().Where(element =>
            element.Name.LocalName == "ComboBox" &&
            GetAttribute(element, "Name") == "MutedQualityComboBox"));
    }

    [Fact]
    public void CodeBehind_UsesPreviewButtonsForLayoutSelection()
    {
        var codeBehindPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml.cs"));
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.Contains("RefreshLayoutSelector", codeBehind);
        Assert.Contains("ApplySelectedLayoutAsync", codeBehind);
        Assert.DoesNotContain("LayoutMenuItem_Click", codeBehind);
        Assert.DoesNotContain("LayoutComboBox", codeBehind);
        Assert.DoesNotContain("LayoutComboBox_SelectionChanged", codeBehind);
    }

    [Fact]
    public void TopMenu_ProvidesViewSettingsAndPresetActions()
    {
        var document = LoadMainWindowDocument();
        var settingsMenu = FindMenuItem(document, "설정");
        var layoutMenu = FindMenuItem(document, "레이아웃");
        var expectedButtons = new Dictionary<string, string>
        {
            ["불러오기"] = "LoadWorkspaceButton_Click",
            ["현재 상태 저장"] = "SaveCurrentWorkspaceButton_Click",
            ["다른 이름으로 저장"] = "SaveWorkspaceAsButton_Click"
        };

        Assert.NotNull(settingsMenu);
        Assert.Equal("EditLayoutsButton_Click", GetAttribute(layoutMenu, "Click"));
        Assert.Null(FindMenuItemOrDefault(document, "보기"));
        Assert.Contains(settingsMenu.Descendants(), element =>
            element.Name.LocalName == "MenuItem" &&
            GetAttribute(element, "Header") == "프리셋");

        foreach (var (content, clickHandler) in expectedButtons)
        {
            var button = document
                .Descendants()
                .Single(element => element.Name.LocalName == "MenuItem" &&
                    GetAttribute(element, "Header") == content);

            Assert.Equal(clickHandler, GetAttribute(button, "Click"));
        }
    }

    [Fact]
    public void SettingsMenu_ProvidesRemappableShortcutEntryWiredToDialog()
    {
        var document = LoadMainWindowDocument();
        var settingsMenu = FindMenuItem(document, "설정");
        var shortcutItem = FindMenuItemOrDefault(document, "단축키");
        var codeBehind = File.ReadAllText(GetMainWindowCodeBehindPath());

        Assert.NotNull(shortcutItem);
        Assert.Equal("ShortcutSettingsButton_Click", GetAttribute(shortcutItem, "Click"));
        Assert.Contains(settingsMenu.Descendants(), element => element == shortcutItem);

        // 단축키 메뉴는 다이얼로그를 띄우고, 키 캡처마다 통지받아 즉시 적용·영속화하며 라벨을 갱신한다.
        Assert.Contains("new ShortcutSettingsDialog(_shortcutSettings)", codeBehind);
        Assert.Contains("dialog.ShortcutsChanged += ApplyShortcutSettings;", codeBehind);
        Assert.Contains("_shortcutSettings = settings;", codeBehind);
        Assert.Contains("ApplyShortcutLabelsToSlots();", codeBehind);
        Assert.Contains("_presetStorageService.SaveAppState(CaptureAppState());", codeBehind);
        // AppState 캡처에 단축키 매핑이 포함된다.
        Assert.Contains("Shortcuts = _shortcutSettings", codeBehind);
        Assert.Contains("_shortcutSettings = _loadedAppState?.Shortcuts", codeBehind);
    }

    [Fact]
    public void CodeBehind_RecoversEntireSoopProfileGroupAfterPlaybackLimitWarning()
    {
        var codeBehind = File.ReadAllText(GetMainWindowCodeBehindPath());

        Assert.Contains("SoopPlaybackLimitDetected += SlotView_SoopPlaybackLimitDetected", codeBehind);
        Assert.Contains("RecoverSoopProfileGroupAsync", codeBehind);
        Assert.Contains("SoopGroupRecoveryCooldown", codeBehind);
        Assert.Contains("_recoveringSoopLimitGroups", codeBehind);
        Assert.Contains("StopPlaybackForReplacementAsync", codeBehind);
        Assert.Contains("SoopSlotReplacementReleaseDelay", codeBehind);
        Assert.Contains("ShouldReleaseSlotBeforeNavigation", codeBehind);
        Assert.Contains("IsSoopUrl", codeBehind);
    }

    private static string GetMainWindowCodeBehindPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml.cs"));
    }

    private static XDocument LoadMainWindowDocument()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml"));

        return XDocument.Load(path);
    }

    private static XElement FindElementByName(XDocument document, string name)
    {
        return document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value == name));
    }

    private static XElement? FindElementByNameOrDefault(XDocument document, string name)
    {
        return document
            .Descendants()
            .SingleOrDefault(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value == name));
    }

    private static XElement FindButton(XDocument document, string content)
    {
        return document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button" &&
                GetAttribute(element, "Content") == content);
    }

    private static XElement FindMenuItem(XDocument document, string header)
    {
        return document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "MenuItem" &&
                GetAttribute(element, "Header") == header);
    }

    private static XElement? FindMenuItemOrDefault(XDocument document, string header)
    {
        return document
            .Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "MenuItem" &&
                GetAttribute(element, "Header") == header);
    }

    private static string? GetAttribute(XElement element, string name)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name)
            ?.Value;
    }
}
