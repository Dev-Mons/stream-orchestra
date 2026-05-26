using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class SavePresetDialogLayoutTests
{
    [Fact]
    public void SavePresetDialog_CollectsPresetNameBeforeSaving()
    {
        var document = LoadSavePresetDialogDocument();
        var nameTextBox = FindElementByName(document, "PresetNameTextBox");
        var saveButton = FindButton(document, "Save");

        Assert.Equal("PresetNameTextBox_KeyDown", GetAttribute(nameTextBox, "KeyDown"));
        Assert.Equal("True", GetAttribute(saveButton, "IsDefault"));
        Assert.Equal("SaveButton_Click", GetAttribute(saveButton, "Click"));
    }

    [Fact]
    public void SavePresetDialog_ProvidesNonResizingModalCancelFlow()
    {
        var document = LoadSavePresetDialogDocument();
        var window = document.Root!;
        var cancelButton = FindButton(document, "Cancel");

        Assert.Equal("Save Preset", GetAttribute(window, "Title"));
        Assert.Equal("NoResize", GetAttribute(window, "ResizeMode"));
        Assert.Equal("CenterOwner", GetAttribute(window, "WindowStartupLocation"));
        Assert.Equal("CancelButton_Click", GetAttribute(cancelButton, "Click"));
    }

    private static XDocument LoadSavePresetDialogDocument()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "SavePresetDialog.xaml"));

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
