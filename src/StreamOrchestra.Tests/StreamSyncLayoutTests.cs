using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class StreamSyncLayoutTests
{
    [Fact]
    public void MainWindow_ProvidesToolbarSyncButtonAndControlPopup()
    {
        var document = XDocument.Load(GetAppPath("MainWindow.xaml"));

        Assert.Equal("Button", FindByName(document, "SyncButton").Name.LocalName);
        Assert.Equal("SyncButton_Click", Attribute(FindByName(document, "SyncButton"), "Click"));
        Assert.Equal("Popup", FindByName(document, "SyncPopup").Name.LocalName);
        Assert.Equal("SyncStartStopButton_Click", Attribute(FindByName(document, "SyncStartStopButton"), "Click"));
        Assert.NotNull(FindByName(document, "SyncMembersPanel"));
        Assert.NotNull(FindByName(document, "SyncAvailablePanel"));
        Assert.Equal(
            "SyncConfirmAlignmentButton_Click",
            Attribute(FindByName(document, "SyncConfirmAlignmentButton"), "Click"));
        Assert.Equal(
            "SyncExportBiasButton_Click",
            Attribute(FindByName(document, "SyncExportBiasButton"), "Click"));
        Assert.Equal(
            "SyncDeleteBiasButton_Click",
            Attribute(FindByName(document, "SyncDeleteBiasButton"), "Click"));

        var elements = document.Descendants().ToList();
        Assert.True(
            elements.IndexOf(FindByName(document, "SyncMinimumSafetyText"))
            < elements.IndexOf(FindByName(document, "SyncMinimumSafetySlider")),
            "The safety-delay text must be initialized before the slider can raise ValueChanged.");

        var code = File.ReadAllText(GetAppPath("MainWindow.Sync.cs"));
        Assert.Contains("SyncMinimumSafetyText is null", code, StringComparison.Ordinal);
        Assert.Contains("Text = member.StreamName", code, StringComparison.Ordinal);
        Assert.Contains("Text = slot.SyncDisplayName", code, StringComparison.Ordinal);
        Assert.DoesNotContain("슬롯 {member.SlotId}", code, StringComparison.Ordinal);
        Assert.Contains("자동 적용 안 됨", code, StringComparison.Ordinal);
        Assert.Contains("AlgorithmPriorMs", code, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamSlotView_UsesPopupForSyncBadgeAboveWebView()
    {
        var document = XDocument.Load(GetAppViewPath("StreamSlotView.xaml"));
        var popup = FindByName(document, "SyncStatusPopup");

        Assert.Equal("Popup", popup.Name.LocalName);
        Assert.Equal("False", Attribute(popup, "IsHitTestVisible"));
        Assert.Contains("SlotBorder", Attribute(popup, "PlacementTarget"));
        Assert.Equal("SYNC · 방송명", Attribute(FindByName(document, "SyncStatusTextBlock"), "Text"));

        var coordinatorCode = File.ReadAllText(GetAppServicePath("StreamSyncCoordinator.cs"));
        Assert.Contains("SYNC · {streamName}", coordinatorCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SYNC · {FormatError", coordinatorCode, StringComparison.Ordinal);

        var bridgeCode = File.ReadAllText(GetAppViewPath("StreamSlotView.Sync.cs"));
        Assert.Contains("streamName: identity.name", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("SyncDisplayName", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("requestVideoFrameCallback", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("new WeakMap()", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("sourceVideo !== selectedVideo", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("playerEventSequence: state.eventSequence", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("frameAgeMilliseconds", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("getVideoPlaybackQuality", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("[\"waiting\", \"stalled\", \"error\", \"seeking\", \"seeked\", \"ratechange\"]", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("stream-sync-command-result", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("seeked-timeout", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("WebResourceResponseReduced", bridgeCode, StringComparison.Ordinal);
    }

    private static XElement FindByName(XDocument document, string name)
    {
        return document.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == name));
    }

    private static string? Attribute(XElement element, string name)
    {
        return element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;
    }

    private static string GetAppPath(string name) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "StreamOrchestra.App", name));

    private static string GetAppViewPath(string name) => Path.Combine(GetAppPath("Views"), name);

    private static string GetAppServicePath(string name) => Path.Combine(GetAppPath("Services"), name);
}
