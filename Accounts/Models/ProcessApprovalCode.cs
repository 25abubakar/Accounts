using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("ProcessApprovalCodes")]
public sealed class ProcessApprovalCode : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int TenantId { get; set; }
    [MaxLength(100)] public string ProcessName { get; set; } = null!;
    public int PinCode { get; set; }
}

