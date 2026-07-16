using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    public enum AttendanceVisibilityScope
    {
        Self = 0,
        OrganizationNode = 1,
        OrganizationNodeAndDescendants = 2
    }

    /// <summary>
    /// Normalized lookup for job titles.
    /// Replaces the raw string JobTitle column on Vacancies.
    ///
    /// Vacancies now carry a JobTitleId (int FK) instead of a free-text string.
    /// The backend enforces upsert: if a caller sends a new string, it is first
    /// inserted here and the generated Id is used on the Vacancy row.
    /// No duplicates are allowed (TitleName has a UNIQUE constraint).
    /// </summary>
    [Table("JobTitles")]
    public class JobTitle : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // ── ITenantEntity ─────────────────────────────────────────────────
        /// <summary>FK to Tenants.Id — job titles are tenant-scoped.</summary>
        public int TenantId { get; set; }

        [Required]
        [MaxLength(100)]
        public string TitleName { get; set; } = string.Empty;

        /// <summary>
        /// Database-driven attendance visibility attached to this position type.
        /// It is intentionally independent from the Report-To feature.
        /// </summary>
        public AttendanceVisibilityScope AttendanceVisibilityScope { get; set; } = AttendanceVisibilityScope.Self;

        // ── Navigation ────────────────────────────────────────────────────
        public ICollection<Vacancy> Vacancies { get; set; } = new List<Vacancy>();
    }
}
