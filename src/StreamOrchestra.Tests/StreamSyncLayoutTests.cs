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
        Assert.Contains(
            "수동 지연은 즉시 위치 이동",
            Attribute(FindByName(document, "SyncControlModeText"), "Text"),
            StringComparison.Ordinal);

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
        Assert.DoesNotContain("학습 데이터", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("SyncPopup.Closed +=", code, StringComparison.Ordinal);
        Assert.Contains("Deactivated += (_, _) => SyncPopup.IsOpen = false", code, StringComparison.Ordinal);
        Assert.Contains("SetSyncBadgePresentationEnabled(true)", code, StringComparison.Ordinal);
        Assert.Contains("SyncPopup.IsOpen = shouldOpen", code, StringComparison.Ordinal);
        Assert.Contains("QueueSyncControlPopupToFront", code, StringComparison.Ordinal);
        Assert.Contains("SyncPopupHwndTopmost", code, StringComparison.Ordinal);
        Assert.Contains("SetSyncControlPopupWindowPos", code, StringComparison.Ordinal);
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
        Assert.Contains("frameAgeMilliseconds", bridgeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("getVideoPlaybackQuality", bridgeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("navigator.connection", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("[\"waiting\", \"stalled\", \"error\", \"seeking\", \"seeked\", \"ratechange\"]", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("stream-sync-command-result", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("seeked-timeout", bridgeCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Network.enable", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("_isSyncBadgePresentationEnabled", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("SetSyncBadgePresentationEnabled", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("SetPopupNotTopmost(SyncStatusPopup)", bridgeCode, StringComparison.Ordinal);
        Assert.Contains("QueueSyncBadgeZOrderCorrection", bridgeCode, StringComparison.Ordinal);

        var slotCode = File.ReadAllText(GetAppViewPath("StreamSlotView.xaml.cs"));
        Assert.Contains("HwndNotTopmost", slotCode, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rate-assigned", "rate-assigned")]
    [InlineData("seek-assigned", "seek-assigned")]
    [InlineData("pause-requested", "pause-requested")]
    [InlineData("resume-requested", "resume-requested")]
    [InlineData("reset-rate-assigned", "reset-rate-assigned")]
    [InlineData(" RATE-ASSIGNED ", "rate-assigned")]
    [InlineData("unexpected-outcome", "command-failed")]
    [InlineData(null, "command-failed")]
    public void StreamSlotView_PreservesAppliedCommandSuccessOutcomes(
        string? outcome,
        string expected)
    {
        var method = typeof(StreamOrchestra.App.Views.StreamSlotView).GetMethod(
            "NormalizeCommandOutcome",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, [outcome]));
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
