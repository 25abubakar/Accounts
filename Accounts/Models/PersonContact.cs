using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// One-to-many contact records per Person.
    /// Replaces the flat Email / Phone columns on the Persons table.
    /// ContactType: 'Email' | 'PersonalEmail' | 'Phone' | 'WhatsApp' | 'Emergency' | 'Other'
    /// IsPrimary:   only one row per (PersonId, ContactType) can be primary
    ///              (enforced by partial unique index in DB).
    /// </summary>
    [Table("PersonContacts")]
    public class PersonContact
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public Guid PersonId { get; set; }

        /// <summary>Email | PersonalEmail | Phone | WhatsApp | Emergency | Other</summary>
        [Required]
        [MaxLength(20)]
        public string ContactType { get; set; } = "Email";

        [Required]
        [MaxLength(256)]
        public string ContactValue { get; set; } = string.Empty;

        /// <summary>
        /// True for the single primary contact of each type.
        /// Partial unique index (IsPrimary = 1) enforced at DB level.
        /// </summary>
        public bool IsPrimary { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // ── Navigation ────────────────────────────────────────────────────
        [ForeignKey(nameof(PersonId))]
        public Person? Person { get; set; }
    }
}
