using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Direct grant: this person can see this sidebar menu (and its route).
    /// Set by admin when assigning access to a menu section.
    /// </summary>
    [Table("PersonMenus")]
    public class PersonMenu
    {
        public Guid PersonId { get; set; }
        public int MenuId { get; set; }

        [MaxLength(450)]
        public string? GrantedBy { get; set; }

        public DateTime GrantedOnUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(PersonId))]
        public Person? Person { get; set; }

        [ForeignKey(nameof(MenuId))]
        public Menu? Menu { get; set; }
    }
}
