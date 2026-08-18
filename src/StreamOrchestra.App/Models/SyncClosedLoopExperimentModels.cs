namespace StreamOrchestra.App.Models;

public enum SyncClosedLoopMode
{
    Disabled,
    Shadow,
    SuggestionOnly,
    BoundedRateExperiment,
    HardSeekExperiment
}

public enum SyncExperimentArm
{
    NotEnrolled,
    ControlSuggestionOnly,
    CandidateBoundedRate
}

public sealed record SyncClosedLoopExperimentProtocol
{
    public string ProtocolVersion { get; init; } = "sync-closed-loop-v1";

    public DateTimeOffset FrozenAtUtc { get; init; } =
        DateTimeOffset.Parse("2026-08-18T00:00:00Z");

    public double EpsilonMilliseconds { get; init; } = 250;

    public int StableIndependentProgressCount { get; init; } = 3;

    public double ProxyErrorNonInferiorityMarginMilliseconds { get; init; } = 50;

    public double MaximumWrongCorrectionRate { get; init; } = 0.02;

    public double MaximumStallRateIncrease { get; init; } = 0.005;

    public double MaximumCpuPercentagePointIncrease { get; init; } = 3;

    public double MaximumMemoryMegabyteIncrease { get; init; } = 100;

    public double MinimumCoverageRate { get; init; } = 0.95;

    public int CandidateExposurePercent { get; init; } = 50;

    public bool BoundedRateFirst { get; init; } = true;

    public bool HardSeekExperimentEnabled { get; init; }
}

public sealed record SyncClosedLoopEvidence
{
    public bool ExperimentMetricsAvailable { get; init; }

    public bool PassivePilotPassed { get; init; }

    public bool EstimatorHoldoutPassed { get; init; }

    public bool SuggestionHoldoutPassed { get; init; }

    public bool CdpCoveragePassed { get; init; }

    public bool BoundedRatePilotPassed { get; init; }

    public double CoverageRate { get; init; }

    public double ProxyErrorDeltaMilliseconds { get; init; }

    public double WrongCorrectionRate { get; init; }

    public double StallRateIncrease { get; init; }

    public double CpuPercentagePointIncrease { get; init; }

    public double MemoryMegabyteIncrease { get; init; }

    public int PrivacyViolationCount { get; init; }

    public int InvalidSeekCount { get; init; }
}

public sealed record SyncExperimentEnrollmentContext
{
    public bool FeatureFlagEnabled { get; init; }

    public bool ExplicitOptIn { get; init; }

    public string ClusterHash { get; init; } = "";

    public string BlockBucket { get; init; } = "";
}

public sealed record SyncHardSeekEvidence
{
    public bool StableEpoch { get; init; }

    public int IndependentProgressCount { get; init; }

    public bool CalibratedPdtMapping { get; init; }

    public bool CdpCorrelated { get; init; }

    public bool CdpCoverageGatePassed { get; init; }

    public bool FreshRequestVideoFrameCallback { get; init; }

    public bool TargetInsideBufferedSeekableIntersection { get; init; }

    public bool ApplyVerificationAvailable { get; init; }

    public bool SeekedVerificationAvailable { get; init; }

    public bool FollowUpPositionVerificationAvailable { get; init; }
}

public sealed record SyncClosedLoopGateDecision
{
    public SyncClosedLoopMode Mode { get; init; } = SyncClosedLoopMode.SuggestionOnly;

    public SyncExperimentArm Arm { get; init; } = SyncExperimentArm.NotEnrolled;

    public bool BoundedRateDispatchAllowed { get; init; }

    public bool HardSeekDispatchAllowed { get; init; }

    public bool RollbackLatched { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];
}

public sealed record SyncCommandOutcomeAssessment
{
    public bool CountsAsSuccess { get; init; }

    public bool ResetRateToOne { get; init; }

    public bool EnterDegradedState { get; init; }

    public string OutcomeCode { get; init; } = "unknown";
}
