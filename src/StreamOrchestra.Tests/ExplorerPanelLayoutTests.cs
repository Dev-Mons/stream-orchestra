using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class ExplorerPanelLayoutTests
{
    [Fact]
    public void ExplorerPanel_ProvidesNavigationActions()
    {
        var document = LoadExplorerPanelDocument();
        // 버튼은 텍스트 대신 아이콘 이미지로 표시된다(Click 핸들러 → 아이콘 리소스 경로).
        var expectedButtons = new Dictionary<string, string>
        {
            ["GoButton_Click"] = "/Assets/arrow-forward.png",
            ["BackButton_Click"] = "/Assets/arrow-back.png",
            ["RefreshButton_Click"] = "/Assets/refresh.png"
        };

        foreach (var (clickHandler, iconSource) in expectedButtons)
        {
            var button = document
                .Descendants()
                .SingleOrDefault(element => element.Name.LocalName == "Button" &&
                    GetAttribute(element, "Click") == clickHandler);

            Assert.NotNull(button);

            var icon = button!.Descendants().Single(element => element.Name.LocalName == "Image");
            Assert.Equal(iconSource, GetAttribute(icon, "Source"));
        }

        Assert.Equal("ExplorerUrlTextBox", GetElementName(FindNamedElement(document, "ExplorerUrlTextBox")));
        Assert.Equal("ExplorerUrlTextBox_KeyDown", GetAttribute(FindNamedElement(document, "ExplorerUrlTextBox"), "KeyDown"));
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

    [Fact]
    public void ExplorerPanel_HeaderDoesNotShowDragArrowGlyph()
    {
        var document = LoadExplorerPanelDocument();

        var arrowGlyph = document
            .Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "TextBlock" &&
                GetAttribute(element, "Text") == "↗");

        Assert.Null(arrowGlyph);
    }

    [Fact]
    public void CodeBehind_AddsSoopLinkUrlsToDragData()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "ExplorerPanel.xaml.cs"));
        var text = File.ReadAllText(path);

        Assert.Contains("CreateLinkDragScript", text);
        Assert.Contains("a[href]", text);
        Assert.Contains("begin-host-drag", text);
        Assert.Contains("DragDrop.DoDragDrop", text);
        Assert.Contains("HostDragStarted?.Invoke", text);
        Assert.Contains("HostDragCompleted?.Invoke", text);
        Assert.Contains("StreamDragDataFormats.StreamUrl", text);
        Assert.Contains("DataFormats.UnicodeText", text);
        Assert.Contains("NewWindowRequested", text);
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
