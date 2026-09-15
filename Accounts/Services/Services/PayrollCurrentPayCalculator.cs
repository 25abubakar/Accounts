namespace Accounts.Services.Services;

/// <summary>
/// Server-authoritative CurrentPay helpers. Prefer <see cref="PayrollScaleProgression"/> for
/// DOJ service years (first year = 1× INC) and Max → next-scale upgrades.
/// This type remains for installment-month splitting and simple Cap math.
/// </summary>
public static class PayrollCurrentPayCalculator
{
    public static decimal Compute(
        decimal basicSalary,
        decimal yearlyIncrement,
        decimal maximumSalary,
        DateTime? incrementAnchorDate,
        DateOnly asOfDate,
        int? applyAfterYears = null,
        IReadOnlyList<int>? incrementMonths = null)
    {
        if (basicSalary < 0) basicSalary = 0;
        if (yearlyIncrement < 0) yearlyIncrement = 0;

        if (!incrementAnchorDate.HasValue || yearlyIncrement == 0)
            return Cap(basicSalary, maximumSalary);

        var anchorStart = DateOnly.FromDateTime(incrementAnchorDate.Value.Date);
        if (anchorStart > asOfDate)
            return Cap(basicSalary, maximumSalary);

        var months = NormalizeMonths(incrementMonths);
        if (months.Count == 0)
        {
            var years = PayrollScaleProgression.CountServiceYears(incrementAnchorDate, asOfDate, applyAfterYears);
            return Cap(basicSalary + years * yearlyIncrement, maximumSalary);
        }

        var installment = decimal.Round(yearlyIncrement / months.Count, 2, MidpointRounding.AwayFromZero);
        var eligibleFrom = anchorStart.AddYears(Math.Max(0, applyAfterYears ?? 0));
        if (eligibleFrom > asOfDate)
            return Cap(basicSalary, maximumSalary);

        var earned = 0m;
        for (var year = eligibleFrom.Year; year <= asOfDate.Year; year++)
        {
            foreach (var month in months)
            {
                var day = Math.Min(eligibleFrom.Day, DateTime.DaysInMonth(year, month));
                var installmentDate = new DateOnly(year, month, day);
                if (installmentDate < eligibleFrom || installmentDate > asOfDate)
                    continue;
                earned += installment;
            }
        }

        return Cap(basicSalary + earned, maximumSalary);
    }

    public static IReadOnlyList<int> NormalizeMonths(IEnumerable<int>? months)
    {
        if (months == null) return Array.Empty<int>();
        return months
            .Where(m => m is >= 1 and <= 12)
            .Distinct()
            .OrderBy(m => m)
            .ToArray();
    }

    public static IReadOnlyList<int> ParseMonthsCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<int>();
        var parsed = new List<int>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var month) && month is >= 1 and <= 12)
                parsed.Add(month);
        }
        return NormalizeMonths(parsed);
    }

    public static string? ToMonthsCsv(IEnumerable<int>? months)
    {
        var normalized = NormalizeMonths(months);
        return normalized.Count == 0 ? null : string.Join(',', normalized);
    }

    private static decimal Cap(decimal amount, decimal maximumSalary)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (maximumSalary > 0 && rounded > maximumSalary)
            return decimal.Round(maximumSalary, 2, MidpointRounding.AwayFromZero);
        return rounded;
    }
}
