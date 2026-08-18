using System.Security.Cryptography;
using System.Text;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed class SyncClosedLoopExperimentGate
{
    private readonly object _gate = new();
    private readonly byte[] _assignmentKey;
    private readonly SyncClosedLoopExperimentProtocol _protocol;
    private bool _rollbackLatched;

    public SyncClosedLoopExperimentGate(
        byte[] assignmentKey,
        SyncClosedLoopExperimentProtocol? protocol = null)
    {
        if (assignmentKey is not { Length: >= 32 })
        {
            throw new ArgumentException("A 32-byte assignment key is required.", nameof(assignmentKey));
        }

        _assignmentKey = assignmentKey.ToArray();
        _protocol = protocol ?? new SyncClosedLoopExperimentProtocol();
        ValidateProtocol(_protocol);
    }

    public bool RollbackLatched
    {
        get
        {
            lock (_gate)
            {
                return _rollbackLatched;
            }
        }
    }

    public SyncClosedLoopGateDecision EvaluateEnrollment(
        SyncExperimentEnrollmentContext context,
        SyncClosedLoopEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evidence);
        lock (_gate)
        {
            var reasons = GuardrailReasons(evidence).ToList();
            if (reasons.Count > 0)
            {
                _rollbackLatched = true;
            }

            if (_rollbackLatched)
            {
                reasons.Insert(0, "rollback-latched");
                return SuggestionOnly(reasons, rollback: true);
            }

            if (!context.FeatureFlagEnabled)
            {
                return SuggestionOnly(["feature-flag-disabled"]);
            }

            if (!context.ExplicitOptIn)
            {
                return SuggestionOnly(["explicit-opt-in-required"]);
            }

            var prerequisites = PrerequisiteReasons(evidence).ToArray();
            if (prerequisites.Length > 0)
            {
                return SuggestionOnly(prerequisites);
            }

            if (string.IsNullOrWhiteSpace(context.ClusterHash) ||
                string.IsNullOrWhiteSpace(context.BlockBucket))
            {
                return SuggestionOnly(["cluster-block-assignment-missing"]);
            }

            var candidate = IsCandidateArm(context.ClusterHash, context.BlockBucket);
            if (candidate &&
                _protocol.HardSeekExperimentEnabled &&
                evidence.BoundedRatePilotPassed &&
                evidence.CdpCoveragePassed)
            {
                return new SyncClosedLoopGateDecision
                {
                    Mode = SyncClosedLoopMode.HardSeekExperiment,
                    Arm = SyncExperimentArm.CandidateBoundedRate,
                    BoundedRateDispatchAllowed = true,
                    HardSeekDispatchAllowed = true,
                    Reasons = ["hard-seek-evidence-check-required"]
                };
            }

            return candidate
                ? new SyncClosedLoopGateDecision
                {
                    Mode = SyncClosedLoopMode.BoundedRateExperiment,
                    Arm = SyncExperimentArm.CandidateBoundedRate,
                    BoundedRateDispatchAllowed = true,
                    HardSeekDispatchAllowed = false,
                    Reasons = ["bounded-rate-first"]
                }
                : new SyncClosedLoopGateDecision
                {
                    Mode = SyncClosedLoopMode.SuggestionOnly,
                    Arm = SyncExperimentArm.ControlSuggestionOnly,
                    Reasons = ["randomized-control-arm"]
                };
        }
    }

    public bool IsHardSeekEligible(
        SyncClosedLoopGateDecision enrollment,
        SyncHardSeekEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentNullException.ThrowIfNull(evidence);
        lock (_gate)
        {
            return !_rollbackLatched &&
                   _protocol.HardSeekExperimentEnabled &&
                   enrollment.Mode == SyncClosedLoopMode.HardSeekExperiment &&
                   enrollment.HardSeekDispatchAllowed &&
                   evidence.StableEpoch &&
                   evidence.IndependentProgressCount >= _protocol.StableIndependentProgressCount &&
                   evidence.CalibratedPdtMapping &&
                   evidence.CdpCorrelated &&
                   evidence.CdpCoverageGatePassed &&
                   evidence.FreshRequestVideoFrameCallback &&
                   evidence.TargetInsideBufferedSeekableIntersection &&
                   evidence.ApplyVerificationAvailable &&
                   evidence.SeekedVerificationAvailable &&
                   evidence.FollowUpPositionVerificationAvailable;
        }
    }

    public void RollbackImmediately()
    {
        lock (_gate)
        {
            _rollbackLatched = true;
        }
    }

    public static SyncCommandOutcomeAssessment AssessCommandResult(
        string expectedCommandId,
        SyncCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var idMatches = !string.IsNullOrWhiteSpace(expectedCommandId) &&
                        result.CommandId.Equals(expectedCommandId, StringComparison.Ordinal);
        var success = idMatches &&
                      result.Stage == SyncCommandStage.Verified &&
                      result.WasApplied &&
                      result.WasVerified;
        if (success)
        {
            return new SyncCommandOutcomeAssessment
            {
                CountsAsSuccess = true,
                OutcomeCode = "verified"
            };
        }

        return new SyncCommandOutcomeAssessment
        {
            CountsAsSuccess = false,
            ResetRateToOne = true,
            EnterDegradedState = true,
            OutcomeCode = !idMatches
                ? "wrong-command-id"
                : result.Stage == SyncCommandStage.TimedOut
                    ? "timed-out"
                    : result.Stage == SyncCommandStage.Failed
                        ? "failed"
                        : "not-verified"
        };
    }

    private IEnumerable<string> PrerequisiteReasons(SyncClosedLoopEvidence evidence)
    {
        if (!evidence.PassivePilotPassed)
        {
            yield return "passive-pilot-gate-closed";
        }

        if (!evidence.EstimatorHoldoutPassed)
        {
            yield return "estimator-holdout-gate-closed";
        }

        if (!evidence.SuggestionHoldoutPassed)
        {
            yield return "suggestion-holdout-gate-closed";
        }
    }

    private IEnumerable<string> GuardrailReasons(SyncClosedLoopEvidence evidence)
    {
        if (evidence.PrivacyViolationCount > 0)
        {
            yield return "privacy-violation";
        }

        if (evidence.InvalidSeekCount > 0)
        {
            yield return "invalid-seek";
        }

        if (!evidence.ExperimentMetricsAvailable)
        {
            yield break;
        }

        if (!double.IsFinite(evidence.CoverageRate) || evidence.CoverageRate < _protocol.MinimumCoverageRate)
        {
            yield return "coverage-below-threshold";
        }

        if (!double.IsFinite(evidence.ProxyErrorDeltaMilliseconds) ||
            evidence.ProxyErrorDeltaMilliseconds > _protocol.ProxyErrorNonInferiorityMarginMilliseconds)
        {
            yield return "proxy-error-harm";
        }

        if (!double.IsFinite(evidence.WrongCorrectionRate) ||
            evidence.WrongCorrectionRate > _protocol.MaximumWrongCorrectionRate)
        {
            yield return "wrong-correction-harm";
        }

        if (!double.IsFinite(evidence.StallRateIncrease) ||
            evidence.StallRateIncrease > _protocol.MaximumStallRateIncrease)
        {
            yield return "stall-harm";
        }

        if (!double.IsFinite(evidence.CpuPercentagePointIncrease) ||
            evidence.CpuPercentagePointIncrease > _protocol.MaximumCpuPercentagePointIncrease)
        {
            yield return "cpu-harm";
        }

        if (!double.IsFinite(evidence.MemoryMegabyteIncrease) ||
            evidence.MemoryMegabyteIncrease > _protocol.MaximumMemoryMegabyteIncrease)
        {
            yield return "memory-harm";
        }
    }

    private bool IsCandidateArm(string clusterHash, string blockBucket)
    {
        using var hmac = new HMACSHA256(_assignmentKey);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(
            $"{_protocol.ProtocolVersion}\u001f{blockBucket.Trim()}\u001f{clusterHash.Trim()}"));
        var bucket = ((bytes[0] << 8) | bytes[1]) % 100;
        return bucket < _protocol.CandidateExposurePercent;
    }

    private static SyncClosedLoopGateDecision SuggestionOnly(
        IReadOnlyList<string> reasons,
        bool rollback = false) => new()
    {
        Mode = SyncClosedLoopMode.SuggestionOnly,
        Arm = SyncExperimentArm.NotEnrolled,
        BoundedRateDispatchAllowed = false,
        HardSeekDispatchAllowed = false,
        RollbackLatched = rollback,
        Reasons = reasons
    };

    private static void ValidateProtocol(SyncClosedLoopExperimentProtocol protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol.ProtocolVersion) ||
            protocol.FrozenAtUtc == default ||
            !double.IsFinite(protocol.EpsilonMilliseconds) || protocol.EpsilonMilliseconds <= 0 ||
            protocol.StableIndependentProgressCount < 2 ||
            !double.IsFinite(protocol.ProxyErrorNonInferiorityMarginMilliseconds) ||
            protocol.ProxyErrorNonInferiorityMarginMilliseconds < 0 ||
            !IsRate(protocol.MaximumWrongCorrectionRate) ||
            !IsRate(protocol.MaximumStallRateIncrease) ||
            !IsRate(protocol.MinimumCoverageRate) ||
            !double.IsFinite(protocol.MaximumCpuPercentagePointIncrease) ||
            protocol.MaximumCpuPercentagePointIncrease < 0 ||
            !double.IsFinite(protocol.MaximumMemoryMegabyteIncrease) ||
            protocol.MaximumMemoryMegabyteIncrease < 0 ||
            protocol.CandidateExposurePercent is < 1 or > 99 ||
            !protocol.BoundedRateFirst)
        {
            throw new ArgumentOutOfRangeException(nameof(protocol));
        }
    }

    private static bool IsRate(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}
