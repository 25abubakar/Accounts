using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

/// <summary>Primary journal / billing ledger (old tblRoznamcha → TenantId).</summary>
[Table("RoznamchaEntries")]
public sealed class RoznamchaEntry : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int? SNo { get; set; }
    [MaxLength(80)] public string? Ref { get; set; }
    [MaxLength(80)] public string? OldRef { get; set; }
    public int? RoznamchaTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? FromAccountId { get; set; }
    public int? ToAccountId { get; set; }
    public int? ProjectId { get; set; }
    public int? CustomerId { get; set; }
    public Guid? StaffId { get; set; }
    public int? TransTypeId { get; set; }
    public int? TransModeId { get; set; }
    public DateOnly? InstrumentDate { get; set; }
    [MaxLength(100)] public string? InstrumentNo { get; set; }
    [MaxLength(2000)] public string? Descriptions { get; set; }
    public int? CurrencyId { get; set; }
    public int? TaxTypeId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? TaxRate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? TaxAmt { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Adjustment { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Amount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? BalanceAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Qty { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Rate { get; set; }
    public int? EnterStatusId { get; set; }
    [MaxLength(500)] public string? Attachment { get; set; }
    [MaxLength(2000)] public string? Remarks { get; set; }
    [MaxLength(100)] public string? BankLtRef { get; set; }
    public DateOnly TransDate { get; set; }
    public bool IsDeleted { get; set; }
    [MaxLength(100)] public string? BankRef { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? UsdAmount { get; set; }
    public bool IsShow { get; set; } = true;
    public bool IsApproved { get; set; }
    public bool IsSettled { get; set; }
    public bool IsFromLibrary { get; set; }
    [MaxLength(80)] public string? LibRef { get; set; }
    public bool IsLocked { get; set; }
    public bool IsLedger { get; set; }
    public bool IsManual { get; set; }
    [MaxLength(80)] public string? EobCheckNo { get; set; }
    [MaxLength(80)] public string? ClaimNoIcn { get; set; }
    [MaxLength(500)] public string? PatientInfo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PaidCopayAmount { get; set; }
    [MaxLength(200)] public string? ReferralSource { get; set; }
    [MaxLength(200)] public string? Reference { get; set; }
    public bool IsMatchedCopay { get; set; }
    public bool IsClosed { get; set; }
    [MaxLength(500)] public string? ImagePath { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("AccountsChartAccounts")]
public sealed class AccountsChartAccount : ITenantEntity
{
    [Key]
    public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(50)] public string AccountNumber { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string AccountName { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public int? ParentId { get; set; }
    [MaxLength(80)] public string? AccountCode { get; set; }
    [MaxLength(80)] public string? AccountReference { get; set; }
    [MaxLength(100)] public string? BankAccountNumber { get; set; }
    [MaxLength(100)] public string? CnicNtn { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(200)] public string? FullName { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(50)] public string? Phone { get; set; }
    public int? DesignationId { get; set; }
    public Guid? PersonId { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(500)] public string? Photo { get; set; }
    [MaxLength(500)] public string? Attachment { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal BudgetAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal UsedAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal BalanceAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AccountLimit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Credit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Debit { get; set; }
    public int StatusId { get; set; } = 11;
    public bool IsStatement { get; set; }
    public bool IsInventory { get; set; }
    public bool IsStaff { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("AccountsCategories")]
public sealed class AccountsCategory : ITenantEntity
{
    [Key]
    public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(40)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public int? CategoryTypeId { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(80)] public string? Number { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal BudgetAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal UsedAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal BalanceAmount { get; set; }
    public bool IsNotInReport { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("AccountsCategoryTypes")]
public sealed class AccountsCategoryType : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("AccountsRoznamchaTypes")]
public sealed class AccountsRoznamchaType
{
    [Key]
    public int Id { get; set; }
    public int? TenantId { get; set; }
    [Required, MaxLength(40)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

[Table("AccountsEntryStatuses")]
public sealed class AccountsEntryStatus
{
    [Key]
    public int Id { get; set; }
    public int? TenantId { get; set; }
    [Required, MaxLength(40)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(20)] public string? ColorCode { get; set; }
    [MaxLength(20)] public string? FontColor { get; set; }
    public bool IsActive { get; set; } = true;
}

[Table("AccountsTransTypes")]
public sealed class AccountsTransType
{
    [Key]
    public int Id { get; set; }
    public int? TenantId { get; set; }
    [Required, MaxLength(40)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

[Table("AccountsTransModes")]
public sealed class AccountsTransMode
{
    [Key]
    public int Id { get; set; }
    public int? TenantId { get; set; }
    [Required, MaxLength(40)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

[Table("AccountsCurrencies")]
public sealed class AccountsCurrency
{
    [Key]
    public int Id { get; set; }
    public int? TenantId { get; set; }
    [Required, MaxLength(10)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>Account Type → Date tab master (legacy tblAccountDates.DateLabel).</summary>
[Table("AccountsDateLabels")]
public sealed class AccountsDateLabel
{
    [Key]
    public int Id { get; set; }
    public int? TenantId { get; set; }
    [Required, MaxLength(40)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>Tenant Accounts/ROZ settings — no hardcoded billing category or account IDs in services.</summary>
[Table("AccountsModuleSettings")]
public sealed class AccountsModuleSettings : ITenantEntity
{
    [Key]
    public int TenantId { get; set; }
    public int? BillingCategoryId { get; set; }
    public int? DefaultFromAccountId { get; set; }
    public int? DefaultToAccountId { get; set; }
    public int? DefaultCurrencyId { get; set; }
    public int? PaymentRozTypeId { get; set; }
    public int? ReceiptRozTypeId { get; set; }
}

[Table("BillingRoznamchaImports")]
public sealed class BillingRoznamchaImport : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(260)] public string FileName { get; set; } = string.Empty;
    [MaxLength(450)] public string? UploadedByUserId { get; set; }
    public DateTime UploadedOnUtc { get; set; } = DateTime.UtcNow;
    [Required, MaxLength(40)] public string Status { get; set; } = "Pending";
    public int ImportRowCount { get; set; }
    [MaxLength(2000)] public string? ErrorMessage { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
}

[Table("BankStatements")]
public sealed class BankStatement : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int? ChartAccountId { get; set; }
    [MaxLength(50)] public string? AccountNumber { get; set; }
    public DateOnly? StatementDate { get; set; }
    public DateOnly? ValueDate { get; set; }
    public DateOnly? PostingDate { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Debit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Credit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Balance { get; set; }
    [MaxLength(100)] public string? BankRef { get; set; }
    [MaxLength(100)] public string? TransactionReferenceNumber { get; set; }
    [MaxLength(2000)] public string? Remarks { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Attachment { get; set; }
    [MaxLength(100)] public string? InstrumentNo { get; set; }
    public bool IsMatched { get; set; }
    public bool IsSettled { get; set; }
    public int? ColorId { get; set; }
    public int? YearId { get; set; }
    public bool IsReversal { get; set; }
    public bool IsManual { get; set; }
    public int? StatusId { get; set; }
    [MaxLength(40)] public string? DateFormat { get; set; }
    public long? MatchedRoznamchaEntryId { get; set; }
    [MaxLength(2000)] public string? RawLine { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("RoznamchaEntryProcessLogs")]
public sealed class RoznamchaEntryProcessLog : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long RoznamchaEntryId { get; set; }
    [Required, MaxLength(60)] public string Action { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Notes { get; set; }
    [MaxLength(450)] public string? ProcessedByUserId { get; set; }
    public DateTime ProcessedOnUtc { get; set; } = DateTime.UtcNow;
}

[Table("RoznamchaEntryAccesses")]
public sealed class RoznamchaEntryAccess : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long RoznamchaEntryId { get; set; }
    /// <summary>OrganizationTree node id (department / branch).</summary>
    public int DepartmentId { get; set; }
    public Guid? StaffId { get; set; }
}

[Table("AccountsEntryDocuments")]
public sealed class AccountsEntryDocument : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long? RoznamchaEntryId { get; set; }
    public long? AnnualReportHeaderId { get; set; }
    [Required, MaxLength(260)] public string FileName { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string StoredPath { get; set; } = string.Empty;
    [MaxLength(150)] public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    [MaxLength(2000)] public string? Remarks { get; set; }
    [MaxLength(80)] public string? DocumentReference { get; set; }
    [MaxLength(450)] public string? UploadedByUserId { get; set; }
    public DateTime UploadedOnUtc { get; set; } = DateTime.UtcNow;
}
