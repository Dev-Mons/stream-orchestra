namespace StreamOrchestra.App.Services;

public sealed class FeasibilityResultValidationService
{
    public string? Validate(
        int playbackCount,
        string? outcome,
        bool sameAccountSession,
        bool restartSession,
        bool resourceUsageAcceptable,
        double? observedCpuPercent = null,
        double? observedGpuPercent = null,
        double? observedMemoryMegabytes = null,
        IReadOnlyList<string>? verifiedProfileGroups = null,
        string? accountLabel = null)
    {
        if (playbackCount is < 1 or > PlaybackTestPlanService.MaxSlotCount)
        {
            return "Playback count must be between 1 and 16.";
        }

        if (string.IsNullOrWhiteSpace(outcome))
        {
            return "Outcome must be success, partial, or failure.";
        }

        var normalizedOutcome = outcome.Trim().ToLowerInvariant();
        if (!FeasibilityOutcomeService.IsKnown(normalizedOutcome))
        {
            return "Outcome must be success, partial, or failure.";
        }

        var resourceValidationError = FeasibilityResourceObservationService.ValidateValues(
            observedCpuPercent,
            observedGpuPercent,
            observedMemoryMegabytes);
        if (resourceValidationError is not null)
        {
            return resourceValidationError;
        }

        var profileGroupValidationError = FeasibilityProfileGroupEvidenceService.ValidateValues(verifiedProfileGroups);
        if (profileGroupValidationError is not null)
        {
            return profileGroupValidationError;
        }

        var accountEvidenceValidationError = ValidateSameAccountEvidence(
            sameAccountSession,
            verifiedProfileGroups,
            accountLabel);
        var restartEvidenceValidationError = ValidateRestartSessionEvidence(
            restartSession,
            sameAccountSession);

        if (normalizedOutcome != "success")
        {
            var resourceObservationError = ValidateResourceAcceptanceObservation(
                resourceUsageAcceptable,
                observedCpuPercent,
                observedGpuPercent,
                observedMemoryMegabytes);

            return resourceObservationError ?? accountEvidenceValidationError ?? restartEvidenceValidationError;
        }

        if (playbackCount < 9)
        {
            return "Success requires at least 9 simultaneous streams.";
        }

        if (!sameAccountSession)
        {
            return "Success requires same-account session persistence.";
        }

        if (!FeasibilityProfileGroupEvidenceService.HasRequiredGroups(playbackCount, verifiedProfileGroups))
        {
            var requiredGroups = FeasibilityProfileGroupEvidenceService.FormatRequiredGroups(playbackCount);
            return $"Success requires same-account profile group evidence for groups {requiredGroups}.";
        }

        if (!restartSession)
        {
            return "Success requires restart session persistence.";
        }

        if (!resourceUsageAcceptable)
        {
            return "Success requires acceptable resource usage.";
        }

        var successResourceObservationError = ValidateResourceAcceptanceObservation(
            resourceUsageAcceptable,
            observedCpuPercent,
            observedGpuPercent,
            observedMemoryMegabytes);

        return successResourceObservationError ?? accountEvidenceValidationError;
    }

    private static string? ValidateRestartSessionEvidence(
        bool restartSession,
        bool sameAccountSession)
    {
        return restartSession && !sameAccountSession
            ? "Restart evidence requires same-account evidence."
            : null;
    }

    private static string? ValidateResourceAcceptanceObservation(
        bool resourceUsageAcceptable,
        double? observedCpuPercent,
        double? observedGpuPercent,
        double? observedMemoryMegabytes)
    {
        if (!resourceUsageAcceptable)
        {
            return null;
        }

        if (observedCpuPercent is null || observedGpuPercent is null || observedMemoryMegabytes is null)
        {
            return "Resource OK requires CPU %, GPU %, and memory MB observations.";
        }

        return null;
    }

    private static string? ValidateSameAccountEvidence(
        bool sameAccountSession,
        IReadOnlyList<string>? verifiedProfileGroups,
        string? accountLabel)
    {
        if (!sameAccountSession)
        {
            return string.IsNullOrWhiteSpace(accountLabel)
                ? null
                : "Account label requires same-account evidence.";
        }

        if (FeasibilityProfileGroupEvidenceService.Normalize(verifiedProfileGroups).Count == 0)
        {
            return "Same-account evidence requires at least one verified profile group.";
        }

        return string.IsNullOrWhiteSpace(accountLabel)
            ? "Same-account evidence requires an account label."
            : null;
    }
}
