using Microsoft.AspNetCore.Identity;

namespace Accounts.Models
{
    /// <summary>
    /// Extended Identity user that adds multi-tenant SaaS fields.
    ///
    /// Three user archetypes:
    ///
    ///   1. Super Admin  — IsSuperAdmin=true, TenantId=null
    ///      • Manages OrganizationTree and creates/manages Tenants.
    ///      • Never accesses operational data (Staff, Vacancies, Notes).
    ///      • Sees only "Organization Management" and "Platform Settings" in sidebar.
    ///
    ///   2. Tenant Admin — IsTenantAdmin=true, TenantId=&lt;tenantId&gt;
    ///      • Created atomically by TenantController when a new Tenant is provisioned.
    ///      • Manages operational data strictly scoped to their TenantId.
    ///      • Can sub-delegate menu access to their staff.
    ///
    ///   3. Staff Member — IsSuperAdmin=false, IsTenantAdmin=false, TenantId=&lt;tenantId&gt;
    ///      • Normal employee of a tenant.
    ///      • Sees only what their Tenant Admin has delegated.
    ///
    /// TenantId flows through the system as a claim in the cookie and is read
    /// by ITenantService on every request to scope all EF queries.
    /// </summary>
    public class ApplicationUser : IdentityUser
    {
        /// <summary>
        /// Null for Super Admin accounts.
        /// Non-null for Tenant Admins and all Staff Members.
        /// </summary>
        public int? TenantId { get; set; }

        /// <summary>True only for the system-level Super Admin users.</summary>
        public bool IsSuperAdmin { get; set; } = false;

        /// <summary>True for the designated Tenant Admin of each tenant.</summary>
        public bool IsTenantAdmin { get; set; } = false;
    }
}
