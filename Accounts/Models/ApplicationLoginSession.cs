using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("ApplicationLoginSessions")]
public sealed class ApplicationLoginSession : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public int TenantId { get; set; }

    public Guid? StaffId { get; set; }

    public Guid? PersonId { get; set; }

    [Required, MaxLength(450)]
    public string IdentityUserId { get; set; } = string.Empty;

    public DateOnly SessionDate { get; set; }

    public DateTime LoginUtc { get; set; }

    public DateTime? LogoutUtc { get; set; }

    public int WorkingMinutes { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [MaxLength(300)]
    public string? UserAgent { get; set; }

    [MaxLength(50)]
    public string Source { get; set; } = "Software";

    [MaxLength(500)]
    public string? Remarks { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? ModifiedDate { get; set; }

    public StaffVacancy? Staff { get; set; }

    public Person? Person { get; set; }

    public ApplicationUser? IdentityUser { get; set; }
}
