using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class StreamSlotViewLayoutTests
{
    [Fact]
    public void StreamSlotView_KeepsInteractiveChromeOutsideBrowserContentRow()
    {
        var document = LoadStreamSlotViewDocument();

        var browser = FindElementByName(document, "Browser");
        var browserContentGrid = browser.Parent;

        Assert.NotNull(browserContentGrid);
        Assert.Empty(browserContentGrid!
            .Descendants()
            .Where(element => element.Name.LocalName is "Button" or "TextBox" or "ContextMenu" or "MenuItem"));
    }

    [Fact]
    public void StreamSlotView_RemovesSlotChromeControls()
    {
        var document = LoadStreamSlotViewDocument();

        Assert.DoesNotContain(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value is "SlotChrome" or "ControlBar" or "SlotUrlEditor" or "SlotTitleTextBlock" or "GroupTextBlock" or "MuteButton" or "MenuButton"));
        Assert.Empty(document
            .Descendants()
            .Where(element => element.Name.LocalName is "Button" or "TextBox" or "ContextMenu" or "MenuItem"));
    }

    [Fact]
    public void StreamSlotView_ProvidesSlotSelectionWheelMuteAndDropTarget()
    {
        var document = LoadStreamSlotViewDocument();
        var slotBorder = FindElementByName(document, "SlotBorder");

        Assert.Equal("SlotBorder_PreviewMouseLeftButtonDown", GetAttribute(slotBorder, "PreviewMouseLeftButtonDown"));
        Assert.Equal("SlotBorder_PreviewMouseWheel", GetAttribute(slotBorder, "PreviewMouseWheel"));
        Assert.Equal("True", GetAttribute(slotBorder, "AllowDrop"));
        Assert.Equal("SlotBorder_DragOver", GetAttribute(slotBorder, "DragOver"));
        Assert.Equal("SlotBorder_Drop", GetAttribute(slotBorder, "Drop"));
        Assert.Equal("True", GetAttribute(FindElementByName(document, "Browser"), "AllowExternalDrop"));
        Assert.Equal("SlotBorder_DragOver", GetAttribute(FindElementByName(document, "Browser"), "DragOver"));
        Assert.Equal("SlotBorder_Drop", GetAttribute(FindElementByName(document, "Browser"), "Drop"));
        Assert.Equal("SlotBorder_PreviewMouseLeftButtonDown", GetAttribute(FindElementByName(document, "Browser"), "PreviewMouseLeftButtonDown"));
        Assert.Equal("SlotBorder_PreviewMouseWheel", GetAttribute(FindElementByName(document, "Browser"), "PreviewMouseWheel"));
    }

    [Fact]
    public void StreamSlotView_ShowsVolumeIndicatorInPopupAboveBrowser()
    {
        var document = LoadStreamSlotViewDocument();
        var popup = FindElementByName(document, "VolumeIndicatorPopup");
        var indicator = FindElementByName(document, "VolumeIndicatorOverlay");

        Assert.Equal("Popup", popup.Name.LocalName);
        Assert.Equal("False", GetAttribute(popup, "IsOpen"));
        Assert.Equal("Center", GetAttribute(popup, "Placement"));
        Assert.Contains("SlotBorder", GetAttribute(popup, "PlacementTarget"));
        Assert.Equal("False", GetAttribute(popup, "IsHitTestVisible"));
        Assert.Equal("Border", indicator.Name.LocalName);
    }

    [Fact]
    public void CodeBehind_AcceptsExplorerUrlDropsOnSlots()
    {
        var slotPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "StreamSlotView.xaml.cs"));
        var dropReaderPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "StreamDropDataReader.cs"));
        var slotText = File.ReadAllText(slotPath);
        var dropReaderText = File.ReadAllText(dropReaderPath);

        Assert.Contains("StreamUrlDropRequested?.Invoke", slotText);
        Assert.Contains("StreamDropDataReader.TryGetDroppedStream", slotText);
        Assert.Contains("CoreWebView2_WebMessageReceived", slotText);
        Assert.Contains("stream-drop", slotText);
        Assert.Contains("slot-wheel", slotText);
        Assert.Contains("StreamDragDataFormats.StreamUrl", dropReaderText);
        Assert.Contains("DataFormats.UnicodeText", dropReaderText);
        Assert.Contains("PlainTextUrlPattern", dropReaderText);
        Assert.Contains("DragDropEffects.Copy", slotText);
    }

    [Theory]
    [InlineData(100, -120, 100)]
    [InlineData(100, 120, 90)]
    [InlineData(50, -120, 60)]
    [InlineData(50, 120, 40)]
    [InlineData(0, 120, 0)]
    public void CodeBehind_CalculatesWheelVolumeInTenPercentSteps(
        int currentVolumePercent,
        double deltaY,
        int expectedVolumePercent)
    {
        var method = typeof(StreamOrchestra.App.Views.StreamSlotView).GetMethod(
            "CalculateWheelVolumePercent",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expectedVolumePercent, method!.Invoke(null, [currentVolumePercent, deltaY]));
    }

    [Fact]
    public void StreamSlotView_DefaultsVolumeToOneHundredPercent()
    {
        var xaml = File.ReadAllText(GetAppViewPath("StreamSlotView.xaml"));
        var codeBehind = File.ReadAllText(GetAppViewPath("StreamSlotView.xaml.cs"));

        Assert.Contains("Text=\"100%\"", xaml);
        Assert.Contains("private const int InitialVolumePercent = 100;", codeBehind);
        Assert.Contains("_volumePercent = InitialVolumePercent", codeBehind);
        Assert.Contains("window.__streamOrchestraVolumePercent = 100;", codeBehind);
    }

    [Fact]
    public void CodeBehind_ProvidesBestEffortQualityControlAutomation()
    {
        var path = GetAppViewPath("StreamSlotView.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("ApplyQualityAsync", text);
        Assert.Contains(".quality_box", text);
        Assert.Contains("ul button", text);
        Assert.Contains("button.click();", text);
        Assert.Contains("\"q1440\"", text);
        Assert.Contains("\"original\"", text);
        Assert.Contains("\"hd4k\"", text);
        Assert.Contains("\"sd\"", text);
    }

    private static XDocument LoadStreamSlotViewDocument()
    {
        return XDocument.Load(GetAppViewPath("StreamSlotView.xaml"));
    }

    private static string GetAppViewPath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            fileName));
    }

    private static XElement FindElementByName(XDocument document, string name)
    {
        return document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value == name));
    }

    private static string? GetAttribute(XElement element, string name)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name)
            ?.Value;
    }

}
