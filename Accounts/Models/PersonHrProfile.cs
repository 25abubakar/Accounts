using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("PersonHrProfiles")]
public class PersonHrProfile : ITenantEntity
{
    [Key]
    public Guid PersonId { get; set; }
    public int TenantId { get; set; }

    [MaxLength(50)] public string? CnicOrLicense { get; set; }
    [MaxLength(80)] public string? Nationality { get; set; }
    [MaxLength(80)] public string? Race { get; set; }
    [MaxLength(120)] public string? Language { get; set; }
    [MaxLength(10)] public string? BloodGroup { get; set; }
    [MaxLength(250)] public string? Disability { get; set; }
    [MaxLength(150)] public string? PoliceStation { get; set; }
    [MaxLength(50)] public string? EmergencyContactNo { get; set; }

    public DateTime? MedicalFrom { get; set; }
    public DateTime? MedicalTo { get; set; }
    [MaxLength(250)] public string? Treatment { get; set; }
    [MaxLength(250)] public string? DiagnosisDisease { get; set; }
    [MaxLength(150)] public string? Doctor { get; set; }
    [MaxLength(50)] public string? DoctorContactNo { get; set; }

    [MaxLength(150)] public string? BankName { get; set; }
    [MaxLength(150)] public string? BankBranchName { get; set; }
    [MaxLength(50)] public string? BankBranchCode { get; set; }
    [MaxLength(50)] public string? SwiftCode { get; set; }
    [MaxLength(150)] public string? AccountTitle { get; set; }
    [MaxLength(80)] public string? AccountNo { get; set; }
    [MaxLength(80)] public string? IbanNo { get; set; }
    [MaxLength(50)] public string? BankBranchContactNo { get; set; }
    [MaxLength(50)] public string? TaxNumber { get; set; }
    [MaxLength(50)] public string? PaymentMode { get; set; }

    [MaxLength(30)] public string? InductionType { get; set; }
    public DateTime? JoiningDate { get; set; }
    public DateTime? TrainingFrom { get; set; }
    public DateTime? TrainingTo { get; set; }
    public DateTime? ProbationFrom { get; set; }
    public DateTime? ProbationTo { get; set; }
    public DateTime? ContractFrom { get; set; }
    public DateTime? ContractTo { get; set; }

    [MaxLength(120)] public string? WorkingDays { get; set; }
    [MaxLength(30)] public string? WorkingHours { get; set; }
    [MaxLength(10)] public string? TimingFrom { get; set; }
    [MaxLength(10)] public string? TimingTo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PostingPerHour { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PostingPerDay { get; set; }
    public DateTime? PromotionFrom { get; set; }
    public DateTime? PromotionTo { get; set; }

    [MaxLength(80)] public string? Scale { get; set; }
    public DateTime? ScaleDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? BasicSalary { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? IncrementSalary { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? MaxSalary { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? CurrentPay { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? AccountsPerDay { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? AccountsPerHour { get; set; }

    public DateTime? LeaveFrom { get; set; }
    public DateTime? LeaveTo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? LeaveEntitled { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? LeaveAvailed { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    [ForeignKey(nameof(PersonId))]
    public Person? Person { get; set; }
}
