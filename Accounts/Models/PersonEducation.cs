using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("PersonEducations")]
public class PersonEducation : ITenantEntity
{
    [Key]
    public Guid EducationId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public Guid PersonId { get; set; }
    [MaxLength(80)] public string? EducationLevel { get; set; }
    [MaxLength(150)] public string? DegreeTitle { get; set; }
    [MaxLength(180)] public string? Institute { get; set; }
    [MaxLength(20)] public string? PassingYear { get; set; }
    [MaxLength(50)] public string? Grade { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PersonId))]
    public Person? Person { get; set; }
}
