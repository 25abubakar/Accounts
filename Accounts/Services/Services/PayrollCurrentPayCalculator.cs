namespace Accounts.Services.Services;

/// <summary>
/// Server-authoritative CurrentPay = Basic + completed ScaleDate years × yearly increment (after ApplyAfter), capped at Max.
/// </summary>
public static class PayrollCurrentPayCalculator
{
    public static decimal Compute(
        decimal basicSalary,
        decimal yearlyIncrement,
        decimal maximumSalary,
        DateTime? scaleDate,
        DateOnly asOfDate,
        int? applyAfterYears = null)
    {
        if (basicSalary < 0) basicSalary = 0;
        if (yearlyIncrement < 0) yearlyIncrement = 0;

        if (!scaleDate.HasValue || yearlyIncrement == 0)
            return Cap(basicSalary, maximumSalary);

        var scaleStart = DateOnly.FromDateTime(scaleDate.Value.Date);
        if (scaleStart > asOfDate)
            return Cap(basicSalary, maximumSalary);

        var completedYears = asOfDate.Year - scaleStart.Year;
        if (asOfDate < scaleStart.AddYears(completedYears))
            completedYears--;
        if (completedYears < 0) completedYears = 0;

        var applyAfter = Math.Max(0, applyAfterYears ?? 0);
        var incrementCount = Math.Max(0, completedYears - applyAfter);
        var raw = basicSalary + incrementCount * yearlyIncrement;
        return Cap(raw, maximumSalary);
    }

    private static decimal Cap(decimal amount, decimal maximumSalary)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (maximumSalary > 0 && rounded > maximumSalary)
            return decimal.Round(maximumSalary, 2, MidpointRounding.AwayFromZero);
        return rounded;
    }
}
