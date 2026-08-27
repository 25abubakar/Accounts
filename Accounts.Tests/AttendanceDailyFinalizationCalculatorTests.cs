using Accounts.Models;
using Accounts.Services.Services;

namespace Accounts.Tests;

public sealed class AttendanceDailyFinalizationCalculatorTests
{
    private static readonly DateTime ShiftDeadline = new(2026, 8, 24, 20, 0, 0);

    [Fact]
    public void CheckedInWithoutCheckout_RemainsOpenAndCreatesNoDeduction()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 12, 0, 0),
            checkIn: new DateTime(2026, 8, 24, 8, 56, 0));

        Assert.Equal(AttendanceFinalizationStates.InProgress, result.State);
        Assert.False(result.IsFinalized);
        Assert.Equal(0, result.ShortMinutes);
    }

    [Fact]
    public void MissingCheckoutAfterDeadline_RequiresReviewAndCreatesNoDeduction()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 21, 0, 0),
            checkIn: new DateTime(2026, 8, 24, 8, 56, 0));

        Assert.Equal(AttendanceFinalizationStates.PendingReview, result.State);
        Assert.False(result.IsFinalized);
        Assert.Equal(0, result.ShortMinutes);
    }

    [Fact]
    public void NoCheckInAfterDeadline_FinalizesFullDayAbsence()
    {
        var result = Calculate(now: new DateTime(2026, 8, 24, 21, 0, 0));

        Assert.Equal(AttendanceFinalizationStates.Absent, result.State);
        Assert.True(result.IsFinalized);
        Assert.True(result.IsFullDayAbsent);
        Assert.Equal(540, result.ShortMinutes);
    }

    [Fact]
    public void CompletedAttendance_CalculatesOnlyActualShortMinutes()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 18, 0, 0),
            checkIn: new DateTime(2026, 8, 24, 9, 0, 0),
            checkOut: new DateTime(2026, 8, 24, 17, 20, 0));

        Assert.True(result.IsFinalized);
        Assert.Equal(500, result.WorkedMinutes);
        Assert.Equal(40, result.ShortMinutes);
        Assert.Equal(0, result.OvertimeMinutes);
    }

    [Fact]
    public void DayOff_NeverCreatesRequiredOrShortMinutes()
    {
        var result = AttendanceDailyFinalizationCalculator.Calculate(
            new AttendanceDayCalculationInput(
                false,
                false,
                540,
                new DateTime(2026, 8, 23, 23, 0, 0),
                new DateTime(2026, 8, 23, 20, 0, 0),
                null,
                null,
                0));

        Assert.Equal(AttendanceFinalizationStates.DayOff, result.State);
        Assert.True(result.IsFinalized);
        Assert.Equal(0, result.RequiredMinutes);
        Assert.Equal(0, result.ShortMinutes);
    }

    [Fact]
    public void ExcusedWorkingDay_DoesNotConsumeShortMinutes()
    {
        var result = AttendanceDailyFinalizationCalculator.Calculate(
            new AttendanceDayCalculationInput(
                true,
                true,
                540,
                new DateTime(2026, 8, 24, 21, 0, 0),
                ShiftDeadline,
                null,
                null,
                0));

        Assert.Equal(AttendanceFinalizationStates.Excused, result.State);
        Assert.True(result.IsFinalized);
        Assert.False(result.IsFullDayAbsent);
        Assert.Equal(0, result.ShortMinutes);
    }

    [Fact]
    public void LateEmployee_CompletesRequiredHours_AppliesConfiguredPercentageOfLateBand()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 18, 30, 0),
            checkIn: new DateTime(2026, 8, 24, 9, 30, 0),
            checkOut: new DateTime(2026, 8, 24, 18, 30, 0),
            lateRuleActive: true,
            completedLatePercentage: 50m);

        Assert.True(result.IsFinalized);
        Assert.Equal(540, result.WorkedMinutes);
        Assert.Equal(0, result.ShortMinutes);
        Assert.Equal(25, result.LateMinutes);
        Assert.Equal(60, result.LateBandMinutes);
        Assert.Equal(30, result.LatePenaltyMinutes);
    }

    [Fact]
    public void LateEmployee_DoesNotCompleteRequiredHours_AppliesFullBand()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 18, 0, 0),
            checkIn: new DateTime(2026, 8, 24, 9, 30, 0),
            checkOut: new DateTime(2026, 8, 24, 18, 0, 0),
            lateRuleActive: true,
            completedLatePercentage: 20m);

        Assert.Equal(510, result.WorkedMinutes);
        Assert.Equal(30, result.ShortMinutes);
        Assert.Equal(60, result.LateBandMinutes);
        Assert.Equal(60, result.LatePenaltyMinutes);
    }

    [Fact]
    public void LateEmployee_WhenCompletedRuleIsInactive_UsesFullLateBand()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 18, 30, 0),
            checkIn: new DateTime(2026, 8, 24, 9, 30, 0),
            checkOut: new DateTime(2026, 8, 24, 18, 30, 0));

        Assert.Equal(60, result.LateBandMinutes);
        Assert.Equal(60, result.LatePenaltyMinutes);
    }

    [Fact]
    public void ArrivalInsideGrace_CreatesNoLateBandOrPenalty()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 18, 5, 0),
            checkIn: new DateTime(2026, 8, 24, 9, 5, 0),
            checkOut: new DateTime(2026, 8, 24, 18, 5, 0),
            lateRuleActive: true);

        Assert.Equal(0, result.LateMinutes);
        Assert.Equal(0, result.LateBandMinutes);
        Assert.Equal(0, result.LatePenaltyMinutes);
    }

    [Fact]
    public void ExtremeLateEmployee_CompletesRequiredHours_UsesTwoHourBand()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 19, 5, 0),
            checkIn: new DateTime(2026, 8, 24, 10, 5, 0),
            checkOut: new DateTime(2026, 8, 24, 19, 5, 0),
            lateRuleActive: true,
            completedLatePercentage: 20m);

        Assert.Equal(60, result.LateMinutes);
        Assert.Equal(120, result.LateBandMinutes);
        Assert.Equal(24, result.LatePenaltyMinutes);
    }

    [Fact]
    public void ExplicitAbsent_FinalizesImmediatelyAndCreatesFullDayShortage()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 12, 0, 0),
            explicitAbsent: true);

        Assert.Equal(AttendanceFinalizationStates.Absent, result.State);
        Assert.True(result.IsFinalized);
        Assert.True(result.IsFullDayAbsent);
        Assert.Equal(540, result.ShortMinutes);
    }

    [Fact]
    public void CorrectedAbsent_RecalculatesFromUpdatedAttendance()
    {
        var result = Calculate(
            now: new DateTime(2026, 8, 24, 18, 0, 0),
            checkIn: new DateTime(2026, 8, 24, 9, 0, 0),
            checkOut: new DateTime(2026, 8, 24, 18, 0, 0),
            explicitAbsent: false);

        Assert.Equal(AttendanceFinalizationStates.Completed, result.State);
        Assert.True(result.IsFinalized);
        Assert.False(result.IsFullDayAbsent);
        Assert.Equal(0, result.ShortMinutes);
    }

    private static AttendanceDayCalculation Calculate(
        DateTime now,
        DateTime? checkIn = null,
        DateTime? checkOut = null,
        bool lateRuleActive = false,
        decimal completedLatePercentage = 50m,
        bool explicitAbsent = false) =>
        AttendanceDailyFinalizationCalculator.Calculate(
            new AttendanceDayCalculationInput(
                true,
                false,
                540,
                now,
                ShiftDeadline,
                checkIn,
                checkOut,
                0,
                new DateTime(2026, 8, 24, 9, 0, 0),
                5,
                60,
                lateRuleActive,
                completedLatePercentage,
                explicitAbsent));
}
