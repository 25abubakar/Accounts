using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

/// <summary>
/// Credit card terminal / merchant statement record (old tblCopayStatment → TenantId).
/// Tracks daily/monthly processor statements (Clover, Square, TSYS, etc.) for copay reconciliation.
/// </summary>
[Table("CopayStatements")]
public sealed class CopayStatement : ITenantEntity
{
    [Key]
    public long Id { get; set; }

    public int TenantId { get; set; }

    /// <summary>Unique generated reference e.g. {OrgCode}-CO-105.</summary>
    [MaxLength(80)]
    public string? Ref { get; set; }

    /// <summary>Associated bank / chart account ID.</summary>
    public int? AcctId { get; set; }

    [MaxLength(50)]
    public string? CardNumber { get; set; }

    [MaxLength(10)]
    public string? Currency { get; set; }

    [MaxLength(100)]
    public string? PaymentNo { get; set; }

    public DateOnly? PaymentDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? TransactionAmount { get; set; }

    [MaxLength(100)]
    public string? FundSource { get; set; }

    [MaxLength(200)]
    public string? BusinessName { get; set; }

    [MaxLength(100)]
    public string? MerchantID { get; set; }

    [MaxLength(100)]
    public string? PaymentType { get; set; }

    [MaxLength(500)]
    public string? AdjustmentDescription { get; set; }

    [MaxLength(100)]
    public string? GBOK_BatchID { get; set; }

    [MaxLength(500)]
    public string? InterchangeDescription { get; set; }

    [MaxLength(100)]
    public string? ARNNumber { get; set; }

    /// <summary>Reference to the matched Copay Roznamcha entry (e.g. LTCR-501 or batch ref).</summary>
    [MaxLength(100)]
    public string? CopayRef { get; set; }

    [MaxLength(500)]
    public string? Attachment { get; set; }

    [MaxLength(2000)]
    public string? Remarks { get; set; }

    public bool IsSettled { get; set; }

    /// <summary>
    /// Status:
    /// 14 = Open / Pending,
    /// 20 = Settled,
    /// 1060 = Merchant Fee,
    /// 1066 = Settled Manually
    /// </summary>
    public int? StatusId { get; set; }

    public bool IsManual { get; set; }

    public long? MatchedRoznamchaEntryId { get; set; }

    public int? YearId { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public DateTime? UpdatedOnUtc { get; set; }
}
