using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

/// <summary>
/// Computes interval-controller proposals only. The application never dispatches these commands; even
/// OptInPreview remains a preview until held-out calibration and a separate production gate exist.
/// </summary>
public sealed record SyncIntervalControlPolicyOptions
{
    public SyncIntervalControllerMode Mode { get; init; } = SyncIntervalControllerMode.Shadow;

    public double CorrectionDeadbandMilliseconds { get; init; } = 350;

    public double HardSeekThresholdMilliseconds { get; init; } = 1500;

    public double MinimumPlaybackRate { get; init; } = 0.98;

    public double MaximumPlaybackRate { get; init; } = 1.02;

    public double RateGainPerSecond { get; init; } = 0.02;

    public double TargetMarginMilliseconds { get; init; } = 50;

    public double? HardSeekMaximumCombinedUncertaintyMilliseconds { get; init; }
}

public sealed class SyncIntervalControlPolicy
{
    private readonly SyncIntervalControlPolicyOptions _options;

    public SyncIntervalControlPolicy(SyncIntervalControlPolicyOptions? options = null)
    {
        _options = options ?? new SyncIntervalControlPolicyOptions();
        if (!IsPositiveFinite(_options.CorrectionDeadbandMilliseconds) ||
            !IsPositiveFinite(_options.HardSeekThresholdMilliseconds) ||
            !IsPositiveFinite(_options.RateGainPerSecond) ||
            !IsPositiveFinite(_options.TargetMarginMilliseconds) ||
            !double.IsFinite(_options.MinimumPlaybackRate) ||
            !double.IsFinite(_options.MaximumPlaybackRate) ||
            _options.MinimumPlaybackRate <= 0 ||
            _options.MaximumPlaybackRate < _options.MinimumPlaybackRate ||
            (_options.HardSeekMaximumCombinedUncertaintyMilliseconds is { } threshold &&
             !IsNonNegativeFinite(threshold)))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    public SyncIntervalPolicyResult Evaluate(SyncIntervalPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_options.Mode == SyncIntervalControllerMode.Disabled)
        {
            return Result(
                SyncIntervalPolicyState.Disabled,
                "low-confidence",
                request.Members,
                []);
        }

        if (request.Members.Count < 2 || request.SafetyDelayMilliseconds < 0)
        {
            return Result(
                SyncIntervalPolicyState.Degraded,
                "invalid-range",
                request.Members,
                []);
        }

        var orderedMembers = request.Members.OrderBy(member => member.SlotId).ToArray();
        var suppressionReason = FindSuppressionReason(orderedMembers);
        if (suppressionReason is not null)
        {
            return Result(
                SyncIntervalPolicyState.Suppressed,
                suppressionReason,
                orderedMembers,
                []);
        }

        var mapped = new List<IReadOnlyList<SyncTimeIntervalMilliseconds>>(orderedMembers.Length);
        foreach (var member in orderedMembers)
        {
            var mappingUncertainty = ValidUncertainty(member.TimelineUncertaintyMilliseconds) +
                                     ValidUncertainty(member.BiasUncertaintyMilliseconds);
            var intervals = SyncMediaRangePolicy.Normalize(member.PlayableRanges)
                .Select(range => new SyncTimeIntervalMilliseconds(
                    range.StartSeconds * 1000 + member.MediaToGroupOffsetMilliseconds +
                    member.ManualDelayMilliseconds + mappingUncertainty,
                    range.EndSeconds * 1000 + member.MediaToGroupOffsetMilliseconds +
                    member.ManualDelayMilliseconds - mappingUncertainty))
                .Where(range => range.IsValid)
                .ToArray();
            if (intervals.Length == 0)
            {
                return Result(
                    SyncIntervalPolicyState.Degraded,
                    "invalid-range",
                    orderedMembers,
                    []);
            }

            mapped.Add(intervals);
        }

        var intersections = mapped[0];
        foreach (var intervals in mapped.Skip(1))
        {
            intersections = Intersect(intersections, intervals);
            if (intersections.Count == 0)
            {
                return Result(
                    SyncIntervalPolicyState.NoIntersection,
                    "no-intersection",
                    orderedMembers,
                    []);
            }
        }

        var eligibleIntervals = intersections
            .Where(interval => interval.EndMilliseconds - interval.StartMilliseconds >=
                               _options.TargetMarginMilliseconds * 2)
            .ToArray();
        if (eligibleIntervals.Length == 0)
        {
            return Result(
                SyncIntervalPolicyState.NoIntersection,
                "no-intersection",
                orderedMembers,
                intersections);
        }

        var selected = eligibleIntervals[^1];
        var targetGroupTime = Math.Clamp(
            selected.EndMilliseconds - request.SafetyDelayMilliseconds,
            selected.StartMilliseconds + _options.TargetMarginMilliseconds,
            selected.EndMilliseconds - _options.TargetMarginMilliseconds);
        var decisions = orderedMembers
            .Select(member => Decide(member, targetGroupTime))
            .ToArray();
        return new SyncIntervalPolicyResult
        {
            Mode = _options.Mode,
            State = SyncIntervalPolicyState.Shadow,
            IsShadowOnly = true,
            CommonPlayableIntervals = eligibleIntervals,
            SelectedCommonInterval = selected,
            TargetGroupTimeMilliseconds = targetGroupTime,
            Members = decisions,
            Reason = decisions.Any(decision => decision.Reason == "low-confidence")
                ? "low-confidence"
                : decisions.Any(decision => decision.Reason == "hard-seek")
                    ? "hard-seek"
                    : decisions.All(decision => decision.Reason == "deadband")
                        ? "deadband"
                        : "bounded-rate"
        };
    }

    private SyncIntervalMemberDecision Decide(
        SyncIntervalMemberInput member,
        double targetGroupTimeMilliseconds)
    {
        var targetMediaTime = (targetGroupTimeMilliseconds -
                               member.MediaToGroupOffsetMilliseconds -
                               member.ManualDelayMilliseconds) / 1000;
        var targetValid = SyncMediaRangePolicy.FindContainingRange(
            member.PlayableRanges,
            targetMediaTime,
            _options.TargetMarginMilliseconds / 1000) is not null;
        if (!targetValid)
        {
            return SuppressedMember(member, "invalid-range", targetMediaTime);
        }

        if (!HasCalibratedUncertainty(member))
        {
            return SuppressedMember(member, "low-confidence", targetMediaTime);
        }

        var combinedUncertainty = member.TimelineUncertaintyMilliseconds!.Value +
                                  member.BiasUncertaintyMilliseconds!.Value +
                                  member.ControllabilityUncertaintyMilliseconds!.Value;
        var error = (member.CurrentMediaTimeSeconds - targetMediaTime) * 1000;
        var reliableError = Math.Max(0, Math.Abs(error) - combinedUncertainty);
        if (reliableError <= _options.CorrectionDeadbandMilliseconds)
        {
            return new SyncIntervalMemberDecision
            {
                SlotId = member.SlotId,
                TargetMediaTimeSeconds = targetMediaTime,
                ErrorMilliseconds = error,
                ProposedCommand = "rate",
                ProposedValue = 1,
                HardSeekAllowed = false,
                Reason = "deadband",
                CombinedUncertaintyMilliseconds = combinedUncertainty
            };
        }

        var hardSeekAllowed = Math.Abs(error) >= _options.HardSeekThresholdMilliseconds &&
                              member.HardSeekEvidenceEligible &&
                              _options.HardSeekMaximumCombinedUncertaintyMilliseconds is { } maximum &&
                              combinedUncertainty <= maximum;
        if (hardSeekAllowed)
        {
            return new SyncIntervalMemberDecision
            {
                SlotId = member.SlotId,
                TargetMediaTimeSeconds = targetMediaTime,
                ErrorMilliseconds = error,
                ProposedCommand = "seek",
                ProposedValue = targetMediaTime,
                HardSeekAllowed = true,
                Reason = "hard-seek",
                CombinedUncertaintyMilliseconds = combinedUncertainty
            };
        }

        var signedReliableError = Math.Sign(error) * reliableError;
        var rate = Math.Clamp(
            1 - signedReliableError / 1000 * _options.RateGainPerSecond,
            _options.MinimumPlaybackRate,
            _options.MaximumPlaybackRate);
        return new SyncIntervalMemberDecision
        {
            SlotId = member.SlotId,
            TargetMediaTimeSeconds = targetMediaTime,
            ErrorMilliseconds = error,
            ProposedCommand = "rate",
            ProposedValue = rate,
            HardSeekAllowed = false,
            Reason = "bounded-rate",
            CombinedUncertaintyMilliseconds = combinedUncertainty
        };
    }

    private static string? FindSuppressionReason(IReadOnlyList<SyncIntervalMemberInput> members)
    {
        if (members.Any(member => !member.TimelineFresh))
        {
            return "stale";
        }

        if (members.Any(member => !member.SourceStable))
        {
            return "source-reset";
        }

        if (members.Any(member => !member.EpochStable))
        {
            return "epoch-unstable";
        }

        if (members.Any(member => !member.PlayerHealthy || !member.HasFullRangeObservation))
        {
            return "invalid-range";
        }

        if (members.Any(member =>
                !double.IsFinite(member.CurrentMediaTimeSeconds) ||
                !double.IsFinite(member.MediaToGroupOffsetMilliseconds) ||
                member.PlayableRanges.Count == 0))
        {
            return "invalid-range";
        }

        return null;
    }

    private static bool HasCalibratedUncertainty(SyncIntervalMemberInput member) =>
        member.TimelineUncertaintyMilliseconds is { } timeline &&
        IsNonNegativeFinite(timeline) &&
        member.BiasUncertaintyMilliseconds is { } bias &&
        IsNonNegativeFinite(bias) &&
        member.ControllabilityUncertaintyMilliseconds is { } controllability &&
        IsNonNegativeFinite(controllability);

    private static double ValidUncertainty(double? value) =>
        value is { } uncertainty && IsNonNegativeFinite(uncertainty) ? uncertainty : 0;

    private SyncIntervalPolicyResult Result(
        SyncIntervalPolicyState state,
        string reason,
        IReadOnlyList<SyncIntervalMemberInput> members,
        IReadOnlyList<SyncTimeIntervalMilliseconds> intersections) => new()
    {
        Mode = _options.Mode,
        State = state,
        IsShadowOnly = true,
        CommonPlayableIntervals = intersections,
        Members = members
            .OrderBy(member => member.SlotId)
            .Select(member => SuppressedMember(member, reason))
            .ToArray(),
        Reason = reason
    };

    private static SyncIntervalMemberDecision SuppressedMember(
        SyncIntervalMemberInput member,
        string reason,
        double? target = null) => new()
    {
        SlotId = member.SlotId,
        TargetMediaTimeSeconds = target,
        ProposedCommand = "rate",
        ProposedValue = 1,
        HardSeekAllowed = false,
        Reason = reason
    };

    private static IReadOnlyList<SyncTimeIntervalMilliseconds> Intersect(
        IReadOnlyList<SyncTimeIntervalMilliseconds> left,
        IReadOnlyList<SyncTimeIntervalMilliseconds> right)
    {
        var result = new List<SyncTimeIntervalMilliseconds>();
        foreach (var first in left)
        {
            foreach (var second in right)
            {
                var start = Math.Max(first.StartMilliseconds, second.StartMilliseconds);
                var end = Math.Min(first.EndMilliseconds, second.EndMilliseconds);
                if (end > start)
                {
                    result.Add(new SyncTimeIntervalMilliseconds(start, end));
                }
            }
        }

        return NormalizeIntervals(result);
    }

    private static IReadOnlyList<SyncTimeIntervalMilliseconds> NormalizeIntervals(
        IEnumerable<SyncTimeIntervalMilliseconds> source)
    {
        var ordered = source
            .Where(interval => interval.IsValid)
            .OrderBy(interval => interval.StartMilliseconds)
            .ThenBy(interval => interval.EndMilliseconds)
            .ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var result = new List<SyncTimeIntervalMilliseconds> { ordered[0] };
        foreach (var interval in ordered.Skip(1))
        {
            var previous = result[^1];
            if (interval.StartMilliseconds <= previous.EndMilliseconds)
            {
                result[^1] = previous with
                {
                    EndMilliseconds = Math.Max(
                        previous.EndMilliseconds,
                        interval.EndMilliseconds)
                };
            }
            else
            {
                result.Add(interval);
            }
        }

        return result;
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static bool IsNonNegativeFinite(double value) => double.IsFinite(value) && value >= 0;
}
