using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

// 창 밖에 드롭해 연 크롬 새 창을 드롭한 모니터로 옮겨 최대화한다.
// 크롬이 이미 켜져 있으면 --window-position 같은 실행 옵션은 기존 프로세스로 넘어가며 무시되므로,
// 새 창이 화면에 나타나는 순간(EVENT_OBJECT_SHOW)을 잡아 직접 옮긴다.
public sealed class ChromeWindowPlacementWatcher : IDisposable
{
    // 크롬이 꺼져 있다가 처음 켜질 때까지 기다리는 시간. 이 안에 새 창이 없으면 그냥 둔다.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private const string ChromeWindowClassName = "Chrome_WidgetWin_1";

    private readonly string _chromeExecutablePath;
    private readonly IntPtr _targetMonitor;
    private readonly HashSet<IntPtr> _windowsBeforeLaunch;
    // 네이티브 훅이 콜백을 부르는 동안 GC되지 않도록 대리자를 필드로 붙잡아 둔다.
    private readonly WinEventProc _winEventProc;
    private readonly DispatcherTimer _timeoutTimer;
    private IntPtr _hook;

    private ChromeWindowPlacementWatcher(string chromeExecutablePath, IntPtr targetMonitor, TimeSpan timeout)
    {
        _chromeExecutablePath = Path.GetFullPath(chromeExecutablePath);
        _targetMonitor = targetMonitor;
        _windowsBeforeLaunch = EnumerateTopLevelWindows();
        _winEventProc = OnWinEvent;
        _timeoutTimer = new DispatcherTimer { Interval = timeout };
        _timeoutTimer.Tick += (_, _) => Dispose();
    }

    // 드롭한 순간 커서가 있던 모니터. 브라우저 탐색 등으로 시간이 지나기 전에 먼저 잡아 둔다.
    public static IntPtr CaptureMonitorUnderCursor()
    {
        using var _ = new PerMonitorDpiScope();
        return GetCursorPos(out var point) ? MonitorFromPoint(point, MonitorDefaultToNearest) : IntPtr.Zero;
    }

    // 크롬을 실행하기 전에 불러야 새 창이 뜨는 순간을 놓치지 않는다. 훅을 걸 수 없으면 null.
    public static ChromeWindowPlacementWatcher? Start(string chromeExecutablePath, IntPtr targetMonitor, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(chromeExecutablePath) || targetMonitor == IntPtr.Zero)
        {
            return null;
        }

        var watcher = new ChromeWindowPlacementWatcher(chromeExecutablePath, targetMonitor, timeout);
        // 아웃오브컨텍스트 훅은 이 스레드의 메시지 루프로 콜백이 들어오므로 UI 스레드에서 걸어야 한다.
        watcher._hook = SetWinEventHook(
            EventObjectShow,
            EventObjectShow,
            IntPtr.Zero,
            watcher._winEventProc,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
        if (watcher._hook == IntPtr.Zero)
        {
            return null;
        }

        watcher._timeoutTimer.Start();
        return watcher;
    }

    // 창의 보통(복원) 크기는 유지하되 대상 작업 영역보다 크면 줄이고, 작업 영역 가운데에 둔다.
    // 최대화를 풀었을 때도 같은 모니터에 남도록 하기 위한 값이다.
    public static ScreenRect CalculateNormalBounds(int normalWidth, int normalHeight, ScreenRect workArea)
    {
        var width = normalWidth > 0 ? Math.Min(normalWidth, workArea.Width) : workArea.Width;
        var height = normalHeight > 0 ? Math.Min(normalHeight, workArea.Height) : workArea.Height;
        var left = workArea.Left + (workArea.Width - width) / 2;
        var top = workArea.Top + (workArea.Height - height) / 2;
        return new ScreenRect(left, top, left + width, top + height);
    }

    public void Dispose()
    {
        _timeoutTimer.Stop();
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (_hook == IntPtr.Zero ||
            objectId != ObjectIdWindow ||
            childId != ChildIdSelf ||
            _windowsBeforeLaunch.Contains(window) ||
            !IsChromeBrowserWindow(window))
        {
            return;
        }

        // 처음 나타난 새 크롬 창 하나만 옮기고 감시를 끝낸다.
        Dispose();
        if (!MoveToMonitorMaximized(window, _targetMonitor))
        {
            Trace.WriteLine("[ExternalBrowser] Could not move the new Chrome window to the drop monitor.");
        }
    }

    // 툴팁·말풍선 같은 크롬 보조 창은 제외하고, 제목 표시줄이 있는 최상위 브라우저 창만 고른다.
    private bool IsChromeBrowserWindow(IntPtr window)
    {
        if (window == IntPtr.Zero ||
            GetAncestor(window, GetAncestorRoot) != window ||
            GetWindow(window, GetWindowOwner) != IntPtr.Zero ||
            !IsWindowVisible(window))
        {
            return false;
        }

        var style = GetWindowLongPtr(window, WindowLongStyle).ToInt64();
        var extendedStyle = GetWindowLongPtr(window, WindowLongExtendedStyle).ToInt64();
        if ((style & WindowStyleCaption) != WindowStyleCaption || (extendedStyle & WindowStyleExToolWindow) != 0)
        {
            return false;
        }

        var className = new StringBuilder(64);
        if (GetClassName(window, className, className.Capacity) == 0 ||
            !string.Equals(className.ToString(), ChromeWindowClassName, StringComparison.Ordinal))
        {
            return false;
        }

        // 같은 창 클래스를 쓰는 Edge·Electron 앱과 구분하려고 실행 파일 경로까지 맞춰 본다.
        GetWindowThreadProcessId(window, out var processId);
        return string.Equals(TryGetProcessPath(processId), _chromeExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MoveToMonitorMaximized(IntPtr window, IntPtr monitor)
    {
        // 앱의 DPI 모드와 관계없이 다른 배율 모니터에서도 물리 픽셀 좌표로 계산한다.
        using var _ = new PerMonitorDpiScope();

        // 이미 그 모니터에 최대화돼 떴으면 건드리지 않아 깜빡임을 피한다.
        if (IsZoomed(window) && MonitorFromWindow(window, MonitorDefaultToNearest) == monitor)
        {
            return true;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var primaryInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var primaryMonitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo) ||
            !GetMonitorInfo(primaryMonitor, ref primaryInfo) ||
            !GetWindowPlacement(window, ref placement))
        {
            return false;
        }

        var normal = CalculateNormalBounds(
            placement.NormalPosition.Width,
            placement.NormalPosition.Height,
            monitorInfo.Work);

        // rcNormalPosition은 화면 좌표가 아니라 주 모니터 작업 영역 왼쪽 위를 (0,0)으로 하는 작업 영역 좌표다.
        placement.NormalPosition = normal.Offset(-primaryInfo.Work.Left, -primaryInfo.Work.Top);
        // 이미 최대화된 창은 SW_SHOWMAXIMIZED로 보통 위치만 바꿔서는 다른 모니터로 옮겨지지 않는다.
        // 대상 모니터에 보통 크기로 먼저 놓은 뒤 그 자리에서 최대화한다.
        placement.ShowCommand = ShowNormal;
        placement.Flags = 0;
        if (!SetWindowPlacement(window, ref placement))
        {
            return false;
        }

        ShowWindow(window, ShowMaximized);
        return true;
    }

    private static HashSet<IntPtr> EnumerateTopLevelWindows()
    {
        var windows = new HashSet<IntPtr>();
        EnumWindows((window, _) =>
        {
            windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static string? TryGetProcessPath(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var length = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref length) ? buffer.ToString(0, length) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    // 이 스레드의 DPI 인식을 잠시 Per-Monitor v2로 바꿨다가 되돌린다.
    private readonly struct PerMonitorDpiScope : IDisposable
    {
        private readonly IntPtr _previous;

        public PerMonitorDpiScope()
        {
            _previous = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        }

        public void Dispose()
        {
            if (_previous != IntPtr.Zero)
            {
                SetThreadDpiAwarenessContext(_previous);
            }
        }
    }

    private const uint EventObjectShow = 0x8002;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint GetAncestorRoot = 2;
    private const uint GetWindowOwner = 4;
    private const int WindowLongStyle = -16;
    private const int WindowLongExtendedStyle = -20;
    private const long WindowStyleCaption = 0x00C00000;
    private const long WindowStyleExToolWindow = 0x00000080;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;
    private const int ShowNormal = 1;
    private const int ShowMaximized = 3;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    private delegate void WinEventProc(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr window, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public ScreenRect Monitor;
        public ScreenRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public ScreenRect NormalPosition;
    }
}
