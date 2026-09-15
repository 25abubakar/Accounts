using Accounts.Models;
using Accounts.Services.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class PayrollCurrentPayCalculatorTests
{
    [Fact]
    public void Compute_WithoutAnchor_ReturnsBasic()
    {
        var pay = PayrollCurrentPayCalculator.Compute(50_000m, 5_000m, 100_000m, null, new DateOnly(2026, 9, 8));
        Assert.Equal(50_000m, pay);
    }

    [Fact]
    public void Compute_FirstServiceYear_AddsOneIncrement()
    {
        // DOJ 2026-03-01, as of 2026-10-15 → completedYears 0 → service years 1
        var pay = PayrollCurrentPayCalculator.Compute(
            basicSalary: 35_000m,
            yearlyIncrement: 2_000m,
            maximumSalary: 45_000m,
            incrementAnchorDate: new DateTime(2026, 3, 1),
            asOfDate: new DateOnly(2026, 10, 15),
            applyAfterYears: 0);
        Assert.Equal(37_000m, pay);
    }

    [Fact]
    public void Compute_AfterOneCompletedYear_AddsTwoIncrements()
    {
        var pay = PayrollCurrentPayCalculator.Compute(
            basicSalary: 35_000m,
            yearlyIncrement: 2_000m,
            maximumSalary: 45_000m,
            incrementAnchorDate: new DateTime(2026, 3, 1),
            asOfDate: new DateOnly(2027, 3, 1),
            applyAfterYears: 0);
        Assert.Equal(39_000m, pay);
    }

    [Fact]
    public void Compute_AppliesCompletedYearsAndCap()
    {
        var pay = PayrollCurrentPayCalculator.Compute(
            basicSalary: 50_000m,
            yearlyIncrement: 5_000m,
            maximumSalary: 58_000m,
            incrementAnchorDate: new DateTime(2023, 9, 1),
            asOfDate: new DateOnly(2026, 9, 8),
            applyAfterYears: 0);
        // completedYears 3 → service years 4 → 50k + 20k = 70k, capped at 58k
        Assert.Equal(58_000m, pay);
    }

    [Fact]
    public void Compute_HonorsApplyAfter()
    {
        var pay = PayrollCurrentPayCalculator.Compute(
            basicSalary: 40_000m,
            yearlyIncrement: 2_000m,
            maximumSalary: 0,
            incrementAnchorDate: new DateTime(2024, 9, 1),
            asOfDate: new DateOnly(2026, 9, 8),
            applyAfterYears: 1);
        // completedYears 2 → +1 = 3 − ApplyAfter 1 → 2 increments
        Assert.Equal(44_000m, pay);
    }

    [Fact]
    public void Compute_WithIncMonths_SplitsYearlyIncrementAcrossMonths()
    {
        var pay = PayrollCurrentPayCalculator.Compute(
            basicSalary: 50_000m,
            yearlyIncrement: 1_000m,
            maximumSalary: 0,
            incrementAnchorDate: new DateTime(2024, 1, 1),
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
            incrementAnchorDate: new DateTime(2025, 3, 1),
            asOfDate: new DateOnly(2025, 5, 1),
            applyAfterYears: 0,
            incrementMonths: [1, 6]);
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
        // completedYears 2 → service years 3 → 50k + 3k
        Assert.Equal(53_000m, without);
    }

    [Fact]
    public void Progression_UpgradesToNextScale_AfterMaxYear()
    {
        var scales = new List<SalaryScale>
        {
            new() { Id = 10, ScaleName = "RLT-10", DisplayOrder = 10, BasicSalary = 85_000m, YearlyIncrement = 3_000m, MaximumSalary = 100_000m, IsActive = true },
            new() { Id = 11, ScaleName = "RLT-11", DisplayOrder = 11, BasicSalary = 100_000m, YearlyIncrement = 6_250m, MaximumSalary = 125_000m, IsActive = true },
        };

        // stepsToMax on RLT-10 = floor(15000/3000) = 5. Service years 5 → stay at Max.
        var atMax = PayrollScaleProgression.Resolve(
            scales[0], scales, new DateTime(2020, 11, 1), new DateOnly(2024, 11, 1));
        // completedYears from 2020-11-01 to 2024-11-01 = 4 → service years 5
        Assert.Equal("RLT-10", atMax.EffectiveScale!.ScaleName);
        Assert.Equal(100_000m, atMax.CurrentPay);
        Assert.False(atMax.Upgraded);

        // Service years 6 → upgrade to RLT-11 with 1 year → 100000 + 6250
        var upgraded = PayrollScaleProgression.Resolve(
            scales[0], scales, new DateTime(2020, 11, 1), new DateOnly(2025, 11, 1));
        Assert.Equal("RLT-11", upgraded.EffectiveScale!.ScaleName);
        Assert.Equal(106_250m, upgraded.CurrentPay);
        Assert.True(upgraded.Upgraded);
        Assert.Equal(new DateOnly(2025, 11, 1), upgraded.EffectiveScaleDate);
    }

    [Fact]
    public void Progression_UsesScaleDateNotDoj_WhenScaleAppliedMidCareer()
    {
        var scales = new List<SalaryScale>
        {
            new() { Id = 10, ScaleName = "RLT-10", DisplayOrder = 10, BasicSalary = 85_000m, YearlyIncrement = 3_000m, MaximumSalary = 100_000m, IsActive = true },
            new() { Id = 11, ScaleName = "RLT-11", DisplayOrder = 11, BasicSalary = 100_000m, YearlyIncrement = 6_250m, MaximumSalary = 125_000m, IsActive = true },
            new() { Id = 12, ScaleName = "RLT-12", DisplayOrder = 12, BasicSalary = 125_000m, YearlyIncrement = 6_250m, MaximumSalary = 150_000m, IsActive = true },
        };

        // DOJ 2020 but RLT-10 applied Aug 2024 — must NOT walk from 2020 (which falsely becomes RLT-12).
        var scaleDate = new DateTime(2024, 8, 1);
        var anchor = PayrollScaleProgression.ResolveIncrementAnchor(scaleDate, new DateTime(2020, 11, 1));
        Assert.Equal(scaleDate, anchor);

        var result = PayrollScaleProgression.Resolve(
            scales[0], scales, anchor, new DateOnly(2026, 9, 15));
        // From 2024-08-01 to 2026-09-15 → completedYears 2 → service years 3 → still on RLT-10
        Assert.Equal("RLT-10", result.EffectiveScale!.ScaleName);
        Assert.Equal(94_000m, result.CurrentPay);
        Assert.False(result.Upgraded);
        Assert.Null(result.EffectiveScaleDate);
    }
}
