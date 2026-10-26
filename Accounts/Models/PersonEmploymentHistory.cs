using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("PersonEmploymentHistory")]
public sealed class PersonEmploymentHistory : ITenantEntity
{
    [Key] public long Id { get; set; }
    public int TenantId { get; set; }
    public Guid PersonId { get; set; }
    public Guid? StaffId { get; set; }
    public Guid? VacancyId { get; set; }
    public int? OrganizationId { get; set; }
    [MaxLength(50)] public string? LoginId { get; set; }
    [MaxLength(50)] public string? VacancyCode { get; set; }
    [MaxLength(100)] public string? JobTitle { get; set; }
    [MaxLength(100)] public string? Department { get; set; }
    [MaxLength(150)] public string? BranchName { get; set; }
    [MaxLength(150)] public string? CompanyName { get; set; }
    [MaxLength(150)] public string? CountryName { get; set; }
    [Required,MaxLength(30)] public string EmploymentStatus { get; set; } = "";
    public DateTime? JoiningDate { get; set; }
    public DateTime? EndingDateUtc { get; set; }
    [MaxLength(500)] public string? EndingReason { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public Person Person { get; set; } = null!;
}
