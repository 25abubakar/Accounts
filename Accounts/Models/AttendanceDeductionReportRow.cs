namespace Accounts.Models;

public sealed class AttendanceDeductionReportRow
{
    public long Id { get; set; }
    public Guid PersonId { get; set; }
    public Guid StaffId { get; set; }
    public string StaffNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal PerDay { get; set; }
    public decimal PerHour { get; set; }
    public int MonthWorkingDays { get; set; }
    public int MonthWorkingMinutes { get; set; }
    public int MonthAttendanceMinutes { get; set; }
    /// <summary>Full calendar chargeable working days in the month (rate denominator).</summary>
    public int FullMonthWorkingDays { get; set; }
    /// <summary>Full calendar required working minutes in the month (rate denominator).</summary>
    public int FullMonthWorkingMinutes { get; set; }
    /// <summary>Attendance rule Adjust Absent Days / Month (hour-bank relaxation).</summary>
    public int AdjustAbsentDays { get; set; }
    /// <summary>
    /// One announced working day in minutes from Map Attendance TimeFrom/TimeTo
    /// (fallback rule WorkingMinutes). Used for Hrs Adjust = AdjustAbsentDays × this day.
    /// </summary>
    public int OneDayWorkingMinutes { get; set; }
    /// <summary>Raw finalized short minutes (before AdjustAbsentDays hour-bank).</summary>
    public int NetShortMinutes { get; set; }
    public int LatePenaltyMinutes { get; set; }
    /// <summary>Raw chargeable minutes per day max(Short, LatePenalty), before hour-bank.</summary>
    public int DeductibleMinutes { get; set; }
    public int NetOvertimeMinutes { get; set; }
    public decimal NetDeduction { get; set; }
    public decimal OvertimeBonusAmount { get; set; }
    public bool IsOvertimeApproved { get; set; }
    public bool IsOvertimeBonusActive { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public bool IsAdjustmentApproved { get; set; }
    public string? AdjustmentRemarks { get; set; }
    public decimal FinalSalary { get; set; }
    public int PendingReviewDays { get; set; }
    public int OpenDays { get; set; }
    public DateOnly? LastFinalizedDate { get; set; }
}
