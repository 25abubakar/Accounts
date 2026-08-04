using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("SecurityAuditLogs")]
public sealed class SecurityAuditLog
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public int? TenantId { get; set; }
    [MaxLength(450)] public string? UserId { get; set; }
    [Required, MaxLength(12)] public string Method { get; set; } = string.Empty;
    [Required, MaxLength(512)] public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool Succeeded { get; set; }
    [MaxLength(64)] public string? RemoteIp { get; set; }
    [Required, MaxLength(64)] public string TraceId { get; set; } = string.Empty;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
}
