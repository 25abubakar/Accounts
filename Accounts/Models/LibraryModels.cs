using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("LibraryCategories")]
public sealed class LibraryCategory : ITenantEntity
{
    [Key]
    public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("LibraryTypes")]
public sealed class LibraryType : ITenantEntity
{
    [Key]
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? LibraryCategoryId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsHardCopyRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public LibraryCategory? LibraryCategory { get; set; }
    public ICollection<LibraryDocument> Documents { get; set; } = new List<LibraryDocument>();
    public ICollection<LibrarySubType> SubTypes { get; set; } = new List<LibrarySubType>();
    public ICollection<LibraryTemplate> Templates { get; set; } = new List<LibraryTemplate>();
}

[Table("LibrarySubTypes")]
public sealed class LibrarySubType : ITenantEntity
{
    [Key]
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int LibraryTypeId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public LibraryType? LibraryType { get; set; }
}

[Table("LibraryDocuments")]
public sealed class LibraryDocument : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int LibraryTypeId { get; set; }
    public int? LibrarySubTypeId { get; set; }
    [MaxLength(40)] public string? DocumentReference { get; set; }
    [Required, MaxLength(20)] public string AssetKind { get; set; } = "Document";
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Description { get; set; }
    [Required, MaxLength(260)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string StoredFileName { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string ContentType { get; set; } = "application/octet-stream";
    [Required, MaxLength(20)] public string FileExtension { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? UploadedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public LibraryType? LibraryType { get; set; }
    public LibrarySubType? LibrarySubType { get; set; }
    public LibraryHardCopyLocation? HardCopyLocation { get; set; }
    public ICollection<RoznamchaLibraryDocument> RoznamchaLinks { get; set; } = new List<RoznamchaLibraryDocument>();
}

[Table("LibraryTemplates")]
public sealed class LibraryTemplate : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int LibraryTypeId { get; set; }
    public int? LibrarySubTypeId { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Description { get; set; }
    [Required] public string Content { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public LibraryType? LibraryType { get; set; }
    public LibrarySubType? LibrarySubType { get; set; }
}

[Table("LibraryHardCopyLocations")]
public sealed class LibraryHardCopyLocation : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long LibraryDocumentId { get; set; }
    [Required, MaxLength(120)] public string Store { get; set; } = string.Empty;
    [MaxLength(120)] public string? Rack { get; set; }
    [MaxLength(120)] public string? FolderReference { get; set; }
    [MaxLength(50)] public string? PageNumber { get; set; }
    [MaxLength(200)] public string? IssuedTo { get; set; }
    public DateTime? IssuedOn { get; set; }
    public DateTime? ReturnedOn { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public LibraryDocument? LibraryDocument { get; set; }
}

[Table("RoznamchaLibraryDocuments")]
public sealed class RoznamchaLibraryDocument : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long RoznamchaEntryId { get; set; }
    public long LibraryDocumentId { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public RoznamchaEntry? RoznamchaEntry { get; set; }
    public LibraryDocument? LibraryDocument { get; set; }
}

[Table("GeneratedInvoices")]
public sealed class GeneratedInvoice : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(50)] public string InvoiceNumber { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string CustomerName { get; set; } = string.Empty;
    [MaxLength(200)] public string? CustomerEmail { get; set; }
    [MaxLength(1000)] public string? CustomerAddress { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    [Required, MaxLength(10)] public string Currency { get; set; } = "PKR";
    [Column(TypeName = "decimal(18,2)")] public decimal Subtotal { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal TaxRate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DiscountAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAmount { get; set; }
    [Required, MaxLength(30)] public string Status { get; set; } = "Draft";
    [MaxLength(2000)] public string? Notes { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public ICollection<GeneratedInvoiceLine> Lines { get; set; } = new List<GeneratedInvoiceLine>();
}

[Table("GeneratedInvoiceLines")]
public sealed class GeneratedInvoiceLine
{
    [Key]
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    [Required, MaxLength(300)] public string Description { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,4)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal LineTotal { get; set; }
    public int DisplayOrder { get; set; }
    public GeneratedInvoice? Invoice { get; set; }
}
