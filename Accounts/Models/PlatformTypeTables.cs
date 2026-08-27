using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

/// <summary>
/// Shared CLR shape for the independent platform-type master tables.
/// EF Core uses table-per-concrete-type mapping, so every derived type is
/// persisted in its own physical SQL table while the controller can apply one
/// audited CRUD implementation consistently.
/// </summary>
public abstract class PlatformTypeTableRow : ITenantEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TenantId { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedOnUtc { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? ModifiedByUserId { get; set; }

    public Tenant? Tenant { get; set; }
}

[Table("ContractTypes", Schema = "PlatformTypes")]
public sealed class ContractType : PlatformTypeTableRow;

[Table("FrequencyTypes", Schema = "PlatformTypes")]
public sealed class FrequencyType : PlatformTypeTableRow;

[Table("RateTypes", Schema = "PlatformTypes")]
public sealed class RateType : PlatformTypeTableRow;

[Table("AllowanceTypes", Schema = "PlatformTypes")]
public sealed class AllowanceType : PlatformTypeTableRow
{
    [Required, MaxLength(20)]
    public string AllowanceCategory { get; set; } = "GENERAL";
}

[Table("TadaTypes", Schema = "PlatformTypes")]
public sealed class TadaType : PlatformTypeTableRow;

[Table("LeaveTypes", Schema = "PlatformTypes")]
public sealed class LeaveType : PlatformTypeTableRow;

[Table("AnnouncementTypes", Schema = "PlatformTypes")]
public sealed class AnnouncementType : PlatformTypeTableRow;

[Table("AssessmentTypes", Schema = "PlatformTypes")]
public sealed class AssessmentType : PlatformTypeTableRow;

[Table("AttendanceTypes", Schema = "PlatformTypes")]
public sealed class AttendanceType : PlatformTypeTableRow;

[Table("BenefitTypes", Schema = "PlatformTypes")]
public sealed class BenefitType : PlatformTypeTableRow;
