using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("PersonExperiences")]
public class PersonExperience : ITenantEntity
{
    [Key]
    public Guid ExperienceId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public Guid PersonId { get; set; }
    [MaxLength(180)] public string? CompanyName { get; set; }
    [MaxLength(150)] public string? Role { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    [MaxLength(500)] public string? Summary { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PersonId))]
    public Person? Person { get; set; }
}
