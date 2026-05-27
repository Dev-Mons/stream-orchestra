using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void GlobalLoadToolbar_ProvidesUrlScopeAndLoadActions()
    {
        var document = LoadMainWindowDocument();
        var globalUrlTextBox = FindElementByName(document, "GlobalUrlTextBox");
        var scopeComboBox = FindElementByName(document, "LoadScopeComboBox");

        Assert.Equal("https://www.sooplive.co.kr", GetAttribute(globalUrlTextBox, "Text"));
        Assert.Equal("GlobalUrlTextBox_KeyDown", GetAttribute(globalUrlTextBox, "KeyDown"));
        Assert.Equal(
            ["All:All Groups", "A:Group A", "B:Group B", "C:Group C", "D:Group D"],
            scopeComboBox
                .Elements()
                .Where(element => element.Name.LocalName == "ComboBoxItem")
                .Select(element => $"{GetAttribute(element, "Tag")}:{GetAttribute(element, "Content")}")
                .ToArray());

        Assert.Equal("LoadScopeButton_Click", GetAttribute(FindButton(document, "선택 그룹 로드"), "Click"));
        Assert.Equal("LoadIsolatedScopeButton_Click", GetAttribute(FindButton(document, "그룹 단독"), "Click"));
        Assert.Equal("LoadAllButton_Click", GetAttribute(FindButton(document, "전체 로드"), "Click"));
        Assert.Equal("BlankAllButton_Click", GetAttribute(FindButton(document, "빈 화면"), "Click"));
        Assert.Equal("EditLayoutsButton_Click", GetAttribute(FindButton(document, "레이아웃 편집"), "Click"));
    }

    [Fact]
    public void ViewToolbar_ProvidesExplorerUrlEditorAndControlBarToggles()
    {
        var document = LoadMainWindowDocument();
        var expectedToggles = new Dictionary<string, string>
        {
            ["ToggleExplorerButton"] = "ToggleExplorerButton_Click",
            ["ToggleSlotUrlEditorsButton"] = "ToggleSlotUrlEditorsButton_Click",
            ["ToggleSlotControlBarsButton"] = "ToggleSlotControlBarsButton_Click"
        };

        foreach (var (name, clickHandler) in expectedToggles)
        {
            var button = FindElementByName(document, name);

            Assert.Equal(clickHandler, GetAttribute(button, "Click"));
        }

        Assert.Equal("Collapsed", GetAttribute(FindElementByName(document, "ToggleSlotUrlEditorsButton"), "Visibility"));
        Assert.Equal("Collapsed", GetAttribute(FindElementByName(document, "ToggleSlotControlBarsButton"), "Visibility"));
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

    [Fact]
    public void FeasibilityToolbar_ProvidesAccountLabelAndManualEvidenceFields()
    {
        var document = LoadMainWindowDocument();
        const string accountEvidenceBinding = "{Binding IsChecked, ElementName=SameAccountSessionCheckBox}";

        Assert.Equal("검증 메모", GetAttribute(FindElementByName(document, "FeasibilityNotesTextBox"), "ToolTip"));
        var accountLabelTextBox = FindElementByName(document, "AccountLabelTextBox");
        Assert.Equal("SOOP account label for evidence", GetAttribute(accountLabelTextBox, "ToolTip"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(accountLabelTextBox, "IsEnabled"));
        var sameAccountCheckBox = FindElementByName(document, "SameAccountSessionCheckBox");
        Assert.Equal("계정 유지", GetAttribute(sameAccountCheckBox, "Content"));
        Assert.Equal("SameAccountSessionCheckBox_Unchecked", GetAttribute(sameAccountCheckBox, "Unchecked"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(FindElementByName(document, "VerifiedGroupACheckBox"), "IsEnabled"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(FindElementByName(document, "VerifiedGroupBCheckBox"), "IsEnabled"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(FindElementByName(document, "VerifiedGroupCCheckBox"), "IsEnabled"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(FindElementByName(document, "VerifiedGroupDCheckBox"), "IsEnabled"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(FindElementByName(document, "RestartSessionCheckBox"), "IsEnabled"));
        Assert.Equal("CPU %", GetAttribute(FindElementByName(document, "ObservedCpuTextBox"), "ToolTip"));
        Assert.Equal("GPU %", GetAttribute(FindElementByName(document, "ObservedGpuTextBox"), "ToolTip"));
        Assert.Equal("Memory MB", GetAttribute(FindElementByName(document, "ObservedMemoryTextBox"), "ToolTip"));
    }

    [Fact]
    public void CodeBehind_ClearsSameAccountEvidenceWhenAccountEvidenceIsUnchecked()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml.cs"));
        var text = File.ReadAllText(path);
        var handlerStart = text.IndexOf(
            "private void SameAccountSessionCheckBox_Unchecked",
            StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.Contains("if (!IsInitialized ||", text[handlerStart..]);
        Assert.Contains("AccountLabelTextBox.Clear();", text[handlerStart..]);
        Assert.Contains("VerifiedGroupACheckBox.IsChecked = false;", text[handlerStart..]);
        Assert.Contains("VerifiedGroupBCheckBox.IsChecked = false;", text[handlerStart..]);
        Assert.Contains("VerifiedGroupCCheckBox.IsChecked = false;", text[handlerStart..]);
        Assert.Contains("VerifiedGroupDCheckBox.IsChecked = false;", text[handlerStart..]);
        Assert.Contains("RestartSessionCheckBox.IsChecked = false;", text[handlerStart..]);
    }

    [Fact]
    public void CodeBehind_ClearsResourceOkAfterRecordingFeasibilityResult()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml.cs"));
        var text = File.ReadAllText(path);
        var handlerStart = text.IndexOf(
            "private void RecordFeasibilityResultButton_Click",
            StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.Contains("ObservedCpuTextBox.Clear();", text[handlerStart..]);
        Assert.Contains("ObservedGpuTextBox.Clear();", text[handlerStart..]);
        Assert.Contains("ObservedMemoryTextBox.Clear();", text[handlerStart..]);
        Assert.Contains("ResourceAcceptableCheckBox.IsChecked = false;", text[handlerStart..]);
    }

    [Fact]
    public void CodeBehind_AddsPlanGateHintToCurrentScenarioTooltip()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "MainWindow.xaml.cs"));
        var text = File.ReadAllText(path);
        var methodStart = text.IndexOf(
            "private void UpdateCurrentFeasibilityScenarioText()",
            StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.Contains("FeasibilityScenarioService.CreatePlanGateHint", text[methodStart..]);
        Assert.Contains("Environment.NewLine", text[methodStart..]);
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
