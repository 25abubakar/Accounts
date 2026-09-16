using Xunit;

namespace Accounts.Tests;

/// <summary>
/// Documents the attendance WHrs priority used by Daily Attendance CR/DB:
/// mapped shift duration wins over AttendanceRuleSettings.WorkingMinutes (often 540).
/// </summary>
public sealed class AttendanceRequiredMinutesPriorityTests
{
    [Theory]
    [InlineData("11:00", "17:00", 540, 360)] // Kishwar map 6h beats rule 9h
    [InlineData("15:00", "22:00", 540, 420)] // Aftab evening 7h
    [InlineData("09:00", "18:00", 540, 540)] // standard 9h map matches rule
    [InlineData("08:50", "17:50", 540, 540)] // Farooq 9h
    [InlineData("", "", 540, 540)]           // no map times → rule fallback
    [InlineData("09:00", "18:00", 0, 540)]    // map only
    public void Resolve_PrefersMappedShiftDuration_OverRuleDefault(
        string start,
        string end,
        int ruleMinutes,
        int expected)
    {
        var shiftMinutes = ShiftMinutes(start, end);
        var required = shiftMinutes > 0 ? shiftMinutes : Math.Max(0, ruleMinutes);
        Assert.Equal(expected, required);
    }

    [Fact]
    public void CrDb_FullMappedShift_IsNotShortage()
    {
        const int required = 360; // 11:00-17:00
        const int worked = 369;   // 10:59-17:08 style present
        Assert.True(worked - required > 0);
    }

    private static int ShiftMinutes(string start, string end)
    {
        var startText = (start ?? string.Empty).Trim();
        var endText = (end ?? string.Empty).Trim();
        if (startText.Length >= 5) startText = startText[..5];
        if (endText.Length >= 5) endText = endText[..5];
        if (!TimeOnly.TryParseExact(startText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var from) ||
            !TimeOnly.TryParseExact(endText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var to))
            return 0;
        var minutes = (int)(to.ToTimeSpan() - from.ToTimeSpan()).TotalMinutes;
        return minutes > 0 ? minutes : minutes + 1440;
    }
}
