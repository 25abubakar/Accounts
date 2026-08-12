using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AssessmentSchedules", Schema = "dbo")]
public sealed class AssessmentSchedule : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public int AssessmentYear { get; set; }
    public byte AssessmentMonth { get; set; }
    public byte OpenDay { get; set; } = 25;
    public bool IsManualOverride { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDateUtc { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
}
