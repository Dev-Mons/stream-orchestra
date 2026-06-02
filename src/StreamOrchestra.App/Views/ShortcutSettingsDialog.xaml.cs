using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Views;

public partial class ShortcutSettingsDialog : Window
{
    private readonly Dictionary<ShortcutAction, Button> _buttons;
    private ShortcutAction? _capturingAction;

    public ShortcutSettingsDialog(ShortcutSettings current)
    {
        InitializeComponent();

        Current = current;
        _buttons = new Dictionary<ShortcutAction, Button>
        {
            [ShortcutAction.Remove] = RemoveKeyButton,
            [ShortcutAction.Swap] = SwapKeyButton,
            [ShortcutAction.Switch] = SwitchKeyButton,
            [ShortcutAction.ToggleExplorer] = SidebarKeyButton,
            [ShortcutAction.MuteAll] = MuteAllKeyButton
        };

        RefreshButtons();
    }

    /// <summary>현재 단축키 매핑(키 캡처마다 즉시 갱신됨, 항상 유효한 순열).</summary>
    public ShortcutSettings Current { get; private set; }

    /// <summary>키가 캡처되어 매핑이 바뀔 때마다 발생(호스트가 즉시 적용·저장).</summary>
    public event Action<ShortcutSettings>? ShortcutsChanged;

    private void KeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<ShortcutAction>(tag, out var action))
        {
            return;
        }

        BeginCapture(action);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingAction is null)
        {
            // 캡처 중이 아닐 때 ESC는 다이얼로그를 닫는다.
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }

            return;
        }

        // 캡처 중에는 모든 키 입력을 가로채 버튼·포커스 동작(Space/Enter/Tab 등)을 막는다.
        e.Handled = true;

        // ESC는 단축키로 쓸 수 없고, 캡처 취소 전용이다.
        var resolvedKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (resolvedKey == Key.Escape)
        {
            CancelCapture();
            return;
        }

        if (ShortcutKeyResolver.TryCreateKey(e, out var shortcutKey))
        {
            CommitCapture(_capturingAction.Value, shortcutKey);
            return;
        }

        StatusTextBlock.Text = "인식할 수 없는 키입니다. 다른 키를 눌러 주세요. (ESC: 취소)";
    }

    private void BeginCapture(ShortcutAction action)
    {
        _capturingAction = action;
        RefreshButtons();
        StatusTextBlock.Text = $"{GetActionLabel(action)}에 쓸 키를 누르세요 (ESC를 제외한 아무 키, ESC로 취소).";
        // 키 입력이 다이얼로그로 들어오도록 포커스를 가져온다.
        Focus();
    }

    private void CancelCapture()
    {
        var action = _capturingAction;
        _capturingAction = null;
        RefreshButtons();
        StatusTextBlock.Text = action is { } cancelled
            ? $"{GetActionLabel(cancelled)} 변경을 취소했습니다."
            : string.Empty;
    }

    private void CommitCapture(ShortcutAction action, ShortcutKey key)
    {
        var previousKey = Current.GetKey(action);
        _capturingAction = null;

        if (previousKey.VirtualKey == key.VirtualKey)
        {
            RefreshButtons();
            StatusTextBlock.Text = $"{GetActionLabel(action)}는 이미 {key.Name}입니다.";
            return;
        }

        Current = Assign(Current, action, key);
        RefreshButtons();

        // 충돌이 있었다면 다른 동작의 키와 자동으로 맞바뀐다는 점을 안내한다.
        var swappedAction = FindActionWithKey(Current, previousKey.VirtualKey, exclude: action);
        StatusTextBlock.Text = swappedAction is { } swapped
            ? $"{GetActionLabel(action)} → {key.Name} (← {GetActionLabel(swapped)}와 교체)"
            : $"{GetActionLabel(action)} → {key.Name} 으로 변경·저장했습니다.";

        ShortcutsChanged?.Invoke(Current);
    }

    // 한 동작에 키를 배정하되, 그 키를 쓰던 다른 동작이 있으면 방금 비워진 키를 넘겨 순열을 유지한다.
    private static ShortcutSettings Assign(ShortcutSettings current, ShortcutAction action, ShortcutKey key)
    {
        var previousKey = current.GetKey(action);
        var next = current.With(action, key);

        var conflicting = current.GetAction(key.VirtualKey);
        if (conflicting is { } other && other != action)
        {
            next = next.With(other, previousKey);
        }

        return next;
    }

    private static ShortcutAction? FindActionWithKey(
        ShortcutSettings settings,
        int virtualKey,
        ShortcutAction exclude)
    {
        var action = settings.GetAction(virtualKey);
        return action is { } resolved && resolved != exclude ? resolved : null;
    }

    private void RefreshButtons()
    {
        foreach (var (action, button) in _buttons)
        {
            button.Content = _capturingAction == action
                ? "키 입력…"
                : Current.GetKey(action).Name;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string GetActionLabel(ShortcutAction action) => action switch
    {
        ShortcutAction.Remove => "화면 제거",
        ShortcutAction.Swap => "화면 교체",
        ShortcutAction.Switch => "레이아웃 전환",
        ShortcutAction.ToggleExplorer => "사이드바 열기/닫기",
        ShortcutAction.MuteAll => "전체 볼륨 0%",
        _ => action.ToString()
    };
}
