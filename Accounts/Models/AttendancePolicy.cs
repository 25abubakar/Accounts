using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendancePolicies")]
public sealed class AttendancePolicy
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    [Required,MaxLength(100)] public string PolicyName { get; set; } = string.Empty;
    [Required,MaxLength(100)] public string TimeZoneId { get; set; } = "Pakistan Standard Time";
    public int EarliestCheckInMinutesBefore { get; set; } = 5;
    public int OnTimeGraceMinutesAfter { get; set; } = 5;
    public int AbsentAfterShiftStartMinutes { get; set; } = 120;
    public int MissingCheckoutAfterShiftEndMinutes { get; set; } = 120;
    public int FullDayToleranceMinutes { get; set; }
    public int PresentStatusId { get; set; }
    public int LateStatusId { get; set; }
    public int CompletedLateStatusId { get; set; }
    public int ShortLeaveStatusId { get; set; }
    public int EarlyDepartureStatusId { get; set; }
    public int AbsentStatusId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }
    public Tenant? Tenant { get; set; }
    public ProcessStatusStyle PresentStatus { get; set; } = null!;
    public ProcessStatusStyle LateStatus { get; set; } = null!;
    public ProcessStatusStyle CompletedLateStatus { get; set; } = null!;
    public ProcessStatusStyle ShortLeaveStatus { get; set; } = null!;
    public ProcessStatusStyle EarlyDepartureStatus { get; set; } = null!;
    public ProcessStatusStyle AbsentStatus { get; set; } = null!;
}
