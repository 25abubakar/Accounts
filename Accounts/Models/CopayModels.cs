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

    /// <summary>Original tblCopayStatment.Id when imported from LTAccount.</summary>
    public int? LegacyId { get; set; }

    /// <summary>Original OrganizationId retained for migration reconciliation.</summary>
    public int? LegacyOrganizationId { get; set; }

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

    public long? ImportBatchId { get; set; }

    public int? SourceRowNumber { get; set; }

    [MaxLength(128)]
    public string? SourceFingerprint { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("CopayImportBatches")]
public sealed class CopayImportBatch : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(40)] public string Stage { get; set; } = string.Empty;
    [Required, MaxLength(260)] public string FileName { get; set; } = string.Empty;
    [Required, MaxLength(128)] public string FileHash { get; set; } = string.Empty;
    [Required, MaxLength(40)] public string Status { get; set; } = "Processing";
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int DuplicateRows { get; set; }
    public int InvalidRows { get; set; }
    [MaxLength(4000)] public string? ErrorSummary { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedOnUtc { get; set; }
}

[Table("CopayImportRows")]
public sealed class CopayImportRow : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long ImportBatchId { get; set; }
    public int SourceRowNumber { get; set; }
    [Required, MaxLength(128)] public string Fingerprint { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Result { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Message { get; set; }
    public long? RoznamchaEntryId { get; set; }
    public RoznamchaEntry? RoznamchaEntry { get; set; }
    public long? CopayStatementId { get; set; }
    public CopayStatement? CopayStatement { get; set; }
}
