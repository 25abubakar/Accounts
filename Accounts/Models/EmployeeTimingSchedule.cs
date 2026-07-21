using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("EmployeeTimingSchedules")]
public sealed class EmployeeTimingSchedule : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public Guid StaffId { get; set; }
    public DateOnly ScheduleDate { get; set; }
    public int ScheduleMonth { get; set; }
    public int ScheduleYear { get; set; }
    public int HolidayTypeId { get; set; }

    [MaxLength(5)]
    public string? TimeFrom { get; set; }

    [MaxLength(5)]
    public string? TimeTo { get; set; }

    public bool IsOn { get; set; } = true;
    public int WorkingMinutes { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? ModifiedByUserId { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    public StaffVacancy Staff { get; set; } = null!;
    public AppLookupValue HolidayType { get; set; } = null!;
}
