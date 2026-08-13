using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendanceRecords")]
public sealed class AttendanceRecord : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public Guid PersonId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public DateTime? EffectiveCheckOutUtc { get; set; }
    public int? AttendanceStatusId { get; set; }
    public int? PlatformActionStatusId { get; set; }
    public int? AttendanceEntryTypeId { get; set; }
    public int? AttendanceWorkModeId { get; set; }
    public DateTime? CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }
    public DateTime? CameraCheckInUtc { get; set; }
    public DateTime? CameraCheckOutUtc { get; set; }
    public DateTime? EffectiveCheckInUtc { get; set; }
    public int? VerificationStatusId { get; set; }
    public int? PlatformVerificationStatusId { get; set; }
    public bool HasVerificationAnomaly { get; set; }
    public int? VerificationDifferenceMinutes { get; set; }
    public long? ApprovalRequestId { get; set; }
    [MaxLength(1000)]
    public string? CameraRemarks { get; set; }
    public DateTime? BreakStartedUtc { get; set; }
    public int TotalBreakMinutes { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public Person Person { get; set; } = null!;
    public ProcessStatusStyle? AttendanceStatus { get; set; }
    public PlatformSettingActionStatus? PlatformActionStatus { get; set; }
    public AttendanceEntryType? AttendanceEntryType { get; set; }
    public AttendanceWorkMode? AttendanceWorkMode { get; set; }
    public ProcessStatusStyle? VerificationStatus { get; set; }
    public PlatformSettingActionStatus? PlatformVerificationStatus { get; set; }
    public WorkflowApprovalRequest? ApprovalRequest { get; set; }
}
