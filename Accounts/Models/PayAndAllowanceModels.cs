using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

public abstract class PayDefinitionBase : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string CalculationType { get; set; } = "Fixed";
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal Percentage { get; set; }
    public bool IsTaxable { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("PayrollBenefitDefinitions")]
public sealed class PayrollBenefitDefinition : PayDefinitionBase
{
    public bool IsEobiContributory { get; set; }
}

[Table("PayrollBonusDefinitions")]
public sealed class PayrollBonusDefinition : PayDefinitionBase
{
    [Required, MaxLength(20)] public string Frequency { get; set; } = "Monthly";
}

[Table("PayrollRuns")]
public sealed class PayrollRun : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    [Required, MaxLength(40)] public string RunNumber { get; set; } = string.Empty;
    public DateOnly PayDate { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Draft";
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("EobiSettings")]
public sealed class EobiSetting : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal EmployeeRatePercentage { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal EmployerRatePercentage { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MinimumWage { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MaximumContributionBase { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("PayrollTaxSlabs")]
public sealed class PayrollTaxSlab : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(20)] public string TaxYear { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal FromAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ToAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal FixedTaxAmount { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal RatePercentage { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("EobiEligibilities")]
public sealed class EobiEligibility : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    public Guid PersonId { get; set; }
    [MaxLength(50)] public string? EobiNumber { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsEligible { get; set; } = true;
    [MaxLength(500)] public string? Remarks { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public Person? Person { get; set; }
}

[Table("PayScaleRuleRegistrations")]
public sealed class PayScaleRuleRegistration : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string RuleType { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("PayScaleAllowances")]
public sealed class PayScaleAllowance : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string AllowanceReference { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public int SalaryScaleId { get; set; }
    public int AllowanceTypeId { get; set; }
    [MaxLength(50)] public string? ContractType { get; set; }
    [MaxLength(50)] public string? FrequencyType { get; set; }
    [MaxLength(50)] public string? RateType { get; set; }
    [MaxLength(50)] public string? PayType { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal PayValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal CalculatedValue { get; set; }
    [Required, MaxLength(20)] public string AllowanceCategory { get; set; } = "GENERAL";
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public SalaryScale? SalaryScale { get; set; }
    public AllowanceType? AllowanceType { get; set; }
}

[Table("PayRules")]
public sealed class PayRule : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string RuleType { get; set; } = "Standard";
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    [Required, MaxLength(20)] public string WorkingDaysBasis { get; set; } = "Scheduled";
    public int FixedWorkingDays { get; set; }
    [Column(TypeName = "decimal(6,2)")] public decimal WorkingHoursPerDay { get; set; } = 9;
    [Column(TypeName = "decimal(6,2)")] public decimal OvertimeMultiplier { get; set; } = 1.5m;
    [Required, MaxLength(20)] public string RoundingMode { get; set; } = "Nearest";
    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
}

[Table("SalaryPackages")]
public sealed class SalaryPackage : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public int SalaryScaleId { get; set; }
    public int PayRuleId { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public SalaryScale? SalaryScale { get; set; }
    public PayRule? PayRule { get; set; }
}
