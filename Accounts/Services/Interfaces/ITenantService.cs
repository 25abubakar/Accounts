namespace Accounts.Services.Interfaces
{
    /// <summary>
    /// Reads the current request's tenant context from HttpContext.User claims.
    ///
    /// Used by:
    ///   - ApplicationDbContext Global Query Filters (auto-scope all ITenantEntity queries)
    ///   - Service layer when stamping TenantId on new rows
    ///   - Controllers when checking tenant-level permissions
    ///
    /// Claim names are defined as constants so they are shared between
    /// AuthService (writer) and TenantService (reader) without magic strings.
    /// </summary>
    public interface ITenantService
    {
        // ── Claim name constants — used by AuthService to write and TenantService to read ──
        public const string ClaimTenantId     = "tenant_id";
        public const string ClaimIsSuperAdmin = "is_super_admin";
        public const string ClaimIsTenantAdmin = "is_tenant_admin";

        /// <summary>
        /// The TenantId from the current user's claims.
        /// Null for Super Admin accounts (they have no tenant).
        /// </summary>
        int? TenantId { get; }

        /// <summary>True if the current user is a Super Admin (no tenant scope).</summary>
        bool IsSuperAdmin { get; }

        /// <summary>True if the current user is a Tenant Admin.</summary>
        bool IsTenantAdmin { get; }

        /// <summary>
        /// Returns TenantId or throws InvalidOperationException if called
        /// for a Super Admin or unauthenticated request.
        /// Use this in service methods that require a tenant context.
        /// </summary>
        int RequiredTenantId { get; }
    }
}
