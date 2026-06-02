using StreamOrchestra.App.Models;

namespace StreamOrchestra.Tests;

public sealed class ShortcutSettingsTests
{
    // Win32 가상 키 코드.
    private const int VkTab = 0x09;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkA = 0x41;
    private const int VkM = 0x4D;
    private const int VkEscape = 0x1B;

    [Fact]
    public void Defaults_MapRemoveToCtrlSwapToShiftSwitchToAltToggleToTabMuteToM()
    {
        var settings = new ShortcutSettings();

        Assert.Equal(VkControl, settings.RemoveKey.VirtualKey);
        Assert.Equal(VkShift, settings.SwapKey.VirtualKey);
        Assert.Equal(VkMenu, settings.SwitchKey.VirtualKey);
        Assert.Equal(VkTab, settings.ToggleExplorerKey.VirtualKey);
        Assert.Equal(VkM, settings.MuteAllKey.VirtualKey);
        Assert.Equal("Ctrl", settings.RemoveKey.Name);
        Assert.Equal("Tab", settings.ToggleExplorerKey.Name);
        Assert.Equal("M", settings.MuteAllKey.Name);
        Assert.True(settings.IsValidPermutation());
    }

    [Theory]
    [InlineData(ShortcutAction.Remove, VkControl)]
    [InlineData(ShortcutAction.Swap, VkShift)]
    [InlineData(ShortcutAction.Switch, VkMenu)]
    [InlineData(ShortcutAction.ToggleExplorer, VkTab)]
    [InlineData(ShortcutAction.MuteAll, VkM)]
    public void GetKey_ReturnsMappedKeyForAction(ShortcutAction action, int expectedVirtualKey)
    {
        Assert.Equal(expectedVirtualKey, new ShortcutSettings().GetKey(action).VirtualKey);
    }

    [Theory]
    [InlineData(VkControl, ShortcutAction.Remove)]
    [InlineData(VkShift, ShortcutAction.Swap)]
    [InlineData(VkMenu, ShortcutAction.Switch)]
    [InlineData(VkTab, ShortcutAction.ToggleExplorer)]
    [InlineData(VkM, ShortcutAction.MuteAll)]
    public void GetAction_ReturnsMappedActionForVirtualKey(int virtualKey, ShortcutAction expected)
    {
        Assert.Equal(expected, new ShortcutSettings().GetAction(virtualKey));
    }

    [Fact]
    public void GetAction_ReturnsNullForUnboundOrZeroKey()
    {
        var settings = new ShortcutSettings();

        Assert.Null(settings.GetAction(VkA));
        Assert.Null(settings.GetAction(0));
    }

    [Fact]
    public void GetAction_RoundTripsAfterRemappingToArbitraryKey()
    {
        // 전환을 임의 키('A')로 바꾼 매핑에서도 역매핑이 올바르다.
        var settings = new ShortcutSettings
        {
            RemoveKey = ShortcutKey.Create(VkControl, "Ctrl"),
            SwapKey = ShortcutKey.Create(VkShift, "Shift"),
            SwitchKey = ShortcutKey.Create(VkA, "A")
        };

        Assert.True(settings.IsValidPermutation());
        Assert.Equal(ShortcutAction.Switch, settings.GetAction(VkA));
        Assert.Null(settings.GetAction(VkMenu));
    }

    [Fact]
    public void IsValidPermutation_FalseWhenKeysCollide()
    {
        var settings = new ShortcutSettings
        {
            RemoveKey = ShortcutKey.Create(VkControl, "Ctrl"),
            SwapKey = ShortcutKey.Create(VkControl, "Ctrl"),
            SwitchKey = ShortcutKey.Create(VkMenu, "Alt")
        };

        Assert.False(settings.IsValidPermutation());
    }

    [Fact]
    public void IsValidPermutation_FalseWhenAnyKeyIsEscapeOrZero()
    {
        var withEscape = new ShortcutSettings
        {
            RemoveKey = ShortcutKey.Create(VkEscape, "Esc"),
            SwapKey = ShortcutKey.Create(VkShift, "Shift"),
            SwitchKey = ShortcutKey.Create(VkMenu, "Alt")
        };
        var withZero = new ShortcutSettings
        {
            RemoveKey = ShortcutKey.Create(0, ""),
            SwapKey = ShortcutKey.Create(VkShift, "Shift"),
            SwitchKey = ShortcutKey.Create(VkMenu, "Alt")
        };

        Assert.False(withEscape.IsValidPermutation());
        Assert.False(withZero.IsValidPermutation());
    }

    [Fact]
    public void With_ReplacesOnlyTheRequestedActionsKey()
    {
        var updated = new ShortcutSettings().With(ShortcutAction.Remove, ShortcutKey.Create(VkA, "A"));

        Assert.Equal(VkA, updated.RemoveKey.VirtualKey);
        Assert.Equal(VkShift, updated.SwapKey.VirtualKey);
        Assert.Equal(VkMenu, updated.SwitchKey.VirtualKey);
        Assert.Equal(VkTab, updated.ToggleExplorerKey.VirtualKey);
        Assert.Equal(VkM, updated.MuteAllKey.VirtualKey);
    }

    [Fact]
    public void With_RemappingAnotherActionKeepsMuteAllKey()
    {
        // MuteAll을 'A'로 바꾼 뒤 다른 동작(제거)을 바꿔도 MuteAll 매핑이 유지된다.
        var withMutedToA = new ShortcutSettings().With(ShortcutAction.MuteAll, ShortcutKey.Create(VkA, "A"));
        var updated = withMutedToA.With(ShortcutAction.Remove, ShortcutKey.Create(VkM, "M"));

        Assert.Equal(VkA, updated.MuteAllKey.VirtualKey);
        Assert.Equal(VkM, updated.RemoveKey.VirtualKey);
    }
}
