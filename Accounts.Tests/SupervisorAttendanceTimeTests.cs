using Accounts.Services.Services;

namespace Accounts.Tests;

public sealed class SupervisorAttendanceTimeTests
{
    [Fact]
    public void Parse_StoresSameDayTimesAsUnspecifiedDatabaseLocalValues()
    {
        var date = new DateOnly(2026, 8, 24);

        var result = SupervisorAttendanceTime.Parse(date, "08:15", "17:45:30");

        Assert.Equal(new DateTime(2026, 8, 24, 8, 15, 0), result.CheckIn);
        Assert.Equal(new DateTime(2026, 8, 24, 17, 45, 30), result.CheckOut);
        Assert.Equal(DateTimeKind.Unspecified, result.CheckIn!.Value.Kind);
        Assert.Equal(DateTimeKind.Unspecified, result.CheckOut!.Value.Kind);
    }

    [Fact]
    public void Parse_MovesCheckoutToNextDayForOvernightShift()
    {
        var date = new DateOnly(2026, 8, 24);

        var result = SupervisorAttendanceTime.Parse(date, "20:10", "08:20");

        Assert.Equal(new DateTime(2026, 8, 24, 20, 10, 0), result.CheckIn);
        Assert.Equal(new DateTime(2026, 8, 25, 8, 20, 0), result.CheckOut);
    }

    [Fact]
    public void Parse_AllowsOneSidedEntryButRejectsInvalidTime()
    {
        var date = new DateOnly(2026, 8, 24);

        var oneSided = SupervisorAttendanceTime.Parse(date, "09:00", null);

        Assert.NotNull(oneSided.CheckIn);
        Assert.Null(oneSided.CheckOut);
        Assert.Throws<ArgumentException>(() =>
            SupervisorAttendanceTime.Parse(date, "9 AM", "17:00"));
    }

    [Theory]
    [InlineData(2026, 8, 24, null, true)]
    [InlineData(2026, 8, 22, null, false)]
    [InlineData(2026, 8, 23, null, false)]
    [InlineData(2026, 8, 22, true, true)]
    [InlineData(2026, 8, 24, false, false)]
    public void IsWorkingDay_UsesEmployeeScheduleBeforeWeekendDefault(
        int year,
        int month,
        int day,
        bool? scheduledIsOn,
        bool expected)
    {
        Assert.Equal(
            expected,
            SupervisorAttendanceTime.IsWorkingDay(
                new DateOnly(year, month, day),
                scheduledIsOn));
    }
}
