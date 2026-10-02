using Accounts.Services.Services;

namespace Accounts.Tests;

public sealed class BusinessClockTests
{
    [Theory]
    [InlineData("Asia/Karachi", 5)]
    [InlineData("Pakistan Standard Time", 5)]
    [InlineData("Asia/Dubai", 4)]
    [InlineData("Arabian Standard Time", 4)]
    public void ConvertsUtcInstantToConfiguredBusinessTime(string timeZoneId, int expectedHour)
    {
        var utc = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);

        var local = BusinessClock.ToLocal(utc, timeZoneId);

        Assert.Equal(new DateTime(2026, 9, 30, expectedHour, 0, 0), local);
        Assert.Equal(DateTimeKind.Unspecified, local.Kind);
    }

    [Theory]
    [InlineData("Asia/Karachi", 5)]
    [InlineData("Asia/Dubai", 4)]
    public void BusinessWallTimeRoundTripsThroughUtc(string timeZoneId, int offsetHours)
    {
        var wallTime = new DateTime(2026, 9, 30, 10, 15, 0, DateTimeKind.Unspecified);

        var utc = BusinessClock.ToUtc(wallTime, timeZoneId);
        var roundTrip = BusinessClock.ToLocal(utc, timeZoneId);

        Assert.Equal(wallTime, roundTrip);
        Assert.Equal(10 - offsetHours, utc.Hour);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void InvalidTimeZoneUsesExplicitKarachiFallback()
    {
        var utc = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);

        var local = BusinessClock.ToLocal(utc, "Invalid/TimeZone");

        Assert.Equal(new DateTime(2026, 9, 30, 5, 0, 0), local);
    }
}
