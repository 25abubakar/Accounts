using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// RBAC Tier-2: Per-feature flag under a StaffMenuAccess grant.
    ///
    /// Example: a staff has MENU_8 granted (StaffMenuAccess).
    ///   - MENU_8_VIEW   IsAllow=true   → can list employees
    ///   - MENU_8_ADD    IsAllow=true   → can create
    ///   - MENU_8_EDIT   IsAllow=false  → explicitly denied
    ///   - MENU_8_DELETE (no row)       → falls back to default (deny)
    ///
    /// CASCADE DELETE from StaffMenuAccess ensures all child feature rows
    /// are removed when the parent menu grant is revoked.
    /// </summary>
    [Table("AccessFeatures")]
    public class AccessFeature
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int StaffMenuAccessId { get; set; }

        /// <summary>FK to Features.PermissionId (int PK)</summary>
        [Required]
        public int PermissionId { get; set; }

        /// <summary>True = ALLOW; False = DENY.</summary>
        public bool IsAllow { get; set; } = true;

        // ── Navigation ────────────────────────────────────────────────────
        [ForeignKey(nameof(StaffMenuAccessId))]
        public StaffMenuAccess? StaffMenuAccess { get; set; }

        [ForeignKey(nameof(PermissionId))]
        public Feature? Feature { get; set; }
    }
}
