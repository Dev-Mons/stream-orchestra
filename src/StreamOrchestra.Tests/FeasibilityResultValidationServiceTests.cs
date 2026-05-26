using StreamOrchestra.App.Services;

namespace StreamOrchestra.Tests;

public sealed class FeasibilityResultValidationServiceTests
{
    [Fact]
    public void Validate_AllowsSuccessWhenAllPlanCriteriaAreMet()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resourceUsageAcceptable: true,
            observedCpuPercent: 45,
            observedGpuPercent: 60,
            observedMemoryMegabytes: 12000,
            verifiedProfileGroups: ["A", "B", "C"],
            accountLabel: "main_soop");

        Assert.Null(error);
    }

    [Fact]
    public void Validate_AllowsOutcomeWithDifferentCasing()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: "Success",
            sameAccountSession: true,
            restartSession: true,
            resourceUsageAcceptable: true,
            observedCpuPercent: 45,
            observedGpuPercent: 60,
            observedMemoryMegabytes: 12000,
            verifiedProfileGroups: ["a", "b", "c"],
            accountLabel: "main_soop");

        Assert.Null(error);
    }

    [Fact]
    public void Validate_RejectsSameAccountEvidenceWithoutAccountLabel()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 4,
            outcome: "partial",
            sameAccountSession: true,
            restartSession: false,
            resourceUsageAcceptable: false,
            verifiedProfileGroups: ["A"]);

        Assert.Equal("Same-account evidence requires an account label.", error);
    }

    [Fact]
    public void Validate_RejectsSuccessWithoutAccountLabel()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resourceUsageAcceptable: true,
            observedCpuPercent: 45,
            observedGpuPercent: 60,
            observedMemoryMegabytes: 12000,
            verifiedProfileGroups: ["A", "B", "C"]);

        Assert.Equal("Same-account evidence requires an account label.", error);
    }

    [Fact]
    public void Validate_RejectsSameAccountEvidenceWithoutProfileGroups()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 4,
            outcome: "partial",
            sameAccountSession: true,
            restartSession: false,
            resourceUsageAcceptable: false,
            accountLabel: "main_soop");

        Assert.Equal("Same-account evidence requires at least one verified profile group.", error);
    }

    [Fact]
    public void Validate_RejectsAccountLabelWithoutSameAccountEvidence()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 4,
            outcome: "partial",
            sameAccountSession: false,
            restartSession: false,
            resourceUsageAcceptable: false,
            accountLabel: "main_soop");

        Assert.Equal("Account label requires same-account evidence.", error);
    }

    [Fact]
    public void Validate_RejectsRestartEvidenceWithoutSameAccountEvidence()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: "partial",
            sameAccountSession: false,
            restartSession: true,
            resourceUsageAcceptable: false);

        Assert.Equal("Restart evidence requires same-account evidence.", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    public void Validate_RejectsMissingOrUnknownOutcome(string? outcome)
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: outcome,
            sameAccountSession: true,
            restartSession: true,
            resourceUsageAcceptable: true,
            observedCpuPercent: 45,
            observedGpuPercent: 60,
            observedMemoryMegabytes: 12000,
            verifiedProfileGroups: ["A", "B", "C"]);

        Assert.Equal("Outcome must be success, partial, or failure.", error);
    }

    [Theory]
    [InlineData(8, true, true, true, "Success requires at least 9 simultaneous streams.")]
    [InlineData(9, false, true, true, "Success requires same-account session persistence.")]
    [InlineData(9, true, false, true, "Success requires restart session persistence.")]
    [InlineData(9, true, true, false, "Success requires acceptable resource usage.")]
    public void Validate_RejectsSuccessWhenAnyPlanCriterionIsMissing(
        int playbackCount,
        bool sameAccountSession,
        bool restartSession,
        bool resourceUsageAcceptable,
        string expectedError)
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount,
            "success",
            sameAccountSession,
            restartSession,
            resourceUsageAcceptable,
            observedCpuPercent: 45,
            observedGpuPercent: 60,
            observedMemoryMegabytes: 12000,
            verifiedProfileGroups: ["A", "B", "C"]);

        Assert.Equal(expectedError, error);
    }

    [Theory]
    [InlineData("success", null, 60.0, 12000.0)]
    [InlineData("success", 45.0, null, 12000.0)]
    [InlineData("success", 45.0, 60.0, null)]
    [InlineData("partial", null, 60.0, 12000.0)]
    [InlineData("partial", 45.0, null, 12000.0)]
    public void Validate_RejectsResourceOkWhenStructuredResourceObservationIsMissing(
        string outcome,
        double? observedCpuPercent,
        double? observedGpuPercent,
        double? observedMemoryMegabytes)
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: outcome,
            sameAccountSession: true,
            restartSession: true,
            resourceUsageAcceptable: true,
            observedCpuPercent: observedCpuPercent,
            observedGpuPercent: observedGpuPercent,
            observedMemoryMegabytes: observedMemoryMegabytes,
            verifiedProfileGroups: ["A", "B", "C"]);

        Assert.Equal("Resource OK requires CPU %, GPU %, and memory MB observations.", error);
    }

    [Fact]
    public void Validate_RejectsFailureWithRestartEvidence()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: "failure",
            sameAccountSession: true,
            restartSession: true,
            resourceUsageAcceptable: false,
            verifiedProfileGroups: ["A", "B", "C"],
            accountLabel: "main_soop");

        Assert.Equal("Failure records cannot include restart evidence.", error);
    }

    [Fact]
    public void Validate_RejectsFailureWithResourceOkEvidence()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: "failure",
            sameAccountSession: false,
            restartSession: false,
            resourceUsageAcceptable: true,
            observedCpuPercent: 45,
            observedGpuPercent: 60,
            observedMemoryMegabytes: 12000);

        Assert.Equal("Failure records cannot include resource OK evidence.", error);
    }

    [Fact]
    public void Validate_RejectsSuccessWhenRequiredProfileGroupEvidenceIsMissing()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 12,
            outcome: "success",
            sameAccountSession: true,
            restartSession: true,
            resourceUsageAcceptable: true,
            observedCpuPercent: 45,
            observedGpuPercent: 60,
            observedMemoryMegabytes: 12000,
            verifiedProfileGroups: ["A", "B"]);

        Assert.Equal("Success requires same-account profile group evidence for groups A, B, C.", error);
    }

    [Fact]
    public void Validate_RejectsInvalidProfileGroupEvidence()
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: "partial",
            sameAccountSession: true,
            restartSession: true,
            resourceUsageAcceptable: true,
            verifiedProfileGroups: ["A", "Z"]);

        Assert.Equal("Profile groups must be A, B, C, and/or D.", error);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("failure")]
    public void Validate_AllowsNonSuccessWithoutSuccessCriteria(string outcome)
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 4,
            outcome: outcome,
            sameAccountSession: false,
            restartSession: false,
            resourceUsageAcceptable: false);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(-1.0, 60.0, 12000.0, "CPU % must be between 0 and 100.")]
    [InlineData(101.0, 60.0, 12000.0, "CPU % must be between 0 and 100.")]
    [InlineData(45.0, -1.0, 12000.0, "GPU % must be between 0 and 100.")]
    [InlineData(45.0, 101.0, 12000.0, "GPU % must be between 0 and 100.")]
    [InlineData(45.0, 60.0, -1.0, "Memory MB must be 0 or higher.")]
    public void Validate_RejectsInvalidStructuredResourceValues(
        double? observedCpuPercent,
        double? observedGpuPercent,
        double? observedMemoryMegabytes,
        string expectedError)
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: "partial",
            sameAccountSession: false,
            restartSession: false,
            resourceUsageAcceptable: false,
            observedCpuPercent: observedCpuPercent,
            observedGpuPercent: observedGpuPercent,
            observedMemoryMegabytes: observedMemoryMegabytes);

        Assert.Equal(expectedError, error);
    }

    [Theory]
    [MemberData(nameof(NonFiniteResourceValues))]
    public void Validate_RejectsNonFiniteStructuredResourceValues(
        double? observedCpuPercent,
        double? observedGpuPercent,
        double? observedMemoryMegabytes,
        string expectedError)
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount: 9,
            outcome: "partial",
            sameAccountSession: false,
            restartSession: false,
            resourceUsageAcceptable: false,
            observedCpuPercent: observedCpuPercent,
            observedGpuPercent: observedGpuPercent,
            observedMemoryMegabytes: observedMemoryMegabytes);

        Assert.Equal(expectedError, error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Validate_RejectsPlaybackCountsOutsideSlotRange(int playbackCount)
    {
        var service = new FeasibilityResultValidationService();

        var error = service.Validate(
            playbackCount,
            "partial",
            sameAccountSession: false,
            restartSession: false,
            resourceUsageAcceptable: false);

        Assert.Equal("Playback count must be between 1 and 16.", error);
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
}
