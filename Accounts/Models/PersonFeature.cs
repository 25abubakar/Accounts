using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Direct grant: this person can use this feature/permission.
    /// Set by admin when assigning menu access (all child features) or individual features.
    /// </summary>
    [Table("PersonFeatures")]
    public class PersonFeature
    {
        public Guid PersonId { get; set; }
        public int PermissionId { get; set; }

        [MaxLength(450)]
        public string? GrantedBy { get; set; }

        public DateTime GrantedOnUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(PersonId))]
        public Person? Person { get; set; }

        [ForeignKey(nameof(PermissionId))]
        public Feature? Feature { get; set; }
    }
}
