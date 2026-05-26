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
        IReadOnlyList<string>? verifiedProfileGroups = null)
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

        if (normalizedOutcome != "success")
        {
            return ValidateResourceAcceptanceObservation(
                resourceUsageAcceptable,
                observedCpuPercent,
                observedGpuPercent,
                observedMemoryMegabytes);
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

        return ValidateResourceAcceptanceObservation(
            resourceUsageAcceptable,
            observedCpuPercent,
            observedGpuPercent,
            observedMemoryMegabytes);
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
}
