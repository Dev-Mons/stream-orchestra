using System.Windows.Threading;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class StreamSyncCoordinator
{
    private const int MaximumEffectiveSafetyDelayMs = 30000;
    private const double CorrectionDeadbandMs = 350;
    private const double HardSeekThresholdMs = 1500;
    private const int HardSeekConfirmationTicks = 3;
    private static readonly TimeSpan SnapshotFreshness = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TimelineFreshness = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PreparationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BufferRecoveryMinimum = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan BufferRecoveryMaximum = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StableWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HardSeekCooldown = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyDictionary<int, IStreamSyncTarget> _targets;
    private readonly Dictionary<int, MemberRuntime> _members = [];
    private readonly DispatcherTimer _timer;
    private bool _tickInProgress;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _recoveryStartedAtUtc;
    private DateTimeOffset? _stableSinceUtc;
    private bool _pausedForRecovery;
    private int _minimumSafetyDelayMs = SyncPresetNormalizationService.DefaultMinimumSafetyDelayMs;
    private int _effectiveSafetyDelayMs = SyncPresetNormalizationService.DefaultMinimumSafetyDelayMs;

    public StreamSyncCoordinator(IEnumerable<IStreamSyncTarget> targets)
    {
        _targets = targets.ToDictionary(target => target.SlotId);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += async (_, _) => await RunTimerTickAsync();
    }

    public event Action? StateChanged;

    public bool IsEnabled { get; private set; }

    public SyncRuntimeState RuntimeState { get; private set; } = SyncRuntimeState.Stopped;

    public int MemberCount => _members.Count;

    public int MinimumSafetyDelayMs => _minimumSafetyDelayMs;

    public int EffectiveSafetyDelayMs => _effectiveSafetyDelayMs;

    public bool ContainsMember(int slotId) => _members.ContainsKey(slotId);

    public void LoadPreset(SyncGroupPreset? preset)
    {
        var normalized = SyncPresetNormalizationService.Normalize(preset);
        _members.Clear();
        foreach (var member in normalized.Members)
        {
            if (_targets.ContainsKey(member.SlotId))
            {
                _members[member.SlotId] = new MemberRuntime(
                    member.SlotId,
                    member.ManualDelayMs,
                    member.CalibratedStreamUrl);
            }
        }

        _minimumSafetyDelayMs = normalized.MinimumSafetyDelayMs;
        _effectiveSafetyDelayMs = _minimumSafetyDelayMs;
        RuntimeState = SyncRuntimeState.Stopped;
        IsEnabled = false;
        UpdateBadges(DateTimeOffset.UtcNow);
        StateChanged?.Invoke();
    }

    public SyncGroupPreset CapturePreset()
    {
        return SyncPresetNormalizationService.Normalize(new SyncGroupPreset
        {
            MinimumSafetyDelayMs = _minimumSafetyDelayMs,
            Members = _members.Values
                .OrderBy(member => member.SlotId)
                .Select(member => new SyncMemberPreset
                {
                    SlotId = member.SlotId,
                    ManualDelayMs = member.ManualDelayMs,
                    CalibratedStreamUrl = member.CalibratedStreamUrl
                })
                .ToArray()
        });
    }

    public bool AddMember(int slotId)
    {
        if (_members.ContainsKey(slotId) ||
            _members.Count >= SlotProfileGroupMapping.MaxSlotCount ||
            !_targets.TryGetValue(slotId, out var target))
        {
            return false;
        }

        _members[slotId] = new MemberRuntime(
            slotId,
            manualDelayMs: 0,
            SyncPresetNormalizationService.CreateStreamKey(target.CurrentUrl));
        if (IsEnabled)
        {
            _members[slotId].IsTemporarilyExcluded = true;
            _members[slotId].RejoinReadyTicks = 0;
        }
        StateChanged?.Invoke();
        return true;
    }

    public async Task<bool> RemoveMemberAsync(int slotId)
    {
        if (!_members.Remove(slotId) || !_targets.TryGetValue(slotId, out var target))
        {
            return false;
        }

        await target.ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.ResetRate));
        if (_pausedForRecovery)
        {
            await target.ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.Resume));
        }
        target.SetSyncBadge(new SyncBadgeState(false, RuntimeState, SyncTimelineSource.None, null, ""));

        if (IsEnabled && _members.Count < 2)
        {
            await EnterWaitingStateAsync(DateTimeOffset.UtcNow);
            return true;
        }

        StateChanged?.Invoke();
        return true;
    }

    public bool SetManualDelay(int slotId, int manualDelayMs)
    {
        if (!_members.TryGetValue(slotId, out var member))
        {
            return false;
        }

        member.ManualDelayMs = Math.Clamp(
            manualDelayMs,
            -SyncPresetNormalizationService.MaximumManualDelayMs,
            SyncPresetNormalizationService.MaximumManualDelayMs);
        member.LastHardSeekAtUtc = null;
        member.HardSeekCandidateTicks = 0;
        member.HardSeekCandidateDirection = 0;
        member.ForceRealign = IsEnabled;
        if (_targets.TryGetValue(slotId, out var target) &&
            SyncPresetNormalizationService.CreateStreamKey(target.CurrentUrl) is { Length: > 0 } streamKey)
        {
            member.CalibratedStreamUrl = streamKey;
        }
        StateChanged?.Invoke();
        return true;
    }

    public bool SetMinimumSafetyDelay(int minimumSafetyDelayMs)
    {
        var clamped = Math.Clamp(
            minimumSafetyDelayMs,
            SyncPresetNormalizationService.MinimumSafetyDelayMs,
            SyncPresetNormalizationService.MaximumSafetyDelayMs);
        if (_minimumSafetyDelayMs == clamped)
        {
            return false;
        }

        _minimumSafetyDelayMs = clamped;
        _effectiveSafetyDelayMs = Math.Max(_effectiveSafetyDelayMs, _minimumSafetyDelayMs);
        _stableSinceUtc = DateTimeOffset.UtcNow;
        foreach (var member in _members.Values)
        {
            member.ForceRealign = IsEnabled;
            member.HardSeekCandidateTicks = 0;
            member.HardSeekCandidateDirection = 0;
        }
        StateChanged?.Invoke();
        return true;
    }

    public bool ReconcileMemberStreamIdentity(int slotId)
    {
        if (!_members.TryGetValue(slotId, out var member) || !_targets.TryGetValue(slotId, out var target))
        {
            return false;
        }

        var nextKey = SyncPresetNormalizationService.CreateStreamKey(target.CurrentUrl);
        if (string.IsNullOrWhiteSpace(nextKey))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(member.CalibratedStreamUrl))
        {
            member.CalibratedStreamUrl = nextKey;
            return true;
        }

        if (member.CalibratedStreamUrl.Equals(nextKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        member.CalibratedStreamUrl = nextKey;
        member.ManualDelayMs = 0;
        member.LastErrorMs = null;
        return true;
    }

    public async Task<bool> StartAsync()
    {
        if (_members.Count < 2)
        {
            return false;
        }

        IsEnabled = true;
        RuntimeState = SyncRuntimeState.Preparing;
        var now = DateTimeOffset.UtcNow;
        _startedAtUtc = now;
        _stableSinceUtc = now;
        _recoveryStartedAtUtc = null;
        _pausedForRecovery = false;
        foreach (var member in _members.Values)
        {
            member.IsTemporarilyExcluded = false;
            member.BufferingSinceUtc = null;
            member.RejoinReadyTicks = 0;
            member.LastErrorMs = null;
            member.HardSeekCandidateTicks = 0;
            member.HardSeekCandidateDirection = 0;
            member.ForceRealign = false;
        }

        _effectiveSafetyDelayMs = CalculateBaseSafetyDelay(now, GetControllableMembers(now));
        _timer.Start();
        StateChanged?.Invoke();
        await TickAsync(now);
        return true;
    }

    public async Task StopAsync()
    {
        _timer.Stop();
        IsEnabled = false;
        RuntimeState = SyncRuntimeState.Stopped;
        _startedAtUtc = null;
        _recoveryStartedAtUtc = null;
        _stableSinceUtc = null;

        var resumePausedMembers = _pausedForRecovery;
        _pausedForRecovery = false;
        await Task.WhenAll(_members.Keys.Select(async slotId =>
        {
            if (_targets.TryGetValue(slotId, out var target))
            {
                await target.ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.ResetRate));
                if (resumePausedMembers)
                {
                    await target.ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.Resume));
                }
                target.SetSyncBadge(new SyncBadgeState(false, RuntimeState, SyncTimelineSource.None, null, ""));
            }
        }));

        StateChanged?.Invoke();
    }

    public async Task TickAsync(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            return;
        }

        foreach (var slotId in _members.Keys.ToArray())
        {
            ReconcileMemberStreamIdentity(slotId);
        }

        var controllable = GetControllableMembers(now);
        if (await TryRejoinRecoveredMembersAsync(now, controllable))
        {
            return;
        }

        var active = controllable.Where(item => !item.Member.IsTemporarilyExcluded).ToArray();
        if (active.Length < 2)
        {
            if (RuntimeState == SyncRuntimeState.Preparing &&
                now - (_startedAtUtc ?? now) < PreparationTimeout)
            {
                UpdateBadges(now);
                StateChanged?.Invoke();
                return;
            }

            await EnterWaitingStateAsync(now);
            return;
        }

        var allConfiguredMembersReady = _members.Count == active.Length;
        if (RuntimeState == SyncRuntimeState.Preparing &&
            !allConfiguredMembersReady &&
            now - (_startedAtUtc ?? now) < PreparationTimeout)
        {
            UpdateBadges(now);
            StateChanged?.Invoke();
            return;
        }

        if (RuntimeState == SyncRuntimeState.Recovering)
        {
            await ContinueRecoveryAsync(now, controllable);
            return;
        }

        foreach (var item in active)
        {
            if (item.Snapshot.Buffering)
            {
                item.Member.BufferingSinceUtc ??= now;
                var bufferingDuration = now - item.Member.BufferingSinceUtc;
                var hasSignificantDrift = item.Member.LastErrorMs is { } lastError &&
                                          Math.Abs(lastError) >= HardSeekThresholdMs;
                if ((bufferingDuration >= BufferRecoveryMinimum && hasSignificantDrift) ||
                    bufferingDuration >= BufferRecoveryMaximum)
                {
                    await BeginRecoveryAsync(now, controllable);
                    return;
                }
            }
            else
            {
                item.Member.BufferingSinceUtc = null;
            }
        }

        var firstAlignment = RuntimeState is SyncRuntimeState.Preparing or SyncRuntimeState.Waiting;
        await AlignMembersAsync(now, active, forceSeek: firstAlignment, resumeAfter: firstAlignment);
    }

    public SyncGroupViewState CreateViewState(DateTimeOffset? nowOverride = null)
    {
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var members = _members.Values
            .OrderBy(member => member.SlotId)
            .Select(member => CreateMemberViewState(member, now))
            .ToArray();
        var notice = RuntimeState switch
        {
            SyncRuntimeState.Stopped when members.Length < 2 => "방송을 2개 이상 추가해 주세요.",
            SyncRuntimeState.Stopped => "그룹이 준비되었습니다. 시작을 누르면 동기화합니다.",
            SyncRuntimeState.Preparing => "HLS 시간축과 플레이어 상태를 확인하고 있습니다.",
            SyncRuntimeState.Waiting => "동기화 가능한 SOOP 방송 2개를 기다리고 있습니다.",
            SyncRuntimeState.Recovering => "버퍼를 확보한 뒤 그룹 전체를 다시 정렬합니다.",
            SyncRuntimeState.Degraded => "일부 방송은 라이브 엣지 기반 추정 모드입니다.",
            _ => "동기화가 실행 중입니다."
        };

        return new SyncGroupViewState(
            RuntimeState,
            IsEnabled,
            _minimumSafetyDelayMs,
            _effectiveSafetyDelayMs,
            members.Count(member => member.IsReady && !member.IsTemporarilyExcluded),
            members,
            notice);
    }

    public static SyncCommand CalculateCorrection(double errorMs, double mediaTarget)
    {
        if (Math.Abs(errorMs) >= HardSeekThresholdMs)
        {
            return new SyncCommand(SyncCommandType.Seek, mediaTarget);
        }

        return new SyncCommand(SyncCommandType.SetRate, CalculateRateCorrection(errorMs));
    }

    private async Task RunTimerTickAsync()
    {
        if (_tickInProgress)
        {
            return;
        }

        _tickInProgress = true;
        try
        {
            await TickAsync(DateTimeOffset.UtcNow);
        }
        finally
        {
            _tickInProgress = false;
        }
    }

    private async Task<bool> TryRejoinRecoveredMembersAsync(
        DateTimeOffset now,
        IReadOnlyList<ControllableMember> controllable)
    {
        var recovered = false;
        foreach (var item in controllable.Where(item => item.Member.IsTemporarilyExcluded))
        {
            if (!item.Snapshot.Buffering && item.Snapshot.ReadyState >= 3 && item.Snapshot.BufferSec >= 1)
            {
                item.Member.RejoinReadyTicks++;
                if (item.Member.RejoinReadyTicks >= 2)
                {
                    item.Member.IsTemporarilyExcluded = false;
                    item.Member.RejoinReadyTicks = 0;
                    recovered = true;
                }
            }
            else
            {
                item.Member.RejoinReadyTicks = 0;
            }
        }

        if (!recovered || controllable.Count(item => !item.Member.IsTemporarilyExcluded) < 2)
        {
            return false;
        }

        await BeginRecoveryAsync(now, controllable);
        return true;
    }

    private async Task EnterWaitingStateAsync(DateTimeOffset now)
    {
        var changed = RuntimeState != SyncRuntimeState.Waiting;
        RuntimeState = SyncRuntimeState.Waiting;
        if (changed)
        {
            var resumePausedMembers = _pausedForRecovery;
            _pausedForRecovery = false;
            await Task.WhenAll(_members.Keys.Select(async slotId =>
            {
                await _targets[slotId].ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.ResetRate));
                if (resumePausedMembers)
                {
                    await _targets[slotId].ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.Resume));
                }
            }));
        }

        UpdateBadges(now);
        StateChanged?.Invoke();
    }

    private async Task BeginRecoveryAsync(
        DateTimeOffset now,
        IReadOnlyList<ControllableMember> controllable)
    {
        RuntimeState = SyncRuntimeState.Recovering;
        _pausedForRecovery = true;
        _recoveryStartedAtUtc = now;
        _stableSinceUtc = now;
        var segmentValues = controllable
            .Select(item => GetFreshTimeline(item.Target, now)?.SegmentDurationSec * 1000)
            .OfType<double>()
            .Where(value => value > 0)
            .OrderBy(value => value)
            .ToArray();
        var segmentMs = segmentValues.Length == 0 ? 1000 : segmentValues[segmentValues.Length / 2];
        _effectiveSafetyDelayMs = Math.Clamp(
            _effectiveSafetyDelayMs + (int)Math.Max(1000, segmentMs),
            _minimumSafetyDelayMs,
            MaximumEffectiveSafetyDelayMs);

        await Task.WhenAll(controllable.Select(item =>
            item.Target.ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.Pause))));
        UpdateBadges(now);
        StateChanged?.Invoke();
    }

    private async Task ContinueRecoveryAsync(
        DateTimeOffset now,
        IReadOnlyList<ControllableMember> controllable)
    {
        var active = controllable.Where(item => !item.Member.IsTemporarilyExcluded).ToArray();
        var recovered = active.Length >= 2 && active.All(item =>
            !item.Snapshot.Buffering && item.Snapshot.ReadyState >= 3 && item.Snapshot.BufferSec >= 1);

        if (recovered)
        {
            await AlignMembersAsync(now, active, forceSeek: true, resumeAfter: true);
            _recoveryStartedAtUtc = null;
            _pausedForRecovery = false;
            return;
        }

        if (now - (_recoveryStartedAtUtc ?? now) < RecoveryTimeout)
        {
            UpdateBadges(now);
            StateChanged?.Invoke();
            return;
        }

        foreach (var item in active.Where(item =>
                     item.Snapshot.Buffering || item.Snapshot.ReadyState < 3 || item.Snapshot.BufferSec < 1))
        {
            item.Member.IsTemporarilyExcluded = true;
            item.Member.RejoinReadyTicks = 0;
        }

        var healthy = controllable.Where(item => !item.Member.IsTemporarilyExcluded).ToArray();
        await Task.WhenAll(controllable.Select(item =>
            item.Target.ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.Resume))));
        _recoveryStartedAtUtc = null;
        _pausedForRecovery = false;

        if (healthy.Length < 2)
        {
            await EnterWaitingStateAsync(now);
            return;
        }

        await AlignMembersAsync(now, healthy, forceSeek: true, resumeAfter: false);
    }

    private async Task AlignMembersAsync(
        DateTimeOffset now,
        IReadOnlyList<ControllableMember> active,
        bool forceSeek,
        bool resumeAfter)
    {
        var baseSafety = CalculateBaseSafetyDelay(now, active);
        _effectiveSafetyDelayMs = Math.Max(_effectiveSafetyDelayMs, baseSafety);
        var absoluteEdges = active
            .Select(item => GetFreshTimeline(item.Target, now))
            .Where(timeline => timeline is not null)
            .Select(timeline => ProjectEdgeUtcMs(timeline!, now))
            .ToArray();
        var commonEdgeUtcMs = absoluteEdges.Length == 0 ? (double?)null : absoluteEdges.Min();
        var hasEstimatedMember = false;
        var errors = new List<double>();
        var commands = new List<Task>();

        foreach (var item in active)
        {
            var timeline = GetFreshTimeline(item.Target, now);
            var target = CalculateMediaTarget(item, timeline, commonEdgeUtcMs, now);
            if (target is null)
            {
                item.Member.LastErrorMs = null;
                continue;
            }

            var errorMs = (item.Snapshot.CurrentTime - target.Value) * 1000;
            item.Member.LastErrorMs = errorMs;
            errors.Add(errorMs);
            hasEstimatedMember |= timeline is null;

            if (item.Snapshot.Buffering)
            {
                item.Member.HardSeekCandidateTicks = 0;
                item.Member.HardSeekCandidateDirection = 0;
                continue;
            }

            var shouldForceSeek = forceSeek || item.Member.ForceRealign;
            item.Member.ForceRealign = false;

            SyncCommand command;
            if (shouldForceSeek && Math.Abs(errorMs) > CorrectionDeadbandMs)
            {
                command = new SyncCommand(SyncCommandType.Seek, target.Value);
                item.Member.HardSeekCandidateTicks = 0;
                item.Member.HardSeekCandidateDirection = 0;
            }
            else
            {
                command = CalculateCorrection(errorMs, target.Value);
                if (command.Type == SyncCommandType.Seek)
                {
                    var direction = Math.Sign(errorMs);
                    if (item.Member.HardSeekCandidateDirection == direction)
                    {
                        item.Member.HardSeekCandidateTicks++;
                    }
                    else
                    {
                        item.Member.HardSeekCandidateDirection = direction;
                        item.Member.HardSeekCandidateTicks = 1;
                    }
                    var isConfirmed = item.Member.HardSeekCandidateTicks >= HardSeekConfirmationTicks;
                    var isCoolingDown = item.Member.LastHardSeekAtUtc is { } lastSeek &&
                                        now - lastSeek < HardSeekCooldown;
                    if (!isConfirmed || isCoolingDown)
                    {
                        command = new SyncCommand(
                            SyncCommandType.SetRate,
                            CalculateRateCorrection(errorMs));
                    }
                }
                else
                {
                    item.Member.HardSeekCandidateTicks = 0;
                    item.Member.HardSeekCandidateDirection = 0;
                }
            }

            if (command.Type == SyncCommandType.Seek)
            {
                if (!IsSeekTargetValid(item.Snapshot, target.Value))
                {
                    item.Member.LastCommandFailed = true;
                    continue;
                }

                item.Member.LastHardSeekAtUtc = now;
                item.Member.HardSeekCandidateTicks = 0;
                item.Member.HardSeekCandidateDirection = 0;
            }

            item.Member.LastCommandFailed = false;
            commands.Add(item.Target.ExecuteSyncCommandAsync(command));
        }

        await Task.WhenAll(commands);
        if (resumeAfter)
        {
            await Task.WhenAll(active.Select(item =>
                item.Target.ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.Resume))));
        }

        var hasExcludedMember = _members.Values.Any(member => member.IsTemporarilyExcluded);
        RuntimeState = hasEstimatedMember || hasExcludedMember || _members.Values.Any(member => member.LastCommandFailed)
            ? SyncRuntimeState.Degraded
            : SyncRuntimeState.Running;

        UpdateStableSafety(now, baseSafety, errors);
        UpdateBadges(now);
        StateChanged?.Invoke();
    }

    private double? CalculateMediaTarget(
        ControllableMember item,
        TimelineObservation? timeline,
        double? commonEdgeUtcMs,
        DateTimeOffset now)
    {
        var manualDelaySec = item.Member.ManualDelayMs / 1000d;
        if (timeline is not null && commonEdgeUtcMs is not null)
        {
            var mediaToUtcOffsetMs = ResolveMediaToUtcOffset(timeline, item.Snapshot);
            var snapshotAgeMs = Math.Max(0, (now - item.Snapshot.ObservedAtUtc).TotalMilliseconds);
            var commonEdgeAtSnapshotUtcMs = commonEdgeUtcMs.Value - snapshotAgeMs;
            return (commonEdgeAtSnapshotUtcMs - _effectiveSafetyDelayMs - mediaToUtcOffsetMs) / 1000 -
                   manualDelaySec;
        }

        return item.Snapshot.SeekableEnd - _effectiveSafetyDelayMs / 1000d - manualDelaySec;
    }

    private static double ResolveMediaToUtcOffset(
        TimelineObservation timeline,
        SyncMemberSnapshot snapshot)
    {
        var edgeUtcMs = ProjectEdgeUtcMs(timeline, snapshot.ObservedAtUtc);
        if (timeline.MediaToUtcOffsetMs is { } parsedOffset && snapshot.SeekableEnd is { } seekableEnd)
        {
            var impliedEdgeMediaTime = (edgeUtcMs - parsedOffset) / 1000;
            var toleranceSec = Math.Max(5, timeline.SegmentDurationSec * 3);
            if (Math.Abs(impliedEdgeMediaTime - seekableEnd) <= toleranceSec)
            {
                return parsedOffset;
            }
        }

        return edgeUtcMs - snapshot.SeekableEnd!.Value * 1000;
    }

    private static double ProjectEdgeUtcMs(TimelineObservation timeline, DateTimeOffset atUtc)
    {
        var elapsedMs = Math.Max(0, (atUtc - timeline.ObservedAtUtc).TotalMilliseconds);
        return timeline.EdgeUtc.ToUnixTimeMilliseconds() + elapsedMs;
    }

    private static double CalculateRateCorrection(double errorMs)
    {
        if (Math.Abs(errorMs) <= CorrectionDeadbandMs)
        {
            return 1;
        }

        return Math.Clamp(1 - errorMs / 1000 * 0.02, 0.98, 1.02);
    }

    private int CalculateBaseSafetyDelay(
        DateTimeOffset now,
        IReadOnlyList<ControllableMember> active)
    {
        var segmentDurations = active
            .Select(item => GetFreshTimeline(item.Target, now)?.SegmentDurationSec)
            .OfType<double>()
            .Where(duration => duration > 0)
            .OrderBy(duration => duration)
            .ToArray();
        var medianSegmentMs = segmentDurations.Length == 0
            ? 0
            : segmentDurations[segmentDurations.Length / 2] * 2000;
        var hasEstimatedMember = active.Any(item => GetFreshTimeline(item.Target, now) is null);

        return Math.Clamp(
            (int)Math.Max(
                Math.Max(_minimumSafetyDelayMs, medianSegmentMs),
                hasEstimatedMember ? 5000 : 0),
            _minimumSafetyDelayMs,
            MaximumEffectiveSafetyDelayMs);
    }

    private void UpdateStableSafety(DateTimeOffset now, int baseSafety, IReadOnlyList<double> errors)
    {
        if (errors.Count == 0 || errors.Any(error => Math.Abs(error) > 300))
        {
            _stableSinceUtc = now;
            return;
        }

        _stableSinceUtc ??= now;
        if (now - _stableSinceUtc < StableWindow)
        {
            return;
        }

        _effectiveSafetyDelayMs = Math.Max(baseSafety, _effectiveSafetyDelayMs - 500);
        _stableSinceUtc = now;
    }

    private IReadOnlyList<ControllableMember> GetControllableMembers(DateTimeOffset now)
    {
        return _members.Values
            .Select(member =>
            {
                if (!_targets.TryGetValue(member.SlotId, out var target) ||
                    target.LatestSyncSnapshot is not { } snapshot ||
                    now - snapshot.ObservedAtUtc > SnapshotFreshness ||
                    !snapshot.HasVideo ||
                    !snapshot.IsSoop ||
                    snapshot.SeekableStart is null ||
                    snapshot.SeekableEnd is null ||
                    snapshot.SeekableEnd <= snapshot.SeekableStart)
                {
                    return null;
                }

                return new ControllableMember(member, target, snapshot);
            })
            .OfType<ControllableMember>()
            .ToArray();
    }

    private static bool IsSeekTargetValid(SyncMemberSnapshot snapshot, double mediaTarget)
    {
        return snapshot.SeekableStart is { } start &&
               snapshot.SeekableEnd is { } end &&
               mediaTarget >= start + 0.05 &&
               mediaTarget <= end - 0.05;
    }

    private static TimelineObservation? GetFreshTimeline(IStreamSyncTarget target, DateTimeOffset now)
    {
        return target.LatestTimeline is { } timeline && now - timeline.ObservedAtUtc <= TimelineFreshness
            ? timeline
            : null;
    }

    private SyncMemberViewState CreateMemberViewState(MemberRuntime member, DateTimeOffset now)
    {
        var target = _targets[member.SlotId];
        var snapshot = target.LatestSyncSnapshot;
        var ready = snapshot is not null &&
                    now - snapshot.ObservedAtUtc <= SnapshotFreshness &&
                    snapshot.HasVideo && snapshot.IsSoop &&
                    snapshot.SeekableStart is not null && snapshot.SeekableEnd is not null;
        var timeline = ready ? GetFreshTimeline(target, now) : null;
        var source = !ready
            ? SyncTimelineSource.None
            : timeline?.Source ?? SyncTimelineSource.LiveEdgeEstimate;
        var statusText = !ready
            ? "신호 대기"
            : member.IsTemporarilyExcluded
                ? "복구 대기"
                : source == SyncTimelineSource.LiveEdgeEstimate
                    ? "추정"
                    : "절대";

        return new SyncMemberViewState(
            member.SlotId,
            target.SyncDisplayName,
            target.CurrentUrl,
            ready,
            member.IsTemporarilyExcluded,
            source,
            snapshot?.BufferSec,
            member.LastErrorMs,
            member.ManualDelayMs,
            statusText);
    }

    private void UpdateBadges(DateTimeOffset now)
    {
        foreach (var member in _members.Values)
        {
            if (!_targets.TryGetValue(member.SlotId, out var target))
            {
                continue;
            }

            if (!IsEnabled)
            {
                target.SetSyncBadge(new SyncBadgeState(false, RuntimeState, SyncTimelineSource.None, null, ""));
                continue;
            }

            var view = CreateMemberViewState(member, now);
            var streamName = string.IsNullOrWhiteSpace(target.SyncDisplayName)
                ? "이름 없는 방송"
                : target.SyncDisplayName;
            var text = RuntimeState == SyncRuntimeState.Recovering
                ? $"SYNC · {streamName} · 버퍼 복구"
                : !view.IsReady || view.IsTemporarilyExcluded
                    ? $"SYNC · {streamName} · 신호 대기"
                    : view.TimelineSource == SyncTimelineSource.LiveEdgeEstimate
                        ? $"SYNC 추정 · {streamName}"
                        : $"SYNC · {streamName}";
            target.SetSyncBadge(new SyncBadgeState(
                true,
                RuntimeState,
                view.TimelineSource,
                view.ErrorMs,
                text));
        }
    }

    private sealed class MemberRuntime
    {
        public MemberRuntime(int slotId, int manualDelayMs, string calibratedStreamUrl)
        {
            SlotId = slotId;
            ManualDelayMs = manualDelayMs;
            CalibratedStreamUrl = calibratedStreamUrl;
        }

        public int SlotId { get; }

        public int ManualDelayMs { get; set; }

        public string CalibratedStreamUrl { get; set; }

        public double? LastErrorMs { get; set; }

        public DateTimeOffset? BufferingSinceUtc { get; set; }

        public DateTimeOffset? LastHardSeekAtUtc { get; set; }

        public int HardSeekCandidateTicks { get; set; }

        public int HardSeekCandidateDirection { get; set; }

        public bool ForceRealign { get; set; }

        public bool LastCommandFailed { get; set; }

        public bool IsTemporarilyExcluded { get; set; }

        public int RejoinReadyTicks { get; set; }
    }

    private sealed record ControllableMember(
        MemberRuntime Member,
        IStreamSyncTarget Target,
        SyncMemberSnapshot Snapshot);
}
