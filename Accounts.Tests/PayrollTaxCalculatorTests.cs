using Accounts.Models;
using Accounts.Services.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class PayrollTaxCalculatorTests
{
    /// <summary>
    /// FBR salaried slabs: FromAmount = excess threshold, ToAmount = inclusive band ceiling.
    /// </summary>
    private static List<PayrollTaxSlab> ProgressiveSlabs() =>
    [
        new() { SlabName = "Slab 1", FromAmount = 0, ToAmount = 600_000m, FixedTaxAmount = 0, RatePercentage = 0, IsActive = true },
        new() { SlabName = "Slab 2", FromAmount = 600_000m, ToAmount = 1_200_000m, FixedTaxAmount = 0, RatePercentage = 1, IsActive = true },
        new() { SlabName = "Slab 3", FromAmount = 1_200_000m, ToAmount = 2_200_000m, FixedTaxAmount = 6_000m, RatePercentage = 11, IsActive = true },
        new() { SlabName = "Slab 4", FromAmount = 2_200_000m, ToAmount = 3_200_000m, FixedTaxAmount = 116_000m, RatePercentage = 23, IsActive = true },
        new() { SlabName = "Slab 5", FromAmount = 3_200_000m, ToAmount = 4_100_000m, FixedTaxAmount = 346_000m, RatePercentage = 30, IsActive = true },
        new() { SlabName = "Slab 6", FromAmount = 4_100_000m, ToAmount = null, FixedTaxAmount = 616_000m, RatePercentage = 35, IsActive = true },
    ];

    [Theory]
    [InlineData(500_000, 0)]
    [InlineData(600_000, 0)]
    [InlineData(600_001, 0.01)]          // 1% of 1
    [InlineData(1_020_000, 4_200)]       // FBR sample: (1,020,000 − 600,000) × 1%
    [InlineData(900_000, 3_000)]
    [InlineData(1_200_000, 6_000)]       // top of 1% band
    [InlineData(1_200_001, 6_000.11)]    // 6,000 + 11% of 1
    [InlineData(1_400_000, 28_000)]      // 6k + 11% of 200k
    [InlineData(2_500_000, 185_000)]
    [InlineData(3_500_000, 436_000)]
    [InlineData(4_500_000, 756_000)]
    public void CalculateAnnualTax_MatchesFbrProgressiveExamples(decimal annual, decimal expected)
    {
        var tax = PayrollTaxCalculator.CalculateAnnualTax(annual, ProgressiveSlabs());
        Assert.Equal(expected, tax);
    }

    [Fact]
    public void Resolve_FbrSample_1020000_Returns4200YearlyAnd350Monthly()
    {
        var b = PayrollTaxCalculator.Resolve(1_020_000m, ProgressiveSlabs());
        Assert.Equal("Slab 2", b.SlabName);
        Assert.Equal(600_000m, b.ExcessBase);
        Assert.Equal(420_000m, b.ExcessAmount);
        Assert.Equal(4_200m, b.AnnualTax);
        Assert.Equal(350m, b.MonthlyTax);
    }

    [Fact]
    public void CalculateMonthlyTax_FbrSample_85000Monthly_Is350()
    {
        // Annual 1,020,000 / 12 = 85,000 → monthly tax 350
        var monthly = PayrollTaxCalculator.CalculateMonthlyTax(85_000m, ProgressiveSlabs());
        Assert.Equal(350m, monthly);
    }

    [Fact]
    public void CalculateMonthlyTax_DividesAnnualByTwelve()
    {
        var monthly = PayrollTaxCalculator.CalculateMonthlyTax(1_400_000m / 12m, ProgressiveSlabs());
        Assert.Equal(2_333.33m, monthly);
    }

    [Fact]
    public void IsTaxableAnnual_FalseBelowOrAtCeiling()
    {
        Assert.False(PayrollTaxCalculator.IsTaxableAnnual(600_000m, ProgressiveSlabs()));
        Assert.True(PayrollTaxCalculator.IsTaxableAnnual(600_001m, ProgressiveSlabs()));
    }
}
