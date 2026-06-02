using System.Windows.Input;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App;

/// <summary>
/// WPF <see cref="Key"/>와 Win32 가상 키 코드를 변환한다. 좌우 보조 키(Ctrl/Shift/Alt)는
/// 일반 가상 키(VK_CONTROL/VK_SHIFT/VK_MENU)로 합쳐, WebView2가 보내는 <c>keyCode</c>와 동일하게 맞춘다.
/// </summary>
internal static class ShortcutKeyResolver
{
    public const int VkTab = 0x09;
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;

    /// <summary>키 이벤트를 가상 키 코드로 해석한다(Alt는 SystemKey로 들어오므로 함께 처리).</summary>
    public static bool TryResolveVirtualKey(KeyEventArgs e, out int virtualKey)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return TryResolveVirtualKey(key, out virtualKey);
    }

    public static bool TryResolveVirtualKey(Key key, out int virtualKey)
    {
        switch (key)
        {
            case Key.LeftShift:
            case Key.RightShift:
                virtualKey = VkShift;
                return true;
            case Key.LeftCtrl:
            case Key.RightCtrl:
                virtualKey = VkControl;
                return true;
            case Key.LeftAlt:
            case Key.RightAlt:
                virtualKey = VkMenu;
                return true;
        }

        virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    /// <summary>키 이벤트를 표시 이름과 가상 키 코드를 가진 <see cref="ShortcutKey"/>로 만든다.</summary>
    public static bool TryCreateKey(KeyEventArgs e, out ShortcutKey shortcutKey)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!TryResolveVirtualKey(key, out var virtualKey))
        {
            shortcutKey = ShortcutKey.Create(0, "");
            return false;
        }

        shortcutKey = ShortcutKey.Create(virtualKey, GetFriendlyName(key));
        return true;
    }

    public static string GetFriendlyName(Key key)
    {
        switch (key)
        {
            case Key.LeftShift:
            case Key.RightShift:
                return "Shift";
            case Key.LeftCtrl:
            case Key.RightCtrl:
                return "Ctrl";
            case Key.LeftAlt:
            case Key.RightAlt:
                return "Alt";
            case Key.Space:
                return "Space";
            case Key.Return:
                return "Enter";
            case Key.Tab:
                return "Tab";
            case Key.Back:
                return "Backspace";
            case >= Key.D0 and <= Key.D9:
                return ((char)('0' + (key - Key.D0))).ToString();
            case >= Key.NumPad0 and <= Key.NumPad9:
                return "Num" + (key - Key.NumPad0);
            default:
                return key.ToString();
        }
    }
}
