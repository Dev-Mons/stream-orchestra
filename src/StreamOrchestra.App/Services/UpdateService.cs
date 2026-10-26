using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public enum UpdateCheckOutcome
{
    Disabled,
    Throttled,
    NoUpdate,
    Skipped,
    Available,
    Failed,
    NotInstalled
}

public sealed record UpdateCheckResult(
    UpdateCheckOutcome Outcome,
    AvailableUpdate? Update,
    Exception? Error = null);

public sealed class UpdateService
{
    // 앱이 켜져 있는 동안 이 간격마다 새 버전을 확인한다. 앱을 다시 켤 때도 이 간격 안이면 건너뛴다.
    public static readonly TimeSpan DefaultMinimumCheckInterval = TimeSpan.FromMinutes(30);

    private readonly IUpdateChecker _checker;
    private readonly TimeSpan _minimumCheckInterval;
    private readonly Func<DateTimeOffset> _clock;
    // 안내 창을 띄운 버전. 저장하지 않으므로 앱을 다시 켜면 한 번 더 안내한다.
    private readonly HashSet<string> _promptedVersions = new(StringComparer.OrdinalIgnoreCase);
    private AutoUpdateState _state;

    public UpdateService(IUpdateChecker checker, AutoUpdateState initialState)
        : this(checker, initialState, DefaultMinimumCheckInterval, () => DateTimeOffset.UtcNow)
    {
    }

    public UpdateService(
        IUpdateChecker checker,
        AutoUpdateState initialState,
        TimeSpan minimumCheckInterval,
        Func<DateTimeOffset> clock)
    {
        _checker = checker;
        _state = initialState ?? new AutoUpdateState();
        _minimumCheckInterval = minimumCheckInterval;
        _clock = clock;
    }

    public AutoUpdateState CurrentState => _state;

    public async Task<UpdateCheckResult> RunAutomaticCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_state.Enabled)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Disabled, null);
        }

        if (_state.LastCheckUtc is { } last && _clock() - last < _minimumCheckInterval)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Throttled, null);
        }

        return await CheckCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<UpdateCheckResult> RunStartupCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_state.Enabled)
        {
            return Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.Disabled, null));
        }

        return CheckCoreAsync(cancellationToken);
    }

    public Task<UpdateCheckResult> RunManualCheckAsync(CancellationToken cancellationToken = default)
    {
        return CheckCoreAsync(cancellationToken);
    }

    public void SkipVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        _state = new AutoUpdateState
        {
            Enabled = _state.Enabled,
            SkippedVersion = version,
            LastCheckUtc = _state.LastCheckUtc
        };
    }

    public bool HasPromptBeenShown(string version) => _promptedVersions.Contains(version);

    public void MarkPromptShown(string version)
    {
        if (!string.IsNullOrWhiteSpace(version))
        {
            _promptedVersions.Add(version);
        }
    }

    public void SetEnabled(bool enabled)
    {
        _state = new AutoUpdateState
        {
            Enabled = enabled,
            SkippedVersion = _state.SkippedVersion,
            LastCheckUtc = _state.LastCheckUtc
        };
    }

    // 다운로드할 업데이트가 없어 재시작하지 않으면 false를 돌려준다.
    public async Task<bool> DownloadAndApplyAsync(Action beforeRestart, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeRestart);

        if (!await _checker.DownloadUpdateAsync(cancellationToken))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        beforeRestart();
        _checker.ApplyUpdateAndRestart();
        return true;
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(CancellationToken cancellationToken)
    {
        // 실제로 확인하지 않았으므로 마지막 확인 시각도 갱신하지 않는다.
        if (!_checker.IsInstalled)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.NotInstalled, null);
        }

        AvailableUpdate? update;
        try
        {
            update = await _checker.CheckForUpdateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, null, ex);
        }

        _state = new AutoUpdateState
        {
            Enabled = _state.Enabled,
            SkippedVersion = _state.SkippedVersion,
            LastCheckUtc = _clock()
        };

        if (update is null)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.NoUpdate, null);
        }

        if (!string.IsNullOrEmpty(_state.SkippedVersion) &&
            string.Equals(_state.SkippedVersion, update.Version, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Skipped, update);
        }

        return new UpdateCheckResult(UpdateCheckOutcome.Available, update);
    }
}
