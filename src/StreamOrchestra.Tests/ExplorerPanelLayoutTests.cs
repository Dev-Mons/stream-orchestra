using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class ExplorerPanelLayoutTests
{
    [Fact]
    public void ExplorerPanel_ProvidesNavigationAndCurrentUrlInsertionActions()
    {
        var document = LoadExplorerPanelDocument();
        var expectedButtons = new Dictionary<string, string>
        {
            ["Go"] = "GoButton_Click",
            ["Back"] = "BackButton_Click",
            ["Refresh"] = "RefreshButton_Click",
            ["선택 슬롯에 넣기"] = "UseCurrentUrlButton_Click"
        };

        foreach (var (content, clickHandler) in expectedButtons)
        {
            var button = document
                .Descendants()
                .SingleOrDefault(element => element.Name.LocalName == "Button" &&
                    GetAttribute(element, "Content") == content &&
                    GetAttribute(element, "Click") == clickHandler);

            Assert.NotNull(button);
        }

        Assert.Equal("ExplorerUrlTextBox", GetElementName(FindNamedElement(document, "ExplorerUrlTextBox")));
        Assert.Equal("ExplorerUrlTextBox_KeyDown", GetAttribute(FindNamedElement(document, "ExplorerUrlTextBox"), "KeyDown"));
    }

    [Fact]
    public void ExplorerPanel_ProvidesAppFavoriteActions()
    {
        var document = LoadExplorerPanelDocument();

        Assert.NotNull(FindNamedElement(document, "FavoriteNameTextBox"));
        Assert.NotNull(FindNamedElement(document, "FavoriteComboBox"));
        Assert.NotNull(FindButton(document, "현재 URL 추가", "AddFavoriteButton_Click"));
        Assert.NotNull(FindButton(document, "선택 슬롯에 넣기", "UseFavoriteButton_Click"));
    }

    [Fact]
    public void ExplorerPanel_ExposesCurrentUrlDragSource()
    {
        var document = LoadExplorerPanelDocument();
        var dragSource = FindNamedElement(document, "ExplorerDragSource");

        Assert.Equal("Hand", GetAttribute(dragSource, "Cursor"));
        Assert.Equal("ExplorerDragSource_PreviewMouseLeftButtonDown", GetAttribute(dragSource, "PreviewMouseLeftButtonDown"));
        Assert.Equal("ExplorerDragSource_MouseMove", GetAttribute(dragSource, "MouseMove"));
    }

    private static XDocument LoadExplorerPanelDocument()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "ExplorerPanel.xaml"));

        return XDocument.Load(path);
    }

    private static XElement FindNamedElement(XDocument document, string name)
    {
        return document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value == name));
    }

    private static XElement? FindButton(XDocument document, string content, string clickHandler)
    {
        return document
            .Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "Button" &&
                GetAttribute(element, "Content") == content &&
                GetAttribute(element, "Click") == clickHandler);
    }

    private static string? GetElementName(XElement element)
    {
        return GetAttribute(element, "Name");
    }

    private static string? GetAttribute(XElement element, string name)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name)
            ?.Value;
    }
}
