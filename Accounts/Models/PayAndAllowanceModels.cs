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

[Table("PayrollBenefitRules")]
public sealed class PayrollBenefitRule : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    [Required, MaxLength(30)] public string BenefitReference { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string BenefitsType { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(120)] public string? Company { get; set; }
    [MaxLength(120)] public string? Entitled { get; set; }
    [MaxLength(50)] public string? Contract { get; set; }
    [MaxLength(30)] public string? Frequency { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MaximumExpense { get; set; }
    [MaxLength(30)] public string? ServiceStatus { get; set; }
    [MaxLength(50)] public string? Scale { get; set; }
    public DateOnly? Wef { get; set; }
    [Column(TypeName = "decimal(9,2)")] public decimal MinimumService { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MaximumPh { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MinimumPh { get; set; }
    public bool IsIneligible { get; set; }
    [MaxLength(30)] public string? ShareType { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal CompanyShare { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal StaffShare { get; set; }
    public int? OrganizationId { get; set; }
    [MaxLength(120)] public string? CompanyName { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public ICollection<PayrollBenefitParameter> Parameters { get; set; } = new List<PayrollBenefitParameter>();
}

[Table("PayrollBenefitParameters")]
public sealed class PayrollBenefitParameter : ITenantEntity
{
    [Key] public int Id { get; set; }
    public int TenantId { get; set; }
    public int BenefitRuleId { get; set; }
    [Required, MaxLength(30)] public string Reference { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    [Column(TypeName = "decimal(9,2)")] public decimal MinimumService { get; set; }
    [Required, MaxLength(30)] public string AmountType { get; set; } = "PH";
    [Required, MaxLength(30)] public string PayType { get; set; } = "Basic";
    [Column(TypeName = "decimal(18,2)")] public decimal CompanyShare { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal StaffShare { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public PayrollBenefitRule? BenefitRule { get; set; }
}

[Table("PayrollBonusDefinitions")]
public sealed class PayrollBonusDefinition : PayDefinitionBase
{
    [Required, MaxLength(20)] public string Frequency { get; set; } = "Monthly";
}

[Table("PayrollBonusRuns")]
public sealed class PayrollBonusRun : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public int BenefitRuleId { get; set; }
    [Required, MaxLength(40)] public string RunNumber { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string BenefitReference { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string RuleName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Generated";
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    [MaxLength(150)] public string? CreatedByName { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? VerifiedByUserId { get; set; }
    [MaxLength(150)] public string? VerifiedByName { get; set; }
    public DateTime? VerifiedOnUtc { get; set; }
    [MaxLength(450)] public string? ApprovedByUserId { get; set; }
    [MaxLength(150)] public string? ApprovedByName { get; set; }
    public DateTime? ApprovedOnUtc { get; set; }
    public int TotalEmployees { get; set; }
    public int TotalEligibleEmployees { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalBonus { get; set; }
    public bool IsInactive { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public PayrollBenefitRule? BenefitRule { get; set; }
    public ICollection<PayrollBonusLine> Lines { get; set; } = new List<PayrollBonusLine>();
}

[Table("PayrollBonusLines")]
public sealed class PayrollBonusLine : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public long BonusRunId { get; set; }
    public Guid PersonId { get; set; }
    public Guid? StaffId { get; set; }
    [Required, MaxLength(50)] public string EmployeeNumber { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(150)] public string? Designation { get; set; }
    [MaxLength(150)] public string? Department { get; set; }
    public DateOnly? DateOfJoining { get; set; }
    [MaxLength(80)] public string? Scale { get; set; }
    public bool IsValid { get; set; }
    [MaxLength(500)] public string? ValidationMessage { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal BaseSalary { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal BonusAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal BasicBonus { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AttendanceBonus { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal LeaveBonus { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DisciplineBonus { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AssessmentBonus { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ServiceBonus { get; set; }
    [Column(TypeName = "decimal(9,2)")] public decimal ServiceYears { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalBonus { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal BasicPercent { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal ServicePercent { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal AttendancePercent { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal AssessmentPercent { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal LeavePercent { get; set; }
    [Column(TypeName = "decimal(9,4)")] public decimal DisciplinePercent { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal InstallmentAmount { get; set; }
    public int Installment { get; set; } = 1;
    public bool IsApproved { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidOnUtc { get; set; }
    public bool IsInactive { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedOnUtc { get; set; }
    public PayrollBonusRun? BonusRun { get; set; }
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
