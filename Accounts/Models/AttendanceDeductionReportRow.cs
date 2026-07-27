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
    public int Day { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public int TotalWorkingMinutes { get; set; }
    public int TotalAttendanceMinutes { get; set; }
    public int HoursDiffMinutes { get; set; }
    public int DeductionMinutes { get; set; }
    public decimal DeductionDays { get; set; }
    public int HoursAdjustMinutes { get; set; }
    public int NetStandardMinutes { get; set; }
    public decimal GrossDeduction { get; set; }
    public decimal AdjustAmount { get; set; }
    public decimal NetDeduction { get; set; }
    public decimal PerHour { get; set; }
    public decimal PerDay { get; set; }
    public bool Approved { get; set; }
    public bool Pending { get; set; }
}
