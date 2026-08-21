namespace StreamOrchestra.App.Services;

/// <summary>비동기 레이아웃 변경을 한 번에 하나만 실행하도록 직렬화한다.</summary>
public sealed class LayoutTransitionCoordinator
{
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    /// <summary>
    /// 실행권을 얻으면 전환을 수행하고 true를 반환한다. 이미 전환 중이면 대기열을 만들지 않고 false를 반환한다.
    /// </summary>
    public async Task<bool> TryRunAsync(Func<Task> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            await transition();
            return true;
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }
}
