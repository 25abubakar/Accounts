namespace Accounts.DTOs;

public sealed class MyAttendanceTodayDto
{
    public long? Id { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string ShiftStartTime { get; set; } = string.Empty;
    public string ShiftEndTime { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public DateTime? CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }
    public DateTime? BreakStartedUtc { get; set; }
    public int TotalBreakMinutes { get; set; }
    public int WorkedMinutes { get; set; }
    public int RequiredMinutes { get; set; }
    public int ShortMinutes { get; set; }
    public int RemainingMinutes { get; set; }
    public double ProgressPercent { get; set; }
    public int? AttendanceEntryTypeId { get; set; }
    public string? AttendanceEntryType { get; set; }
    public int? AttendanceWorkModeId { get; set; }
    public string? AttendanceWorkMode { get; set; }
    public bool IsOnBreak => BreakStartedUtc.HasValue;
}

public sealed class AttendanceReportStaffDto
{
    public Guid PersonId { get; set; }
    public Guid? StaffId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public int AttendanceDays { get; set; }
    public int CompletedDays { get; set; }
    public double AttendancePercentage { get; set; }
    public double ShiftCompletionPercentage { get; set; }
    public double PunctualityPercentage { get; set; }
}

public sealed class MonthlyAttendanceRowDto
{
    public long Id { get; set; }
    public Guid PersonId { get; set; }
    public Guid? StaffId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public DateOnly AttendanceDate { get; set; }
    public string? CheckIn { get; set; }
    public string? CheckOut { get; set; }
    public int WorkingMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int RequiredMinutes { get; set; }
    public int ShortMinutes { get; set; }
    public int? AttendanceStatusId { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusName { get; set; }
    public string? StatusColorCode { get; set; }
}

public sealed class MonthlyAttendanceReportDto
{
    public AttendanceReportStaffDto Employee { get; set; } = new();
    public int Year { get; set; }
    public int Month { get; set; }
    public IReadOnlyList<MonthlyAttendanceRowDto> Rows { get; set; } = Array.Empty<MonthlyAttendanceRowDto>();
}

public sealed class DailyAttendanceRowDto
{
    public long? Id { get; set; }
    public Guid PersonId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string? ReportingManager { get; set; }
    public DateOnly Date { get; set; }
    public string AttendanceType { get; set; } = string.Empty;
    public int? AttendanceEntryTypeId { get; set; }
    public int? AttendanceWorkModeId { get; set; }
    public string? WorkMode { get; set; }
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }
    public int WorkingMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyDepartureMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int? AttendanceStatusId { get; set; }
    public string AttendanceStatus { get; set; } = string.Empty;
    public string? StatusCode { get; set; }
    public string? StatusColorCode { get; set; }
    public bool Present { get; set; }
    public bool Absent { get; set; }
    public bool OnLeave { get; set; }
    public bool Remote { get; set; }
    public bool MissingCheckIn { get; set; }
    public bool MissingCheckOut { get; set; }
    public string? Comments { get; set; }
}

public sealed class DailyAttendanceSummaryDto
{
    public int TotalEmployees { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int OnLeave { get; set; }
    public int Remote { get; set; }
    public int MissingCheckIn { get; set; }
    public int MissingCheckOut { get; set; }
    public int TotalWorkingMinutes { get; set; }
    public int TotalOvertimeMinutes { get; set; }
}

public sealed class DailyAttendanceReportDto
{
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public DailyAttendanceSummaryDto Summary { get; set; } = new();
    public IReadOnlyList<DailyAttendanceRowDto> Rows { get; set; } = Array.Empty<DailyAttendanceRowDto>();
}
