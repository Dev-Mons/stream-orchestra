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
    }

    [Fact]
    public void PlaybackToolbar_ProvidesPlanRequiredPlaybackCounts()
    {
        var document = LoadMainWindowDocument();

        var playbackButtonTags = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Button" &&
                GetAttribute(element, "Click") == "LoadFirstCountButton_Click")
            .Select(element => GetAttribute(element, "Tag"))
            .ToArray();

        Assert.Equal(["4", "8", "9", "12", "16"], playbackButtonTags);
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
        Assert.Equal("계정 유지", GetAttribute(FindElementByName(document, "SameAccountSessionCheckBox"), "Content"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(FindElementByName(document, "VerifiedGroupACheckBox"), "IsEnabled"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(FindElementByName(document, "VerifiedGroupBCheckBox"), "IsEnabled"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(FindElementByName(document, "VerifiedGroupCCheckBox"), "IsEnabled"));
        Assert.Equal(accountEvidenceBinding, GetAttribute(FindElementByName(document, "VerifiedGroupDCheckBox"), "IsEnabled"));
        Assert.Equal("CPU %", GetAttribute(FindElementByName(document, "ObservedCpuTextBox"), "ToolTip"));
        Assert.Equal("GPU %", GetAttribute(FindElementByName(document, "ObservedGpuTextBox"), "ToolTip"));
        Assert.Equal("Memory MB", GetAttribute(FindElementByName(document, "ObservedMemoryTextBox"), "ToolTip"));
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
