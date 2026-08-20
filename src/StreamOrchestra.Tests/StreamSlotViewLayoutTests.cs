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
        // 키 상태는 일반화된 KeyStateChanged(가상 키 코드) 이벤트로 호스트에 전달된다.
        Assert.Contains("KeyStateChanged?.Invoke", codeBehind);
        Assert.Contains("shortcut-key", codeBehind);
        // 편집 가능한 요소(채팅 입력 등) 포커스 시에는 임의 키 단축키를 보고하지 않는다.
        Assert.Contains("isEditableTarget", codeBehind);
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
        Assert.Contains("IsKeyPhysicallyDown(_shortcutSettings.SwapKey.VirtualKey)", mainWindowText);
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
        Assert.Contains("GetAsyncKeyState(virtualKey)", mainWindowText);
    }

    [Theory]
    [InlineData(100, -120, false, 100)]
    [InlineData(100, 120, false, 90)]
    [InlineData(50, -120, false, 60)]
    [InlineData(50, 120, false, 40)]
    [InlineData(0, 120, false, 0)]
    [InlineData(50, -120, true, 55)]
    [InlineData(50, 120, true, 45)]
    public void CodeBehind_CalculatesWheelVolumeInNormalAndFineSteps(
        int currentVolumePercent,
        double deltaY,
        bool ctrlKey,
        int expectedVolumePercent)
    {
        var method = typeof(StreamOrchestra.App.Views.StreamSlotView).GetMethod(
            "CalculateWheelVolumePercent",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expectedVolumePercent, method!.Invoke(null, [currentVolumePercent, deltaY, ctrlKey]));
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
    public void CodeBehind_NotifiesHostWhenPlaybackOrVolumeStateChanges()
    {
        var codeBehind = File.ReadAllText(GetAppViewPath("StreamSlotView.xaml.cs"));

        Assert.Contains("public event Action<StreamSlotView>? PlaybackStateChanged;", codeBehind);
        Assert.Contains("public event Action<StreamSlotView>? VolumeChanged;", codeBehind);
        Assert.Contains("PlaybackStateChanged?.Invoke(this);", codeBehind);
        Assert.Contains("VolumeChanged?.Invoke(this);", codeBehind);
    }

    [Fact]
    public void CodeBehind_AddsCopyAddressCommandToBrowserContextMenu()
    {
        var codeBehind = File.ReadAllText(GetAppViewPath("StreamSlotView.xaml.cs"));

        Assert.Contains("coreWebView.ContextMenuRequested += CoreWebView2_ContextMenuRequested;", codeBehind);
        Assert.Contains("coreWebView.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;", codeBehind);
        Assert.Contains("\"주소 복사하기\"", codeBehind);
        Assert.Contains("Clipboard.SetText(address);", codeBehind);
    }

    [Theory]
    [InlineData("https://play.sooplive.com/channel", true)]
    [InlineData("http://example.com/stream", true)]
    [InlineData("about:blank", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void CodeBehind_CopyAddressCommandAcceptsOnlyWebAddresses(string address, bool expected)
    {
        var method = typeof(StreamOrchestra.App.Views.StreamSlotView).GetMethod(
            "IsCopyableAddress",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, [address]));
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
    public void CodeBehind_HidesSoopBroadcasterProfileInsidePlaybackViewport()
    {
        var path = GetAppViewPath("StreamSlotView.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("#webplayer #player_info", text);
        Assert.Contains("\"#player_info\"", text);
        Assert.Contains("\"#serviceHeader\"", text);
        Assert.Contains("\"#serviceLnb\"", text);
    }

    [Fact]
    public void CodeBehind_MeasuresAndEnforcesSoopPlaybackViewport()
    {
        var text = GetPlaybackViewportScript();

        Assert.Contains("requestSoopFullscreenViewport", text);
        Assert.Contains("isSoopViewportSatisfied", text);
        Assert.Contains("getSoopViewportRoot", text);
        Assert.Contains("getBoundingClientRect()", text);
        Assert.Contains("videoFillsViewport", text);
        Assert.Contains("videoUsesCenteredContain", text);
        Assert.Contains("hasVisiblePageChrome()", text);
        Assert.Contains("applyKnownSoopViewportFallback", text);
        Assert.Contains("stream-orchestra-immersive-mode", text);
        Assert.Contains("playerRoot.classList.add(\"stream-orchestra-viewport-root\")", text);
        Assert.Contains("video.classList.add(\"stream-orchestra-playback-video\")", text);
        Assert.Contains("videos = Array.from(document.querySelectorAll(\"video\"));", text);
        Assert.DoesNotContain(":has(", text);
        Assert.Contains("fullscreenchange", text);
        Assert.Contains("addEventListener(\"play\"", text);
        Assert.DoesNotContain("findLargestVisibleVideoAncestor", text);
        Assert.DoesNotContain("hideNonPlayerSiblings", text);

        var resolverStart = text.IndexOf("const getSoopViewportRoot", StringComparison.Ordinal);
        Assert.True(resolverStart >= 0);
        var resolverEnd = text.IndexOf("const isSoopViewportSatisfied", resolverStart, StringComparison.Ordinal);
        Assert.True(resolverEnd > resolverStart);
        var resolver = text[resolverStart..resolverEnd];
        Assert.True(
            resolver.IndexOf("#webplayer", StringComparison.Ordinal) <
            resolver.IndexOf("#player_area", StringComparison.Ordinal));
        Assert.Contains("video?.parentElement", resolver);

        var geometryStart = text.IndexOf("const fillsViewport", StringComparison.Ordinal);
        Assert.True(geometryStart >= 0);
        var geometryEnd = text.IndexOf("const objectPosition", geometryStart, StringComparison.Ordinal);
        Assert.True(geometryEnd > geometryStart);
        var geometry = text[geometryStart..geometryEnd];
        Assert.Contains("Math.abs(candidateRect.width - viewportWidth)", geometry);
        Assert.Contains("Math.abs(candidateRect.height - viewportHeight)", geometry);
        Assert.Contains("const videoFillsViewport = fillsViewport(videoRect);", geometry);
        Assert.DoesNotContain("||", geometry);
    }

    [Fact]
    public void CodeBehind_ClicksKnownSoopScreenAndFullscreenButtons()
    {
        var text = GetPlaybackViewportScript();

        Assert.Contains(".btn_screen_mode", text);
        Assert.Contains(".btn_fullScreen_mode", text);
        Assert.Contains("clickScreenModeButton", text);
        Assert.Contains("#player .btn_screen_mode", text);
        Assert.Contains("isVisibleElement", text);
        Assert.Contains("nativeScreenModeRequestedAt", text);
        Assert.Contains("Date.now() - nativeScreenModeRequestedAt < 1500", text);
        Assert.Contains("screenModeRequest === \"timed-out\"", text);
        Assert.Contains("document.body?.classList.contains(\"fullScreen_mode\")", text);

        var requestStart = text.IndexOf("const requestSoopFullscreenViewport", StringComparison.Ordinal);
        Assert.True(requestStart >= 0);
        var requestEnd = text.IndexOf("const scheduleSoopFullscreenRetry", requestStart, StringComparison.Ordinal);
        Assert.True(requestEnd > requestStart);
        var request = text[requestStart..requestEnd];
        Assert.True(
            request.IndexOf("if (isSoopViewportSatisfied())", StringComparison.Ordinal) <
            request.IndexOf("clickScreenModeButton(video)", StringComparison.Ordinal));
        Assert.True(
            request.IndexOf("if (waitingForNativeMode)", StringComparison.Ordinal) <
            request.IndexOf("applyKnownSoopViewportFallback()", StringComparison.Ordinal));
    }

    [Fact]
    public void CodeBehind_StylesKnownSoopScreenAndFullscreenModes()
    {
        var text = GetPlaybackViewportScript();

        Assert.Contains("body.stream-orchestra-immersive-mode .stream-orchestra-viewport-root", text);
        Assert.Contains("z-index: 2147483000", text);
        Assert.Contains(".stream-orchestra-playback-video", text);
        Assert.Contains(".stream-orchestra-player-layer", text);
        Assert.Contains(".htmlplayer_wrap", text);
        Assert.Contains(".htmlplayer_content", text);
        Assert.DoesNotContain(".stream-orchestra-control-layer", text);
        Assert.DoesNotContain(".float_box", text);
        Assert.Contains("max-width: none !important", text);
        Assert.Contains("object-position: center center !important", text);
        Assert.Contains("body.screen_mode #webplayer #webplayer_contents", text);
        Assert.Contains("body.fullScreen_mode #webplayer #webplayer_contents", text);
        Assert.Contains("body.fullScreen_mode #webplayer #webplayer_contents .wrapping.side", text);
    }

    [Fact]
    public void CodeBehind_RetriesSoopFullscreenUntilLatePlayerControlsExist()
    {
        var text = GetPlaybackViewportScript();

        Assert.Contains("scheduleSoopFullscreenRetry", text);
        Assert.Contains("soopFullscreenRetryCount", text);
        Assert.Contains("setTimeout", text);
        Assert.Contains("isSoopPlaybackModeActive", text);
        Assert.Contains("const retryDelay = soopFullscreenRetryCount < 120 ? 250 : 2000;", text);
        Assert.DoesNotContain("soopFullscreenRetryCount >= 120", text);
        Assert.Contains("attachSoopObserver", text);
        Assert.Contains("observer.observe(document.body, { attributes: true", text);
        Assert.Contains("mutationTouchesSoopPlayback", text);
        Assert.Contains("...hideSelectors", text);
        Assert.DoesNotContain("mutation.type === \"attributes\" || !soopViewportSatisfied", text);
        Assert.Contains("scheduleSoopDomRefresh", text);
        Assert.Contains("clearSoopFullscreenRetry", text);
        Assert.Contains("window.__streamOrchestraEnsurePlaybackViewport", text);
    }

    [Fact]
    public void CodeBehind_RevalidatesEveryPlaybackViewportWhenHostSizeChanges()
    {
        var script = GetPlaybackViewportScript();
        var codeBehind = File.ReadAllText(GetAppViewPath("StreamSlotView.xaml.cs"));

        Assert.Contains("const invalidatePlaybackViewport", script);
        Assert.Contains("soopViewportSatisfied = false;", script);
        Assert.Contains("window.addEventListener(\"resize\", invalidatePlaybackViewport", script);
        Assert.Contains("window.visualViewport?.addEventListener(\"resize\", invalidatePlaybackViewport", script);
        Assert.Contains("new ResizeObserver(invalidatePlaybackViewport)", script);
        Assert.Contains("let observedViewportResizeRoot = null;", script);
        Assert.Contains("viewportResizeObserver.disconnect()", script);
        Assert.Contains("viewportResizeObserver.observe(playerRoot)", script);
        Assert.DoesNotContain("observedViewportResizeElements", script);
        Assert.DoesNotContain("window.dispatchEvent(new Event(\"resize\"))", script);
        Assert.Contains("window.__streamOrchestraHandleHostResize", script);
        Assert.Contains("window.requestAnimationFrame", script);

        Assert.Contains("private readonly DispatcherTimer _playbackViewportResizeTimer;", codeBehind);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(100)", codeBehind);
        Assert.Contains("SlotBorder.SizeChanged += SlotBorder_SizeChanged;", codeBehind);
        Assert.Contains("await NotifyPlaybackViewportSizeChangedAsync();", codeBehind);
        Assert.Contains("window.__streamOrchestraHandleHostResize();", codeBehind);
    }

    [Fact]
    public void CodeBehind_RechecksSoopViewportAfterNavigationAndPlaybackReady()
    {
        var path = GetAppViewPath("StreamSlotView.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("_ = EnsurePlaybackViewportAsync();", text);
        Assert.Contains("await EnsurePlaybackViewportAsync();", text);
        Assert.Contains("typeof window.__streamOrchestraEnsurePlaybackViewport", text);
    }

    [Fact]
    public void CodeBehind_ReportsSoopPlaybackLimitWarningsToHost()
    {
        var path = GetAppViewPath("StreamSlotView.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("SoopPlaybackLimitDetected", text);
        Assert.Contains("installSoopPlaybackLimitDetector", text);
        Assert.Contains("soop-playback-limit", text);
        Assert.Contains("방송개수", text);
        Assert.Contains("동시시청", text);
        Assert.Contains("StopPlaybackForReplacementAsync", text);
        Assert.Contains("Browser.CoreWebView2.Navigate(\"about:blank\")", text);
        Assert.Contains("RunNavigationAndWaitAsync", text);
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

    private static string GetPlaybackViewportScript()
    {
        var method = typeof(StreamOrchestra.App.Views.StreamSlotView).GetMethod(
            "CreatePlaybackViewportScript",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, null));
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
