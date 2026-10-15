using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("SaleRoznamchaCompanies")]
public sealed class SaleRoznamchaCompany : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public ICollection<SaleRoznamchaPlatform> Platforms { get; set; } = [];
}

[Table("SaleRoznamchaPlatforms")]
public sealed class SaleRoznamchaPlatform : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    public int CompanyId { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public SaleRoznamchaCompany Company { get; set; } = null!;
    public ICollection<SaleRoznamchaProductCategory> ProductCategories { get; set; } = [];
}

[Table("SaleRoznamchaProductCategories")]
public sealed class SaleRoznamchaProductCategory : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    public int PlatformId { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public SaleRoznamchaPlatform Platform { get; set; } = null!;
    public ICollection<SaleRoznamchaProduct> Products { get; set; } = [];
}

[Table("SaleRoznamchaProducts")]
public sealed class SaleRoznamchaProduct : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public int ProductCategoryId { get; set; }
    [Required, MaxLength(80)] public string ProductCode { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Description { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal QuantityOnHand { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PurchasePrice { get; set; }
    public int? CurrencyId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public SaleRoznamchaProductCategory ProductCategory { get; set; } = null!;
    public AccountsCurrency? Currency { get; set; }
    public ICollection<SaleRoznamchaInventoryMovement> Movements { get; set; } = [];
    public ICollection<SaleRoznamchaStockReceipt> StockReceipts { get; set; } = [];
}

[Table("SaleRoznamchaPurchaseImports")]
public sealed class SaleRoznamchaPurchaseImport : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(64)] public string FileHash { get; set; } = string.Empty;
    [Required, MaxLength(260)] public string OriginalFileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int ImportedRows { get; set; }
    public int ProductCount { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
}

[Table("PaymentRoznamcha")]
public sealed class SaleRoznamchaStockReceipt : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long ProductId { get; set; }
    [MaxLength(100)] public string? Reference { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal PurchasedQuantity { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal RemainingQuantity { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal UnitCost { get; set; }
    [Column("Debit", TypeName = "decimal(18,2)")] public decimal TotalPurchaseCost { get; set; }
    [Column("UsdAmt", TypeName = "decimal(18,2)")] public decimal? UsdAmount { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal? ExchangeRate { get; set; }
    public int CurrencyId { get; set; }
    public int FromAccountId { get; set; }
    public int? ToAccountId { get; set; }
    public long? RoznamchaEntryId { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    [MaxLength(260)] public string? AttachmentOriginalName { get; set; }
    [MaxLength(260)] public string? AttachmentStoredName { get; set; }
    [MaxLength(100)] public string? AttachmentContentType { get; set; }
    public long? AttachmentSize { get; set; }
    public DateOnly PurchasedDate { get; set; }
    public DateTime PurchasedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public SaleRoznamchaProduct Product { get; set; } = null!;
    public AccountsCurrency Currency { get; set; } = null!;
    public ICollection<SaleRoznamchaDailySaleStockAllocation> SaleAllocations { get; set; } = [];
}

[Table("SaleRoznamchaInventoryMovements")]
public sealed class SaleRoznamchaInventoryMovement : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long ProductId { get; set; }
    public long? DailySaleId { get; set; }
    public long? StockReceiptId { get; set; }
    [Required, MaxLength(30)] public string MovementType { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,4)")] public decimal QuantityDelta { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal BalanceAfter { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public SaleRoznamchaProduct Product { get; set; } = null!;
    public SaleRoznamchaDailySale? DailySale { get; set; }
    public SaleRoznamchaStockReceipt? StockReceipt { get; set; }
}

[Table("SaleRoznamchaSaleStatuses")]
public sealed class SaleRoznamchaSaleStatus : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(20)] public string? ColorCode { get; set; }
    public short InventoryEffect { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public ICollection<SaleRoznamchaDailySale> Sales { get; set; } = [];
}

[Table("SaleRoznamchaDailySales")]
public sealed class SaleRoznamchaDailySale : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long? InvoiceId { get; set; }
    public long ProductId { get; set; }
    public int SaleStatusId { get; set; }
    [MaxLength(100)] public string? MarketplaceCompanyId { get; set; }
    [MaxLength(100)] public string? OrderId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal UnitCost { get; set; }
    public int? UnitCostCurrencyId { get; set; }
    public int SaleCurrencyId { get; set; }
    public int PayChargeEffectId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal UnitSalePrice { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal BaseSaleAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAdditions { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalDeductions { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NetSaleTotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal GrossTotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalRevenue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal CostAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PayCharges { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DeliveryFee { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OtherAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PromotionAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal RtoChargeAmount { get; set; }
    public int? TransactionTypeId { get; set; }
    public int? PaidStatusId { get; set; }
    public int? ToAccountId { get; set; }
    [MaxLength(100)] public string? InterProReference { get; set; }
    [MaxLength(100)] public string? OrderReference { get; set; }
    public DateOnly? SaleDate { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal InventoryDelta { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal BalanceAfter { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public SaleRoznamchaProduct Product { get; set; } = null!;
    public SaleRoznamchaInvoice? Invoice { get; set; }
    public SaleRoznamchaSaleStatus SaleStatus { get; set; } = null!;
    public AccountsCurrency? UnitCostCurrency { get; set; }
    public AccountsCurrency SaleCurrency { get; set; } = null!;
    public SaleRoznamchaAmountEffect PayChargeEffect { get; set; } = null!;
    public ICollection<SaleRoznamchaInventoryMovement> InventoryMovements { get; set; } = [];
    public ICollection<SaleRoznamchaDailySaleStockAllocation> StockAllocations { get; set; } = [];
    public ICollection<SaleRoznamchaDailySaleCharge> Charges { get; set; } = [];
    public ICollection<SaleRoznamchaDailySaleTransactionDate> TransactionDates { get; set; } = [];
}

[Table("SaleRoznamchaInvoices")]
public sealed class SaleRoznamchaInvoice : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(40)] public string ReceiptNumber { get; set; } = string.Empty;
    [MaxLength(100)] public string? OrderId { get; set; }
    public int SaleStatusId { get; set; }
    public int SaleCurrencyId { get; set; }
    public int PayChargeEffectId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PayCharges { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DeliveryFee { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OtherAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PromotionAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal RtoChargeAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ItemsSubtotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAdditions { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalDeductions { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NetTotal { get; set; }
    public int? TransactionTypeId { get; set; }
    public int? PaidStatusId { get; set; }
    public int? ToAccountId { get; set; }
    [MaxLength(100)] public string? InterProReference { get; set; }
    [MaxLength(100)] public string? OrderReference { get; set; }
    public DateOnly? SaleDate { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public SaleRoznamchaSaleStatus SaleStatus { get; set; } = null!;
    public AccountsCurrency SaleCurrency { get; set; } = null!;
    public SaleRoznamchaAmountEffect PayChargeEffect { get; set; } = null!;
    public ICollection<SaleRoznamchaDailySale> Lines { get; set; } = [];
    public ICollection<SaleRoznamchaInvoiceCharge> Charges { get; set; } = [];
    public ICollection<SaleRoznamchaInvoiceTransactionDate> TransactionDates { get; set; } = [];
}

[Table("SaleRoznamchaInvoiceCharges")]
public sealed class SaleRoznamchaInvoiceCharge : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long InvoiceId { get; set; }
    public int ChargeLabelId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public SaleRoznamchaInvoice Invoice { get; set; } = null!;
    public SaleRoznamchaChargeLabel ChargeLabel { get; set; } = null!;
}

[Table("SaleRoznamchaInvoiceTransactionDates")]
public sealed class SaleRoznamchaInvoiceTransactionDate : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long InvoiceId { get; set; }
    public int DateLabelId { get; set; }
    public DateOnly Value { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public SaleRoznamchaInvoice Invoice { get; set; } = null!;
    public SaleRoznamchaTransactionDateLabel DateLabel { get; set; } = null!;
}

[Table("SaleRoznamchaPaidStatuses")]
public sealed class SaleRoznamchaPaidStatus : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(20)] public string? ColorCode { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("SaleRoznamchaChargeLabels")]
public sealed class SaleRoznamchaChargeLabel : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<SaleRoznamchaDailySaleCharge> Charges { get; set; } = [];
}

[Table("SaleRoznamchaTransactionDateLabels")]
public sealed class SaleRoznamchaTransactionDateLabel : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<SaleRoznamchaDailySaleTransactionDate> Dates { get; set; } = [];
}

[Table("SaleRoznamchaAmountEffects")]
public sealed class SaleRoznamchaAmountEffect : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public short Multiplier { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<SaleRoznamchaDailySale> PayChargeSales { get; set; } = [];
}

[Table("SaleRoznamchaDailySaleCharges")]
public sealed class SaleRoznamchaDailySaleCharge : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long DailySaleId { get; set; }
    public int ChargeLabelId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public SaleRoznamchaDailySale DailySale { get; set; } = null!;
    public SaleRoznamchaChargeLabel ChargeLabel { get; set; } = null!;
}

[Table("SaleRoznamchaDailySaleTransactionDates")]
public sealed class SaleRoznamchaDailySaleTransactionDate : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long DailySaleId { get; set; }
    public int DateLabelId { get; set; }
    public DateOnly Value { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public SaleRoznamchaDailySale DailySale { get; set; } = null!;
    public SaleRoznamchaTransactionDateLabel DateLabel { get; set; } = null!;
}

[Table("SaleRoznamchaDailySaleStockAllocations")]
public sealed class SaleRoznamchaDailySaleStockAllocation : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long DailySaleId { get; set; }
    public long StockReceiptId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal UnitCost { get; set; }
    public int CurrencyId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public SaleRoznamchaDailySale DailySale { get; set; } = null!;
    public SaleRoznamchaStockReceipt StockReceipt { get; set; } = null!;
    public AccountsCurrency Currency { get; set; } = null!;
}
