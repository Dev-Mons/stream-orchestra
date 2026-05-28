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
    public void ExplorerSidebar_ProvidesExplorerToggleInTopRightCorner()
    {
        var document = LoadMainWindowDocument();
        var button = FindElementByName(document, "ToggleExplorerButton");

        Assert.Equal("ToggleExplorerButton_Click", GetAttribute(button, "Click"));
        Assert.Equal("Right", GetAttribute(button, "HorizontalAlignment"));
        Assert.Equal("Top", GetAttribute(button, "VerticalAlignment"));
        Assert.NotEqual("Collapsed", GetAttribute(button, "Visibility"));
        Assert.Contains(button.Descendants(), element =>
            element.Name.LocalName == "Image" &&
            GetAttribute(element, "Source") == "Assets/explorer-toggle.png");
        Assert.Contains(button.Ancestors(), ancestor => GetAttribute(ancestor, "Name") == "ExplorerBorder");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value is "ToggleSlotUrlEditorsButton" or "ToggleSlotControlBarsButton"));
    }

    [Fact]
    public void HiddenExplorer_EdgeToggleUsesLeftHitTarget()
    {
        var document = LoadMainWindowDocument();
        var mainContentGrid = FindElementByName(document, "MainContentGrid");
        var hitTarget = FindElementByName(document, "AutoShowExplorerHitTarget");
        var popup = FindElementByName(document, "AutoShowExplorerPopup");
        var button = FindElementByName(document, "AutoShowExplorerButton");
        var codeBehindPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml.cs"));
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.Equal("1", GetAttribute(mainContentGrid, "Grid.Row"));
        Assert.Equal("1", GetAttribute(hitTarget, "Grid.Row"));
        Assert.Equal("Left", GetAttribute(hitTarget, "HorizontalAlignment"));
        Assert.Equal("28", GetAttribute(hitTarget, "Width"));
        Assert.Equal("Transparent", GetAttribute(hitTarget, "Background"));
        Assert.Equal("Collapsed", GetAttribute(hitTarget, "Visibility"));
        Assert.Equal("AutoShowExplorerHitTarget_MouseEnter", GetAttribute(hitTarget, "MouseEnter"));
        Assert.Equal("AutoShowExplorerHitTarget_MouseLeave", GetAttribute(hitTarget, "MouseLeave"));
        Assert.Equal("True", GetAttribute(popup, "AllowsTransparency"));
        Assert.Contains("AutoShowExplorerHitTarget", GetAttribute(popup, "PlacementTarget"));
        Assert.Equal("Relative", GetAttribute(popup, "Placement"));
        Assert.Equal("False", GetAttribute(popup, "IsOpen"));
        Assert.Equal("AutoShowExplorerButton_Click", GetAttribute(button, "Click"));
        Assert.Equal("AutoShowExplorerButton_MouseEnter", GetAttribute(button, "MouseEnter"));
        Assert.Equal("AutoShowExplorerButton_MouseLeave", GetAttribute(button, "MouseLeave"));
        Assert.Contains("AutoShowExplorerHitTarget.Visibility = _isExplorerPanelVisible ? Visibility.Collapsed : Visibility.Visible", codeBehind);
        Assert.Contains("AutoShowExplorerPopup.IsOpen = true", codeBehind);
        Assert.Contains("AutoShowExplorerPopup.IsOpen = false", codeBehind);
        Assert.DoesNotContain("GetCursorPos", codeBehind);
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
        Assert.Contains("LayoutTreePresetConverter.Convert", codeBehind);
        Assert.Contains("LayoutTreeRenderer.Build", codeBehind);
    }

    [Fact]
    public void PlaybackGrid_WiresDynamicDockingPreviewAndDropFlow()
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

        Assert.Equal("SlotsGrid_DragLeave", GetAttribute(slotsGrid, "DragLeave"));
        Assert.Contains("DockingOverlayPresenter", codeBehind);
        Assert.Contains("LayoutTreeMutationService", codeBehind);
        Assert.Contains("ApplyLayoutTree", codeBehind);
        Assert.Contains("CreateDockedSlotFromDropAsync", codeBehind);
        Assert.Contains("DockingInputOverlay", codeBehind);
        Assert.Contains("ShowDockingInputOverlay", codeBehind);
        Assert.Contains("HideDockingInputOverlay", codeBehind);
        Assert.Contains("GetDockPointerPosition", codeBehind);
        Assert.Contains("_lastDockTargetSlot", codeBehind);
        Assert.Contains("_lastDockDirection", codeBehind);
        Assert.Contains("ShowDockingInputPreview", codeBehind);
        Assert.Contains("_dockingInputPreview", codeBehind);
        Assert.Contains("RemoveFromLayoutRequested += SlotView_RemoveFromLayoutRequested", codeBehind);
        Assert.Contains("RemoveSlotFromDynamicLayoutAsync", codeBehind);
        Assert.Contains("_layoutTreeMutationService.RemoveLeaf", codeBehind);
        Assert.Contains("SetRemoveSlotActionAvailable", codeBehind);
        Assert.Contains("slot.ClearAsync()", codeBehind);
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
