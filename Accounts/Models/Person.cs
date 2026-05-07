using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("Persons")]
    public class Person
    {
        [Key]
        public Guid PersonId { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? Gender { get; set; }

        public DateTime? DateOfBirth { get; set; }

        [MaxLength(50)]
        public string? MaritalStatus { get; set; }

        /// <summary>Profile picture URL — stored in wwwroot/uploads/persons/</summary>
        [MaxLength(500)]
        public string? ProfilePhotoUrl { get; set; }

        /// <summary>Auto-generated e.g. LT-10001 — used as ASP.NET Identity UserName and Email</summary>
        [Required]
        [MaxLength(30)]
        public string LoginId { get; set; } = string.Empty;

        /// <summary>FK to AspNetUsers.Id</summary>
        [Required]
        [MaxLength(450)]
        public string IdentityUserId { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<PersonAddress> Addresses { get; set; } = new List<PersonAddress>();
        public Staff? Staff { get; set; }
    }
}
