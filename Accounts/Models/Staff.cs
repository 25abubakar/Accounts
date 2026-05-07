using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("Staff")]
    public class Staff
    {
        [Key]
        public Guid StaffId { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        /// <summary>Profile picture URL — stored in wwwroot/uploads/staff/</summary>
        [MaxLength(500)]
        public string? PhotoUrl { get; set; }

        /// <summary>One vacancy = one employee (UNIQUE enforced in DB)</summary>
        public Guid? VacancyId { get; set; }

        [ForeignKey("VacancyId")]
        public Vacancy? Vacancy { get; set; }

        public DateTime JoiningDate { get; set; } = DateTime.UtcNow;
    }
}
