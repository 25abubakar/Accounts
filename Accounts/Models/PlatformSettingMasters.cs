using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

public abstract class PlatformSettingNamedRow : ITenantEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedOnUtc { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    [MaxLength(450)] public string? ModifiedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
}

[Table("Actions", Schema = "PlatformSettings")]
public sealed class PlatformSettingAction : PlatformSettingNamedRow;

[Table("Statuses", Schema = "PlatformSettings")]
public sealed class PlatformSettingStatus : PlatformSettingNamedRow;

[Table("Colors", Schema = "PlatformSettings")]
public sealed class PlatformSettingColor : ITenantEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(9)] public string ColorCode { get; set; } = string.Empty;
    [MaxLength(9)] public string? FontColor { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedOnUtc { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    [MaxLength(450)] public string? ModifiedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
}

[Table("ActionStatuses", Schema = "PlatformSettings")]
public sealed class PlatformSettingActionStatus : ITenantEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ActionId { get; set; }
    public int StatusId { get; set; }
    public int? ColorId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedOnUtc { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    [MaxLength(450)] public string? ModifiedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
    public PlatformSettingAction Action { get; set; } = null!;
    public PlatformSettingStatus Status { get; set; } = null!;
    public PlatformSettingColor? Color { get; set; }
}

[Table("StatusCrDbValues", Schema = "PlatformSettings")]
public sealed class PlatformSettingStatusCrDbValue : ITenantEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int StatusId { get; set; }
    [Required, MaxLength(150)] public string CrValue { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string DbValue { get; set; } = string.Empty;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedOnUtc { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    [MaxLength(450)] public string? ModifiedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
    public PlatformSettingStatus Status { get; set; } = null!;
}
