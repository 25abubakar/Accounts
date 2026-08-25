using System.Globalization;

namespace Accounts.Services.Services;

public static class SupervisorAttendanceTime
{
    public static bool IsWorkingDay(DateOnly attendanceDate, bool? scheduledIsOn) =>
        scheduledIsOn ??
        (attendanceDate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);

    public static (DateTime? CheckIn, DateTime? CheckOut) Parse(
        DateOnly attendanceDate,
        string? checkInTime,
        string? checkOutTime)
    {
        var checkIn = ParseOne(attendanceDate, checkInTime, "Check-in");
        var checkOut = ParseOne(attendanceDate, checkOutTime, "Check-out");
        if (checkIn.HasValue && checkOut.HasValue && checkOut.Value < checkIn.Value)
            checkOut = checkOut.Value.AddDays(1);
        return (checkIn, checkOut);
    }

    private static DateTime? ParseOne(DateOnly date, string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!TimeOnly.TryParseExact(
                value.Trim(),
                ["HH:mm", "HH:mm:ss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
            throw new ArgumentException($"{label} time must be in HH:mm format.");

        return PakistanClock.AsDatabaseLocal(date.ToDateTime(time));
    }
}
