using System.ComponentModel.DataAnnotations;

namespace Accounts.Models
{
    // ── VACANCY RESPONSE ─────────────────────────────────────────────

    public class VacancyDto
    {
        public Guid VacancyId { get; set; }
        public int OrganizationId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public string NodeLabel { get; set; } = string.Empty;

        /// <summary>Auto-generated e.g. LT-KHI-MGR-01</summary>
        public string VacancyCode { get; set; } = string.Empty;

        public string JobTitle { get; set; } = string.Empty;
        public string? Department { get; set; }
        public bool IsFilled { get; set; }
        public DateTime CreatedDate { get; set; }
        public StaffDto? Employee { get; set; }
    }

    // ── VACANCY REQUESTS ─────────────────────────────────────────────

    public class CreateVacancyDto
    {
        [Required]
        public int OrganizationId { get; set; }

        /// <summary>
        /// ID-first: send the normalized JobTitles.Id if the user picks an existing title.
        /// Mutually exclusive with JobTitleName — send one or the other, not both.
        /// </summary>
        public int? JobTitleId { get; set; }

        /// <summary>
        /// Name-fallback: send a new string if the user types a brand-new title.
        /// The backend will upsert JobTitles and use the generated Id.
        /// Mutually exclusive with JobTitleId.
        /// </summary>
        [MaxLength(100)]
        public string? JobTitleName { get; set; }

        /// <summary>
        /// Legacy support: still accepted but internally resolved to JobTitleId via upsert.
        /// Prefer JobTitleId or JobTitleName in new integrations.
        /// </summary>
        [MaxLength(100)]
        public string? JobTitle { get; set; }

        [MaxLength(100)]
        public string? Department { get; set; }

        /// <summary>
        /// How many vacancies to create in one request.
        /// Default = 1. Max = 100.
        /// </summary>
        [Range(1, 100, ErrorMessage = "VacancyCount must be between 1 and 100.")]
        public int VacancyCount { get; set; } = 1;
    }

    public class UpdateVacancyDto
    {
        [Required]
        public int OrganizationId { get; set; }

        /// <summary>ID-first: pick an existing title from the dropdown.</summary>
        public int? JobTitleId { get; set; }

        /// <summary>Name-fallback: type a new title string.</summary>
        [MaxLength(100)]
        public string? JobTitleName { get; set; }

        /// <summary>Legacy string — still accepted, resolved to Id internally.</summary>
        [MaxLength(100)]
        public string? JobTitle { get; set; }

        [MaxLength(100)]
        public string? Department { get; set; }
    }

    // ── STAFF RESPONSE ───────────────────────────────────────────────

    public class StaffDto
    {
        public Guid StaffId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? PhotoUrl { get; set; }
        public Guid? VacancyId { get; set; }
        public string? VacancyCode { get; set; }
        public string? JobTitle { get; set; }
        public string? Department { get; set; }   // ← added
        public string? BranchName { get; set; }
        public string? CompanyName { get; set; }
        public string? CountryName { get; set; }  // ← required for country filter
        public DateTime JoiningDate { get; set; }
    }

    // ── STAFF REQUESTS ───────────────────────────────────────────────

    public class CreateStaffDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(150)]
        [EmailAddress]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        [Required]
        public Guid VacancyId { get; set; }
    }

    public class UpdateStaffDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(150)]
        [EmailAddress]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }
    }

    // ── HIRE / TRANSFER ──────────────────────────────────────────────

    public class HireStaffDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(150)]
        [EmailAddress]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }
    }

    public class TransferStaffDto
    {
        [Required]
        public Guid NewVacancyId { get; set; }
    }

    // ── FULL REPORT ROW ──────────────────────────────────────────────

    public class OrgVacancyReportDto
    {
        public string Country { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string VacancyCode { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string? Department { get; set; }
        public bool IsFilled { get; set; }
        public string Status => IsFilled ? "Filled" : "Vacant";
        public string? EmployeeName { get; set; }
        public string? EmployeeEmail { get; set; }
        public DateTime? JoiningDate { get; set; }
    }
}
