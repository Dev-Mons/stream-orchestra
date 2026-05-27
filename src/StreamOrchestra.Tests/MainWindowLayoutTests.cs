using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void TopToolbar_RemovesManualLoadAndVerificationControls()
    {
        var document = LoadMainWindowDocument();

        Assert.Equal("EditLayoutsButton_Click", GetAttribute(FindButton(document, "레이아웃 편집"), "Click"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value is "GlobalUrlTextBox" or "LoadScopeComboBox" or "FeasibilityResultTextBlock" or "CurrentFeasibilityScenarioTextBlock"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            GetAttribute(element, "Content") is "선택 그룹 로드" or "그룹 단독" or "전체 로드" or "빈 화면" or "성공" or "부분" or "실패" or "리포트 저장" or "브라우저 스크립트" or "감사 복사");
    }

    [Fact]
    public void ViewToolbar_ProvidesOnlyExplorerToggle()
    {
        var document = LoadMainWindowDocument();
        var button = FindElementByName(document, "ToggleExplorerButton");

        Assert.Equal("ToggleExplorerButton_Click", GetAttribute(button, "Click"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value is "ToggleSlotUrlEditorsButton" or "ToggleSlotControlBarsButton"));
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
        Assert.Contains("layout.ColumnWeights", codeBehind);
        Assert.Contains("layout.RowWeights", codeBehind);
        Assert.Contains("GetGridWeight", codeBehind);
    }

    [Fact]
    public void PlaybackToolbar_RemovesPlanPlaybackTestShortcutsAfterVerification()
    {
        var document = LoadMainWindowDocument();
        var audibleQualityComboBox = FindElementByName(document, "AudibleQualityComboBox");
        var mutedQualityComboBox = FindElementByName(document, "MutedQualityComboBox");
        var qualityLockCheckBox = FindElementByName(document, "QualityLockCheckBox");

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
        var mutedQualityOptions = mutedQualityComboBox
            .Elements()
            .Where(element => element.Name.LocalName == "ComboBoxItem")
            .Select(element => $"{GetAttribute(element, "Tag")}:{GetAttribute(element, "Content")}")
            .ToArray();

        Assert.Empty(playbackButtonTags);
        Assert.Equal("True", GetAttribute(qualityLockCheckBox, "IsChecked"));
        Assert.Equal("QualityPolicyControl_Changed", GetAttribute(qualityLockCheckBox, "Checked"));
        Assert.Equal(["original:최대화질", "master:자동", "hd4k:720p", "hd:540p", "sd:360p"], audibleQualityOptions);
        Assert.Equal(["original:최대화질", "master:자동", "hd4k:720p", "hd:540p", "sd:360p"], mutedQualityOptions);
        Assert.Equal("ApplyQualityPolicyButton_Click", GetAttribute(FindButton(document, "화질 적용"), "Click"));
        Assert.Equal("RefreshDiagnosticsButton_Click", GetAttribute(FindButton(document, "진단 갱신"), "Click"));
    }

    [Fact]
    public void PresetToolbar_ProvidesPlanRequiredPresetActions()
    {
        var document = LoadMainWindowDocument();
        var expectedButtons = new Dictionary<string, string>
        {
            ["불러오기"] = "LoadWorkspaceButton_Click",
            ["현재 상태 저장"] = "SaveCurrentWorkspaceButton_Click",
            ["다른 이름으로 저장"] = "SaveWorkspaceAsButton_Click",
            ["되돌리기"] = "RevertWorkspaceButton_Click"
        };

        foreach (var (content, clickHandler) in expectedButtons)
        {
            var button = document
                .Descendants()
                .Single(element => element.Name.LocalName == "Button" &&
                    GetAttribute(element, "Content") == content);

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

    private static XElement FindButton(XDocument document, string content)
    {
        return document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button" &&
                GetAttribute(element, "Content") == content);
    }

    private static string? GetAttribute(XElement element, string name)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name)
            ?.Value;
    }
}
