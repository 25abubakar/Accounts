using System.ComponentModel.DataAnnotations;

namespace Accounts.Models
{
    public class VacancyDto
    {
        public Guid VacancyId { get; set; }
        public int OrganizationId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public string NodeLabel { get; set; } = string.Empty;
        public string VacancyCode { get; set; } = string.Empty;
        public int? DesignationId { get; set; }
        public string Designation { get; set; } = string.Empty;
        public string? Department { get; set; }
        public bool IsFilled { get; set; }
        public DateTime CreatedDate { get; set; }
        public StaffDto? Employee { get; set; }

        public int? JobTitleId
        {
            get => DesignationId;
            set => DesignationId = value;
        }

        public string JobTitle
        {
            get => Designation;
            set => Designation = value;
        }
    }

    public class CreateVacancyDto
    {
        [Required]
        public int OrganizationId { get; set; }

        public int? DesignationId { get; set; }

        [MaxLength(100)]
        public string? DesignationName { get; set; }

        /// <summary>Legacy alias — resolved to DesignationId via upsert.</summary>
        [MaxLength(100)]
        public string? JobTitle { get; set; }

        public int? JobTitleId
        {
            get => DesignationId;
            set => DesignationId = value;
        }

        public string? JobTitleName
        {
            get => DesignationName;
            set => DesignationName = value;
        }

        [MaxLength(100)]
        public string? Department { get; set; }

        [Range(1, 100, ErrorMessage = "VacancyCount must be between 1 and 100.")]
        public int VacancyCount { get; set; } = 1;
    }

    public class UpdateVacancyDto
    {
        [Required]
        public int OrganizationId { get; set; }

        public int? DesignationId { get; set; }

        [MaxLength(100)]
        public string? DesignationName { get; set; }

        [MaxLength(100)]
        public string? JobTitle { get; set; }

        public int? JobTitleId
        {
            get => DesignationId;
            set => DesignationId = value;
        }

        public string? JobTitleName
        {
            get => DesignationName;
            set => DesignationName = value;
        }

        [MaxLength(100)]
        public string? Department { get; set; }
    }

    public class StaffDto
    {
        public Guid StaffId { get; set; }
        public Guid? PersonId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? PhotoUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public string? LoginId { get; set; }
        public Guid? VacancyId { get; set; }
        public string? VacancyCode { get; set; }
        public string? Designation { get; set; }
        public string? Department { get; set; }
        public string? BranchName { get; set; }
        public string? CompanyName { get; set; }
        public string? CountryName { get; set; }
        public string? GroupName { get; set; }
        public string? ShiftStartTime { get; set; }
        public string? ShiftEndTime { get; set; }
        public DateTime JoiningDate { get; set; }

        public string? JobTitle
        {
            get => Designation;
            set => Designation = value;
        }
    }

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

    public class OrgVacancyReportDto
    {
        public string Country { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string VacancyCode { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string? Department { get; set; }
        public bool IsFilled { get; set; }
        public string Status => IsFilled ? "Filled" : "Vacant";
        public string? EmployeeName { get; set; }
        public string? EmployeeEmail { get; set; }
        public DateTime? JoiningDate { get; set; }

        public string JobTitle
        {
            get => Designation;
            set => Designation = value;
        }
    }
}
