using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

/// <summary>
/// Amazon / E-Marketing sales roznamcha line (LT tblSalesRoznamcha).
/// Stock Info (ShowSalesStock) reads purchase/sale qty &amp; amounts from these rows.
/// </summary>
[Table("EMarketingSalesRozEntries")]
public sealed class EMarketingSalesRozEntry : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    [MaxLength(80)] public string? Ref { get; set; }
    public int AccountId { get; set; }
    [MaxLength(120)] public string? PlatformName { get; set; }
    public int? TransTypeId { get; set; }
    [MaxLength(120)] public string? TransTypeName { get; set; }
    public int? SalesTypeId { get; set; }
    [MaxLength(120)] public string? SalesTypeName { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal Qty { get; set; }
    [MaxLength(120)] public string? AmzProRef { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotProCharges { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotPromotion { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotProRebate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AmazonFee { get; set; }
    [MaxLength(2000)] public string? Descriptions { get; set; }
    [MaxLength(120)] public string? OrderId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Other { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PurchaseAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal SaleAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal QtyPurchase { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal QtySale { get; set; }
    public int? StatusId { get; set; }
    [MaxLength(120)] public string? StatusName { get; set; }
    public DateOnly TransDate { get; set; }
    [MaxLength(200)] public string? DocName { get; set; }
    [MaxLength(500)] public string? AttachmentPath { get; set; }
    [MaxLength(2000)] public string? Remarks { get; set; }
    public bool IsDeleted { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}
