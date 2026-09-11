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

    [Fact]
    public void Compute_WithIncMonths_SplitsYearlyIncrementAcrossMonths()
    {
        // IncrementSal 1000 split Jan(1)+June(6) → 500 each installment.
        // Scale 2024-01-01, as of 2025-07-01:
        // 2024 Jan, 2024 Jun, 2025 Jan, 2025 Jun = 4 × 500 = 2000
        var pay = PayrollCurrentPayCalculator.Compute(
            basicSalary: 50_000m,
            yearlyIncrement: 1_000m,
            maximumSalary: 0,
            scaleDate: new DateTime(2024, 1, 1),
            asOfDate: new DateOnly(2025, 7, 1),
            applyAfterYears: 0,
            incrementMonths: [1, 6]);
        Assert.Equal(52_000m, pay);
    }

    [Fact]
    public void Compute_WithIncMonths_BeforeFirstInstallment_StaysBasic()
    {
        var pay = PayrollCurrentPayCalculator.Compute(
            basicSalary: 50_000m,
            yearlyIncrement: 1_000m,
            maximumSalary: 0,
            scaleDate: new DateTime(2025, 3, 1),
            asOfDate: new DateOnly(2025, 5, 1),
            applyAfterYears: 0,
            incrementMonths: [1, 6]);
        // Eligible from Mar; next Jan/Jun installments not yet reached (Jun is after asOf May)
        Assert.Equal(50_000m, pay);
    }

    [Fact]
    public void Compute_EmptyIncMonths_UsesLegacyYearlyIncrement()
    {
        var withEmpty = PayrollCurrentPayCalculator.Compute(
            50_000m, 1_000m, 0, new DateTime(2024, 1, 1), new DateOnly(2026, 1, 1), 0, []);
        var without = PayrollCurrentPayCalculator.Compute(
            50_000m, 1_000m, 0, new DateTime(2024, 1, 1), new DateOnly(2026, 1, 1), 0, null);
        Assert.Equal(without, withEmpty);
        Assert.Equal(52_000m, without);
    }
}
