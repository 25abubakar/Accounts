using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("PlatformTypeValues")]
public sealed class PlatformTypeValue : ITenantEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TenantId { get; set; }
    public int CategoryId { get; set; }

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

    public PlatformTypeCategory? Category { get; set; }
    public Tenant? Tenant { get; set; }
}
