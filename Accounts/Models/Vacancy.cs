using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("Vacancies")]
    public class Vacancy : ITenantEntity
    {
        [Key]
        public Guid VacancyId { get; set; } = Guid.NewGuid();

        // ── ITenantEntity ─────────────────────────────────────────────────
        /// <summary>FK to Tenants.Id — set on creation, never changed.</summary>
        public int TenantId { get; set; }

        /// <summary>Links to a Branch/Department node in OrganizationTree</summary>
        [Required]
        public int OrganizationId { get; set; }

        [ForeignKey(nameof(OrganizationId))]
        public OrganizationTree? Organization { get; set; }

        /// <summary>
        /// FK to normalized JobTitles table.
        /// Nullable — will be made required after all rows are backfilled.
        /// </summary>
        public int? JobTitleId { get; set; }

        [ForeignKey(nameof(JobTitleId))]
        public JobTitle? JobTitleNav { get; set; }

        /// <summary>
        /// Legacy string column — kept for backward compat until all code
        /// migrates to JobTitleId. Do NOT use in new write paths.
        /// </summary>
        [MaxLength(100)]
        public string? JobTitle { get; set; }

        /// <summary>
        /// Legacy department string — replaced by OrganizationId (FK).
        /// Kept for backward compat; will be dropped after migration.
        /// </summary>
        [MaxLength(100)]
        public string? Department { get; set; }

        /// <summary>e.g. LT-RWP-DEV-01 — auto-generated, never edited</summary>
        [Required]
        [MaxLength(50)]
        public string VacancyCode { get; set; } = string.Empty;

        /// <summary>false = Empty seat, true = Employee assigned</summary>
        public bool IsFilled { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // ── Navigation ────────────────────────────────────────────────────
        /// <summary>One vacancy has at most one staff member.</summary>
        public StaffVacancy? Staff { get; set; }

        // ── Computed helper ───────────────────────────────────────────────
        /// <summary>
        /// Returns the resolved job title string from the normalized FK first,
        /// falling back to the legacy string column.
        /// </summary>
        [NotMapped]
        public string ResolvedJobTitle =>
            JobTitleNav?.TitleName ?? JobTitle ?? string.Empty;
    }
}
