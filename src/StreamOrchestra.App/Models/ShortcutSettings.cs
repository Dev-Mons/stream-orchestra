namespace StreamOrchestra.App.Models;

/// <summary>단축키로 토글되는 화면 조작 동작.</summary>
public enum ShortcutAction
{
    /// <summary>화면 제거 모드(제거 버튼 표시).</summary>
    Remove,

    /// <summary>화면 교체 모드(드래그 오버레이 표시).</summary>
    Swap,

    /// <summary>레이아웃 전환 카드 표시.</summary>
    Switch,

    /// <summary>왼쪽 탐색(사이드바) 패널 열기/닫기 토글.</summary>
    ToggleExplorer
}

/// <summary>
/// 단축키로 쓰는 키 하나. Win32 가상 키 코드(<see cref="VirtualKey"/>)로 식별하며,
/// UI 표시용 이름(<see cref="Name"/>)을 함께 보관한다. ESC를 제외한 임의 키를 담을 수 있다.
/// </summary>
public sealed class ShortcutKey
{
    public int VirtualKey { get; init; }

    public string Name { get; init; } = "";

    public static ShortcutKey Create(int virtualKey, string name) =>
        new() { VirtualKey = virtualKey, Name = name };
}

/// <summary>각 화면 조작 동작에 어떤 키를 매핑할지 정의한다. 세 동작은 서로 다른 키를 가져야 한다(순열).</summary>
public sealed class ShortcutSettings
{
    // Win32 가상 키 코드 기본값: Ctrl=0x11, Shift=0x10, Alt=0x12.
    public ShortcutKey RemoveKey { get; init; } = ShortcutKey.Create(0x11, "Ctrl");

    public ShortcutKey SwapKey { get; init; } = ShortcutKey.Create(0x10, "Shift");

    public ShortcutKey SwitchKey { get; init; } = ShortcutKey.Create(0x12, "Alt");

    // 사이드바 토글 기본값: Tab(0x09).
    public ShortcutKey ToggleExplorerKey { get; init; } = ShortcutKey.Create(0x09, "Tab");

    /// <summary>지정한 동작에 매핑된 키를 돌려준다.</summary>
    public ShortcutKey GetKey(ShortcutAction action) => action switch
    {
        ShortcutAction.Remove => RemoveKey,
        ShortcutAction.Swap => SwapKey,
        ShortcutAction.Switch => SwitchKey,
        ShortcutAction.ToggleExplorer => ToggleExplorerKey,
        _ => RemoveKey
    };

    /// <summary>지정한 가상 키 코드에 매핑된 동작을 돌려준다(없으면 null).</summary>
    public ShortcutAction? GetAction(int virtualKey)
    {
        if (virtualKey == 0)
        {
            return null;
        }

        if (RemoveKey.VirtualKey == virtualKey)
        {
            return ShortcutAction.Remove;
        }

        if (SwapKey.VirtualKey == virtualKey)
        {
            return ShortcutAction.Swap;
        }

        if (SwitchKey.VirtualKey == virtualKey)
        {
            return ShortcutAction.Switch;
        }

        if (ToggleExplorerKey.VirtualKey == virtualKey)
        {
            return ShortcutAction.ToggleExplorer;
        }

        return null;
    }

    /// <summary>세 동작이 서로 다른(0·ESC가 아닌) 키를 가지는 유효한 순열인지 검사한다.</summary>
    public bool IsValidPermutation()
    {
        int[] keys =
        [
            RemoveKey.VirtualKey,
            SwapKey.VirtualKey,
            SwitchKey.VirtualKey,
            ToggleExplorerKey.VirtualKey
        ];

        if (keys.Any(virtualKey => virtualKey == 0 || virtualKey == VkEscape))
        {
            return false;
        }

        return keys.Distinct().Count() == keys.Length;
    }

    public ShortcutSettings With(ShortcutAction action, ShortcutKey key) => action switch
    {
        ShortcutAction.Remove => new ShortcutSettings { RemoveKey = key, SwapKey = SwapKey, SwitchKey = SwitchKey, ToggleExplorerKey = ToggleExplorerKey },
        ShortcutAction.Swap => new ShortcutSettings { RemoveKey = RemoveKey, SwapKey = key, SwitchKey = SwitchKey, ToggleExplorerKey = ToggleExplorerKey },
        ShortcutAction.Switch => new ShortcutSettings { RemoveKey = RemoveKey, SwapKey = SwapKey, SwitchKey = key, ToggleExplorerKey = ToggleExplorerKey },
        ShortcutAction.ToggleExplorer => new ShortcutSettings { RemoveKey = RemoveKey, SwapKey = SwapKey, SwitchKey = SwitchKey, ToggleExplorerKey = key },
        _ => this
    };

    /// <summary>ESC(취소 전용) 가상 키 코드. 단축키로는 쓸 수 없다.</summary>
    public const int VkEscape = 0x1B;
}
