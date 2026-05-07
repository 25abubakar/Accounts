using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("PersonAddresses")]
    public class PersonAddress
    {
        [Key]
        public Guid AddressId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid PersonId { get; set; }

        [ForeignKey("PersonId")]
        public Person? Person { get; set; }

        /// <summary>Must be "Current" or "Permanent" — enforced at application layer</summary>
        [Required]
        [MaxLength(20)]
        public string AddressType { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? AddressLine { get; set; }

        [MaxLength(100)]
        public string? Country { get; set; }

        [MaxLength(100)]
        public string? Province { get; set; }

        [MaxLength(100)]
        public string? District { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(20)]
        public string? PostalCode { get; set; }
    }
}
