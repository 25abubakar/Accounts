namespace Accounts.Models;

/// <summary>Read model returned by dbo.usp_Attendance_DailyReport.</summary>
public sealed class AttendanceDailyReportRow
{
    public long? Id { get; set; }
    public Guid PersonId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public DateOnly AttendanceDate { get; set; }
    public int? AttendanceStatusId { get; set; }
    public string? StatusName { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public string? StatusFontSize { get; set; }
    public int? AttendanceEntryTypeId { get; set; }
    public string? AttendanceEntryType { get; set; }
    public int? AttendanceWorkModeId { get; set; }
    public string? AttendanceWorkMode { get; set; }
    public DateTime? CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }
    public int? TotalBreakMinutes { get; set; }
    public string ShiftStartTime { get; set; } = "09:00";
    public string ShiftEndTime { get; set; } = "18:00";
    public string TimeZoneId { get; set; } = "Asia/Karachi";
    public Guid? ReportsToPersonId { get; set; }
}
