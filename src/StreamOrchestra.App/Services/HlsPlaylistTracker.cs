using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class HlsPlaylistTracker
{
    private readonly Dictionary<string, TrackState> _states = new(StringComparer.Ordinal);

    public HlsPlaylistTrackingResult Observe(HlsPlaylistTrackingContext context)
    {
        ArgumentNullException.ThrowIfNull(context.Document);
        ArgumentNullException.ThrowIfNull(context.ProgressKey);
        if (context.SlotId <= 0 || context.ObservedMonotonicTicks < 0 || context.MonotonicFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }

        var lane = string.IsNullOrWhiteSpace(context.TimelineLaneIdentity)
            ? context.PlaylistIdentityHash
            : context.TimelineLaneIdentity;
        var stateKey = $"{context.SlotId}:{lane}";
        if (!_states.TryGetValue(stateKey, out var state))
        {
            var isEvidence = IsUsableEvidence(context.Document);
            state = TrackState.Create(context, epoch: 1, evidenceCount: isEvidence ? 1 : 0);
            _states[stateKey] = state;
            return Result(
                state,
                HlsProgressDisposition.NewEvidence,
                HlsEpochResetReason.InitialObservation,
                isEvidence,
                isStable: false);
        }

        var resetReason = FindContextResetReason(state, context);
        if (resetReason != HlsEpochResetReason.None)
        {
            var isEvidence = IsUsableEvidence(context.Document);
            state = TrackState.Create(context, state.Epoch + 1, evidenceCount: isEvidence ? 1 : 0);
            _states[stateKey] = state;
            return Result(
                state,
                HlsProgressDisposition.NewEvidence,
                resetReason,
                isEvidence,
                isStable: false);
        }

        if (state.ProgressKey.LastDiscontinuitySequence is { } previousDiscontinuity &&
            context.ProgressKey.LastDiscontinuitySequence is { } nextDiscontinuity &&
            nextDiscontinuity > previousDiscontinuity)
        {
            var isEvidence = IsUsableEvidence(context.Document);
            state = TrackState.Create(context, state.Epoch + 1, evidenceCount: isEvidence ? 1 : 0);
            _states[stateKey] = state;
            return Result(
                state,
                HlsProgressDisposition.NewEvidence,
                HlsEpochResetReason.Discontinuity,
                isEvidence,
                isStable: false);
        }

        if (IsRollback(state.ProgressKey, context.ProgressKey))
        {
            if (state.PendingRollbackProgress is not null &&
                IsForwardProgress(state.PendingRollbackProgress, context.ProgressKey))
            {
                var isEvidence = IsUsableEvidence(context.Document);
                state = TrackState.Create(context, state.Epoch + 1, evidenceCount: isEvidence ? 1 : 0);
                _states[stateKey] = state;
                return Result(
                    state,
                    HlsProgressDisposition.NewEvidence,
                    HlsEpochResetReason.SequenceRollback,
                    isEvidence,
                    isStable: false);
            }

            state = state with { PendingRollbackProgress = context.ProgressKey };
            _states[stateKey] = state;
            return Result(
                state,
                HlsProgressDisposition.Rollback,
                HlsEpochResetReason.None,
                isEvidence: false,
                state.EvidenceCount >= 2);
        }

        if (state.ProgressKey.LastDiscontinuitySequence != context.ProgressKey.LastDiscontinuitySequence)
        {
            var isEvidence = IsUsableEvidence(context.Document);
            state = TrackState.Create(context, state.Epoch + 1, evidenceCount: isEvidence ? 1 : 0);
            _states[stateKey] = state;
            return Result(
                state,
                HlsProgressDisposition.NewEvidence,
                HlsEpochResetReason.Discontinuity,
                isEvidence,
                isStable: false);
        }

        if (state.ProgressKey.PersistenceHash.Equals(
                context.ProgressKey.PersistenceHash,
                StringComparison.Ordinal))
        {
            var unchangedTicks = Math.Max(0, context.ObservedMonotonicTicks - state.LastProgressMonotonicTicks);
            var unchangedSeconds = unchangedTicks / (double)context.MonotonicFrequency;
            var staleAfterSeconds = Math.Max(0.001, (context.Document.TargetDurationSeconds ?? 6) * 1.5);
            var disposition = unchangedSeconds > staleAfterSeconds
                ? HlsProgressDisposition.Stale
                : HlsProgressDisposition.Duplicate;
            return Result(
                state,
                disposition,
                HlsEpochResetReason.None,
                isEvidence: false,
                state.EvidenceCount >= 2);
        }

        var acceptedAsEvidence = IsUsableEvidence(context.Document);
        state = TrackState.Create(
            context,
            state.Epoch,
            state.EvidenceCount + (acceptedAsEvidence ? 1 : 0));
        _states[stateKey] = state;
        return Result(
            state,
            HlsProgressDisposition.NewEvidence,
            HlsEpochResetReason.None,
            acceptedAsEvidence,
            state.EvidenceCount >= 2);
    }

    private static HlsEpochResetReason FindContextResetReason(
        TrackState state,
        HlsPlaylistTrackingContext context)
    {
        if (state.NavigationGeneration != context.NavigationGeneration)
        {
            return HlsEpochResetReason.Navigation;
        }

        if (!state.SessionIdentity.Equals(context.SessionIdentity, StringComparison.Ordinal))
        {
            return HlsEpochResetReason.Session;
        }

        if (state.RenditionKind != context.RenditionKind ||
            !state.PlaylistIdentityHash.Equals(context.PlaylistIdentityHash, StringComparison.Ordinal))
        {
            return HlsEpochResetReason.Rendition;
        }

        if (!state.SourceIdentity.Equals(context.SourceIdentity, StringComparison.Ordinal))
        {
            return HlsEpochResetReason.Source;
        }

        return HlsEpochResetReason.None;
    }

    private static bool IsRollback(HlsProgressKey previous, HlsProgressKey next)
    {
        if (previous.LastDiscontinuitySequence is { } previousDiscontinuity &&
            next.LastDiscontinuitySequence is { } nextDiscontinuity &&
            nextDiscontinuity < previousDiscontinuity)
        {
            return true;
        }

        if (previous.LastMediaSequence is not { } previousSequence ||
            next.LastMediaSequence is not { } nextSequence)
        {
            return false;
        }

        if (nextSequence != previousSequence)
        {
            return nextSequence < previousSequence;
        }

        return previous.LastPartIndex is { } previousPart &&
               next.LastPartIndex is { } nextPart &&
               nextPart < previousPart;
    }

    private static bool IsForwardProgress(HlsProgressKey previous, HlsProgressKey next)
    {
        if (previous.LastDiscontinuitySequence is { } previousDiscontinuity &&
            next.LastDiscontinuitySequence is { } nextDiscontinuity &&
            nextDiscontinuity != previousDiscontinuity)
        {
            return nextDiscontinuity > previousDiscontinuity;
        }

        if (previous.LastMediaSequence is not { } previousSequence ||
            next.LastMediaSequence is not { } nextSequence)
        {
            return false;
        }

        if (nextSequence != previousSequence)
        {
            return nextSequence > previousSequence;
        }

        return previous.LastPartIndex is { } previousPart &&
               next.LastPartIndex is { } nextPart &&
               nextPart > previousPart;
    }

    private static bool IsUsableEvidence(HlsPlaylistDocument document)
    {
        if (document.Kind != HlsPlaylistKind.Media || document.Segments.Count == 0)
        {
            return false;
        }

        var tail = document.Segments[^1];
        return !tail.IsGap && (document.TrailingParts.Count == 0 || !document.TrailingParts[^1].IsGap);
    }

    private static HlsPlaylistTrackingResult Result(
        TrackState state,
        HlsProgressDisposition disposition,
        HlsEpochResetReason resetReason,
        bool isEvidence,
        bool isStable) => new(
        state.Epoch,
        disposition,
        resetReason,
        isEvidence,
        isStable,
        state.EvidenceCount);

    private sealed record TrackState(
        long Epoch,
        int NavigationGeneration,
        string SessionIdentity,
        string PlaylistIdentityHash,
        HlsRenditionKind RenditionKind,
        string SourceIdentity,
        HlsProgressKey ProgressKey,
        long LastProgressMonotonicTicks,
        int EvidenceCount,
        HlsProgressKey? PendingRollbackProgress)
    {
        public static TrackState Create(
            HlsPlaylistTrackingContext context,
            long epoch,
            int evidenceCount) => new(
            epoch,
            context.NavigationGeneration,
            context.SessionIdentity,
            context.PlaylistIdentityHash,
            context.RenditionKind,
            context.SourceIdentity,
            context.ProgressKey,
            context.ObservedMonotonicTicks,
            evidenceCount,
            null);
    }
}

/// <summary>
/// Unwraps only validated presentation timestamps supplied by a container/parser contract. Private HLS
/// extension values must never be passed to this class without separate validation of provenance and units.
/// </summary>
public sealed class HlsTimestampUnwrapper
{
    private readonly long _modulus;
    private readonly long _halfModulus;
    private long? _lastRaw;
    private long? _lastUnwrapped;

    public HlsTimestampUnwrapper(int bitWidth = 33)
    {
        if (bitWidth is <= 1 or >= 63)
        {
            throw new ArgumentOutOfRangeException(nameof(bitWidth));
        }

        _modulus = 1L << bitWidth;
        _halfModulus = _modulus / 2;
    }

    public HlsTimestampUnwrapResult Observe(long rawValue, bool explicitDiscontinuity = false)
    {
        if (rawValue < 0 || rawValue >= _modulus)
        {
            throw new ArgumentOutOfRangeException(nameof(rawValue));
        }

        if (explicitDiscontinuity || _lastRaw is null || _lastUnwrapped is null)
        {
            _lastRaw = rawValue;
            _lastUnwrapped = rawValue;
            return new HlsTimestampUnwrapResult(rawValue, false, explicitDiscontinuity);
        }

        var delta = rawValue - _lastRaw.Value;
        var rollover = false;
        if (delta < -_halfModulus)
        {
            delta += _modulus;
            rollover = true;
        }
        else if (delta > _halfModulus)
        {
            delta -= _modulus;
        }

        var unwrapped = checked(_lastUnwrapped.Value + delta);
        _lastRaw = rawValue;
        _lastUnwrapped = unwrapped;
        return new HlsTimestampUnwrapResult(unwrapped, rollover, false);
    }
}
