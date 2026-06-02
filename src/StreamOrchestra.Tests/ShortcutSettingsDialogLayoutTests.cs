using System.Xml.Linq;

namespace StreamOrchestra.Tests;

public sealed class ShortcutSettingsDialogLayoutTests
{
    [Fact]
    public void ShortcutSettingsDialog_ProvidesOneKeyCaptureButtonPerAction()
    {
        var document = LoadDialogDocument();

        var expectedTags = new Dictionary<string, string>
        {
            ["RemoveKeyButton"] = "Remove",
            ["SwapKeyButton"] = "Swap",
            ["SwitchKeyButton"] = "Switch",
            ["SidebarKeyButton"] = "ToggleExplorer",
            ["MuteAllKeyButton"] = "MuteAll"
        };

        foreach (var (buttonName, tag) in expectedTags)
        {
            var button = FindElementByName(document, buttonName);
            Assert.Equal("Button", button.Name.LocalName);
            Assert.Equal("KeyButton_Click", GetAttribute(button, "Click"));
            Assert.Equal(tag, GetAttribute(button, "Tag"));
        }

        // 콤보 박스 방식의 흔적이 남아 있지 않다.
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ComboBox");
    }

    [Fact]
    public void ShortcutSettingsDialog_CapturesKeysAtWindowLevelAndProvidesClose()
    {
        var document = LoadDialogDocument();
        var window = document.Root!;
        var closeButton = FindButton(document, "닫기");

        Assert.Equal("단축키 설정", GetAttribute(window, "Title"));
        Assert.Equal("NoResize", GetAttribute(window, "ResizeMode"));
        Assert.Equal("CenterOwner", GetAttribute(window, "WindowStartupLocation"));
        // 키 입력은 창 단위 PreviewKeyDown으로 가로챈다(버튼·포커스 단축 동작 방지).
        Assert.Equal("Window_PreviewKeyDown", GetAttribute(window, "PreviewKeyDown"));
        Assert.Equal("CloseButton_Click", GetAttribute(closeButton, "Click"));
    }

    [Fact]
    public void CodeBehind_CommitsAnyKeyExceptEscapeAndKeepsPermutation()
    {
        var codeBehind = File.ReadAllText(GetDialogPath("ShortcutSettingsDialog.xaml.cs"));

        // 버튼을 누르면 캡처를 시작하고, ESC를 제외한 임의 키를 가상 키로 해석해 커밋한다.
        Assert.Contains("BeginCapture(action)", codeBehind);
        Assert.Contains("ShortcutKeyResolver.TryCreateKey(e, out var shortcutKey)", codeBehind);
        Assert.Contains("CommitCapture(_capturingAction.Value, shortcutKey)", codeBehind);
        // ESC는 단축키로 쓸 수 없고 캡처 취소(미캡처 시 닫기) 전용이다.
        Assert.Contains("Key.Escape", codeBehind);
        Assert.Contains("CancelCapture()", codeBehind);
        // 캡처 성공 시 즉시 통지(저장)하며, 충돌 키는 자동 교체로 순열을 유지한다.
        Assert.Contains("ShortcutsChanged?.Invoke(Current)", codeBehind);
        Assert.Contains("next = next.With(other, previousKey)", codeBehind);
        // 더 이상 Ctrl·Shift·Alt로 제한하지 않는다.
        Assert.DoesNotContain("Ctrl·Shift·Alt", codeBehind);
    }

    private static XDocument LoadDialogDocument()
    {
        return XDocument.Load(GetDialogPath("ShortcutSettingsDialog.xaml"));
    }

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
