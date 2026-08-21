using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendanceRuleSettings")]
public sealed class AttendanceRuleSetting
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int AttendanceEntryTypeId { get; set; }

    [Required, MaxLength(50)]
    public string Reference { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string RuleName { get; set; } = string.Empty;

    public int WorkingMinutes { get; set; } = 540;
    public int BeforeCheckInMinutes { get; set; } = 5;
    public int AfterCheckOutMinutes { get; set; } = 0;
    public int CheckInAdjustMinutes { get; set; } = 5;
    public int CheckOutAdjustMinutes { get; set; } = 5;
    public int AbsentAfterShiftStartMinutes { get; set; } = 120;
    public int EarlyCheckoutAbsentAfterMinutes { get; set; } = 120;
    public int MissingCheckoutAfterShiftEndMinutes { get; set; } = 120;
    public int CameraVerificationToleranceMinutes { get; set; } = 10;
    public int AccountLockAbsentDays { get; set; }
    public decimal WeekendChargeValue { get; set; }
    public int AdjustAbsentDays { get; set; }

    public int? PlatformLateStatusId { get; set; }
    public int? PlatformExtremeLateStatusId { get; set; }
    public int ExtremeLateAfterMinutes { get; set; } = 120;
    public int? PlatformEarlyDepartureStatusId { get; set; }
    public int? PlatformExtremeEarlyDepartureStatusId { get; set; }
    public int ExtremeEarlyDepartureAfterMinutes { get; set; } = 120;
    public bool IsApproved { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsOvertimeBonusActive { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? ModifiedByUserId { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public AttendanceType AttendanceEntryType { get; set; } = null!;
}
