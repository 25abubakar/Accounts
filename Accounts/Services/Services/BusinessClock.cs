namespace Accounts.Services.Services;

/// <summary>
/// Central time-zone conversion helpers. Instants and audit timestamps stay in
/// UTC; business dates and wall-clock values are calculated in an explicit
/// IANA/Windows time zone. Never falls back to the server machine's local zone.
/// </summary>
public static class BusinessClock
{
    public const string DefaultTimeZoneId = "Asia/Karachi";

    public static DateTime UtcNow() => DateTime.UtcNow;

    public static DateTime Now(string? timeZoneId) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(UtcNow(), Resolve(timeZoneId)),
            DateTimeKind.Unspecified);

    public static DateOnly Today(string? timeZoneId) => DateOnly.FromDateTime(Now(timeZoneId));

    public static DateTime ToLocal(DateTime utcValue, string? timeZoneId)
    {
        var utc = utcValue.Kind == DateTimeKind.Utc
            ? utcValue
            : DateTime.SpecifyKind(utcValue, DateTimeKind.Utc);
        return DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(utc, Resolve(timeZoneId)),
            DateTimeKind.Unspecified);
    }

    public static DateTime? ToLocal(DateTime? utcValue, string? timeZoneId) =>
        utcValue.HasValue ? ToLocal(utcValue.Value, timeZoneId) : null;

    public static DateTime ToUtc(DateTime localValue, string? timeZoneId)
    {
        var zone = Resolve(timeZoneId);
        var local = DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified);

        // A wall time inside the daylight-saving gap does not exist. Move to
        // the first valid minute instead of silently using the server zone.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static DateTime? ToUtc(DateTime? localValue, string? timeZoneId) =>
        localValue.HasValue ? ToUtc(localValue.Value, timeZoneId) : null;

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        var requested = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim();
        if (TryFind(requested, out var zone)) return zone;

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(requested, out var windowsId)
            && TryFind(windowsId, out zone))
            return zone;

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(requested, out var ianaId)
            && TryFind(ianaId, out zone))
            return zone;

        if (TryFind(DefaultTimeZoneId, out zone)) return zone;
        if (TryFind("Pakistan Standard Time", out zone)) return zone;

        return TimeZoneInfo.CreateCustomTimeZone(
            DefaultTimeZoneId,
            TimeSpan.FromHours(5),
            "Pakistan Standard Time",
            "Pakistan Standard Time");
    }

    public static string BrowserTimeZoneId(string? timeZoneId)
    {
        var requested = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim();
        if (requested.Contains('/')) return requested;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(requested, out var ianaId)
            ? ianaId
            : DefaultTimeZoneId;
    }

    private static bool TryFind(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        zone = null!;
        return false;
    }
}
