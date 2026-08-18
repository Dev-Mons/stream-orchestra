using System.Diagnostics;
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
    private readonly Func<long> _monotonicTimestampProvider;
    private readonly long _monotonicFrequency;
    private readonly ISyncTelemetryRecorder _syncTelemetryRecorder;
    private readonly SyncIntervalControlPolicy _intervalControlPolicy;
    private readonly ISyncBiasPriorService _biasPriorService;
    private bool _tickInProgress;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _recoveryStartedAtUtc;
    private DateTimeOffset? _stableSinceUtc;
    private bool _pausedForRecovery;
    private int _minimumSafetyDelayMs = SyncPresetNormalizationService.DefaultMinimumSafetyDelayMs;
    private int _effectiveSafetyDelayMs = SyncPresetNormalizationService.DefaultMinimumSafetyDelayMs;

    public StreamSyncCoordinator(
        IEnumerable<IStreamSyncTarget> targets,
        Func<long>? monotonicTimestampProvider = null,
        long monotonicFrequency = 0,
        ISyncTelemetryRecorder? syncTelemetryRecorder = null,
        SyncIntervalControlPolicy? intervalControlPolicy = null,
        ISyncBiasPriorService? biasPriorService = null)
    {
        _targets = targets.ToDictionary(target => target.SlotId);
        _monotonicTimestampProvider = monotonicTimestampProvider ?? Stopwatch.GetTimestamp;
        _monotonicFrequency = monotonicFrequency > 0 ? monotonicFrequency : Stopwatch.Frequency;
        _syncTelemetryRecorder = syncTelemetryRecorder ?? SyncTelemetryRecorder.Disabled;
        _intervalControlPolicy = intervalControlPolicy ?? new SyncIntervalControlPolicy();
        _biasPriorService = biasPriorService ?? DisabledSyncBiasPriorService.Instance;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += async (_, _) => await RunTimerTickAsync();
    }

    public event Action? StateChanged;

    public bool IsEnabled { get; private set; }

    public SyncRuntimeState RuntimeState { get; private set; } = SyncRuntimeState.Stopped;

    public int MemberCount => _members.Count;

    public int MinimumSafetyDelayMs => _minimumSafetyDelayMs;

    public int EffectiveSafetyDelayMs => _effectiveSafetyDelayMs;

    public SyncIntervalPolicyResult? LatestIntervalShadowResult { get; private set; }

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
                    SyncPresetNormalizationService.NormalizeDelayComponents(member),
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
                    DelayModelVersion = SyncManualDelaySchema.CurrentVersion,
                    AlgorithmPriorMs = member.AlgorithmPriorMs,
                    UserResidualMs = member.UserResidualMs,
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
            new SyncManualDelayComponents(0, 0, 0),
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

        var previous = member.DelayComponents;
        var finalDelay = Math.Clamp(
            manualDelayMs,
            -SyncPresetNormalizationService.MaximumManualDelayMs,
            SyncPresetNormalizationService.MaximumManualDelayMs);
        member.UserResidualMs = Math.Clamp(
            finalDelay - member.AlgorithmPriorMs,
            -SyncPresetNormalizationService.MaximumUserResidualMs,
            SyncPresetNormalizationService.MaximumUserResidualMs);
        member.ManualDelayMs = Math.Clamp(
            member.AlgorithmPriorMs + member.UserResidualMs,
            -SyncPresetNormalizationService.MaximumManualDelayMs,
            SyncPresetNormalizationService.MaximumManualDelayMs);
        member.LastHardSeekAtUtc = null;
        member.HardSeekCandidateTicks = 0;
        member.HardSeekCandidateDirection = 0;
        member.LastHardSeekEvidenceKey = "";
        member.ForceRealign = IsEnabled;
        if (_targets.TryGetValue(slotId, out var target) &&
            SyncPresetNormalizationService.CreateStreamKey(target.CurrentUrl) is { Length: > 0 } streamKey)
        {
            member.CalibratedStreamUrl = streamKey;
        }
        if (CreateBiasGroupMember(member) is { } biasMember)
        {
            _biasPriorService.RecordUserAdjustment(
                biasMember,
                previous,
                member.DelayComponents,
                DateTimeOffset.UtcNow);
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
            member.LastHardSeekEvidenceKey = "";
        }
        StateChanged?.Invoke();
        return true;
    }

    public bool MarkBiasSuggestionShown(int slotId)
    {
        if (!_members.TryGetValue(slotId, out var member) ||
            member.PendingSuggestion is not { } suggestion ||
            CreateBiasGroupMember(member) is not { } biasMember)
        {
            return false;
        }

        _biasPriorService.RecordSuggestionEvent(
            biasMember,
            suggestion,
            SyncBiasManualEventKind.SuggestionShown,
            member.DelayComponents,
            DateTimeOffset.UtcNow);
        return true;
    }

    public bool AcceptBiasSuggestion(int slotId)
    {
        if (!_members.TryGetValue(slotId, out var member) ||
            member.PendingSuggestion is not { } suggestion)
        {
            return false;
        }

        member.PreSuggestionComponents = member.DelayComponents;
        member.AcceptedSuggestion = suggestion;
        member.AlgorithmPriorMs = Math.Clamp(
            suggestion.SuggestedDelayMilliseconds,
            -SyncPresetNormalizationService.MaximumManualDelayMs,
            SyncPresetNormalizationService.MaximumManualDelayMs);
        member.UserResidualMs = 0;
        member.ManualDelayMs = member.AlgorithmPriorMs;
        member.PendingSuggestion = null;
        member.DismissedSuggestionId = "";
        PrepareMemberForRealign(member);
        if (CreateBiasGroupMember(member) is { } biasMember)
        {
            _biasPriorService.RecordSuggestionEvent(
                biasMember,
                suggestion,
                SyncBiasManualEventKind.SuggestionAccepted,
                member.DelayComponents,
                DateTimeOffset.UtcNow);
        }
        StateChanged?.Invoke();
        return true;
    }

    public bool RejectBiasSuggestion(int slotId)
    {
        if (!_members.TryGetValue(slotId, out var member) ||
            member.PendingSuggestion is not { } suggestion)
        {
            return false;
        }

        if (CreateBiasGroupMember(member) is { } biasMember)
        {
            _biasPriorService.RecordSuggestionEvent(
                biasMember,
                suggestion,
                SyncBiasManualEventKind.SuggestionRejected,
                member.DelayComponents,
                DateTimeOffset.UtcNow);
        }
        member.DismissedSuggestionId = suggestion.SuggestionId;
        member.PendingSuggestion = null;
        StateChanged?.Invoke();
        return true;
    }

    public bool RevertBiasSuggestion(int slotId)
    {
        if (!_members.TryGetValue(slotId, out var member) ||
            member.PreSuggestionComponents is not { } previous ||
            member.AcceptedSuggestion is not { } suggestion)
        {
            return false;
        }

        member.AlgorithmPriorMs = previous.AlgorithmPriorMilliseconds;
        member.UserResidualMs = previous.UserResidualMilliseconds;
        member.ManualDelayMs = previous.FinalDelayMilliseconds;
        member.PreSuggestionComponents = null;
        member.AcceptedSuggestion = null;
        member.DismissedSuggestionId = suggestion.SuggestionId;
        PrepareMemberForRealign(member);
        if (CreateBiasGroupMember(member) is { } biasMember)
        {
            _biasPriorService.RecordSuggestionEvent(
                biasMember,
                suggestion,
                SyncBiasManualEventKind.SuggestionReverted,
                member.DelayComponents,
                DateTimeOffset.UtcNow);
        }
        StateChanged?.Invoke();
        return true;
    }

    public bool ConfirmCurrentManualAlignment()
    {
        var members = _members.Values
            .Select(CreateBiasGroupMember)
            .OfType<SyncBiasGroupMember>()
            .ToArray();
        return _biasPriorService.RecordAlignmentConfirmation(members, DateTimeOffset.UtcNow);
    }

    private void PrepareMemberForRealign(MemberRuntime member)
    {
        member.LastHardSeekAtUtc = null;
        member.HardSeekCandidateTicks = 0;
        member.HardSeekCandidateDirection = 0;
        member.LastHardSeekEvidenceKey = "";
        member.ForceRealign = IsEnabled;
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
        member.AlgorithmPriorMs = 0;
        member.UserResidualMs = 0;
        member.PendingSuggestion = null;
        member.PreSuggestionComponents = null;
        member.AcceptedSuggestion = null;
        member.DismissedSuggestionId = "";
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
            member.LastHardSeekEvidenceKey = "";
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
        RefreshBiasSuggestions(now);
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

    private void RefreshBiasSuggestions(DateTimeOffset now)
    {
        if (!_biasPriorService.IsEnabled)
        {
            return;
        }

        var biasMembers = _members.Values
            .Select(CreateBiasGroupMember)
            .OfType<SyncBiasGroupMember>()
            .ToArray();
        var suggestions = _biasPriorService.GetCompatibleGroupSuggestions(biasMembers, now);
        foreach (var member in _members.Values)
        {
            if (!suggestions.TryGetValue(member.SlotId, out var suggestion) ||
                suggestion.SuggestionId.Equals(member.DismissedSuggestionId, StringComparison.Ordinal) ||
                suggestion.SuggestionId.Equals(
                    member.AcceptedSuggestion?.SuggestionId,
                    StringComparison.Ordinal))
            {
                member.PendingSuggestion = null;
                continue;
            }

            member.PendingSuggestion = suggestion;
        }
    }

    private SyncBiasGroupMember? CreateBiasGroupMember(MemberRuntime member)
    {
        if (!_targets.TryGetValue(member.SlotId, out var target))
        {
            return null;
        }

        var context = _biasPriorService.CreateContext(
            target.CurrentUrl,
            target.SyncQualityBucket,
            target.LatestTimeline?.CdnHostBucket);
        if (context is null || string.IsNullOrWhiteSpace(target.SyncBroadcastSessionIdentity))
        {
            return null;
        }

        return new SyncBiasGroupMember(
            member.SlotId,
            context,
            target.SyncBroadcastSessionIdentity,
            member.ManualDelayMs,
            member.AlgorithmPriorMs,
            member.UserResidualMs);
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

        var pauseCommands = controllable.Select(item =>
        {
            var command = new SyncCommand(SyncCommandType.Pause);
            return (item.Member, Command: command, Task: item.Target.ExecuteSyncCommandAsync(command));
        }).ToArray();
        var pauseResults = await Task.WhenAll(pauseCommands.Select(item => item.Task));
        for (var index = 0; index < pauseCommands.Length; index++)
        {
            pauseCommands[index].Member.LastCommandFailed = !IsCommandVerified(
                pauseCommands[index].Command,
                pauseResults[index]);
        }
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
        var resumeCommands = controllable.Select(item =>
        {
            var command = new SyncCommand(SyncCommandType.Resume);
            return (item.Member, Command: command, Task: item.Target.ExecuteSyncCommandAsync(command));
        }).ToArray();
        var resumeResults = await Task.WhenAll(resumeCommands.Select(item => item.Task));
        var resumeFailed = false;
        for (var index = 0; index < resumeCommands.Length; index++)
        {
            var verified = IsCommandVerified(resumeCommands[index].Command, resumeResults[index]);
            resumeCommands[index].Member.LastCommandFailed = !verified;
            resumeFailed |= !verified;
        }
        _recoveryStartedAtUtc = null;
        _pausedForRecovery = false;

        if (healthy.Length < 2)
        {
            await EnterWaitingStateAsync(now);
            return;
        }

        await AlignMembersAsync(now, healthy, forceSeek: true, resumeAfter: false);
        if (resumeFailed)
        {
            RuntimeState = SyncRuntimeState.Degraded;
            UpdateBadges(now);
            StateChanged?.Invoke();
        }
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
        var commands = new List<(MemberRuntime Member, SyncCommand Command, Task<SyncCommandResult> Task)>();
        LatestIntervalShadowResult = EvaluateIntervalShadow(now, active);

        foreach (var item in active)
        {
            var timeline = GetFreshTimeline(item.Target, now);
            var target = CalculateMediaTarget(item, timeline, commonEdgeUtcMs, now);
            if (target is null)
            {
                item.Member.LastErrorMs = null;
                RecordPairedDecision(now, item, null, null);
                continue;
            }

            var effectiveMediaTime = SyncPlayerMediaClock.GetEffectiveMediaTime(item.Snapshot);
            var errorMs = (effectiveMediaTime - target.Value) * 1000;
            item.Member.LastErrorMs = errorMs;
            errors.Add(errorMs);
            hasEstimatedMember |= timeline is null;

            if (item.Snapshot.Buffering)
            {
                item.Member.HardSeekCandidateTicks = 0;
                item.Member.HardSeekCandidateDirection = 0;
                item.Member.LastHardSeekEvidenceKey = "";
                continue;
            }

            var shouldForceSeek = forceSeek || item.Member.ForceRealign;
            item.Member.ForceRealign = false;
            var hardSeekEligible = IsHardSeekEligible(item, timeline);

            SyncCommand command;
            if (shouldForceSeek && Math.Abs(errorMs) > CorrectionDeadbandMs && hardSeekEligible)
            {
                command = new SyncCommand(SyncCommandType.Seek, target.Value);
                item.Member.HardSeekCandidateTicks = 0;
                item.Member.HardSeekCandidateDirection = 0;
                item.Member.LastHardSeekEvidenceKey = "";
            }
            else
            {
                command = CalculateCorrection(errorMs, target.Value);
                if (command.Type == SyncCommandType.Seek)
                {
                    if (!hardSeekEligible || string.IsNullOrWhiteSpace(timeline?.ProgressKeyHash))
                    {
                        item.Member.HardSeekCandidateTicks = 0;
                        item.Member.HardSeekCandidateDirection = 0;
                        item.Member.LastHardSeekEvidenceKey = "";
                        command = new SyncCommand(
                            SyncCommandType.SetRate,
                            CalculateRateCorrection(errorMs));
                    }
                    else
                    {
                        var direction = Math.Sign(errorMs);
                        var evidenceKey = $"{timeline.SourceEpoch}:{timeline.ProgressKeyHash}";
                        if (item.Member.HardSeekCandidateDirection != direction)
                        {
                            item.Member.HardSeekCandidateDirection = direction;
                            item.Member.HardSeekCandidateTicks = 0;
                            item.Member.LastHardSeekEvidenceKey = "";
                        }

                        if (!item.Member.LastHardSeekEvidenceKey.Equals(evidenceKey, StringComparison.Ordinal))
                        {
                            item.Member.HardSeekCandidateTicks++;
                            item.Member.LastHardSeekEvidenceKey = evidenceKey;
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
                }
                else
                {
                    item.Member.HardSeekCandidateTicks = 0;
                    item.Member.HardSeekCandidateDirection = 0;
                    item.Member.LastHardSeekEvidenceKey = "";
                }
            }

            if (command.Type == SyncCommandType.Seek)
            {
                if (!hardSeekEligible ||
                    !SyncMediaRangePolicy.IsHighConfidenceSeekTargetValid(
                        item.Snapshot,
                        target.Value))
                {
                    item.Member.LastCommandFailed = true;
                    RecordPairedDecision(now, item, target, null);
                    continue;
                }

                item.Member.HardSeekCandidateTicks = 0;
                item.Member.HardSeekCandidateDirection = 0;
                item.Member.LastHardSeekEvidenceKey = "";
            }

            item.Member.LastCommandFailed = false;
            RecordPairedDecision(now, item, target, command);
            commands.Add((item.Member, command, item.Target.ExecuteSyncCommandAsync(command)));
        }

        if (commands.Count > 0)
        {
            var results = await Task.WhenAll(commands.Select(item => item.Task));
            var recoveryTasks = new List<Task<SyncCommandResult>>();
            for (var index = 0; index < commands.Count; index++)
            {
                var issued = commands[index];
                var result = results[index];
                var verified = IsCommandVerified(issued.Command, result);
                issued.Member.LastCommandFailed = !verified;
                if (!verified)
                {
                    recoveryTasks.Add(_targets[issued.Member.SlotId]
                        .ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.ResetRate)));
                }
                if (verified && issued.Command.Type == SyncCommandType.Seek)
                {
                    issued.Member.LastHardSeekAtUtc = now;
                }
            }

            if (recoveryTasks.Count > 0)
            {
                await Task.WhenAll(recoveryTasks);
            }
        }
        if (resumeAfter)
        {
            var resumeCommands = active.Select(item =>
            {
                var command = new SyncCommand(SyncCommandType.Resume);
                return (item.Member, Command: command, Task: item.Target.ExecuteSyncCommandAsync(command));
            }).ToArray();
            var resumeResults = await Task.WhenAll(resumeCommands.Select(item => item.Task));
            var resumeRecoveryTasks = new List<Task<SyncCommandResult>>();
            for (var index = 0; index < resumeCommands.Length; index++)
            {
                if (!IsCommandVerified(resumeCommands[index].Command, resumeResults[index]))
                {
                    resumeCommands[index].Member.LastCommandFailed = true;
                    resumeRecoveryTasks.Add(_targets[resumeCommands[index].Member.SlotId]
                        .ExecuteSyncCommandAsync(new SyncCommand(SyncCommandType.ResetRate)));
                }
            }

            if (resumeRecoveryTasks.Count > 0)
            {
                await Task.WhenAll(resumeRecoveryTasks);
            }
        }

        var hasExcludedMember = _members.Values.Any(member => member.IsTemporarilyExcluded);
        RuntimeState = hasEstimatedMember || hasExcludedMember || _members.Values.Any(member => member.LastCommandFailed)
            ? SyncRuntimeState.Degraded
            : SyncRuntimeState.Running;

        UpdateStableSafety(now, baseSafety, errors);
        UpdateBadges(now);
        StateChanged?.Invoke();
    }

    private SyncIntervalPolicyResult EvaluateIntervalShadow(
        DateTimeOffset now,
        IReadOnlyList<ControllableMember> active)
    {
        var members = active.Select(item =>
        {
            var timeline = GetFreshTimeline(item.Target, now);
            var hasTimeline = timeline is not null;
            var offset = hasTimeline
                ? ResolveMediaToUtcOffset(timeline!, item.Snapshot)
                : double.NaN;
            return new SyncIntervalMemberInput
            {
                SlotId = item.Member.SlotId,
                PlayableRanges = SyncMediaRangePolicy.GetPlayableRanges(item.Snapshot),
                CurrentMediaTimeSeconds = SyncPlayerMediaClock.GetEffectiveMediaTime(item.Snapshot),
                MediaToGroupOffsetMilliseconds = offset,
                ManualDelayMilliseconds = item.Member.ManualDelayMs,
                TimelineFresh = hasTimeline,
                EpochStable = timeline?.IsEpochStable == true,
                SourceStable = timeline is not null &&
                               timeline.Source == SyncTimelineSource.ProgramDateTime &&
                               !string.IsNullOrWhiteSpace(timeline.PlaylistIdentityHash),
                PlayerHealthy = item.Snapshot.PlayerProgressHealthy && !item.Snapshot.Seeking,
                HasFullRangeObservation = item.Snapshot.SeekableRanges.Count > 0 &&
                                          item.Snapshot.BufferedRanges.Count > 0,
                HardSeekEvidenceEligible = IsHardSeekEligible(item, timeline),
                // These uncertainty terms deliberately stay unknown until held-out calibration.
                TimelineUncertaintyMilliseconds = null,
                BiasUncertaintyMilliseconds = null,
                ControllabilityUncertaintyMilliseconds = null
            };
        }).ToArray();
        return _intervalControlPolicy.Evaluate(new SyncIntervalPolicyRequest
        {
            Members = members,
            SafetyDelayMilliseconds = _effectiveSafetyDelayMs
        });
    }

    private void RecordPairedDecision(
        DateTimeOffset now,
        ControllableMember item,
        double? activeTarget,
        SyncCommand? activeCommand)
    {
        if (!_syncTelemetryRecorder.IsEnabled || LatestIntervalShadowResult is null)
        {
            return;
        }

        var candidate = LatestIntervalShadowResult.Members.FirstOrDefault(
            member => member.SlotId == item.Member.SlotId);
        if (candidate is null)
        {
            return;
        }

        var interval = LatestIntervalShadowResult.SelectedCommonInterval;
        var tickIdentity = $"{now.UtcTicks}:{LatestIntervalShadowResult.TargetGroupTimeMilliseconds}";
        var timeline = item.Target.LatestTimeline;
        _syncTelemetryRecorder.RecordDecision(new SyncDecisionTelemetry(
            _syncTelemetryRecorder.SessionId,
            item.Member.SlotId,
            $"{tickIdentity}:{item.Member.SlotId}",
            Math.Max(0, timeline?.SourceEpoch ?? 0),
            new SyncTelemetryClockSample(now.ToUniversalTime(), _monotonicTimestampProvider()),
            new SyncPolicyDecision(
                "legacy",
                RuntimeState == SyncRuntimeState.Degraded ? "degraded" : "running",
                activeTarget,
                ToTelemetryCommand(activeCommand?.Type),
                activeCommand?.Value,
                activeCommand?.Type == SyncCommandType.Seek,
                "legacy-decision"),
            new SyncPolicyDecision(
                "interval-v1",
                ToTelemetryPolicyState(LatestIntervalShadowResult.State),
                candidate.TargetMediaTimeSeconds,
                candidate.ProposedCommand,
                candidate.ProposedValue,
                candidate.HardSeekAllowed,
                candidate.Reason,
                interval?.StartMilliseconds,
                interval?.EndMilliseconds,
                candidate.CombinedUncertaintyMilliseconds),
            CandidateIsShadowOnly: true,
            TickId: tickIdentity));
    }

    private static string ToTelemetryCommand(SyncCommandType? commandType) => commandType switch
    {
        SyncCommandType.Seek => "seek",
        SyncCommandType.SetRate => "rate",
        SyncCommandType.Pause => "pause",
        SyncCommandType.Resume => "resume",
        SyncCommandType.ResetRate => "reset-rate",
        _ => "none"
    };

    private static string ToTelemetryPolicyState(SyncIntervalPolicyState state) => state switch
    {
        SyncIntervalPolicyState.Shadow => "shadow",
        SyncIntervalPolicyState.Suppressed => "suppressed",
        SyncIntervalPolicyState.NoIntersection => "degraded",
        SyncIntervalPolicyState.Degraded => "degraded",
        SyncIntervalPolicyState.Disabled => "waiting",
        _ => "unknown"
    };

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
            var mappedTarget = (commonEdgeAtSnapshotUtcMs - _effectiveSafetyDelayMs - mediaToUtcOffsetMs) / 1000 -
                               manualDelaySec;
            return SyncMediaRangePolicy.IsSeekTargetValid(item.Snapshot, mappedTarget, marginSeconds: 0)
                ? mappedTarget
                : null;
        }

        var estimatedTarget = item.Snapshot.SeekableEnd!.Value - _effectiveSafetyDelayMs / 1000d - manualDelaySec;
        return SyncMediaRangePolicy.IsSeekTargetValid(item.Snapshot, estimatedTarget, marginSeconds: 0)
            ? estimatedTarget
            : null;
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
                    !IsSnapshotFresh(snapshot, now) ||
                    !snapshot.HasVideo ||
                    !snapshot.IsSoop ||
                    !snapshot.PlayerProgressHealthy ||
                    snapshot.Seeking)
                {
                    return null;
                }

                var seekableRanges = SyncMediaRangePolicy.GetSeekableRanges(snapshot);
                var currentRange = SyncMediaRangePolicy.FindContainingRange(
                    seekableRanges,
                    SyncPlayerMediaClock.GetEffectiveMediaTime(snapshot));
                if (seekableRanges.Count == 0 || currentRange is null)
                {
                    return null;
                }

                snapshot = snapshot with
                {
                    SeekableRanges = seekableRanges,
                    SeekableStart = seekableRanges[0].StartSeconds,
                    SeekableEnd = seekableRanges[^1].EndSeconds,
                    CurrentSeekableRange = currentRange
                };
                return new ControllableMember(member, target, snapshot);
            })
            .OfType<ControllableMember>()
            .ToArray();
    }

    private bool IsHardSeekEligible(
        ControllableMember item,
        TimelineObservation? timeline) =>
        timeline is not null &&
        timeline.Source == SyncTimelineSource.ProgramDateTime &&
        timeline.IsEpochStable &&
        timeline.IndependentEvidenceCount >= 2 &&
        timeline.NetworkCapability == SyncNetworkObservationCapability.CdpCorrelated &&
        timeline.CdpHardSeekGatePassed &&
        !string.IsNullOrWhiteSpace(timeline.PlaylistIdentityHash) &&
        !string.IsNullOrWhiteSpace(timeline.ProgressKeyHash) &&
        IsMonotonicallyFresh(
            timeline.ObservedMonotonicTicks,
            timeline.MonotonicFrequency,
            GetTimelineFreshness(timeline)) &&
        IsMonotonicallyFresh(
            item.Snapshot.HostReceivedMonotonicTicks,
            item.Snapshot.HostMonotonicFrequency,
            SnapshotFreshness) &&
        item.Snapshot.MediaClockSource == SyncPlayerClockSource.RequestVideoFrameCallback &&
        item.Snapshot.SeekableRanges.Count > 0 &&
        item.Snapshot.BufferedRanges.Count > 0 &&
        item.Snapshot.PlayerProgressHealthy &&
        !item.Snapshot.Seeking;

    private static bool IsCommandVerified(SyncCommand command, SyncCommandResult result) =>
        result.CommandId.Equals(command.CommandId, StringComparison.Ordinal) &&
        result.WasApplied &&
        result.WasVerified &&
        result.Stage == SyncCommandStage.Verified &&
        command.Type switch
        {
            SyncCommandType.Seek => command.Value is { } target &&
                                    result.ObservedMediaTimeSeconds is { } mediaTime &&
                                    Math.Abs(mediaTime - target) <= 0.5,
            SyncCommandType.SetRate => command.Value is { } target &&
                                       result.ObservedPlaybackRate is { } rate &&
                                       Math.Abs(rate - target) <= 0.005,
            SyncCommandType.ResetRate => result.ObservedPlaybackRate is { } rate &&
                                         Math.Abs(rate - 1) <= 0.005,
            SyncCommandType.Pause => result.ObservedPaused == true,
            SyncCommandType.Resume => result.ObservedPaused == false,
            _ => false
        };

    private TimelineObservation? GetFreshTimeline(IStreamSyncTarget target, DateTimeOffset now)
    {
        if (target.LatestTimeline is not { } timeline)
        {
            return null;
        }

        var freshness = GetTimelineFreshness(timeline);
        var hasMonotonicClock = HasCompatibleMonotonicClock(
            timeline.ObservedMonotonicTicks,
            timeline.MonotonicFrequency);
        var monotonicAge = hasMonotonicClock
            ? GetMonotonicAge(timeline.ObservedMonotonicTicks, timeline.MonotonicFrequency)
            : null;
        var isFresh = hasMonotonicClock
            ? monotonicAge is { } age && age <= freshness
            : now >= timeline.ObservedAtUtc && now - timeline.ObservedAtUtc <= freshness;
        return isFresh ? timeline : null;
    }

    private static TimeSpan GetTimelineFreshness(TimelineObservation timeline) =>
        double.IsFinite(timeline.StaleAfterSeconds) && timeline.StaleAfterSeconds > 0
            ? TimeSpan.FromSeconds(timeline.StaleAfterSeconds)
            : TimelineFreshness;

    private bool IsSnapshotFresh(SyncMemberSnapshot snapshot, DateTimeOffset now)
    {
        var hasMonotonicClock = HasCompatibleMonotonicClock(
            snapshot.HostReceivedMonotonicTicks,
            snapshot.HostMonotonicFrequency);
        var monotonicAge = hasMonotonicClock
            ? GetMonotonicAge(snapshot.HostReceivedMonotonicTicks, snapshot.HostMonotonicFrequency)
            : null;
        return hasMonotonicClock
            ? monotonicAge is { } age && age <= SnapshotFreshness
            : now >= snapshot.ObservedAtUtc && now - snapshot.ObservedAtUtc <= SnapshotFreshness;
    }

    private bool IsMonotonicallyFresh(
        long observedTicks,
        long observedFrequency,
        TimeSpan freshness) =>
        GetMonotonicAge(observedTicks, observedFrequency) is { } age && age <= freshness;

    private TimeSpan? GetMonotonicAge(long observedTicks, long observedFrequency)
    {
        if (!HasCompatibleMonotonicClock(observedTicks, observedFrequency))
        {
            return null;
        }

        var nowTicks = _monotonicTimestampProvider();
        if (nowTicks < observedTicks)
        {
            return null;
        }

        return TimeSpan.FromSeconds((nowTicks - observedTicks) / (double)_monotonicFrequency);
    }

    private bool HasCompatibleMonotonicClock(long observedTicks, long observedFrequency) =>
        observedTicks > 0 &&
        observedFrequency > 0 &&
        observedFrequency == _monotonicFrequency;

    private SyncMemberViewState CreateMemberViewState(MemberRuntime member, DateTimeOffset now)
    {
        var target = _targets[member.SlotId];
        var snapshot = target.LatestSyncSnapshot;
        var ready = snapshot is not null &&
                    IsSnapshotFresh(snapshot, now) &&
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
                    : source == SyncTimelineSource.ProgramDateTime
                        ? "플랫폼 시각"
                        : "CDN 추정";

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
            statusText,
            member.AlgorithmPriorMs,
            member.UserResidualMs,
            member.PendingSuggestion?.SuggestedDelayMilliseconds,
            member.PendingSuggestion?.SuggestionId ?? "",
            member.PendingSuggestion?.IndependentSessionSupport ?? 0,
            member.PendingSuggestion?.HierarchyLevel ?? SyncBiasHierarchyLevel.None,
            member.PreSuggestionComponents is not null && member.AcceptedSuggestion is not null);
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
        public MemberRuntime(
            int slotId,
            SyncManualDelayComponents delayComponents,
            string calibratedStreamUrl)
        {
            SlotId = slotId;
            AlgorithmPriorMs = delayComponents.AlgorithmPriorMilliseconds;
            UserResidualMs = delayComponents.UserResidualMilliseconds;
            ManualDelayMs = delayComponents.FinalDelayMilliseconds;
            CalibratedStreamUrl = calibratedStreamUrl;
        }

        public int SlotId { get; }

        public int ManualDelayMs { get; set; }

        public int AlgorithmPriorMs { get; set; }

        public int UserResidualMs { get; set; }

        public SyncManualDelayComponents DelayComponents => new(
            AlgorithmPriorMs,
            UserResidualMs,
            ManualDelayMs);

        public SyncBiasSuggestion? PendingSuggestion { get; set; }

        public SyncBiasSuggestion? AcceptedSuggestion { get; set; }

        public SyncManualDelayComponents? PreSuggestionComponents { get; set; }

        public string DismissedSuggestionId { get; set; } = "";

        public string CalibratedStreamUrl { get; set; }

        public double? LastErrorMs { get; set; }

        public DateTimeOffset? BufferingSinceUtc { get; set; }

        public DateTimeOffset? LastHardSeekAtUtc { get; set; }

        public int HardSeekCandidateTicks { get; set; }

        public int HardSeekCandidateDirection { get; set; }

        public string LastHardSeekEvidenceKey { get; set; } = "";

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
