using Accounts.Models;

namespace Accounts.Services.Services;

/// <summary>
/// Pakistani salaried progressive annual tax from <see cref="PayrollTaxSlab"/> rows.
/// <para>
/// DB contract: <c>FromAmount</c> = excess threshold (amount income must exceed),
/// <c>ToAmount</c> = inclusive upper of the band (null = open-ended),
/// Tax = FixedTaxAmount + (Annual − FromAmount) × RatePercentage / 100.
/// </para>
/// Example (FBR): annual 1,020,000 → slab 600,001–1,200,000 →
/// (1,020,000 − 600,000) × 1% = 4,200/year → 350/month.
/// </summary>
public static class PayrollTaxCalculator
{
    /// <summary>Annual incomes at or below this amount are tax-free.</summary>
    public const decimal TaxFreeAnnualCeiling = 600_000m;

    public static decimal CalculateAnnualTax(decimal annualTaxable, IReadOnlyList<PayrollTaxSlab> slabs)
        => Resolve(annualTaxable, slabs).AnnualTax;

    public static decimal CalculateMonthlyTax(decimal monthlyTaxablePay, IReadOnlyList<PayrollTaxSlab> slabs)
    {
        if (monthlyTaxablePay <= 0) return 0;
        return Money(CalculateAnnualTax(monthlyTaxablePay * 12m, slabs) / 12m);
    }

    public static bool IsTaxableAnnual(decimal annualTaxable, IReadOnlyList<PayrollTaxSlab> slabs) =>
        CalculateAnnualTax(annualTaxable, slabs) > 0;

    /// <summary>
    /// Full breakdown for Staff Tax / payroll dashboards.
    /// Matching: tax-free if annual ≤ 600,000; else first active slab where
    /// FromAmount &lt; annual ≤ ToAmount (ToAmount null = no ceiling).
    /// </summary>
    public static PayrollTaxBreakdown Resolve(decimal annualTaxable, IReadOnlyList<PayrollTaxSlab> slabs)
    {
        annualTaxable = Money(annualTaxable);
        if (annualTaxable <= 0 || slabs.Count == 0)
            return PayrollTaxBreakdown.None(annualTaxable);

        if (annualTaxable <= TaxFreeAnnualCeiling)
            return PayrollTaxBreakdown.TaxFree(annualTaxable);

        var ordered = slabs
            .Where(x => x.IsActive)
            .OrderBy(x => x.FromAmount)
            .ToList();
        if (ordered.Count == 0)
            return PayrollTaxBreakdown.None(annualTaxable);

        // FBR bands: income must exceed FromAmount (excess base), up to inclusive ToAmount.
        // e.g. 600,001–1,200,000 uses FromAmount=600,000 so 1,020,000 matches and excess = 420,000.
        var slab = ordered.FirstOrDefault(x =>
            annualTaxable > x.FromAmount
            && (!x.ToAmount.HasValue || annualTaxable <= x.ToAmount.Value));

        if (slab == null)
            return PayrollTaxBreakdown.None(annualTaxable);

        if (slab.RatePercentage <= 0 && slab.FixedTaxAmount <= 0)
            return PayrollTaxBreakdown.TaxFree(annualTaxable, slab.SlabName);

        var excess = Money(Math.Max(0m, annualTaxable - slab.FromAmount));
        var annualTax = Money(slab.FixedTaxAmount + excess * slab.RatePercentage / 100m);
        var monthlyTax = Money(annualTax / 12m);

        return new PayrollTaxBreakdown(
            AnnualTaxable: annualTaxable,
            SlabName: slab.SlabName,
            ExcessBase: slab.FromAmount,
            ExcessAmount: excess,
            FixedTaxAmount: slab.FixedTaxAmount,
            RatePercentage: slab.RatePercentage,
            AnnualTax: annualTax,
            MonthlyTax: monthlyTax,
            BandToAmount: slab.ToAmount);
    }

    public static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Resolved progressive tax for one annual taxable income.</summary>
public sealed record PayrollTaxBreakdown(
    decimal AnnualTaxable,
    string SlabName,
    decimal ExcessBase,
    decimal ExcessAmount,
    decimal FixedTaxAmount,
    decimal RatePercentage,
    decimal AnnualTax,
    decimal MonthlyTax,
    decimal? BandToAmount)
{
    public bool IsTaxFree => AnnualTax <= 0;

    public static PayrollTaxBreakdown None(decimal annual) =>
        new(annual, string.Empty, 0, 0, 0, 0, 0, 0, null);

    public static PayrollTaxBreakdown TaxFree(decimal annual, string slabName = "Slab 1") =>
        new(annual, slabName, 0, 0, 0, 0, 0, 0, PayrollTaxCalculator.TaxFreeAnnualCeiling);
}
