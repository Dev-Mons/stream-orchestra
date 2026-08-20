using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class BrowserDataResetDialogLayoutTests
{
    [Fact]
    public void Dialog_SelectsGroupAndBrowserDataKinds()
    {
        var document = LoadDialogDocument();
        var groupSelector = FindElementByName(document, "ProfileGroupComboBox");
        var siteData = FindElementByName(document, "SiteDataCheckBox");
        var cache = FindElementByName(document, "CacheCheckBox");
        var clearButton = FindButton(document, "선택 항목 초기화");

        Assert.Equal("ComboBox", groupSelector.Name.LocalName);
        Assert.Equal("DisplayName", GetAttribute(groupSelector, "DisplayMemberPath"));
        Assert.Equal("True", GetAttribute(siteData, "IsChecked"));
        Assert.Equal("True", GetAttribute(cache, "IsChecked"));
        Assert.Equal("ClearButton_Click", GetAttribute(clearButton, "Click"));
    }

    [Fact]
    public void Dialog_WarnsAboutLoginAndPreservesAppSettings()
    {
        var document = LoadDialogDocument();
        var text = string.Join(
            " ",
            document.Descendants()
                .Where(element => element.Name.LocalName == "TextBlock")
                .Select(element => GetAttribute(element, "Text")));
        var codeBehind = File.ReadAllText(GetDialogPath("BrowserDataResetDialog.xaml.cs"));

        Assert.Contains("로그아웃", text);
        Assert.Contains("레이아웃, 프리셋, 단축키 설정은 삭제하지 않습니다", text);
        Assert.Contains("public BrowserDataClearOptions Options => new(", codeBehind);
        Assert.Contains("SiteDataCheckBox.IsChecked == true", codeBehind);
        Assert.Contains("CacheCheckBox.IsChecked == true", codeBehind);
    }

    private static XDocument LoadDialogDocument() =>
        XDocument.Load(GetDialogPath("BrowserDataResetDialog.xaml"));

    private static string GetDialogPath(string fileName)
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
