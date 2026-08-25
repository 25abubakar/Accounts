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
    int BreakMinutes);

public sealed record AttendanceDayCalculation(
    string State,
    bool IsWorkingDay,
    bool IsFinalized,
    bool IsFullDayAbsent,
    int RequiredMinutes,
    int WorkedMinutes,
    int ShortMinutes,
    int OvertimeMinutes);

public static class AttendanceDailyFinalizationCalculator
{
    public static AttendanceDayCalculation Calculate(AttendanceDayCalculationInput input)
    {
        var required = Math.Max(0, input.RequiredMinutes);
        if (!input.IsWorkingDay || required == 0)
            return Final(AttendanceFinalizationStates.DayOff, false, false, 0, 0);

        if (input.IsExcused)
            return Final(AttendanceFinalizationStates.Excused, true, false, required, 0);

        if (input.CheckInLocal.HasValue && input.CheckOutLocal.HasValue &&
            input.CheckOutLocal.Value >= input.CheckInLocal.Value)
        {
            var worked = Math.Max(
                0,
                (int)Math.Floor((input.CheckOutLocal.Value - input.CheckInLocal.Value).TotalMinutes) -
                Math.Max(0, input.BreakMinutes));
            return new AttendanceDayCalculation(
                AttendanceFinalizationStates.Completed,
                true,
                true,
                false,
                required,
                worked,
                Math.Max(required - worked, 0),
                Math.Max(worked - required, 0));
        }

        if (input.CheckOutLocal.HasValue ||
            (input.CheckInLocal.HasValue && input.LocalNow >= input.FinalizationDeadlineLocal))
        {
            return Pending(required);
        }

        if (input.CheckInLocal.HasValue)
            return Open(AttendanceFinalizationStates.InProgress, required);

        if (input.LocalNow >= input.FinalizationDeadlineLocal)
            return Final(AttendanceFinalizationStates.Absent, true, true, required, 0);

        return Open(AttendanceFinalizationStates.Open, required);
    }

    private static AttendanceDayCalculation Open(string state, int required) =>
        new(state, true, false, false, required, 0, 0, 0);

    private static AttendanceDayCalculation Pending(int required) =>
        new(AttendanceFinalizationStates.PendingReview, true, false, false, required, 0, 0, 0);

    private static AttendanceDayCalculation Final(
        string state,
        bool workingDay,
        bool fullDayAbsent,
        int required,
        int worked) =>
        new(
            state,
            workingDay,
            true,
            fullDayAbsent,
            required,
            worked,
            fullDayAbsent ? required : 0,
            0);
}
