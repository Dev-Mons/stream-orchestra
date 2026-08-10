using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class RecordingWindowLayoutTests
{
    [Fact]
    public void RecordingWorkspace_ProvidesQueueSelectionAndFriendlyDetailView()
    {
        var document = LoadDocument("RecordingWindow.xaml");

        Assert.NotNull(FindByName(document, "RecordingListBox"));
        Assert.NotNull(FindByName(document, "RecordingSearchTextBox"));
        Assert.NotNull(FindByName(document, "SelectedDetailHost"));
        Assert.NotNull(FindByName(document, "EmptyDetailPanel"));
        Assert.Equal("방송 추가", Attribute(FindByName(document, "AddRecordingButton"), "Content"));
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && Attribute(element, "Text") == "최근 활동");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Button" && Attribute(element, "Content") == "{Binding PrimaryActionLabel}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Button" && Attribute(element, "Content") == "방송 제거");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Button" && Attribute(element, "Content") == "저장 위치 변경");
    }

    [Fact]
    public void RecordingWorkspace_RemovesDeveloperConsoleAndCredentialFormFromMainScreen()
    {
        var document = LoadDocument("RecordingWindow.xaml");

        Assert.Null(FindByNameOrDefault(document, "LogTextBox"));
        Assert.Null(FindByNameOrDefault(document, "UsernameTextBox"));
        Assert.Null(FindByNameOrDefault(document, "PasswordBox"));
        Assert.Null(FindByNameOrDefault(document, "StreamUrlTextBox"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && Attribute(element, "FontFamily") == "Consolas");
    }

    [Fact]
    public void AddRecordingDialog_KeepsAdvancedInputsInGuidedFlow()
    {
        var document = LoadDocument("AddRecordingDialog.xaml");

        Assert.NotNull(FindByName(document, "StreamUrlTextBox"));
        Assert.Null(FindByNameOrDefault(document, "OutputFolderTextBox"));
        Assert.NotNull(FindByName(document, "QualityComboBox"));
        Assert.NotNull(FindByName(document, "SubscriberRecordingCheckBox"));
        Assert.NotNull(FindByName(document, "CredentialsPanel"));
        Assert.Equal("Collapsed", Attribute(FindByName(document, "CredentialsPanel"), "Visibility"));
    }

    [Fact]
    public void CodeBehind_UsesIndependentSessionPerRecordingAndFiveSlotLimit()
    {
        var codeBehind = File.ReadAllText(GetViewPath("RecordingWindow.xaml.cs"));
        var sessionCode = File.ReadAllText(GetModelPath("RecordingSessionViewModel.cs"));

        Assert.Contains("MaxConcurrentRecordings = 5", codeBehind);
        Assert.Contains("ObservableCollection<RecordingSessionViewModel>", codeBehind);
        Assert.Contains("new RecordingSessionViewModel(", codeBehind);
        Assert.Contains("_recordingService = new SoopRecordingService();", sessionCode);
        Assert.Contains("recording.RequestStop()", codeBehind);
        Assert.Contains("Task.WhenAll(_recordingTasks.Values.ToArray())", codeBehind);
        Assert.Contains("RecordingCatalogStorageService", codeBehind);
        Assert.Contains("SoopStreamMetadataService", codeBehind);
        Assert.Contains("SaveCatalog()", codeBehind);
    }

    private static XDocument LoadDocument(string fileName) => XDocument.Load(GetViewPath(fileName));

    private static string GetViewPath(string fileName) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "StreamOrchestra.App",
        "Views",
        fileName));

    private static string GetModelPath(string fileName) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "StreamOrchestra.App",
        "Models",
        fileName));

    private static XElement FindByName(XDocument document, string name) =>
        document.Descendants().Single(element => Attribute(element, "Name") == name);

    private static XElement? FindByNameOrDefault(XDocument document, string name) =>
        document.Descendants().SingleOrDefault(element => Attribute(element, "Name") == name);

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;
}
