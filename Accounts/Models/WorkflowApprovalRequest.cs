using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

/// <summary>
/// Shared approval envelope for attendance and future business workflows.
/// Module-specific business data remains in its own table; this table owns
/// approval state, separation of duties and the decision audit trail.
/// </summary>
[Table("WorkflowApprovalRequests")]
public sealed class WorkflowApprovalRequest : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public int TenantId { get; set; }

    [Required, MaxLength(80)]
    public string ProcessCode { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string EntityType { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string EntityId { get; set; } = string.Empty;

    public Guid? SubjectPersonId { get; set; }

    [Required, MaxLength(450)]
    public string RequestedByUserId { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string StatusCode { get; set; } = "PENDING";

    [MaxLength(40)]
    public string? DecisionCode { get; set; }

    [MaxLength(450)]
    public string? DecisionByUserId { get; set; }

    public DateTime? DecisionDate { get; set; }

    [MaxLength(1000)]
    public string? Comments { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
