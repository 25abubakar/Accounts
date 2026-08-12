using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("Persons")]
    public class Person : ITenantEntity
    {
        [Key]
        public Guid PersonId { get; set; } = Guid.NewGuid();

        // ── ITenantEntity ─────────────────────────────────────────────────
        /// <summary>FK to Tenants.Id — set on registration, never changed.</summary>
        public int TenantId { get; set; }

        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        // ── Split name fields (now live in DB) ────────────────────────────
        [Required]
        [MaxLength(60)]
        public string FirstName { get; set; } = string.Empty;

        [MaxLength(60)]
        public string? MiddleName { get; set; }

        [MaxLength(60)]
        public string? LastName { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(256)]
        public string? PersonalEmail { get; set; }

        [MaxLength(20)]
        public string? Gender { get; set; }

        public DateTime? DateOfBirth { get; set; }

        [MaxLength(50)]
        public string? MaritalStatus { get; set; }

        [MaxLength(5)] public string ShiftStartTime { get; set; } = "09:00";
        [MaxLength(5)] public string ShiftEndTime { get; set; } = "18:00";
        [MaxLength(100)] public string TimeZoneId { get; set; } = "Asia/Karachi";
        [MaxLength(500)]
        public string? ProfilePhotoUrl { get; set; }

        /// <summary>FK to AspNetUsers.Id</summary>
        [Required]
        [MaxLength(450)]
        public string IdentityUserId { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        /// <summary>Employment/account status controlled by tenant administrators.</summary>
        public bool IsActive { get; set; } = true;

        [MaxLength(30)]
        public string EmploymentStatus { get; set; } = "Registered";
        public DateTime? TerminationDateUtc { get; set; }
        [MaxLength(500)] public string? TerminationReason { get; set; }
        [MaxLength(50)] public string? LastLoginId { get; set; }
        [MaxLength(50)] public string? LastVacancyCode { get; set; }
        [MaxLength(100)] public string? LastJobTitle { get; set; }
        [MaxLength(100)] public string? LastDepartment { get; set; }
        [MaxLength(150)] public string? LastBranchName { get; set; }
        [MaxLength(150)] public string? LastCompanyName { get; set; }
        [MaxLength(150)] public string? LastCountryName { get; set; }
        public DateTime? LastJoiningDate { get; set; }
        /// <summary>Last assigned organization node, retained for historical branch/department scoping.</summary>
        public int? LastOrganizationId { get; set; }

        /// <summary>Configurable reporting manager within the same tenant.</summary>
        public Guid? ReportsToPersonId { get; set; }

        [ForeignKey(nameof(ReportsToPersonId))]
        public Person? ReportsToPerson { get; set; }

        public ICollection<Person> DirectReports { get; set; } = new List<Person>();

        /// <summary>Optional second reporting manager within the same tenant.</summary>
        public Guid? AlternativeReportsToPersonId { get; set; }

        [ForeignKey(nameof(AlternativeReportsToPersonId))]
        public Person? AlternativeReportsToPerson { get; set; }

        public ICollection<Person> AlternativeDirectReports { get; set; } = new List<Person>();

        public ICollection<PersonAddress>  Addresses     { get; set; } = new List<PersonAddress>();
        public ICollection<PersonContact>  Contacts      { get; set; } = new List<PersonContact>();
        public StaffVacancy?               Staff         { get; set; }

        [NotMapped]
        public string ComputedFullName =>
            string.Join(" ", new[] { FirstName, MiddleName, LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
