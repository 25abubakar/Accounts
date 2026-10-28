using Accounts.Models.Scheduler;
using Accounts.DTOs.Scheduler;

namespace Accounts.Services.Scheduler;

public static class ScheduleTiming
{
    public static readonly string[] Modes = ["IntervalMinutes", "Hourly", "Daily", "Weekly", "Monthly", "IntervalDays", "Once"];
    public static TimeZoneInfo Pakistan => TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
    public static DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Pakistan);
    public static DateTime StartUtc(ScheduleDefinition value) => ToUtc(value.StartLocal);
    private static DateTime ToUtc(DateTime value) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), Pakistan);
    public static string? Validate(ScheduleEdit value)
    {
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 120) return "Name must contain 1–120 characters.";
        if (!Modes.Contains(value.Mode)) return "Choose a supported schedule frequency.";
        if (!TimeOnly.TryParseExact(value.Time, "HH:mm", out _)) return "Time must use HH:mm format.";
        if (value.IntervalDays is < 1 or > 365) return "Interval must be 1–365 days.";
        if (value.IntervalMinutes is < 1 or > 1440) return "Minute interval must be 1–1440.";
        if (value.DayOfMonth is < 1 or > 31 || value.DayOfWeek is < 0 or > 6) return "Choose a valid day.";
        if (value.StartLocal.Year is < 2020 or > 2100 || value.StartLocal.Kind != DateTimeKind.Unspecified) return "Start must be a local Pakistan date/time without a UTC suffix.";
        if (value.MisfirePolicy is not ("RunOnce" or "Skip")) return "Choose RunOnce or Skip for missed executions.";
        if (value.TimeoutSeconds is < 15 or > 600 || value.RetryCount is < 0 or > 3) return "Timeout must be 15–600 seconds and retries 0–3.";
        return value.CreationKey == Guid.Empty ? "A request identifier is required." : null;
    }

    // The first anchored occurrence strictly after this instant; all calendar times are Pakistan time.
    public static DateTime? NextAfter(ScheduleDefinition value, DateTime afterUtc)
    {
        var anchor = StartUtc(value);
        afterUtc = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
        if (value.Mode == "Once") return anchor > afterUtc ? anchor : null;
        if (value.Mode is "IntervalDays" or "IntervalMinutes")
        {
            if (anchor > afterUtc) return anchor;
            var interval = value.Mode=="IntervalDays" ? TimeSpan.FromDays(value.IntervalDays).Ticks : TimeSpan.FromMinutes(value.IntervalMinutes).Ticks;
            return anchor.AddTicks(((afterUtc.Ticks - anchor.Ticks) / interval + 1) * interval);
        }
        var lower = afterUtc >= anchor ? afterUtc : anchor.AddTicks(-1);
        var local = TimeZoneInfo.ConvertTimeFromUtc(lower, Pakistan);
        var time = TimeOnly.ParseExact(value.Time, "HH:mm");
        DateTime candidate;
        switch (value.Mode)
        {
            case "Hourly":
                candidate = local.Date.AddHours(local.Hour).AddMinutes(time.Minute);
                if (ToUtc(candidate) <= lower) candidate = candidate.AddHours(1);
                break;
            case "Daily":
                candidate = local.Date.Add(time.ToTimeSpan());
                if (ToUtc(candidate) <= lower) candidate = candidate.AddDays(1);
                break;
            case "Weekly":
                candidate = local.Date.AddDays((value.DayOfWeek - (int)local.DayOfWeek + 7) % 7).Add(time.ToTimeSpan());
                if (ToUtc(candidate) <= lower) candidate = candidate.AddDays(7);
                break;
            case "Monthly":
                var month = new DateTime(local.Year, local.Month, 1);
                for (var i = 0; i < 24; i++, month = month.AddMonths(1))
                {
                    if (value.DayOfMonth > DateTime.DaysInMonth(month.Year, month.Month)) continue;
                    candidate = month.AddDays(value.DayOfMonth - 1).Add(time.ToTimeSpan());
                    if (ToUtc(candidate) > lower) return ToUtc(candidate);
                }
                throw new InvalidOperationException("No valid monthly occurrence.");
            default: throw new InvalidOperationException("Unsupported frequency.");
        }
        return ToUtc(candidate);
    }

    public static DateTime? FirstDue(ScheduleDefinition value, DateTime nowUtc)
    {
        var first = NextAfter(value, StartUtc(value).AddTicks(-1));
        if (first <= nowUtc && value.MisfirePolicy == "Skip") return NextAfter(value, nowUtc);
        return first;
    }

    public static IReadOnlyList<DateTime> Preview(ScheduleDefinition value, DateTime nowUtc)
    {
        var result = new List<DateTime>();
        var next = NextAfter(value, nowUtc);
        while (next.HasValue && result.Count < 5) { result.Add(next.Value); next = NextAfter(value, next.Value); }
        return result;
    }
}
