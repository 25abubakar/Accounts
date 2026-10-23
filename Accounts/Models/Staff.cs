using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("StaffVacancy")]
    public class StaffVacancy : ITenantEntity
    {
        [Key]
        public Guid StaffId { get; set; } = Guid.NewGuid();

        /// <summary>Original numeric staff ID from an imported staff register; GUID remains the relational key.</summary>
        public int? LegacyStaffId { get; set; }
        /// <summary>Original vacancy ID, including inactive employment history; blank source stays null.</summary>
        public int? LegacyVacancyId { get; set; }
        /// <summary>Raw legacy source metadata only; never determines employment or login status.</summary>
        public int? LegacyServiceStatusId { get; set; }

        // ── ITenantEntity ─────────────────────────────────────────────────
        /// <summary>FK to Tenants.Id — copied from the vacancy at hire time.</summary>
        public int TenantId { get; set; }

        /// <summary>One vacancy = one person (UNIQUE enforced in DB)</summary>
        public Guid? VacancyId { get; set; }

        [ForeignKey("VacancyId")]
        public Vacancy? Vacancy { get; set; }

        /// <summary>
        /// Links to a registered Person. Null for legacy rows.
        /// </summary>
        public Guid? PersonId { get; set; }

        [ForeignKey("PersonId")]
        public Person? Person { get; set; }

        /// <summary>
        /// Moved from Persons.LoginId. Stored here so Persons stays pure profile data.
        /// </summary>
        [MaxLength(50)]
        public string? LoginId { get; set; }
    }
}
