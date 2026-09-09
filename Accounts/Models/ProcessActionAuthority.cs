using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("ProcessActionAuthorities")]
public sealed class ProcessActionAuthority : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(50)] public string ProcessCode { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string ActionCode { get; set; } = string.Empty;
    public Guid StaffId { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedDateUtc { get; set; } = DateTime.UtcNow;
}
