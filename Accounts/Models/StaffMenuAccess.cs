using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// RBAC Tier-1: Links a Staff member to a Menu with an Allow/Deny flag.
    ///
    /// Hierarchy:
    ///   StaffMenuAccess  (which menus a staff can open)
    ///     └── AccessFeatures  (which CRUD features inside that menu are allowed)
    ///
    /// Unique constraint: one row per (StaffId, MenuId).
    /// Cascade delete: removing a StaffMenuAccess row automatically removes
    ///                 all child AccessFeatures rows (FK CASCADE).
    /// </summary>
    [Table("StaffMenuAccess")]
    public class StaffMenuAccess
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public Guid StaffId { get; set; }

        [Required]
        public int MenuId { get; set; }

        /// <summary>True = menu is accessible; False = explicitly denied.</summary>
        public bool IsAllow { get; set; } = true;

        [MaxLength(450)]
        public string? GrantedBy { get; set; }

        public DateTime GrantedDate { get; set; } = DateTime.UtcNow;

        // ── Navigation ────────────────────────────────────────────────────
        [ForeignKey(nameof(StaffId))]
        public StaffVacancy? Staff { get; set; }

        [ForeignKey(nameof(MenuId))]
        public Menu? Menu { get; set; }

        /// <summary>Child feature-level flags for this menu grant.</summary>
        public ICollection<AccessFeature> AccessFeatures { get; set; } = new List<AccessFeature>();
    }
}
