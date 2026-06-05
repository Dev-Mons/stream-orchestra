using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public enum UpdateCheckOutcome
{
    Disabled,
    Throttled,
    NoUpdate,
    Skipped,
    Available,
    Failed
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, AvailableUpdate? Update);

public sealed class UpdateService
{
    public static readonly TimeSpan DefaultMinimumCheckInterval = TimeSpan.FromHours(6);

    private readonly IUpdateChecker _checker;
    private readonly TimeSpan _minimumCheckInterval;
    private readonly Func<DateTimeOffset> _clock;
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

    public void SetEnabled(bool enabled)
    {
        _state = new AutoUpdateState
        {
            Enabled = enabled,
            SkippedVersion = _state.SkippedVersion,
            LastCheckUtc = _state.LastCheckUtc
        };
    }

    public Task DownloadAndApplyAsync(CancellationToken cancellationToken = default)
    {
        return _checker.DownloadAndApplyAsync(cancellationToken);
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(CancellationToken cancellationToken)
    {
        AvailableUpdate? update;
        try
        {
            update = await _checker.CheckForUpdateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, null);
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
