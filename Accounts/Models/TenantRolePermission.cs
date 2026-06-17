using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Phase 3 — Tenant Admin to Staff delegation.
    ///
    /// Tenant-scoped role permission defaults.
    /// A Tenant Admin can assign feature keys to job titles within their tenant,
    /// but ONLY from the pool of features delegated by the Super Admin
    /// via TenantMenuPermissions.
    ///
    /// This replaces the global RolePermissions table for tenant-specific role defaults.
    /// The global RolePermissions table is retained for Super Admin / system-level defaults.
    ///
    /// PK: composite (TenantId, JobTitle, PermissionId, DeptId).
    /// </summary>
    [Table("TenantRolePermissions")]
    public class TenantRolePermission
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int TenantId { get; set; }

        /// <summary>Job title string — scoped to this tenant.</summary>
        [Required]
        [MaxLength(100)]
        public string JobTitle { get; set; } = string.Empty;

        /// <summary>Optional department scope within the tenant (null = tenant-wide default).</summary>
        public int? DeptId { get; set; }

        /// <summary>FK to Features.PermissionId</summary>
        [Required]
        public int PermissionId { get; set; }

        public bool IsAllowed { get; set; } = false;

        public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string? SetByUserId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────
        [ForeignKey(nameof(TenantId))]
        public Tenant? Tenant { get; set; }

        [ForeignKey(nameof(PermissionId))]
        public Feature? Feature { get; set; }

        [ForeignKey(nameof(DeptId))]
        public OrganizationTree? Department { get; set; }
    }
}
