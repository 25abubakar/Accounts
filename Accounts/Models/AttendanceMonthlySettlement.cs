using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendanceMonthlySettlements")]
public sealed class AttendanceMonthlySettlement : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public int TenantId { get; set; }

    public Guid PersonId { get; set; }
    public int SettlementYear { get; set; }
    public int SettlementMonth { get; set; }

    public bool IsOvertimeApproved { get; set; }
    [MaxLength(100)] public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedDateUtc { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? AdjustmentAmount { get; set; }
    public bool IsAdjustmentApproved { get; set; }
    [MaxLength(255)] public string? AdjustmentRemarks { get; set; }

    public Person? Person { get; set; }
}

