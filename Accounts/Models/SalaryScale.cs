using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("SalaryScales")]
public class SalaryScale : ITenantEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TenantId { get; set; }

    [Required]
    [MaxLength(100)]
    public string ScaleName { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    [MaxLength(50)]
    public string ScaleType { get; set; } = "Regular";

    [MaxLength(20)]
    public string PayMode { get; set; } = "PM";

    [Column(TypeName = "decimal(18,2)")]
    public decimal BasicSalary { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MaximumSalary { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal YearlyIncrement { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal GrossSalary { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MedicalAllowance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TravellingAllowance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Other { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }
}
