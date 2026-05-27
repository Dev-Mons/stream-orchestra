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

        Assert.Equal(["레이아웃", "사용자 지정"], tabHeaders);
        Assert.NotNull(FindElementByName(document, "TemplateListPanel"));
        Assert.Null(FindElementByNameOrDefault(document, "TemplatePreviewTitleTextBlock"));
        Assert.Null(FindElementByNameOrDefault(document, "TemplatePreviewHost"));
        Assert.Null(FindElementByNameOrDefault(document, "CustomLayoutListBox"));
        Assert.NotNull(FindElementByName(document, "CustomLayoutListPanel"));
        Assert.NotNull(FindElementByName(document, "SplitEditorHost"));
        Assert.Null(FindElementByNameOrDefault(document, "EditorPreviewHost"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            GetAttribute(element, "Text") == "저장 결과");
        var layoutNameTextBox = FindElementByName(document, "LayoutNameTextBox");
        Assert.Equal("96", GetAttribute(layoutNameTextBox, "Width"));
        Assert.Equal("0,0,8,0", GetAttribute(layoutNameTextBox, "Margin"));
        Assert.Equal("Center", GetAttribute(layoutNameTextBox, "VerticalContentAlignment"));
        Assert.Equal("LayoutNameTextBox_TextChanged", GetAttribute(layoutNameTextBox, "TextChanged"));
        Assert.Equal("VerticalSplitButton_Click", GetAttribute(FindElementByName(document, "VerticalSplitButton"), "Click"));
        Assert.Equal("HorizontalSplitButton_Click", GetAttribute(FindElementByName(document, "HorizontalSplitButton"), "Click"));
        Assert.Equal("RemoveSelectedSlotButton_Click", GetAttribute(FindElementByName(document, "RemoveSelectedSlotButton"), "Click"));
        Assert.Equal("MergeSelectedZonesButton_Click", GetAttribute(FindElementByName(document, "MergeSelectedZonesButton"), "Click"));
        Assert.Equal("ResetZoneSizeButton_Click", GetAttribute(FindElementByName(document, "ResetZoneSizeButton"), "Click"));
        Assert.Null(FindElementByNameOrDefault(document, "DecreaseWidthButton"));
        Assert.Null(FindElementByNameOrDefault(document, "IncreaseWidthButton"));
        Assert.Null(FindElementByNameOrDefault(document, "DecreaseHeightButton"));
        Assert.Null(FindElementByNameOrDefault(document, "IncreaseHeightButton"));
        Assert.Null(FindButtonOrDefault(document, "폭 -"));
        Assert.Null(FindButtonOrDefault(document, "폭 +"));
        Assert.Null(FindButtonOrDefault(document, "높이 -"));
        Assert.Null(FindButtonOrDefault(document, "높이 +"));
        Assert.Null(FindButtonOrDefault(document, "사용자 지정으로 복사"));
        Assert.Null(FindButtonOrDefault(document, "닫기"));
        Assert.Null(FindButtonOrDefault(document, "적용하고 닫기"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            GetAttribute(element, "Content") == "적용" &&
            GetAttribute(element, "Click") == "ApplySelectedLayoutButton_Click");
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
        Assert.Contains("CustomLayoutButton_Click", text);
        Assert.Contains("CreateCustomLayoutName", text);
        Assert.Contains("SplitSelectedZone", text);
        Assert.Contains("SplitAxis.Vertical", text);
        Assert.Contains("SplitAxis.Horizontal", text);
        Assert.Contains("NormalizeZoneIdsFromVisualOrder", text);
        Assert.Contains("LoadZoneEditorFromLayout", text);
        Assert.Contains("_templateLayouts.Concat(_customLayouts.OrderBy", text);
        Assert.Contains("ApplyLayoutAndClose", text);
        Assert.Contains("LayoutNameTextBox_TextChanged", text);
        Assert.Contains("DialogResult = true", text);
        Assert.DoesNotContain("CopyTemplateToCustomButton_Click", text);
        Assert.DoesNotContain("TemplatePreviewHost", text);
        Assert.DoesNotContain("EditorPreviewHost", text);
        Assert.DoesNotContain("CustomLayoutListBox", text);
        Assert.Contains("RemoveSelectedZone", text);
        Assert.Contains("MergeSelectedZones", text);
        Assert.Contains("InsertColumn", text);
        Assert.Contains("InsertRow", text);
        Assert.DoesNotContain("AdjustSelectedZoneWidth", text);
        Assert.DoesNotContain("AdjustSelectedZoneHeight", text);
        Assert.Contains("ColumnWeights", text);
        Assert.Contains("RowWeights", text);
        Assert.Contains("CreateCustomLayoutId", text);
    }

    [Theory]
    [InlineData(360.0, 240.0, "360x240")]
    [InlineData(360.4, 239.5, "360x240")]
    [InlineData(360.5, 239.6, "361x240")]
    [InlineData(-10.0, 0.0, "0x0")]
    public void CodeBehind_FormatsCustomLayoutSlotExpectedSizeLabel(
        double width,
        double height,
        string expectedLabel)
    {
        var method = typeof(StreamOrchestra.App.Views.LayoutEditorDialog).GetMethod(
            "FormatSlotSizeLabel",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expectedLabel, method!.Invoke(null, [width, height]));
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

    private static XElement? FindElementByNameOrDefault(XDocument document, string name)
    {
        return document
            .Descendants()
            .SingleOrDefault(element => element.Attributes().Any(attribute =>
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

    private static XElement? FindButtonOrDefault(XDocument document, string content)
    {
        return document
            .Descendants()
            .SingleOrDefault(element =>
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
