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
    public int NetShortMinutes { get; set; }
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
