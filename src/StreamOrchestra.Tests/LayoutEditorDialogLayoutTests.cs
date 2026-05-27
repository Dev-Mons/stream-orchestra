using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class LayoutEditorDialogLayoutTests
{
    [Fact]
    public void LayoutEditorDialog_ProvidesTemplateAndCustomEditingSurfaces()
    {
        var document = LoadLayoutEditorDialogDocument();
        var tabHeaders = document
            .Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .Select(element => GetAttribute(element, "Header"))
            .ToArray();

        Assert.Equal(["템플릿", "사용자 지정"], tabHeaders);
        Assert.NotNull(FindElementByName(document, "TemplateListPanel"));
        Assert.NotNull(FindElementByName(document, "TemplatePreviewHost"));
        Assert.NotNull(FindElementByName(document, "CustomLayoutListBox"));
        Assert.NotNull(FindElementByName(document, "SplitEditorHost"));
        Assert.Equal("VerticalSplitButton_Click", GetAttribute(FindElementByName(document, "VerticalSplitButton"), "Click"));
        Assert.Equal("HorizontalSplitButton_Click", GetAttribute(FindElementByName(document, "HorizontalSplitButton"), "Click"));
        Assert.Equal("RemoveSelectedSlotButton_Click", GetAttribute(FindElementByName(document, "RemoveSelectedSlotButton"), "Click"));
        Assert.Equal("CopyTemplateToCustomButton_Click", GetAttribute(FindButton(document, "사용자 지정으로 복사"), "Click"));
        Assert.Equal("NewCustomLayoutButton_Click", GetAttribute(FindButton(document, "새 레이아웃"), "Click"));
        Assert.Equal("SaveCustomLayoutButton_Click", GetAttribute(FindButton(document, "저장"), "Click"));
        Assert.Equal("DeleteCustomLayoutButton_Click", GetAttribute(FindButton(document, "삭제"), "Click"));
    }

    [Fact]
    public void CodeBehind_SavesCustomLayoutsAndSplitsSelectedLeafNodes()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "LayoutEditorDialog.xaml.cs"));
        var text = File.ReadAllText(path);

        Assert.Contains("SaveCustomLayouts", text);
        Assert.Contains("SplitSelectedLeaf", text);
        Assert.Contains("SplitAxis.Vertical", text);
        Assert.Contains("SplitAxis.Horizontal", text);
        Assert.Contains("NormalizeSlotIdsFromVisualOrder", text);
        Assert.Contains("TryCreateSplitTreeFromLayout", text);
        Assert.Contains("RemoveSelectedLeaf", text);
        Assert.Contains("TryCollapseParentToSibling", text);
        Assert.Contains("CopyNode", text);
        Assert.Contains("CreateCustomLayoutId", text);
    }

    private static XDocument LoadLayoutEditorDialogDocument()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "StreamOrchestra.App",
            "Views",
            "LayoutEditorDialog.xaml"));

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
