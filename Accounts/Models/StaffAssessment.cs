using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("StaffAssessments", Schema = "dbo")]
public sealed class StaffAssessment : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public Guid AssessorPersonId { get; set; }
    public Guid SubjectPersonId { get; set; }
    public int AssessmentYear { get; set; }
    public byte AssessmentMonth { get; set; }
    public byte? Rating { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Amount { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? SubmittedDateUtc { get; set; }
    public bool IsFinalApproved { get; set; }
    [MaxLength(450)] public string? FinalApprovedByUserId { get; set; }
    [MaxLength(200)] public string? FinalApprovedByName { get; set; }
    public DateTime? FinalApprovedDateUtc { get; set; }
    public bool IsPostedToPayroll { get; set; }
    public long? PostedPayrollRunId { get; set; }
    public DateTime? PostedToPayrollDateUtc { get; set; }
    public DateTime CreatedDateUtc { get; set; }
    public DateTime? ModifiedDateUtc { get; set; }
}
