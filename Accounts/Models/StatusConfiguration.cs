using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("Processes")]
public sealed class ProcessMaster
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string ProcessName { get; set; } = string.Empty;
    public ICollection<ProcessStatusStyle> StatusStyles { get; set; } = new List<ProcessStatusStyle>();
}

[Table("Statuses")]
public sealed class StatusDefinition
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string StatusName { get; set; } = string.Empty;
    public ICollection<ProcessStatusStyle> ProcessStyles { get; set; } = new List<ProcessStatusStyle>();
}

[Table("ColorStyles")]
public sealed class ColorStyle
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string ColorName { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string ColorCode { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string FontColor { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string FontSize { get; set; } = string.Empty;
    public ICollection<ProcessStatusStyle> ProcessStatuses { get; set; } = new List<ProcessStatusStyle>();
}

/// <summary>
/// Ternary many-to-many assignment connecting a process, status and visual style.
/// Transactional modules store this Id as their status foreign key.
/// </summary>
[Table("ProcessStatusStyles")]
public sealed class ProcessStatusStyle
{
    public int Id { get; set; }
    public int ProcessId { get; set; }
    public int StatusId { get; set; }
    public int ColorStyleId { get; set; }
    /// <summary>Null for platform defaults; otherwise owned by one tenant.</summary>
    public int? TenantId { get; set; }
    public bool IsSystem { get; set; }
    [Required, MaxLength(10)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPaid { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public ProcessMaster Process { get; set; } = null!;
    public StatusDefinition Status { get; set; } = null!;
    public ColorStyle ColorStyle { get; set; } = null!;
    public Tenant? Tenant { get; set; }
}
