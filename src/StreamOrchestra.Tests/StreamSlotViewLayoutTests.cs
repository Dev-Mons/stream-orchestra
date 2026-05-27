using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class StreamSlotViewLayoutTests
{
    [Fact]
    public void StreamSlotView_KeepsInteractiveChromeOutsideBrowserContentRow()
    {
        var document = LoadStreamSlotViewDocument();

        var slotChrome = FindElementByName(document, "SlotChrome");
        var browser = FindElementByName(document, "Browser");
        var browserContentGrid = browser.Parent;

        Assert.NotNull(browserContentGrid);
        Assert.Equal("0", GetAttribute(slotChrome, "Grid.Row") ?? "0");
        Assert.Equal("1", GetAttribute(browserContentGrid!, "Grid.Row"));
        Assert.False(IsDescendantOf(FindElementByName(document, "ControlBar"), browserContentGrid!));
        Assert.False(IsDescendantOf(FindElementByName(document, "SlotUrlEditor"), browserContentGrid!));
        Assert.Empty(browserContentGrid!
            .Descendants()
            .Where(element => element.Name.LocalName is "Button" or "TextBox" or "ContextMenu" or "MenuItem"));
    }

    [Fact]
    public void StreamSlotView_MenuButton_ProvidesConcreteSlotActions()
    {
        var document = LoadStreamSlotViewDocument();
        var menuButton = FindElementByName(document, "MenuButton");
        var menuItems = menuButton
            .Descendants()
            .Where(element => element.Name.LocalName == "MenuItem")
            .Select(element => GetAttribute(element, "Header"))
            .ToArray();

        Assert.Equal(["Copy URL", "Clear Slot", "Load SOOP Home"], menuItems);
    }

    [Fact]
    public void StreamSlotView_ProvidesIndividualUrlLoadControls()
    {
        var document = LoadStreamSlotViewDocument();
        var urlTextBox = FindElementByName(document, "SlotUrlTextBox");
        var loadButton = FindButtonByClick(document, "LoadButton_Click");

        Assert.Equal("SlotUrlTextBox_KeyDown", GetAttribute(urlTextBox, "KeyDown"));
        Assert.Equal("Load", GetAttribute(loadButton, "Content"));
    }

    [Fact]
    public void StreamSlotView_ProvidesMuteAndRefreshControls()
    {
        var document = LoadStreamSlotViewDocument();
        var muteButton = FindElementByName(document, "MuteButton");
        var refreshButton = FindButtonByClick(document, "RefreshButton_Click");

        Assert.Equal("MuteButton_Click", GetAttribute(muteButton, "Click"));
        Assert.Equal("Refresh slot", GetAttribute(refreshButton, "ToolTip"));
    }

    [Fact]
    public void StreamSlotView_RestrictsSlotSwappingToDragHandleAndDropTarget()
    {
        var document = LoadStreamSlotViewDocument();
        var dragHandle = FindElementByName(document, "DragHandleTextBlock");
        var slotBorder = FindElementByName(document, "SlotBorder");
        var controlBar = FindElementByName(document, "ControlBar");

        Assert.Equal("DragHandleTextBlock_MouseLeftButtonDown", GetAttribute(dragHandle, "MouseLeftButtonDown"));
        Assert.Equal("DragHandleTextBlock_MouseMove", GetAttribute(dragHandle, "MouseMove"));
        Assert.Equal("SizeAll", GetAttribute(dragHandle, "Cursor"));
        Assert.Equal("True", GetAttribute(slotBorder, "AllowDrop"));
        Assert.Equal("SlotBorder_DragOver", GetAttribute(slotBorder, "DragOver"));
        Assert.Equal("SlotBorder_Drop", GetAttribute(slotBorder, "Drop"));
        Assert.Equal("False", GetAttribute(FindElementByName(document, "Browser"), "AllowExternalDrop"));
        Assert.Equal("SlotBorder_DragOver", GetAttribute(FindElementByName(document, "Browser"), "DragOver"));
        Assert.Equal("SlotBorder_Drop", GetAttribute(FindElementByName(document, "Browser"), "Drop"));
        Assert.Null(GetAttribute(controlBar, "MouseMove"));
    }

    [Fact]
    public void CodeBehind_AcceptsExplorerUrlDropsOnSlots()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "StreamSlotView.xaml.cs"));
        var text = File.ReadAllText(path);

        Assert.Contains("StreamUrlDropRequested?.Invoke", text);
        Assert.Contains("StreamDragDataFormats.StreamUrl", text);
        Assert.Contains("DataFormats.UnicodeText", text);
        Assert.Contains("DragDropEffects.Copy", text);
    }

    [Fact]
    public void CodeBehind_ProvidesBestEffortQualityControlAutomation()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "StreamSlotView.xaml.cs"));
        var text = File.ReadAllText(path);

        Assert.Contains("ApplyQualityAsync", text);
        Assert.Contains(".quality_box", text);
        Assert.Contains("ul button", text);
        Assert.Contains("button.click();", text);
        Assert.Contains("\"original\"", text);
        Assert.Contains("\"hd4k\"", text);
        Assert.Contains("\"sd\"", text);
    }

    private static XDocument LoadStreamSlotViewDocument()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "StreamSlotView.xaml"));

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

    private static XElement FindButtonByClick(XDocument document, string clickHandler)
    {
        return document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button" &&
                GetAttribute(element, "Click") == clickHandler);
    }

    private static string? GetAttribute(XElement element, string name)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name)
            ?.Value;
    }

    private static bool IsDescendantOf(XElement element, XElement ancestor)
    {
        return element
            .Ancestors()
            .Any(candidate => ReferenceEquals(candidate, ancestor));
    }
}
