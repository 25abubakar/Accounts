using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Represents a SaaS Tenant — one Company/Group node in OrganizationTree
    /// elevated to a first-class tenant with its own isolated operational data.
    ///
    /// Creation flow (performed atomically by TenantController):
    ///   1. Super Admin creates / selects a Company node in OrganizationTree.
    ///   2. A Tenant row is inserted pointing at that node.
    ///   3. A new ApplicationUser is created as the Tenant Admin with IsTenantAdmin=true.
    ///   4. Initial TenantMenuPermissions are written to define what menus the tenant can use.
    ///
    /// Cascade policy:
    ///   Deleting a Tenant does NOT delete OrganizationTree rows (Restrict).
    ///   Tenant data isolation is enforced via EF Core Global Query Filters.
    /// </summary>
    [Table("Tenants")]
    public class Tenant
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// FK to OrganizationTree.Id — the Company/Group node that this tenant maps to.
        /// One org node = one tenant (UNIQUE constraint enforced in DB).
        /// </summary>
        [Required]
        public int OrganizationTreeId { get; set; }

        /// <summary>Human-readable display name (e.g. "Lal Technologies").</summary>
        [Required]
        [MaxLength(150)]
        public string TenantName { get; set; } = string.Empty;

        /// <summary>
        /// Short code used for login ID generation prefix (e.g. "LT").
        /// Must be unique across all tenants.
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string TenantCode { get; set; } = string.Empty;

        /// <summary>Soft-disable without deleting the tenant.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────
        [ForeignKey(nameof(OrganizationTreeId))]
        public OrganizationTree? OrganizationNode { get; set; }

        public ICollection<TenantMenuPermission> MenuPermissions { get; set; }
            = new List<TenantMenuPermission>();
    }
}
