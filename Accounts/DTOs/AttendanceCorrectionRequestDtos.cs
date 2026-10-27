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
    public bool CanSubmit { get; set; }
    public string? Reason { get; set; }
    public string? ReportingManagerName { get; set; }
    public IReadOnlyList<AttendanceCorrectionStatusDto> Statuses { get; set; } = [];
}

public sealed class AttendanceCorrectionStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
