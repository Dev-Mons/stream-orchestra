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
            .Where(element => element.Name.LocalName is "TextBox" or "ContextMenu" or "MenuItem"));
        // Ctrl 제거 버튼(RemoveSlotButton)을 제외하면 슬롯 내부에 다른 Button은 없다.
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            GetAttribute(element, "Name") != "RemoveSlotButton");
    }

    [Fact]
    public void StreamSlotView_ProvidesCtrlRevealedRemoveButtonAtBottomRightPopup()
    {
        var document = LoadStreamSlotViewDocument();
        var popup = FindElementByName(document, "RemoveSlotPopup");
        var button = FindElementByName(document, "RemoveSlotButton");

        Assert.Equal("Popup", popup.Name.LocalName);
        Assert.Equal("False", GetAttribute(popup, "IsOpen"));
        Assert.Equal("Relative", GetAttribute(popup, "Placement"));
        Assert.Contains("SlotBorder", GetAttribute(popup, "PlacementTarget"));
        Assert.Equal("Button", button.Name.LocalName);
        Assert.Equal("RemoveSlotButton_Click", GetAttribute(button, "Click"));

        // 버튼 내용은 delete-icon.png 이미지다.
        var icon = button.Descendants().Single(element => element.Name.LocalName == "Image");
        // Views/ 하위 XAML이라 앱 루트 기준 절대 리소스 경로(/Assets/...)여야 한다.
        Assert.Equal("/Assets/delete-icon.png", GetAttribute(icon, "Source"));

        var codeBehind = File.ReadAllText(GetAppViewPath("StreamSlotView.xaml.cs"));
        Assert.Contains("public void SetRemoveModeActive(bool isActive, bool isSelectedForRemoval = false)", codeBehind);
        Assert.Contains("SetRemoveButtonSelected(isSelectedForRemoval)", codeBehind);
        Assert.Contains("RemoveSlotRequested?.Invoke", codeBehind);
        Assert.DoesNotContain("RemoveSlotPopup.IsOpen = false;", codeBehind);
        Assert.Contains("CtrlStateChanged?.Invoke", codeBehind);
        Assert.Contains("ctrl-state", codeBehind);
        // 제거 버튼은 슬롯 오른쪽 하단에 배치된다.
        Assert.Contains("PositionRemoveButtonAtBottomRight", codeBehind);
        Assert.Contains("RemoveSlotPopup.HorizontalOffset", codeBehind);
        Assert.Contains("RemoveSlotPopup.VerticalOffset", codeBehind);
    }

    [Fact]
    public void StreamSlotView_ProvidesSlotSelectionWheelMuteAndDropTarget()
    {
        var document = LoadStreamSlotViewDocument();
        var slotBorder = FindElementByName(document, "SlotBorder");

        Assert.Equal("SlotBorder_PreviewMouseLeftButtonDown", GetAttribute(slotBorder, "PreviewMouseLeftButtonDown"));
        Assert.Equal("SlotBorder_MouseMove", GetAttribute(slotBorder, "MouseMove"));
        Assert.Equal("SlotBorder_PreviewMouseWheel", GetAttribute(slotBorder, "PreviewMouseWheel"));
        Assert.Equal("True", GetAttribute(slotBorder, "AllowDrop"));
        Assert.Equal("SlotBorder_DragOver", GetAttribute(slotBorder, "DragOver"));
        Assert.Equal("SlotBorder_Drop", GetAttribute(slotBorder, "Drop"));
        var browser = FindElementByName(document, "Browser");
        Assert.Equal("True", GetAttribute(browser, "AllowExternalDrop"));
        Assert.Null(GetAttribute(browser, "AllowDrop"));
        Assert.Null(GetAttribute(browser, "DragOver"));
        Assert.Null(GetAttribute(browser, "Drop"));
        Assert.Equal("SlotBorder_PreviewMouseLeftButtonDown", GetAttribute(browser, "PreviewMouseLeftButtonDown"));
        Assert.Equal("SlotBorder_MouseMove", GetAttribute(browser, "MouseMove"));
        Assert.Equal("SlotBorder_PreviewMouseWheel", GetAttribute(browser, "PreviewMouseWheel"));
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
        Assert.Contains("HostDragStarted?.Invoke", slotText);
        Assert.Contains("HostDragCompleted?.Invoke", slotText);
        Assert.Contains("SlotSwapRequested?.Invoke", slotText);
        Assert.Contains("StreamDropDataReader.TryGetDroppedStream", slotText);
        Assert.Contains("CoreWebView2_WebMessageReceived", slotText);
        Assert.Contains("stream-drop", slotText);
        Assert.Contains("slot-wheel", slotText);
        Assert.Contains("StreamDragDataFormats.StreamUrl", dropReaderText);
        Assert.Contains("DataFormats.UnicodeText", dropReaderText);
        Assert.Contains("PlainTextUrlPattern", dropReaderText);
        Assert.Contains("DragDropEffects.Copy", slotText);
        // 동적 도킹 잔재가 제거되었다.
        Assert.DoesNotContain("RemoveFromLayoutRequested", slotText);
        Assert.DoesNotContain("StreamDockDropRequested", slotText);
        Assert.DoesNotContain("DockPreviewRequested", slotText);
        Assert.DoesNotContain("CalculateDockDirection", slotText);
    }

    [Fact]
    public void CodeBehind_ReconcilesSwapOverlayWhenSwapDragCompletes()
    {
        var slotText = File.ReadAllText(GetAppViewPath("StreamSlotView.xaml.cs"));
        var mainWindowText = File.ReadAllText(GetMainWindowPath());

        Assert.Contains("public event Action? SwapDragStarted;", slotText);
        Assert.Contains("public event Action? SwapDragCompleted;", slotText);
        Assert.Contains("SwapDragStarted?.Invoke();", slotText);
        Assert.Contains("SwapDragCompleted?.Invoke();", slotText);
        Assert.Contains("try", slotText);
        Assert.Contains("finally", slotText);
        Assert.Contains("slotView.SwapDragCompleted += ReconcileSwapModeWithKeyboardState;", mainWindowText);
        Assert.Contains("private void ReconcileSwapModeWithKeyboardState()", mainWindowText);
        Assert.Contains("IsLeftShiftPhysicallyDown()", mainWindowText);
    }

    [Fact]
    public void CodeBehind_PollsPhysicalShiftStateWhileSwapOverlayIsOpen()
    {
        var mainWindowText = File.ReadAllText(GetMainWindowPath());

        Assert.Contains("private readonly DispatcherTimer _swapModeKeyboardPollTimer;", mainWindowText);
        Assert.Contains("_swapModeKeyboardPollTimer = CreateSwapModeKeyboardPollTimer();", mainWindowText);
        Assert.Contains("private DispatcherTimer CreateSwapModeKeyboardPollTimer()", mainWindowText);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(50)", mainWindowText);
        Assert.Contains("Tick += (_, _) => ReconcileSwapModeWithKeyboardState();", mainWindowText);
        Assert.Contains("_swapModeKeyboardPollTimer.Start();", mainWindowText);
        Assert.Contains("_swapModeKeyboardPollTimer.Stop();", mainWindowText);
        Assert.Contains("GetAsyncKeyState(LeftShiftVirtualKey)", mainWindowText);
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

    [Fact]
    public void CodeBehind_UsesKnownSoopEmbedSelectorsFromBngtsExtension()
    {
        var path = GetAppViewPath("StreamSlotView.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains(".embeded_mode #webplayer.chat_open #chatting_area", text);
        Assert.Contains(".embeded_mode #webplayer #player div.quality_box", text);
        Assert.Contains("#webplayer", text);
        Assert.Contains("#player", text);
        Assert.Contains("endsWith(\".sooplive.com\")", text);
    }

    [Fact]
    public void CodeBehind_UsesSoopNativePlayerModesWithoutCustomFullscreenFallback()
    {
        var path = GetAppViewPath("StreamSlotView.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("requestSoopFullscreenViewport", text);
        Assert.Contains("fullscreenchange", text);
        Assert.Contains("addEventListener(\"play\"", text);
        Assert.DoesNotContain("stream-orchestra-soop-fullscreen", text);
        Assert.DoesNotContain("findLargestVisibleVideoAncestor", text);
        Assert.DoesNotContain("hideNonPlayerSiblings", text);
    }

    [Fact]
    public void CodeBehind_ClicksKnownSoopScreenAndFullscreenButtons()
    {
        var path = GetAppViewPath("StreamSlotView.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains(".btn_screen_mode", text);
        Assert.Contains(".btn_fullScreen_mode", text);
        Assert.Contains("clickScreenModeButton", text);
    }

    [Fact]
    public void CodeBehind_StylesKnownSoopScreenAndFullscreenModes()
    {
        var path = GetAppViewPath("StreamSlotView.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("body.screen_mode #webplayer #webplayer_contents", text);
        Assert.Contains("body.fullScreen_mode #webplayer #webplayer_contents", text);
        Assert.Contains("body.fullScreen_mode #webplayer #webplayer_contents .wrapping.side", text);
    }

    [Fact]
    public void CodeBehind_RetriesSoopFullscreenUntilLatePlayerControlsExist()
    {
        var path = GetAppViewPath("StreamSlotView.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("scheduleSoopFullscreenRetry", text);
        Assert.Contains("soopFullscreenRetryCount", text);
        Assert.Contains("setTimeout", text);
        Assert.Contains("isSoopPlaybackModeActive", text);
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

    private static string GetMainWindowPath()
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

    private static string? GetAttribute(XElement element, string name)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name)
            ?.Value;
    }

}
