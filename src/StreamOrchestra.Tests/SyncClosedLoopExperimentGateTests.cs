using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class SyncClosedLoopExperimentGateTests
{
    private static readonly byte[] AssignmentKey = Enumerable.Repeat((byte)0x42, 32).ToArray();

    [Fact]
    public void Enrollment_RemainsSuggestionOnlyUntilAllOneThroughFiveGatesPass()
    {
        var gate = new SyncClosedLoopExperimentGate(AssignmentKey);

        var decision = gate.EvaluateEnrollment(
            Context("cluster-a"),
            new SyncClosedLoopEvidence());

        Assert.Equal(SyncClosedLoopMode.SuggestionOnly, decision.Mode);
        Assert.False(decision.BoundedRateDispatchAllowed);
        Assert.False(decision.HardSeekDispatchAllowed);
        Assert.False(decision.RollbackLatched);
        Assert.Contains("passive-pilot-gate-closed", decision.Reasons);
    }

    [Fact]
    public void Enrollment_IsDeterministicClusterBlockRandomizedAndBoundedRateFirst()
    {
        var gate = new SyncClosedLoopExperimentGate(AssignmentKey);
        var decisions = Enumerable.Range(1, 200)
            .Select(index => gate.EvaluateEnrollment(Context($"cluster-{index}"), PassingEvidence()))
            .ToArray();
        var repeated = gate.EvaluateEnrollment(Context("cluster-17"), PassingEvidence());
        var original = gate.EvaluateEnrollment(Context("cluster-17"), PassingEvidence());

        Assert.Equal(original.Arm, repeated.Arm);
        Assert.Contains(decisions, item => item.Arm == SyncExperimentArm.ControlSuggestionOnly);
        Assert.Contains(decisions, item => item.Arm == SyncExperimentArm.CandidateBoundedRate);
        Assert.All(decisions, item => Assert.False(item.HardSeekDispatchAllowed));
        Assert.All(decisions.Where(item => item.Arm == SyncExperimentArm.CandidateBoundedRate), item =>
            Assert.True(item.BoundedRateDispatchAllowed));
    }

    [Fact]
    public void HarmOrCoverageFailure_LatchesImmediateSuggestionOnlyRollback()
    {
        var gate = new SyncClosedLoopExperimentGate(AssignmentKey);
        var harmful = PassingEvidence() with
        {
            CoverageRate = 0.8,
            WrongCorrectionRate = 0.2
        };

        var first = gate.EvaluateEnrollment(Context("cluster-harm"), harmful);
        var second = gate.EvaluateEnrollment(Context("cluster-after"), PassingEvidence());

        Assert.True(first.RollbackLatched);
        Assert.True(second.RollbackLatched);
        Assert.Equal(SyncClosedLoopMode.SuggestionOnly, second.Mode);
        Assert.False(second.BoundedRateDispatchAllowed);
        Assert.Contains("rollback-latched", second.Reasons);
    }

    [Fact]
    public void HardSeek_RequiresEveryPreregisteredEvidenceBitAndRemainsSeparatelyDisabled()
    {
        var gate = new SyncClosedLoopExperimentGate(
            AssignmentKey,
            new SyncClosedLoopExperimentProtocol { HardSeekExperimentEnabled = true });
        var hardSeekEvidence = PassingEvidence() with { BoundedRatePilotPassed = true };
        var enrollment = Enumerable.Range(1, 200)
            .Select(index => gate.EvaluateEnrollment(Context($"hard-seek-cluster-{index}"), hardSeekEvidence))
            .First(decision => decision.Arm == SyncExperimentArm.CandidateBoundedRate);
        var evidence = new SyncHardSeekEvidence
        {
            StableEpoch = true,
            IndependentProgressCount = 3,
            CalibratedPdtMapping = true,
            CdpCorrelated = true,
            CdpCoverageGatePassed = true,
            FreshRequestVideoFrameCallback = true,
            TargetInsideBufferedSeekableIntersection = true,
            ApplyVerificationAvailable = true,
            SeekedVerificationAvailable = true,
            FollowUpPositionVerificationAvailable = true
        };

        Assert.Equal(SyncClosedLoopMode.HardSeekExperiment, enrollment.Mode);
        Assert.True(gate.IsHardSeekEligible(enrollment, evidence));
        Assert.False(gate.IsHardSeekEligible(
            enrollment,
            evidence with { FollowUpPositionVerificationAvailable = false }));
        Assert.False(new SyncClosedLoopExperimentGate(AssignmentKey).IsHardSeekEligible(
            enrollment,
            evidence));
    }

    [Theory]
    [InlineData(SyncCommandStage.Verified, true, true, true, "verified")]
    [InlineData(SyncCommandStage.Applied, true, false, false, "not-verified")]
    [InlineData(SyncCommandStage.Failed, false, false, false, "failed")]
    [InlineData(SyncCommandStage.TimedOut, false, false, false, "timed-out")]
    public void CommandOutcome_OnlyMatchingAppliedAndVerifiedResultCountsAsSuccess(
        SyncCommandStage stage,
        bool applied,
        bool verified,
        bool expectedSuccess,
        string expectedOutcome)
    {
        var assessment = SyncClosedLoopExperimentGate.AssessCommandResult(
            "expected",
            new SyncCommandResult
            {
                CommandId = "expected",
                Stage = stage,
                WasApplied = applied,
                WasVerified = verified
            });

        Assert.Equal(expectedSuccess, assessment.CountsAsSuccess);
        Assert.Equal(!expectedSuccess, assessment.ResetRateToOne);
        Assert.Equal(!expectedSuccess, assessment.EnterDegradedState);
        Assert.Equal(expectedOutcome, assessment.OutcomeCode);
    }

    [Fact]
    public void CommandOutcome_WrongCommandIdNeverCountsAsSuccess()
    {
        var assessment = SyncClosedLoopExperimentGate.AssessCommandResult(
            "expected",
            new SyncCommandResult
            {
                CommandId = "wrong",
                Stage = SyncCommandStage.Verified,
                WasApplied = true,
                WasVerified = true
            });

        Assert.False(assessment.CountsAsSuccess);
        Assert.True(assessment.ResetRateToOne);
        Assert.True(assessment.EnterDegradedState);
        Assert.Equal("wrong-command-id", assessment.OutcomeCode);
    }

    private static SyncExperimentEnrollmentContext Context(string cluster) => new()
    {
        FeatureFlagEnabled = true,
        ExplicitOptIn = true,
        ClusterHash = cluster,
        BlockBucket = "runtime-a|quality-1080p"
    };

    private static SyncClosedLoopEvidence PassingEvidence() => new()
    {
        ExperimentMetricsAvailable = true,
        PassivePilotPassed = true,
        EstimatorHoldoutPassed = true,
        SuggestionHoldoutPassed = true,
        CdpCoveragePassed = true,
        CoverageRate = 1,
        ProxyErrorDeltaMilliseconds = 0,
        WrongCorrectionRate = 0,
        StallRateIncrease = 0,
        CpuPercentagePointIncrease = 0,
        MemoryMegabyteIncrease = 0,
        PrivacyViolationCount = 0,
        InvalidSeekCount = 0
    };
}
