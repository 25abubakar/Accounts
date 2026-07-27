using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendanceDeductionRequests")]
public sealed class AttendanceDeductionRequest : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public int TenantId { get; set; }

    [MaxLength(50)] public string? RegNo { get; set; }
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string UserId { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(50)] public string? Phone { get; set; }
    [MaxLength(256)] public string? Email { get; set; }

    [MaxLength(150)] public string? Office { get; set; }
    [MaxLength(150)] public string? Department { get; set; }
    [MaxLength(150)] public string? Designation { get; set; }

    [MaxLength(100)] public string? Classification { get; set; }
    [MaxLength(150)] public string? Routing { get; set; }
    [MaxLength(150)] public string? Authority { get; set; }

    [MaxLength(250)] public string? Subject { get; set; }
    [MaxLength(260)] public string? DocumentName { get; set; }

    public int DeductionMonth { get; set; }
    public int DeductionYear { get; set; }

    [MaxLength(150)] public string? ActionRouting { get; set; }
    [MaxLength(100)] public string? ActionName { get; set; }
    [MaxLength(1000)] public string? Comments { get; set; }

    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
