using Accounts.Services.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class AssessmentCycleWindowTests
{
    [Fact]
    public void Create_BuildsCrossMonthRecurringWindowWithInclusiveEndpoints()
    {
        var cycle = AssessmentCycleWindow.Create(2026, 9, 28, 8);

        Assert.Equal(new DateOnly(2026, 9, 28), cycle.OpenDate);
        Assert.Equal(new DateOnly(2026, 10, 8), cycle.CloseDate);
        Assert.True(cycle.Contains(new DateOnly(2026, 9, 28)));
        Assert.True(cycle.Contains(new DateOnly(2026, 10, 8)));
        Assert.False(cycle.Contains(new DateOnly(2026, 9, 27)));
        Assert.False(cycle.Contains(new DateOnly(2026, 10, 9)));
    }

    [Fact]
    public void Create_ClampsDaysToActualMonthLength()
    {
        var cycle = AssessmentCycleWindow.Create(2027, 2, 31, 31);

        Assert.Equal(new DateOnly(2027, 2, 28), cycle.OpenDate);
        Assert.Equal(new DateOnly(2027, 3, 31), cycle.CloseDate);
    }

    [Fact]
    public void PreviousCycle_RemainsOpenDuringFollowingMonthClosePeriod()
    {
        var september = AssessmentCycleWindow.Create(2026, 9, 28, 8);

        Assert.True(september.Contains(new DateOnly(2026, 10, 5)));
    }
}
