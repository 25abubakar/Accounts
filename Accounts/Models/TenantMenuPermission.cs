using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Phase 3 — Super Admin to Tenant delegation.
    ///
    /// Records which sidebar menus a specific tenant is allowed to use.
    /// Super Admin grants menus at the tenant level; Tenant Admin can only
    /// sub-delegate from this allowed pool to their own staff.
    ///
    /// PK: composite (TenantId, MenuId) — one row per tenant+menu combination.
    /// IsAllow=true  → tenant can use this menu.
    /// IsAllow=false → tenant is explicitly blocked from this menu.
    /// </summary>
    [Table("TenantMenuPermissions")]
    public class TenantMenuPermission
    {
        [Required]
        public int TenantId { get; set; }

        [Required]
        public int MenuId { get; set; }

        /// <summary>True = granted; False = explicitly denied.</summary>
        public bool IsAllow { get; set; } = true;

        public DateTime GrantedOnUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string? GrantedByUserId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────
        [ForeignKey(nameof(TenantId))]
        public Tenant? Tenant { get; set; }

        [ForeignKey(nameof(MenuId))]
        public Menu? Menu { get; set; }
    }
}
