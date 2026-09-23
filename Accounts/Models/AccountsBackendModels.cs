using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AccountsReportFilters")]
public sealed class AccountsReportFilter : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public int? AccountId { get; set; }
    public int? TypeId { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public ICollection<AccountsReportFilterCategory> Categories { get; set; } = [];
    public ICollection<AccountsReportFilterAccount> Accounts { get; set; } = [];
    public ICollection<AccountsReportFilterSubAccount> SubAccounts { get; set; } = [];
}

[Table("AccountsReportFilterCategories")]
public sealed class AccountsReportFilterCategory
{
    public int ReportFilterId { get; set; }
    public int CategoryId { get; set; }
}

[Table("AccountsReportFilterAccounts")]
public sealed class AccountsReportFilterAccount
{
    public int ReportFilterId { get; set; }
    public int AccountId { get; set; }
}

[Table("AccountsReportFilterSubAccounts")]
public sealed class AccountsReportFilterSubAccount
{
    public int ReportFilterId { get; set; }
    public int SubAccountId { get; set; }
}

[Table("AccountsProjects")]
public sealed class AccountsProject : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

[Table("AccountsProjectCostRules")]
public sealed class AccountsProjectCostRule : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    public int? TypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? AccountId { get; set; }
    public long? EntryId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal PercentageValue { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("AccountsTaxTypes")]
public sealed class AccountsTaxType
{
    [Key] public int Id { get; set; }
    public int? TenantId { get; set; }
    [Required, MaxLength(40)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,4)")] public decimal DefaultRate { get; set; }
    public bool IsActive { get; set; } = true;
}
