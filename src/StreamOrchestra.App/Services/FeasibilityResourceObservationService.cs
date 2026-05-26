using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public static class FeasibilityResourceObservationService
{
    public static string? ValidateValues(
        double? observedCpuPercent,
        double? observedGpuPercent,
        double? observedMemoryMegabytes)
    {
        if (IsNonFinite(observedCpuPercent))
        {
            return "CPU % must be a finite number.";
        }

        if (IsNonFinite(observedGpuPercent))
        {
            return "GPU % must be a finite number.";
        }

        if (IsNonFinite(observedMemoryMegabytes))
        {
            return "Memory MB must be a finite number.";
        }

        if (observedCpuPercent is < 0 or > 100)
        {
            return "CPU % must be between 0 and 100.";
        }

        if (observedGpuPercent is < 0 or > 100)
        {
            return "GPU % must be between 0 and 100.";
        }

        if (observedMemoryMegabytes < 0)
        {
            return "Memory MB must be 0 or higher.";
        }

        return null;
    }

    public static bool HasCompleteValidObservation(FeasibilityTestResult result)
    {
        return HasCompleteValidObservation(
            result.ObservedCpuPercent,
            result.ObservedGpuPercent,
            result.ObservedMemoryMegabytes);
    }

    public static bool HasCompleteValidObservation(
        double? observedCpuPercent,
        double? observedGpuPercent,
        double? observedMemoryMegabytes)
    {
        return IsValidPercent(observedCpuPercent) &&
            IsValidPercent(observedGpuPercent) &&
            IsValidMemory(observedMemoryMegabytes);
    }

    public static double? NormalizePercent(double? value)
    {
        return IsValidPercent(value) ? value : null;
    }

    public static double? NormalizeMemoryMegabytes(double? value)
    {
        return IsValidMemory(value) ? value : null;
    }

    private static bool IsNonFinite(double? value)
    {
        return value is double number && !double.IsFinite(number);
    }

    private static bool IsValidPercent(double? value)
    {
        return value is double number && double.IsFinite(number) && number is >= 0 and <= 100;
    }

    private static bool IsValidMemory(double? value)
    {
        return value is double number && double.IsFinite(number) && number >= 0;
    }
}
