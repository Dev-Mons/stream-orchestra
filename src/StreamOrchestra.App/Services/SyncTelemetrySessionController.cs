using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

/// <summary>
/// Keeps telemetry disabled until a caller explicitly starts a bounded session.
/// The stable wrapper can be injected once while the active recorder changes at runtime.
/// </summary>
public sealed class SyncTelemetrySessionController : ISyncTelemetryRecorder
{
    private readonly ReaderWriterLockSlim _gate = new(LockRecursionPolicy.NoRecursion);
    private ISyncTelemetryRecorder _activeRecorder = SyncTelemetryRecorder.Disabled;
    private SyncTelemetrySnapshot? _completedSnapshot;

    public event Action<bool>? EnabledChanged;

    public bool IsEnabled => Volatile.Read(ref _activeRecorder).IsEnabled;

    public string SessionId => Volatile.Read(ref _activeRecorder).SessionId;

    public bool HasCompletedSession => GetCompletedSnapshot() is not null;

    public SyncTelemetrySnapshot? CompletedSnapshot => GetCompletedSnapshot();

    public bool StartSession(
        SyncTelemetryRecorderOptions? options = null,
        ISyncTelemetryClock? clock = null)
    {
        var started = false;
        _gate.EnterWriteLock();
        try
        {
            if (_activeRecorder.IsEnabled)
            {
                return false;
            }

            var recorder = SyncTelemetryRecorder.CreateEnabled(options, clock);
            _completedSnapshot = null;
            Volatile.Write(ref _activeRecorder, recorder);
            started = true;
        }
        finally
        {
            _gate.ExitWriteLock();
        }

        if (started)
        {
            EnabledChanged?.Invoke(true);
        }

        return started;
    }

    public SyncTelemetrySnapshot? StopSession()
    {
        SyncTelemetrySnapshot? completed = null;
        _gate.EnterWriteLock();
        try
        {
            if (!_activeRecorder.IsEnabled)
            {
                return null;
            }

            _completedSnapshot = _activeRecorder.CreateSnapshot();
            completed = _completedSnapshot;
            Volatile.Write(ref _activeRecorder, SyncTelemetryRecorder.Disabled);
        }
        finally
        {
            _gate.ExitWriteLock();
        }

        EnabledChanged?.Invoke(false);
        return completed;
    }

    public bool DeleteCompletedSession()
    {
        _gate.EnterWriteLock();
        try
        {
            if (_completedSnapshot is null)
            {
                return false;
            }

            _completedSnapshot = null;
            return true;
        }
        finally
        {
            _gate.ExitWriteLock();
        }
    }

    public SyncUrlIdentity CreateUrlIdentity(string? rawUrl) =>
        WithActiveRecorder(recorder => recorder.CreateUrlIdentity(rawUrl));

    public void RecordSession(SyncSessionTelemetry value) =>
        WithActiveRecorder(recorder => recorder.RecordSession(value));

    public void RecordNetwork(SyncNetworkTelemetry value) =>
        WithActiveRecorder(recorder => recorder.RecordNetwork(value));

    public void RecordPlaylist(SyncPlaylistTelemetry value) =>
        WithActiveRecorder(recorder => recorder.RecordPlaylist(value));

    public void RecordPlayer(SyncPlayerTelemetry value) =>
        WithActiveRecorder(recorder => recorder.RecordPlayer(value));

    public void RecordEstimate(SyncEstimateTelemetry value) =>
        WithActiveRecorder(recorder => recorder.RecordEstimate(value));

    public void RecordDecision(SyncDecisionTelemetry value) =>
        WithActiveRecorder(recorder => recorder.RecordDecision(value));

    public void RecordAction(SyncActionTelemetry value) =>
        WithActiveRecorder(recorder => recorder.RecordAction(value));

    public void RecordManualEvent(SyncManualEventTelemetry value) =>
        WithActiveRecorder(recorder => recorder.RecordManualEvent(value));

    public SyncTelemetrySnapshot CreateSnapshot() =>
        WithActiveRecorder(recorder => recorder.CreateSnapshot());

    public SyncTelemetrySummary CreateSummary() =>
        WithActiveRecorder(recorder => recorder.CreateSummary());

    private SyncTelemetrySnapshot? GetCompletedSnapshot()
    {
        _gate.EnterReadLock();
        try
        {
            return _completedSnapshot;
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    private TResult WithActiveRecorder<TResult>(Func<ISyncTelemetryRecorder, TResult> action)
    {
        _gate.EnterReadLock();
        try
        {
            return action(_activeRecorder);
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    private void WithActiveRecorder(Action<ISyncTelemetryRecorder> action)
    {
        _gate.EnterReadLock();
        try
        {
            action(_activeRecorder);
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }
}
