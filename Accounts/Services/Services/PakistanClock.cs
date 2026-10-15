namespace Accounts.Services.Services;

internal static class PakistanClock
{
    private static readonly TimeZoneInfo KarachiTimeZone = ResolveKarachiTimeZone();

    public static DateTime Now()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KarachiTimeZone);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    public static DateOnly Today() => DateOnly.FromDateTime(Now());

    public static TimeZoneInfo TimeZone => KarachiTimeZone;

    public static DateTime AsDatabaseLocal(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    public static DateTime? AsDatabaseLocal(DateTime? value) =>
        value.HasValue ? AsDatabaseLocal(value.Value) : null;

    private static TimeZoneInfo ResolveKarachiTimeZone()
    {
        foreach (var id in new[] { "Pakistan Standard Time", "Asia/Karachi" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Pakistan Standard Time",
            TimeSpan.FromHours(5),
            "Pakistan Standard Time",
            "Pakistan Standard Time");
    }
}
