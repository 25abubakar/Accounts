namespace Accounts.Services.Services;

public readonly record struct AssessmentCycle(
    int Year,
    int Month,
    DateOnly OpenDate,
    DateOnly CloseDate)
{
    public bool Contains(DateOnly date) => date >= OpenDate && date <= CloseDate;
}

public static class AssessmentCycleWindow
{
    public static AssessmentCycle Create(int year, int month, int openDay, int closeDay)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));
        if (openDay is < 1 or > 31 || closeDay is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(openDay));

        var openMonth = new DateOnly(year, month, 1);
        var closeMonth = openMonth.AddMonths(1);
        var openDate = new DateOnly(year, month, Math.Min(openDay, DateTime.DaysInMonth(year, month)));
        var closeDate = new DateOnly(closeMonth.Year, closeMonth.Month,
            Math.Min(closeDay, DateTime.DaysInMonth(closeMonth.Year, closeMonth.Month)));
        return new AssessmentCycle(year, month, openDate, closeDate);
    }
}
