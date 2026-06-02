using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("StaffVacancy")]
    public class StaffVacancy
    {
        [Key]
        public Guid StaffId { get; set; } = Guid.NewGuid();

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
