using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendanceHolidayColorMaps")]
public sealed class AttendanceHolidayColorMap : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int TenantId { get; set; }

    [Required, MaxLength(100)]
    public string HolidayTypeCode { get; set; } = string.Empty;

    [Required, MaxLength(7)]
    public string ColorCode { get; set; } = "#0EA5E9";

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? ModifiedByUserId { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
