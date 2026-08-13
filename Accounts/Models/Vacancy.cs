using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("Vacancies")]
    public class Vacancy : ITenantEntity
    {
        [Key]
        public Guid VacancyId { get; set; } = Guid.NewGuid();

        public int TenantId { get; set; }

        [Required]
        public int OrganizationId { get; set; }

        [ForeignKey(nameof(OrganizationId))]
        public OrganizationTree? Organization { get; set; }

        [Column("JobTitleId")]
        public int? DesignationId { get; set; }

        [NotMapped]
        public int? JobTitleId
        {
            get => DesignationId;
            set => DesignationId = value;
        }

        [ForeignKey(nameof(DesignationId))]
        public Designation? DesignationNav { get; set; }

        /// <summary>Legacy free-text column in dbo.Vacancies.JobTitle.</summary>
        [MaxLength(100)]
        public string? JobTitle { get; set; }

        [MaxLength(100)]
        public string? Department { get; set; }

        [Required]
        [MaxLength(50)]
        public string VacancyCode { get; set; } = string.Empty;

        public bool IsFilled { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public StaffVacancy? Staff { get; set; }

        [NotMapped]
        public string ResolvedDesignation =>
            DesignationNav?.Name ?? JobTitle ?? string.Empty;

        [NotMapped]
        public string ResolvedJobTitle => ResolvedDesignation;
    }
}
