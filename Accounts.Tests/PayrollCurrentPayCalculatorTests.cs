using Accounts.Services.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class PayrollCurrentPayCalculatorTests
{
    [Fact]
    public void Compute_WithoutScaleDate_ReturnsBasic()
    {
        var pay = PayrollCurrentPayCalculator.Compute(50_000m, 5_000m, 100_000m, null, new DateOnly(2026, 9, 8));
        Assert.Equal(50_000m, pay);
    }

    [Fact]
    public void Compute_AppliesCompletedYearsAndCap()
    {
        var pay = PayrollCurrentPayCalculator.Compute(
            basicSalary: 50_000m,
            yearlyIncrement: 5_000m,
            maximumSalary: 58_000m,
            scaleDate: new DateTime(2023, 9, 1),
            asOfDate: new DateOnly(2026, 9, 8),
            applyAfterYears: 0);
        // 3 completed years → 50k + 15k = 65k, capped at 58k
        Assert.Equal(58_000m, pay);
    }

    [Fact]
    public void Compute_HonorsApplyAfter()
    {
        var pay = PayrollCurrentPayCalculator.Compute(
            basicSalary: 40_000m,
            yearlyIncrement: 2_000m,
            maximumSalary: 0,
            scaleDate: new DateTime(2024, 9, 1),
            asOfDate: new DateOnly(2026, 9, 8),
            applyAfterYears: 1);
        // 2 years on scale, ApplyAfter=1 → 1 increment
        Assert.Equal(42_000m, pay);
    }
}
