using System.ComponentModel.DataAnnotations;

namespace Accounts.DTOs;

public sealed class SubmitAttendanceCorrectionRequestDto
{
    public DateOnly AttendanceDate { get; set; }
    [Range(1, int.MaxValue)] public int RequestedStatusId { get; set; }
    [Required, StringLength(2000)] public string Description { get; set; } = string.Empty;
}

public sealed class AttendanceCorrectionRequestContextDto
{
    public Guid PersonId { get; set; }
    public long? AttendanceId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public TimeOnly? CurrentCheckIn { get; set; }
    public TimeOnly? CurrentCheckOut { get; set; }
    public bool CanSubmit { get; set; }
    public string? Reason { get; set; }
    public string? ReportingManagerName { get; set; }
    public IReadOnlyList<AttendanceCorrectionStatusDto> Statuses { get; set; } = [];
    public IReadOnlyList<AttendanceCorrectionStatusDto> LeaveTypes { get; set; } = [];
}

public sealed class AttendanceCorrectionStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class SubmitAttendanceRequestDto
{
    public DateOnly AttendanceDate { get; set; }
    [Required] public string Type { get; set; } = "Status";
    public int? RequestedStatusId { get; set; }
    public TimeOnly? RequestedTime { get; set; }
    public TimeOnly? RequestedCheckInTime { get; set; }
    public TimeOnly? RequestedCheckOutTime { get; set; }
    public TimeOnly? TimeFrom { get; set; }
    public TimeOnly? TimeTo { get; set; }
    public string? MealStatus { get; set; }
    public DateOnly? DateTo { get; set; }
    public int? LeaveTypeId { get; set; }
    [Required, StringLength(2000)] public string Remarks { get; set; } = string.Empty;
}

public sealed class AttendanceRequestDetailsDto
{
    public string Type { get; set; } = "Status";
    public DateOnly Date { get; set; }
    public DateOnly? DateTo { get; set; }
    public int? LeaveTypeId { get; set; }
    public string? LeaveType { get; set; }
    public Guid PersonId { get; set; }
    public long? AttendanceId { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public TimeOnly? CurrentCheckIn { get; set; }
    public TimeOnly? CurrentCheckOut { get; set; }
    public int? RequestedStatusId { get; set; }
    public string? RequestedStatus { get; set; }
    public TimeOnly? RequestedTime { get; set; }
    public TimeOnly? ApprovedTime { get; set; }
    public TimeOnly? RequestedCheckInTime { get; set; }
    public TimeOnly? RequestedCheckOutTime { get; set; }
    public TimeOnly? ApprovedCheckInTime { get; set; }
    public TimeOnly? ApprovedCheckOutTime { get; set; }
    public TimeOnly? TimeFrom { get; set; }
    public TimeOnly? TimeTo { get; set; }
    public int? Minutes { get; set; }
    public string Remarks { get; set; } = string.Empty;
}

public sealed class AttendanceRequestHistoryDto
{
    public long Id { get; set; }
    public string? RequestNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? EmployeeNumber { get; set; }
    public AttendanceRequestDetailsDto Details { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
}
