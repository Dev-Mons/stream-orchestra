using StreamOrchestra.App.Models;
using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class FeasibilityResourceObservationServiceTests
{
    [Fact]
    public void ValidateValues_AllowsMissingValuesForNonSuccessRecords()
    {
        var error = FeasibilityResourceObservationService.ValidateValues(null, null, null);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(-1.0, 60.0, 12000.0, "CPU % must be between 0 and 100.")]
    [InlineData(101.0, 60.0, 12000.0, "CPU % must be between 0 and 100.")]
    [InlineData(45.0, -1.0, 12000.0, "GPU % must be between 0 and 100.")]
    [InlineData(45.0, 101.0, 12000.0, "GPU % must be between 0 and 100.")]
    [InlineData(45.0, 60.0, -1.0, "Memory MB must be 0 or higher.")]
    public void ValidateValues_RejectsOutOfRangeValues(
        double? observedCpuPercent,
        double? observedGpuPercent,
        double? observedMemoryMegabytes,
        string expectedError)
    {
        var error = FeasibilityResourceObservationService.ValidateValues(
            observedCpuPercent,
            observedGpuPercent,
            observedMemoryMegabytes);

        Assert.Equal(expectedError, error);
    }

    [Theory]
    [MemberData(nameof(NonFiniteResourceValues))]
    public void ValidateValues_RejectsNonFiniteValues(
        double? observedCpuPercent,
        double? observedGpuPercent,
        double? observedMemoryMegabytes,
        string expectedError)
    {
        var error = FeasibilityResourceObservationService.ValidateValues(
            observedCpuPercent,
            observedGpuPercent,
            observedMemoryMegabytes);

        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void HasCompleteValidObservation_RequiresAllFiniteValuesInRange()
    {
        var validResult = CreateResult(45, 60, 12000);
        var invalidResult = CreateResult(double.NaN, 60, 12000);
        var missingResult = CreateResult(null, 60, 12000);

        Assert.True(FeasibilityResourceObservationService.HasCompleteValidObservation(validResult));
        Assert.False(FeasibilityResourceObservationService.HasCompleteValidObservation(invalidResult));
        Assert.False(FeasibilityResourceObservationService.HasCompleteValidObservation(missingResult));
    }

    [Fact]
    public void Normalize_DropsInvalidObservationsAndPreservesValidValues()
    {
        Assert.Equal(45.5, FeasibilityResourceObservationService.NormalizePercent(45.5));
        Assert.Null(FeasibilityResourceObservationService.NormalizePercent(101));
        Assert.Null(FeasibilityResourceObservationService.NormalizePercent(double.NaN));
        Assert.Equal(12000, FeasibilityResourceObservationService.NormalizeMemoryMegabytes(12000));
        Assert.Null(FeasibilityResourceObservationService.NormalizeMemoryMegabytes(-1));
    }

    public static IEnumerable<object?[]> NonFiniteResourceValues()
    {
        yield return [double.NaN, 60.0, 12000.0, "CPU % must be a finite number."];
        yield return [double.PositiveInfinity, 60.0, 12000.0, "CPU % must be a finite number."];
        yield return [45.0, double.NaN, 12000.0, "GPU % must be a finite number."];
        yield return [45.0, double.NegativeInfinity, 12000.0, "GPU % must be a finite number."];
        yield return [45.0, 60.0, double.NaN, "Memory MB must be a finite number."];
        yield return [45.0, 60.0, double.PositiveInfinity, "Memory MB must be a finite number."];
    }

    private static FeasibilityTestResult CreateResult(
        double? observedCpuPercent,
        double? observedGpuPercent,
        double? observedMemoryMegabytes)
    {
        return new FeasibilityTestResult
        {
            Id = "result",
            CapturedAt = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero),
            PlaybackCount = 9,
            Outcome = "success",
            ObservedCpuPercent = observedCpuPercent,
            ObservedGpuPercent = observedGpuPercent,
            ObservedMemoryMegabytes = observedMemoryMegabytes
        };
    }
}
