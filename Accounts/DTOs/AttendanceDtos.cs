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
    public bool IsWorkingDay { get; set; } = true;
    public string HolidayType { get; set; } = "WORKING_DAY";
    public int? AttendanceEntryTypeId { get; set; }
    public string? AttendanceEntryType { get; set; }
    public int? AttendanceWorkModeId { get; set; }
    public string? AttendanceWorkMode { get; set; }
    public bool AttendanceRuleConfigured { get; set; }
    public string? AttendanceTypeCode { get; set; }
    public string? AttendanceTypeName { get; set; }
    public string? AttendanceShiftCode { get; set; }
    public bool IsOpenAttendance { get; set; }
    public bool CanSelfCheckIn { get; set; }
    public bool CanCheckOut { get; set; }
    public bool CanToggleBreak { get; set; }
    public bool IsAttendanceClosed { get; set; }
    public string? AttendanceStatus { get; set; }
    public string? CheckInRestrictionReason { get; set; }
    public bool IsOnBreak => BreakStartedUtc.HasValue;
}

public sealed class AttendanceReportStaffDto
{
    public Guid PersonId { get; set; }
    public Guid? StaffId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public bool IsCurrentUser { get; set; }
    public bool CanEditTiming { get; set; }
    public int AttendanceDays { get; set; }
    public int CompletedDays { get; set; }
    public double AttendancePercentage { get; set; }
    public double ShiftCompletionPercentage { get; set; }
    public double PunctualityPercentage { get; set; }
}

public sealed class TimingChartScheduleRowDto
{
    public long? Id { get; set; }
    public Guid PersonId { get; set; }
    public Guid StaffId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateOnly HolidayDate { get; set; }
    public string Day { get; set; } = string.Empty;
    public int HolidayTypeId { get; set; }
    public string HolidayType { get; set; } = string.Empty;
    public string HolidayTypeName { get; set; } = string.Empty;
    public string? TimeFrom { get; set; }
    public string? TimeTo { get; set; }
    public int WorkingMinutes { get; set; }
    public bool IsOn { get; set; }
    public bool IsOverride { get; set; }
}

public sealed class TimingChartScheduleMonthDto
{
    public Guid PersonId { get; set; }
    public Guid StaffId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public bool CanEdit { get; set; }
    public IReadOnlyList<TimingChartHolidayTypeDto> HolidayTypes { get; set; } = Array.Empty<TimingChartHolidayTypeDto>();
    public IReadOnlyList<TimingChartScheduleRowDto> Rows { get; set; } = Array.Empty<TimingChartScheduleRowDto>();
}

public sealed class TimingChartStaffScheduleDayDto
{
    public long? Id { get; set; }
    public DateOnly Date { get; set; }
    public string Day { get; set; } = string.Empty;
    public int HolidayTypeId { get; set; }
    public string HolidayType { get; set; } = string.Empty;
    public string HolidayTypeName { get; set; } = string.Empty;
    public string? TimeFrom { get; set; }
    public string? TimeTo { get; set; }
    public int WorkingMinutes { get; set; }
    public bool IsOn { get; set; }
    public bool IsOverride { get; set; }
}

public sealed class TimingChartStaffScheduleEmployeeDto
{
    public Guid PersonId { get; set; }
    public Guid StaffId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public bool IsCurrentUser { get; set; }
    public bool CanEditTiming { get; set; }
    public IReadOnlyList<TimingChartStaffScheduleDayDto> Days { get; set; } = Array.Empty<TimingChartStaffScheduleDayDto>();
}

public sealed class TimingChartStaffScheduleMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public int DaysInMonth { get; set; }
    public IReadOnlyList<TimingChartHolidayTypeDto> HolidayTypes { get; set; } = Array.Empty<TimingChartHolidayTypeDto>();
    public IReadOnlyList<TimingChartStaffScheduleEmployeeDto> Employees { get; set; } = Array.Empty<TimingChartStaffScheduleEmployeeDto>();
}

public sealed class TimingChartHolidayTypeDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool DefaultIsOn { get; set; }
}

public sealed class SaveTimingChartScheduleDto
{
    public int HolidayTypeId { get; set; }
    public string? TimeFrom { get; set; }
    public string? TimeTo { get; set; }
    public bool IsOn { get; set; }
}

public sealed class SaveTimingChartScheduleRangeDto
{
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public int? DayOfWeek { get; set; }
    public int HolidayTypeId { get; set; }
    public string? TimeFrom { get; set; }
    public string? TimeTo { get; set; }
    public bool IsOn { get; set; }
}

public sealed class TimingChartScheduleRangeResultDto
{
    public Guid PersonId { get; set; }
    public Guid StaffId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public int SavedDays { get; set; }
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
    public string? CameraCheckInTime { get; set; }
    public string? CameraCheckOutTime { get; set; }
    public string? CameraAttendanceStatus { get; set; }
    public string? CameraAttendanceStatusCode { get; set; }
    public string? CameraAttendanceStatusColorCode { get; set; }
    public string? CameraAttendanceStatusFontColor { get; set; }
    public int CameraWorkingMinutes { get; set; }
    public int CameraLateMinutes { get; set; }
    public int CameraEarlyDepartureMinutes { get; set; }
    public int? CameraSystemDifferenceMinutes { get; set; }
    public string? EffectiveCheckInTime { get; set; }
    public string? EffectiveCheckOutTime { get; set; }
    public int? VerificationStatusId { get; set; }
    public string? VerificationStatus { get; set; }
    public string? VerificationStatusCode { get; set; }
    public string? VerificationColorCode { get; set; }
    public string? VerificationFontColor { get; set; }
    public bool HasVerificationAnomaly { get; set; }
    public int? VerificationDifferenceMinutes { get; set; }
    public int VerifiedWorkingMinutes { get; set; }
    public long? ApprovalRequestId { get; set; }
    public string? ApprovalStatusCode { get; set; }
    public int WorkingMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyDepartureMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int? AttendanceStatusId { get; set; }
    public string AttendanceStatus { get; set; } = string.Empty;
    public string? StatusCode { get; set; }
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public string? StatusFontSize { get; set; }
    public bool IsCurrentUser { get; set; }
    public int BreakMinutes { get; set; }
    public int RequiredMinutes { get; set; }
    public int CumulativeRequiredMinutes { get; set; }
    public int CumulativeWorkingMinutes { get; set; }
    public int CrDbMinutes { get; set; }
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

public sealed class SaveCameraAttendanceDto
{
    public Guid PersonId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }
    public string? Remarks { get; set; }
}

public sealed class ReviewCameraAttendanceDto
{
    public string DecisionCode { get; set; } = string.Empty;
    public string? ManualCheckInTime { get; set; }
    public string? ManualCheckOutTime { get; set; }
    public string? Comments { get; set; }
}

public sealed class LoginAttendanceSessionDto
{
    public long Id { get; set; }
    public Guid? StaffId { get; set; }
    public Guid? PersonId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string LoginTime { get; set; } = string.Empty;
    public string? LogoutTime { get; set; }
    public int WorkingMinutes { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? Remarks { get; set; }
}

public sealed class LoginAttendanceReportDto
{
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public IReadOnlyList<LoginAttendanceSessionDto> Rows { get; set; } = Array.Empty<LoginAttendanceSessionDto>();
}

public sealed class AttendanceDeductionRowDto
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
    public decimal FinalSalary { get; set; }
}

public sealed class AttendanceDeductionReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public IReadOnlyList<AttendanceDeductionRowDto> Rows { get; set; } = Array.Empty<AttendanceDeductionRowDto>();
}

public sealed class ApproveOvertimeRequestDto
{
    public Guid PersonId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public bool IsApproved { get; set; }
}

public sealed class SaveAttendanceDeductionRequestDto
{
    public string? RegNo { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Office { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? Classification { get; set; }
    public string? Routing { get; set; }
    public string? Authority { get; set; }
    public string? Subject { get; set; }
    public string? DocumentName { get; set; }
    public int DeductionMonth { get; set; }
    public int DeductionYear { get; set; }
    public string? ActionRouting { get; set; }
    public string? ActionName { get; set; }
    public string? Comments { get; set; }
}

public sealed class MonthlyAttendanceChartCellDto
{
    public long? AttendanceId { get; set; }
    public DateOnly Date { get; set; }
    public int? AttendanceStatusId { get; set; }
    public string? StatusCode { get; set; }
    public string AttendanceStatus { get; set; } = string.Empty;
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public string? StatusFontSize { get; set; }
    public string AttendanceType { get; set; } = string.Empty;
    public string? WorkMode { get; set; }
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }
    public int WorkingMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyDepartureMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public bool Present { get; set; }
    public bool Absent { get; set; }
    public bool OnLeave { get; set; }
    public bool Remote { get; set; }
    public bool MissingCheckIn { get; set; }
    public bool MissingCheckOut { get; set; }
}

public sealed class MonthlyAttendanceChartEmployeeDto
{
    public Guid PersonId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string? ReportingManager { get; set; }
    public bool IsCurrentUser { get; set; }
    public IReadOnlyList<MonthlyAttendanceChartCellDto> Days { get; set; } = Array.Empty<MonthlyAttendanceChartCellDto>();
}

public sealed class MonthlyAttendanceChartLegendItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string ColorCode { get; set; } = string.Empty;
    public string FontColor { get; set; } = "#FFFFFF";
    public string FontSize { get; set; } = "12px";
    public int DisplayOrder { get; set; }
}

public sealed class MonthlyAttendanceChartDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public int DaysInMonth { get; set; }
    public DailyAttendanceSummaryDto Summary { get; set; } = new();
    public IReadOnlyList<MonthlyAttendanceChartLegendItemDto> Legend { get; set; } = Array.Empty<MonthlyAttendanceChartLegendItemDto>();
    public IReadOnlyList<MonthlyAttendanceChartEmployeeDto> Employees { get; set; } = Array.Empty<MonthlyAttendanceChartEmployeeDto>();
}

public sealed class AttendanceMapRuleDto
{
    public int Id { get; set; }
    public Guid StaffId { get; set; }
    public int AttendanceEntryTypeId { get; set; }
    public string AttendanceTypeCode { get; set; } = string.Empty;
    public string AttendanceTypeName { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string TimeFrom { get; set; } = string.Empty;
    public string TimeTo { get; set; } = string.Empty;
    public bool IsOpenAttendance { get; set; }
}

public sealed class SaveAttendanceMapRuleDto
{
    public Guid StaffId { get; set; }
    public int AttendanceEntryTypeId { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    public string TimeFrom { get; set; } = string.Empty;
    public string TimeTo { get; set; } = string.Empty;
    public bool IsOpenAttendance { get; set; }
}

public sealed class AttendanceHolidayColorMapDto
{
    public int Id { get; set; }
    public string HolidayTypeCode { get; set; } = string.Empty;
    public string HolidayTypeName { get; set; } = string.Empty;
    public string ColorCode { get; set; } = string.Empty;
}

public sealed class SaveAttendanceHolidayColorMapDto
{
    public string HolidayTypeCode { get; set; } = string.Empty;
    public string ColorCode { get; set; } = string.Empty;
}

public sealed class AttendanceRuleSettingDto
{
    public int Id { get; set; }
    public int AttendanceEntryTypeId { get; set; }
    public string AttendanceTypeCode { get; set; } = string.Empty;
    public string AttendanceTypeName { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public int WorkingMinutes { get; set; }
    public int BeforeCheckInMinutes { get; set; }
    public int AfterCheckOutMinutes { get; set; }
    public int CheckInAdjustMinutes { get; set; }
    public int CheckOutAdjustMinutes { get; set; }
    public int AbsentAfterShiftStartMinutes { get; set; }
    public int EarlyCheckoutAbsentAfterMinutes { get; set; }
    public int MissingCheckoutAfterShiftEndMinutes { get; set; }
    public int CameraVerificationToleranceMinutes { get; set; }
    public int AccountLockAbsentDays { get; set; }
    public decimal WeekendChargeValue { get; set; }
    public int AdjustAbsentDays { get; set; }
    public int ExtremeLateAfterMinutes { get; set; }
    public int? PlatformLateStatusId { get; set; }
    public int? PlatformExtremeLateStatusId { get; set; }
    public int ExtremeEarlyDepartureAfterMinutes { get; set; }
    public int? PlatformEarlyDepartureStatusId { get; set; }
    public int? PlatformExtremeEarlyDepartureStatusId { get; set; }
    public bool IsApproved { get; set; }
    public bool IsActive { get; set; }
    public string? Remarks { get; set; }
}

public sealed class SaveAttendanceRuleSettingDto
{
    public int AttendanceEntryTypeId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public int WorkingMinutes { get; set; }
    public int BeforeCheckInMinutes { get; set; }
    public int AfterCheckOutMinutes { get; set; }
    public int CheckInAdjustMinutes { get; set; }
    public int CheckOutAdjustMinutes { get; set; }
    public int AbsentAfterShiftStartMinutes { get; set; }
    public int EarlyCheckoutAbsentAfterMinutes { get; set; }
    public int MissingCheckoutAfterShiftEndMinutes { get; set; }
    public int CameraVerificationToleranceMinutes { get; set; } = 10;
    public int AccountLockAbsentDays { get; set; }
    public decimal WeekendChargeValue { get; set; }
    public int AdjustAbsentDays { get; set; }
    public int ExtremeLateAfterMinutes { get; set; }
    public int? PlatformLateStatusId { get; set; }
    public int? PlatformExtremeLateStatusId { get; set; }
    public int ExtremeEarlyDepartureAfterMinutes { get; set; }
    public int? PlatformEarlyDepartureStatusId { get; set; }
    public int? PlatformExtremeEarlyDepartureStatusId { get; set; }
    public bool IsApproved { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Remarks { get; set; }
}
