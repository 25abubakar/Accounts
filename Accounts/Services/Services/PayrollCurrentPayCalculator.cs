namespace Accounts.Services.Services;

/// <summary>
/// Server-authoritative CurrentPay from Basic + ScaleDate increments, capped at Max.
/// <list type="bullet">
/// <item>No Inc Months → Basic + completedYearsAfterApplyAfter × YearlyIncrement (legacy).</item>
/// <item>Inc Months set (e.g. 1,6) → YearlyIncrement is split equally; each configured calendar month
/// after ApplyAfter earns one installment (1000 / 2 = 500 in Jan and 500 in June).</item>
/// </list>
/// </summary>
public static class PayrollCurrentPayCalculator
{
    public static decimal Compute(
        decimal basicSalary,
        decimal yearlyIncrement,
        decimal maximumSalary,
        DateTime? scaleDate,
        DateOnly asOfDate,
        int? applyAfterYears = null,
        IReadOnlyList<int>? incrementMonths = null)
    {
        if (basicSalary < 0) basicSalary = 0;
        if (yearlyIncrement < 0) yearlyIncrement = 0;

        if (!scaleDate.HasValue || yearlyIncrement == 0)
            return Cap(basicSalary, maximumSalary);

        var scaleStart = DateOnly.FromDateTime(scaleDate.Value.Date);
        if (scaleStart > asOfDate)
            return Cap(basicSalary, maximumSalary);

        var months = NormalizeMonths(incrementMonths);
        if (months.Count == 0)
        {
            var completedYears = asOfDate.Year - scaleStart.Year;
            if (asOfDate < scaleStart.AddYears(completedYears))
                completedYears--;
            if (completedYears < 0) completedYears = 0;

            var applyAfter = Math.Max(0, applyAfterYears ?? 0);
            var incrementCount = Math.Max(0, completedYears - applyAfter);
            return Cap(basicSalary + incrementCount * yearlyIncrement, maximumSalary);
        }

        var installment = decimal.Round(yearlyIncrement / months.Count, 2, MidpointRounding.AwayFromZero);
        var eligibleFrom = scaleStart.AddYears(Math.Max(0, applyAfterYears ?? 0));
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
