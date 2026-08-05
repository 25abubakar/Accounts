using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AssessmentBonusRules", Schema = "dbo")]
public sealed class AssessmentBonusRule : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    public int RankNumber { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal BonusAmount { get; set; }
    public bool AppliesToHigherRanks { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDateUtc { get; set; }
    public DateTime? ModifiedDateUtc { get; set; }
}
