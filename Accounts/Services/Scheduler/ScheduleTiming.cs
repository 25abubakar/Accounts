namespace Accounts.Services.Scheduler;

// Display/business clock only. Scheduling, calendars and missed-run policy live in SQL.
public static class ScheduleTiming
{
    public static TimeZoneInfo Pakistan=>TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
    public static DateTime LocalNow=>TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,Pakistan);
}
