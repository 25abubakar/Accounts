using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AnnualReportTypes")]
public sealed class AnnualReportType
{
    [Key]
    public int Id { get; set; }
    public int? TenantId { get; set; }
    [Required, MaxLength(40)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

[Table("AnnualReportFilters")]
public sealed class AnnualReportFilter : ITenantEntity
{
    [Key]
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ReportTypeId { get; set; }
    public int CategoryId { get; set; }
    public bool IsInclude { get; set; } = true;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("AnnualReportHeaders")]
public sealed class AnnualReportHeader : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(20)] public string ReportTypeCode { get; set; } = "EXPENSE";
    [Required, MaxLength(20)] public string FiscalYear { get; set; } = string.Empty;
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    [MaxLength(2000)] public string? Remarks { get; set; }
    public bool IsApproved { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    [MaxLength(450)] public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedOnUtc { get; set; }
}

[Table("AnnualReportLines")]
public sealed class AnnualReportLine
{
    [Key]
    public long Id { get; set; }
    public long HeaderId { get; set; }
    public int CategoryId { get; set; }
    [MaxLength(120)] public string? CategoryName { get; set; }
    public int CalendarYear { get; set; }
    public int CalendarMonth { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
}
