using System.Xml.Linq;
using StreamOrchestra.App.Views;

namespace StreamOrchestra.Tests;

public sealed class LayoutEditorDialogLayoutTests
{
    [Fact]
    public void LayoutEditorDialog_ProvidesCustomEditingSurfaceOnly()
    {
        var document = LoadLayoutEditorDialogDocument();
        var tabHeaders = document
            .Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .Select(element => GetAttribute(element, "Header"))
            .ToArray();

        // 상단 공간 절약을 위해 탭(TabControl/TabItem)을 제거하고 편집 화면을 바로 노출한다.
        Assert.Empty(tabHeaders);
        Assert.Null(FindElementByNameOrDefault(document, "EditorTabControl"));
        Assert.Null(FindElementByNameOrDefault(document, "TemplateListPanel"));
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
        Assert.Equal("200", GetAttribute(layoutNameTextBox, "MinWidth"));
        Assert.Equal("Center", GetAttribute(layoutNameTextBox, "VerticalContentAlignment"));
        Assert.Equal("LayoutNameTextBox_TextChanged", GetAttribute(layoutNameTextBox, "TextChanged"));
        // 헤더 요약/미저장 표시와 선택 컨텍스트 바가 존재한다.
        Assert.NotNull(FindElementByName(document, "LayoutSummaryTextBlock"));
        Assert.NotNull(FindElementByName(document, "UnsavedIndicator"));
        Assert.NotNull(FindElementByName(document, "SelectionInfoTextBlock"));
        Assert.Null(FindElementByNameOrDefault(document, "VerticalSplitButton"));
        Assert.Null(FindElementByNameOrDefault(document, "HorizontalSplitButton"));
        Assert.Equal("HorizontalAlignButton_Click", GetAttribute(FindElementByName(document, "HorizontalAlignButton"), "Click"));
        Assert.Equal("VerticalAlignButton_Click", GetAttribute(FindElementByName(document, "VerticalAlignButton"), "Click"));
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
        Assert.Null(FindButtonOrDefault(document, "적용하고 닫기"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            GetAttribute(element, "Content") == "적용" &&
            GetAttribute(element, "Click") == "ApplySelectedLayoutButton_Click");
        Assert.Equal("NewCustomLayoutButton_Click", GetAttribute(FindButton(document, "새 레이아웃"), "Click"));
        Assert.Equal("SaveCustomLayoutButton_Click", GetAttribute(FindButton(document, "저장"), "Click"));
        Assert.Equal("DeleteCustomLayoutButton_Click", GetAttribute(FindButton(document, "삭제"), "Click"));
        // 푸터: 저장 후 적용 / 닫기
        Assert.Equal("SaveAndApplyButton_Click", GetAttribute(FindButton(document, "저장 후 적용"), "Click"));
        Assert.Equal("CloseButton_Click", GetAttribute(FindButton(document, "닫기"), "Click"));
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
        Assert.Contains("LayoutNameTextBox_TextChanged", text);
        // 템플릿 탭 제거: 템플릿 적용/선택 코드가 더 이상 존재하지 않는다.
        Assert.DoesNotContain("RefreshTemplateList", text);
        Assert.DoesNotContain("ApplyLayoutAndClose", text);
        Assert.DoesNotContain("TemplateButton_Click", text);
        Assert.DoesNotContain("_templateLayouts", text);
        Assert.DoesNotContain("CopyTemplateToCustomButton_Click", text);
        Assert.DoesNotContain("TemplatePreviewHost", text);
        Assert.DoesNotContain("EditorPreviewHost", text);
        Assert.DoesNotContain("CustomLayoutListBox", text);
        Assert.Contains("RemoveSelectedZone", text);
        Assert.Contains("MergeSelectedZones", text);
        // A+B 개선: 저장 후 적용/닫기, 미저장 표시, zone 호버 핸들.
        Assert.Contains("SaveAndApplyButton_Click", text);
        Assert.Contains("CloseButton_Click", text);
        Assert.Contains("AppliedLayoutId", text);
        Assert.Contains("UpdateEditorChrome", text);
        Assert.Contains("CreateZoneElement", text);
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

    [Fact]
    public void CompactRedundantTracks_CollapsesMergedRowBoundaryToCurrentDesign()
    {
        var method = typeof(StreamOrchestra.App.Views.LayoutEditorDialog).GetMethod(
            "CompactRedundantTracks",
            System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        // 2열 x 3행에서 아래 두 행이 같은 zone(3,4)으로 병합된 상태 → 마지막 행 경계가 중복.
        var cells = new int[3, 2] { { 1, 2 }, { 3, 4 }, { 3, 4 } };
        var result = method!.Invoke(null, [cells, new[] { 1d, 1d }, new[] { 1d, 1d, 1d }])!;
        var resultType = result.GetType();
        var compactedCells = (int[,])resultType.GetField("Item1")!.GetValue(result)!;
        var rowWeights = (double[])resultType.GetField("Item3")!.GetValue(result)!;

        // 중복 행 경계가 제거되어 현재 디자인(2x2) 그리드가 된다.
        Assert.Equal(2, compactedCells.GetLength(0));
        Assert.Equal(2, compactedCells.GetLength(1));
        Assert.Equal(2, rowWeights.Length);
    }

    [Fact]
    public void CompactRedundantTracks_KeepsMinimalGridUnchanged()
    {
        var method = typeof(StreamOrchestra.App.Views.LayoutEditorDialog).GetMethod(
            "CompactRedundantTracks",
            System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        // 이미 최소 그리드(2x2)면 행/열 개수가 유지되어야 한다.
        var cells = new int[2, 2] { { 1, 2 }, { 3, 4 } };
        var result = method!.Invoke(null, [cells, new[] { 1d, 1d }, new[] { 1d, 1d }])!;
        var resultType = result.GetType();
        var compactedCells = (int[,])resultType.GetField("Item1")!.GetValue(result)!;

        Assert.Equal(2, compactedCells.GetLength(0));
        Assert.Equal(2, compactedCells.GetLength(1));
    }

    [Fact]
    public void ComputeDesignBalancedWeights_MakesSpanningZoneEqualToSumOfSmallerZones()
    {
        var method = typeof(StreamOrchestra.App.Views.LayoutEditorDialog).GetMethod(
            "ComputeDesignBalancedWeights",
            System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        // 우측이 2행에 걸친 zone(2), 하단이 가로로 걸친 zone(4)인 2x3 / 4슬롯 레이아웃.
        // 1 2
        // 3 2
        // 4 4
        var cells = new int[3, 2] { { 1, 2 }, { 3, 2 }, { 4, 4 } };
        var result = method!.Invoke(null, [cells])!;
        var resultType = result.GetType();
        var columnWeights = (double[])resultType.GetField("Item1")!.GetValue(result)!;
        var rowWeights = (double[])resultType.GetField("Item2")!.GetValue(result)!;

        // 행 비율은 1 : 1 : 2 (위 두 행은 각각 절반, 아래 행은 두 배) → 정규화 후에도 비율 유지.
        Assert.Equal(rowWeights[0], rowWeights[1], 3);
        Assert.Equal(rowWeights[2], rowWeights[0] * 2, 3);
        // 열은 균등(둘 다 폭 2짜리 zone에 걸쳐 동일).
        Assert.Equal(columnWeights[0], columnWeights[1], 3);
    }

    [Fact]
    public void TryApplyLocalizedColumnResize_AddsBoundaryForDraggedSegmentOnly()
    {
        var method = typeof(StreamOrchestra.App.Views.LayoutEditorDialog).GetMethod(
            "TryApplyLocalizedColumnResize",
            System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var cells = new int[2, 2] { { 1, 2 }, { 3, 4 } };
        object?[] parameters =
        [
            cells,
            new[] { 1d, 1d },
            new LayoutEditorSplitterSegment(Boundary: 1, Start: 0, Span: 1),
            0.4d,
            null,
            null
        ];

        var applied = (bool)method!.Invoke(null, parameters)!;
        var nextCells = (int[,])parameters[4]!;
        var nextColumnWeights = (double[])parameters[5]!;

        Assert.True(applied);
        AssertCellsEqual(new int[2, 3] { { 1, 1, 2 }, { 3, 4, 4 } }, nextCells);
        Assert.Equal([1d, 0.4d, 0.6d], nextColumnWeights, new RoundedDoubleComparer(3));
    }

    [Fact]
    public void TryApplyLocalizedColumnResize_UsesSmallDragDeltaWithoutMinimumJump()
    {
        var method = typeof(StreamOrchestra.App.Views.LayoutEditorDialog).GetMethod(
            "TryApplyLocalizedColumnResize",
            System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var cells = new int[2, 2] { { 1, 2 }, { 3, 4 } };
        object?[] parameters =
        [
            cells,
            new[] { 1d, 1d },
            new LayoutEditorSplitterSegment(Boundary: 1, Start: 0, Span: 1),
            0.05d,
            null,
            null
        ];

        var applied = (bool)method!.Invoke(null, parameters)!;
        var nextColumnWeights = (double[])parameters[5]!;

        Assert.True(applied);
        Assert.Equal([1d, 0.05d, 0.95d], nextColumnWeights, new RoundedDoubleComparer(3));
    }

    [Fact]
    public void TryApplyWeightDelta_AllowsIndependentTrackBelowLegacyMinimum()
    {
        var method = typeof(StreamOrchestra.App.Views.LayoutEditorDialog).GetMethod(
            "TryApplyWeightDelta",
            System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var weights = new[] { 1d, 0.4d, 0.6d };

        var applied = (bool)method!.Invoke(null, [weights, new[] { 1 }, new[] { 2 }, -0.35d])!;

        Assert.True(applied);
        Assert.Equal([1d, 0.05d, 0.95d], weights, new RoundedDoubleComparer(3));
    }

    [Fact]
    public void TryApplyLocalizedRowResize_AddsBoundaryForDraggedSegmentOnly()
    {
        var method = typeof(StreamOrchestra.App.Views.LayoutEditorDialog).GetMethod(
            "TryApplyLocalizedRowResize",
            System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var cells = new int[2, 2] { { 1, 2 }, { 3, 4 } };
        object?[] parameters =
        [
            cells,
            new[] { 1d, 1d },
            new LayoutEditorSplitterSegment(Boundary: 1, Start: 0, Span: 1),
            0.4d,
            null,
            null
        ];

        var applied = (bool)method!.Invoke(null, parameters)!;
        var nextCells = (int[,])parameters[4]!;
        var nextRowWeights = (double[])parameters[5]!;

        Assert.True(applied);
        AssertCellsEqual(new int[3, 2] { { 1, 2 }, { 1, 4 }, { 3, 4 } }, nextCells);
        Assert.Equal([1d, 0.4d, 0.6d], nextRowWeights, new RoundedDoubleComparer(3));
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

    private static void AssertCellsEqual(int[,] expected, int[,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));

        for (var y = 0; y < expected.GetLength(0); y++)
        {
            for (var x = 0; x < expected.GetLength(1); x++)
            {
                Assert.Equal(expected[y, x], actual[y, x]);
            }
        }
    }

    private sealed class RoundedDoubleComparer(int digits) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Round(x, digits) == Math.Round(y, digits);

        public int GetHashCode(double obj) => Math.Round(obj, digits).GetHashCode();
    }
}
