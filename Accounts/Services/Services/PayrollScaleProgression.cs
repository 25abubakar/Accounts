using Accounts.Models;

namespace Accounts.Services.Services;

/// <summary>
/// Current pay + optional auto scale upgrade when Max is exceeded after another service year.
/// Increment years are counted from the scale application date (ScaleDate), falling back to DOJ.
/// Service years = completed anniversary years + 1 (first year on the scale already earns 1× INC).
/// When BAS + years×INC would pass Max, the employee stays at Max through that year; the next
/// service year moves them to the next ladder scale (e.g. RLT-10 → RLT-11) and applies remaining years there.
/// </summary>
public static class PayrollScaleProgression
{
    public sealed record Result(
        SalaryScale? EffectiveScale,
        decimal ScaleBasic,
        decimal YearlyIncrement,
        decimal MaxSalary,
        decimal CurrentPay,
        int ServiceYears,
        bool Upgraded,
        DateOnly? EffectiveScaleDate);

    /// <summary>
    /// ScaleDate is when the current scale was applied (join or later upgrade/extension).
    /// Prefer it over DOJ so mid-career scale placement is not re-walked from hire date.
    /// </summary>
    public static DateTime? ResolveIncrementAnchor(DateTime? scaleDate, DateTime? joiningDate) =>
        scaleDate ?? joiningDate;

    public static int CountServiceYears(DateTime? incrementAnchorDate, DateOnly asOfDate, int? applyAfterYears = null)
    {
        if (!incrementAnchorDate.HasValue)
            return 0;

        var anchor = DateOnly.FromDateTime(incrementAnchorDate.Value.Date);
        if (anchor > asOfDate)
            return 0;

        var completedYears = asOfDate.Year - anchor.Year;
        if (asOfDate < anchor.AddYears(completedYears))
            completedYears--;
        if (completedYears < 0)
            completedYears = 0;

        // First service year (completedYears == 0) still gets 1× INC.
        return Math.Max(0, completedYears + 1 - Math.Max(0, applyAfterYears ?? 0));
    }

    public static Result Resolve(
        SalaryScale? startingScale,
        IReadOnlyList<SalaryScale> activeScales,
        DateTime? incrementAnchorDate,
        DateOnly asOfDate,
        decimal? profileBasic = null,
        decimal? profileIncrement = null,
        decimal? profileMax = null)
    {
        if (startingScale == null)
        {
            var basic = Money(profileBasic ?? 0);
            var inc = Money(profileIncrement ?? 0);
            var max = Money(profileMax ?? 0);
            var years = CountServiceYears(incrementAnchorDate, asOfDate, null);
            var pay = ComputeFlat(basic, inc, max, years, incrementAnchorDate, asOfDate, null, null);
            return new Result(null, basic, inc, max, pay, years, false, null);
        }

        var months = PayrollCurrentPayCalculator.ParseMonthsCsv(startingScale.IncrementMonths);
        // Mid-year installment schedules stay on the starting scale (no ladder walk).
        if (months.Count > 0)
        {
            var basic = Money(startingScale.BasicSalary);
            var inc = Money(startingScale.YearlyIncrement);
            var max = Money(startingScale.MaximumSalary);
            var pay = PayrollCurrentPayCalculator.Compute(
                basic, inc, max, incrementAnchorDate, asOfDate, startingScale.ApplyAfter, months);
            return new Result(startingScale, basic, inc, max, pay, CountServiceYears(incrementAnchorDate, asOfDate, startingScale.ApplyAfter), false, null);
        }

        var yearsLeft = CountServiceYears(incrementAnchorDate, asOfDate, startingScale.ApplyAfter);
        var ladder = BuildLadder(startingScale, activeScales);
        var scale = startingScale;
        var upgraded = false;
        var yearsConsumedBeforeCurrentScale = 0;
        var guard = 0;

        while (guard++ < 32)
        {
            var basic = Money(scale.BasicSalary);
            var inc = Money(scale.YearlyIncrement);
            var max = Money(scale.MaximumSalary);

            if (inc <= 0 || yearsLeft <= 0)
                return new Result(scale, basic, inc, max, Cap(basic, max), yearsLeft, upgraded, EffectiveScaleDate(incrementAnchorDate, yearsConsumedBeforeCurrentScale, upgraded));

            var stepsToMax = StepsToReachMax(basic, inc, max);
            if (yearsLeft <= stepsToMax)
            {
                var pay = Cap(basic + yearsLeft * inc, max);
                return new Result(scale, basic, inc, max, pay, yearsLeft, upgraded, EffectiveScaleDate(incrementAnchorDate, yearsConsumedBeforeCurrentScale, upgraded));
            }

            var next = FindNext(scale, ladder);
            if (next == null)
            {
                // No higher scale — remain capped at Max.
                return new Result(scale, basic, inc, max, Cap(basic + yearsLeft * inc, max), yearsLeft, upgraded, EffectiveScaleDate(incrementAnchorDate, yearsConsumedBeforeCurrentScale, upgraded));
            }

            yearsLeft -= stepsToMax;
            yearsConsumedBeforeCurrentScale += stepsToMax;
            scale = next;
            upgraded = true;
        }

        return new Result(
            scale,
            Money(scale.BasicSalary),
            Money(scale.YearlyIncrement),
            Money(scale.MaximumSalary),
            Cap(Money(scale.BasicSalary), Money(scale.MaximumSalary)),
            yearsLeft,
            upgraded,
            EffectiveScaleDate(incrementAnchorDate, yearsConsumedBeforeCurrentScale, upgraded));
    }

    /// <summary>Legacy single-scale formula used when no SalaryScale row is available.</summary>
    public static decimal ComputeFlat(
        decimal basicSalary,
        decimal yearlyIncrement,
        decimal maximumSalary,
        int serviceYears,
        DateTime? incrementAnchorDate,
        DateOnly asOfDate,
        int? applyAfterYears,
        IReadOnlyList<int>? incrementMonths)
    {
        var months = PayrollCurrentPayCalculator.NormalizeMonths(incrementMonths);
        if (months.Count > 0)
            return PayrollCurrentPayCalculator.Compute(basicSalary, yearlyIncrement, maximumSalary, incrementAnchorDate, asOfDate, applyAfterYears, months);

        var years = serviceYears >= 0
            ? serviceYears
            : CountServiceYears(incrementAnchorDate, asOfDate, applyAfterYears);
        return Cap(basicSalary + Math.Max(0, years) * yearlyIncrement, maximumSalary);
    }

    public static int StepsToReachMax(decimal basic, decimal yearlyIncrement, decimal maximumSalary)
    {
        if (yearlyIncrement <= 0)
            return int.MaxValue / 4;
        if (maximumSalary <= 0 || maximumSalary <= basic)
            return int.MaxValue / 4;
        return (int)decimal.Floor((maximumSalary - basic) / yearlyIncrement);
    }

    public static IReadOnlyList<SalaryScale> BuildLadder(SalaryScale start, IReadOnlyList<SalaryScale> activeScales)
    {
        var prefix = ScalePrefix(start.ScaleName);
        return activeScales
            .Where(x => x.IsActive && string.Equals(ScalePrefix(x.ScaleName), prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.ScaleName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.Id).First())
            .OrderBy(x => x.DisplayOrder == 0 ? int.MaxValue : x.DisplayOrder)
            .ThenBy(x => ScaleNumber(x.ScaleName))
            .ThenBy(x => x.ScaleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static SalaryScale? FindNext(SalaryScale current, IReadOnlyList<SalaryScale> ladder)
    {
        for (var i = 0; i < ladder.Count; i++)
        {
            if (!string.Equals(ladder[i].ScaleName, current.ScaleName, StringComparison.OrdinalIgnoreCase))
                continue;
            return i + 1 < ladder.Count ? ladder[i + 1] : null;
        }

        // Fallback: first scale with higher DisplayOrder / number.
        var currentOrder = current.DisplayOrder == 0 ? ScaleNumber(current.ScaleName) : current.DisplayOrder;
        return ladder.FirstOrDefault(x =>
            !string.Equals(x.ScaleName, current.ScaleName, StringComparison.OrdinalIgnoreCase)
            && (x.DisplayOrder == 0 ? ScaleNumber(x.ScaleName) : x.DisplayOrder) > currentOrder);
    }

    public static string ScalePrefix(string? scaleName)
    {
        if (string.IsNullOrWhiteSpace(scaleName))
            return string.Empty;
        var name = scaleName.Trim();
        var dash = name.LastIndexOf('-');
        if (dash <= 0)
            return name;
        var suffix = name[(dash + 1)..];
        return int.TryParse(suffix, out _) ? name[..dash] : name;
    }

    public static int ScaleNumber(string? scaleName)
    {
        if (string.IsNullOrWhiteSpace(scaleName))
            return 0;
        var dash = scaleName.Trim().LastIndexOf('-');
        if (dash < 0 || dash == scaleName.Trim().Length - 1)
            return 0;
        return int.TryParse(scaleName.Trim()[(dash + 1)..], out var n) ? n : 0;
    }

    private static DateOnly? EffectiveScaleDate(DateTime? incrementAnchorDate, int yearsConsumedBeforeCurrentScale, bool upgraded)
    {
        if (!upgraded || !incrementAnchorDate.HasValue || yearsConsumedBeforeCurrentScale <= 0)
            return null;
        return DateOnly.FromDateTime(incrementAnchorDate.Value.Date).AddYears(yearsConsumedBeforeCurrentScale);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Cap(decimal amount, decimal maximumSalary)
    {
        var rounded = Money(amount);
        if (maximumSalary > 0 && rounded > maximumSalary)
            return Money(maximumSalary);
        return rounded;
    }
}
