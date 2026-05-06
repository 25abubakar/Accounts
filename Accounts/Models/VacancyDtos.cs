using System.ComponentModel.DataAnnotations;

namespace Accounts.Models
{
    // ── VACANCY RESPONSE ─────────────────────────────────────────────

    public class VacancyDto
    {
        public int VacancyId { get; set; }
        public int OrganizationId { get; set; }

        /// <summary>The org node this position is directly attached to</summary>
        public string BranchName { get; set; } = string.Empty;

        /// <summary>One level up from the attached node</summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>Two levels up from the attached node</summary>
        public string CountryName { get; set; } = string.Empty;

        /// <summary>Full path label of the attached node (e.g. "Branch" / "Company" / "Group")</summary>
        public string NodeLabel { get; set; } = string.Empty;

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

        [Required, MaxLength(50)]
        public string VacancyCode { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string JobTitle { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Department { get; set; }
    }

    public class UpdateVacancyDto
    {
        [Required, MaxLength(50)]
        public string VacancyCode { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string JobTitle { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Department { get; set; }

        [Required]
        public int OrganizationId { get; set; }
    }

    // ── STAFF RESPONSE ───────────────────────────────────────────────

    public class StaffDto
    {
        public int StaffId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? PhotoUrl { get; set; }
        public int? VacancyId { get; set; }
        public string? VacancyCode { get; set; }
        public string? JobTitle { get; set; }
        public string? BranchName { get; set; }
        public string? CompanyName { get; set; }
        public string? CountryName { get; set; }
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

        /// <summary>Vacancy this employee is hired for</summary>
        [Required]
        public int VacancyId { get; set; }
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
        /// <summary>New vacancy to transfer the employee to</summary>
        [Required]
        public int NewVacancyId { get; set; }
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
