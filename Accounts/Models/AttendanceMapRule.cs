using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendanceMapRules")]
public sealed class AttendanceMapRule : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Guid StaffId { get; set; }
    public int AttendanceEntryTypeId { get; set; }

    [Required, MaxLength(100)]
    public string ShiftCode { get; set; } = string.Empty;

    [Required, MaxLength(5)]
    public string TimeFrom { get; set; } = string.Empty;

    [Required, MaxLength(5)]
    public string TimeTo { get; set; } = string.Empty;

    public bool IsOpenAttendance { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? ModifiedByUserId { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    public StaffVacancy Staff { get; set; } = null!;
    public AttendanceType AttendanceEntryType { get; set; } = null!;
}
