using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Normalized designation lookup (Platform Settings → Types → Designation).
    /// Backed by dbo.JobTitles until the rename migration is applied.
    /// </summary>
    [Table("JobTitles")]
    public class Designation : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int TenantId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("TitleName")]
        public string Name { get; set; } = string.Empty;

        [NotMapped]
        public string TitleName
        {
            get => Name;
            set => Name = value;
        }

        public AttendanceVisibilityScope AttendanceVisibilityScope { get; set; } = AttendanceVisibilityScope.Self;

        public ICollection<Vacancy> Vacancies { get; set; } = new List<Vacancy>();
    }
}
