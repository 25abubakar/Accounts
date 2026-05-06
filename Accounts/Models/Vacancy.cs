using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("Vacancies")]
    public class Vacancy
    {
        [Key]
        public int VacancyId { get; set; }

        /// <summary>Links to a Branch node in OrganizationTree</summary>
        [Required]
        public int OrganizationId { get; set; }

        [ForeignKey("OrganizationId")]
        public OrganizationTree? Organization { get; set; }

        /// <summary>e.g. LT-RWP-DEV-01</summary>
        [Required]
        [MaxLength(50)]
        public string VacancyCode { get; set; } = string.Empty;

        /// <summary>e.g. Developer / Manager / HR Officer</summary>
        [Required]
        [MaxLength(100)]
        public string JobTitle { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Department { get; set; }

        /// <summary>false = Empty seat, true = Employee assigned</summary>
        public bool IsFilled { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Navigation — one vacancy has at most one staff
        public Staff? Staff { get; set; }
    }
}
