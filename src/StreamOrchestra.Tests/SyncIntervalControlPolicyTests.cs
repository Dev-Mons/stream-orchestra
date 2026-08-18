using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncIntervalControlPolicyTests
{
    [Fact]
    public void EvaluateUsesLatestContiguousIntersectionAndNeverTargetsAGap()
    {
        var policy = CalibratedPolicy();
        var first = Member(1, 114, [new MediaTimeRange(100, 105), new MediaTimeRange(110, 120)]);
        var second = Member(2, 114, [new MediaTimeRange(102, 115)]);

        var result = policy.Evaluate(Request(1000, first, second));

        Assert.Equal(SyncIntervalPolicyState.Shadow, result.State);
        Assert.Equal(2, result.CommonPlayableIntervals.Count);
        Assert.Equal(110000, result.SelectedCommonInterval!.StartMilliseconds);
        Assert.Equal(115000, result.SelectedCommonInterval.EndMilliseconds);
        Assert.Equal(114000, result.TargetGroupTimeMilliseconds);
        Assert.All(result.Members, decision => Assert.Equal(114, decision.TargetMediaTimeSeconds));
    }

    [Fact]
    public void EvaluateMapsEachMemberBackFromTheSharedGroupInterval()
    {
        var policy = CalibratedPolicy();
        var first = Member(1, 112, [new MediaTimeRange(100, 120)]);
        var second = Member(2, 102, [new MediaTimeRange(90, 105)]) with
        {
            MediaToGroupOffsetMilliseconds = 10000
        };

        var result = policy.Evaluate(Request(3000, first, second));

        Assert.Equal(112, result.Members.Single(member => member.SlotId == 1).TargetMediaTimeSeconds);
        Assert.Equal(102, result.Members.Single(member => member.SlotId == 2).TargetMediaTimeSeconds);
    }

    [Fact]
    public void EvaluateDegradesWithoutACommonPlayableInterval()
    {
        var policy = CalibratedPolicy();

        var result = policy.Evaluate(Request(
            1000,
            Member(1, 104, [new MediaTimeRange(100, 105)]),
            Member(2, 114, [new MediaTimeRange(110, 115)])));

        Assert.Equal(SyncIntervalPolicyState.NoIntersection, result.State);
        Assert.Equal("no-intersection", result.Reason);
        Assert.All(result.Members, decision =>
        {
            Assert.Equal("rate", decision.ProposedCommand);
            Assert.Equal(1, decision.ProposedValue);
            Assert.False(decision.HardSeekAllowed);
        });
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("source-reset")]
    [InlineData("epoch-unstable")]
    [InlineData("invalid-range")]
    public void UnsafeContextIsSuggestionOnlyAtNormalRate(string reason)
    {
        var policy = CalibratedPolicy();
        var unsafeMember = Member(1, 110, [new MediaTimeRange(100, 120)]) with
        {
            TimelineFresh = reason != "stale",
            SourceStable = reason != "source-reset",
            EpochStable = reason != "epoch-unstable",
            PlayerHealthy = reason != "invalid-range"
        };

        var result = policy.Evaluate(Request(
            1000,
            unsafeMember,
            Member(2, 110, [new MediaTimeRange(100, 120)])));

        Assert.Equal(SyncIntervalPolicyState.Suppressed, result.State);
        Assert.Equal(reason, result.Reason);
        Assert.All(result.Members, decision =>
        {
            Assert.Equal(1, decision.ProposedValue);
            Assert.False(decision.HardSeekAllowed);
        });
    }

    [Fact]
    public void MissingCalibrationStillComputesIntervalButSuppressesCorrection()
    {
        var policy = new SyncIntervalControlPolicy();
        var uncalibrated = Member(1, 119, [new MediaTimeRange(100, 120)]) with
        {
            TimelineUncertaintyMilliseconds = null,
            BiasUncertaintyMilliseconds = null,
            ControllabilityUncertaintyMilliseconds = null
        };

        var result = policy.Evaluate(Request(
            1000,
            uncalibrated,
            Member(2, 119, [new MediaTimeRange(100, 120)]) with
            {
                TimelineUncertaintyMilliseconds = null,
                BiasUncertaintyMilliseconds = null,
                ControllabilityUncertaintyMilliseconds = null
            }));

        Assert.NotNull(result.SelectedCommonInterval);
        Assert.Equal("low-confidence", result.Reason);
        Assert.All(result.Members, decision =>
        {
            Assert.Equal(1, decision.ProposedValue);
            Assert.False(decision.HardSeekAllowed);
        });
    }

    [Fact]
    public void HardSeekRequiresExplicitEvidenceAndCalibratedUncertaintyThreshold()
    {
        var policy = CalibratedPolicy(hardSeekMaximumUncertainty: 100);
        var first = Member(1, 119, [new MediaTimeRange(100, 120)]);
        var second = Member(2, 117, [new MediaTimeRange(100, 120)]);

        var allowed = policy.Evaluate(Request(3000, first, second));
        var blocked = policy.Evaluate(Request(
            3000,
            first with { TimelineUncertaintyMilliseconds = 200 },
            second));

        Assert.Equal("seek", allowed.Members.Single(member => member.SlotId == 1).ProposedCommand);
        Assert.True(allowed.Members.Single(member => member.SlotId == 1).HardSeekAllowed);
        Assert.Equal("rate", blocked.Members.Single(member => member.SlotId == 1).ProposedCommand);
        Assert.False(blocked.Members.Single(member => member.SlotId == 1).HardSeekAllowed);
    }

    [Fact]
    public void DefaultShadowPolicyCannotProposeHardSeekBeforeCalibrationGate()
    {
        var policy = new SyncIntervalControlPolicy();

        var result = policy.Evaluate(Request(
            3000,
            Member(1, 119, [new MediaTimeRange(100, 120)]),
            Member(2, 117, [new MediaTimeRange(100, 120)])));

        Assert.True(result.IsShadowOnly);
        Assert.DoesNotContain(result.Members, member => member.ProposedCommand == "seek");
    }

    private static SyncIntervalControlPolicy CalibratedPolicy(
        double? hardSeekMaximumUncertainty = null) => new(new SyncIntervalControlPolicyOptions
    {
        HardSeekMaximumCombinedUncertaintyMilliseconds = hardSeekMaximumUncertainty
    });

    private static SyncIntervalPolicyRequest Request(
        int safetyDelayMilliseconds,
        params SyncIntervalMemberInput[] members) => new()
    {
        Members = members,
        SafetyDelayMilliseconds = safetyDelayMilliseconds
    };

    private static SyncIntervalMemberInput Member(
        int slotId,
        double currentTime,
        IReadOnlyList<MediaTimeRange> ranges) => new()
    {
        SlotId = slotId,
        PlayableRanges = ranges,
        CurrentMediaTimeSeconds = currentTime,
        MediaToGroupOffsetMilliseconds = 0,
        TimelineFresh = true,
        EpochStable = true,
        SourceStable = true,
        PlayerHealthy = true,
        HasFullRangeObservation = true,
        HardSeekEvidenceEligible = true,
        TimelineUncertaintyMilliseconds = 0,
        BiasUncertaintyMilliseconds = 0,
        ControllabilityUncertaintyMilliseconds = 0
    };
}
