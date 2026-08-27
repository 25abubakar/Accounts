using Accounts.Models;

namespace Accounts.Services.Services;

public sealed record AttendanceDayCalculationInput(
    bool IsWorkingDay,
    bool IsExcused,
    int RequiredMinutes,
    DateTime LocalNow,
    DateTime FinalizationDeadlineLocal,
    DateTime? CheckInLocal,
    DateTime? CheckOutLocal,
    int BreakMinutes,
    DateTime? ShiftStartLocal = null,
    int CheckInGraceMinutes = 0,
    int ExtremeLateAfterMinutes = 60,
    bool IsCompletedLateDeductionActive = false,
    decimal CompletedLateDeductionPercentage = 50m,
    bool IsExplicitAbsent = false);

public sealed record AttendanceDayCalculation(
    string State,
    bool IsWorkingDay,
    bool IsFinalized,
    bool IsFullDayAbsent,
    int RequiredMinutes,
    int WorkedMinutes,
    int ShortMinutes,
    int OvertimeMinutes,
    int LateMinutes,
    int LateBandMinutes,
    int LatePenaltyMinutes);

public static class AttendanceDailyFinalizationCalculator
{
    public static AttendanceDayCalculation Calculate(AttendanceDayCalculationInput input)
    {
        var required = Math.Max(0, input.RequiredMinutes);
        if (!input.IsWorkingDay || required == 0)
            return Final(AttendanceFinalizationStates.DayOff, false, false, 0, 0, 0, 0, 0);

        if (input.IsExcused)
            return Final(AttendanceFinalizationStates.Excused, true, false, required, 0, 0, 0, 0);

        if (input.IsExplicitAbsent)
            return Final(AttendanceFinalizationStates.Absent, true, true, required, 0, 0, 0, 0);

        if (input.CheckInLocal.HasValue && input.CheckOutLocal.HasValue &&
            input.CheckOutLocal.Value >= input.CheckInLocal.Value)
        {
            var worked = Math.Max(
                0,
                (int)Math.Floor((input.CheckOutLocal.Value - input.CheckInLocal.Value).TotalMinutes) -
                Math.Max(0, input.BreakMinutes));
            var late = CalculateLateMinutes(input);
            var lateBand = CalculateLateBandMinutes(late, input.ExtremeLateAfterMinutes);
            var latePenalty = CalculateLatePenaltyMinutes(
                lateBand,
                worked >= required,
                input.IsCompletedLateDeductionActive,
                input.CompletedLateDeductionPercentage);
            return new AttendanceDayCalculation(
                AttendanceFinalizationStates.Completed,
                true,
                true,
                false,
                required,
                worked,
                Math.Max(required - worked, 0),
                Math.Max(worked - required, 0),
                late,
                lateBand,
                latePenalty);
        }

        if (input.CheckOutLocal.HasValue ||
            (input.CheckInLocal.HasValue && input.LocalNow >= input.FinalizationDeadlineLocal))
        {
            return Pending(required);
        }

        if (input.CheckInLocal.HasValue)
            return Open(AttendanceFinalizationStates.InProgress, required);

        if (input.LocalNow >= input.FinalizationDeadlineLocal)
            return Final(AttendanceFinalizationStates.Absent, true, true, required, 0, 0, 0, 0);

        return Open(AttendanceFinalizationStates.Open, required);
    }

    private static AttendanceDayCalculation Open(string state, int required) =>
        new(state, true, false, false, required, 0, 0, 0, 0, 0, 0);

    private static AttendanceDayCalculation Pending(int required) =>
        new(AttendanceFinalizationStates.PendingReview, true, false, false, required, 0, 0, 0, 0, 0, 0);

    private static AttendanceDayCalculation Final(
        string state,
        bool workingDay,
        bool fullDayAbsent,
        int required,
        int worked,
        int lateMinutes,
        int lateBandMinutes,
        int latePenaltyMinutes) =>
        new(
            state,
            workingDay,
            true,
            fullDayAbsent,
            required,
            worked,
            fullDayAbsent ? required : 0,
            0,
            lateMinutes,
            lateBandMinutes,
            latePenaltyMinutes);

    private static int CalculateLateMinutes(AttendanceDayCalculationInput input)
    {
        if (!input.CheckInLocal.HasValue || !input.ShiftStartLocal.HasValue)
            return 0;

        var minutesAfterShiftStart = Math.Max(
            0,
            (int)Math.Floor((input.CheckInLocal.Value - input.ShiftStartLocal.Value).TotalMinutes));
        var grace = Math.Max(0, input.CheckInGraceMinutes);
        return minutesAfterShiftStart <= grace ? 0 : minutesAfterShiftStart - grace;
    }

    private static int CalculateLateBandMinutes(int lateMinutes, int extremeLateAfterMinutes)
    {
        if (lateMinutes <= 0) return 0;
        return lateMinutes >= Math.Max(1, extremeLateAfterMinutes) ? 120 : 60;
    }

    private static int CalculateLatePenaltyMinutes(
        int lateBandMinutes,
        bool completedRequiredMinutes,
        bool ruleActive,
        decimal completedPercentage)
    {
        if (lateBandMinutes <= 0) return 0;
        if (!ruleActive) return lateBandMinutes;
        if (!completedRequiredMinutes) return lateBandMinutes;

        var percentage = Math.Clamp(completedPercentage, 0m, 100m);
        return (int)decimal.Round(
            lateBandMinutes * percentage / 100m,
            0,
            MidpointRounding.AwayFromZero);
    }
}
