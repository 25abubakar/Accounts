using Accounts.Data;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Authorization
{
    /// <summary>
    /// Marks an endpoint as requiring a specific feature permission.
    ///
    /// Usage:
    ///   [HasPermission("PERSON_REGISTER")]
    ///   [HttpPost("register")]
    ///   public async Task<IActionResult> Register(...) { }
    ///
    /// SuperAdmin role bypasses all permission checks.
    /// Resolution order: UserOverride → RoleDefault → Matrix → false
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class HasPermissionAttribute : Attribute, IFilterFactory
    {
        private readonly string _featureKey;

        public HasPermissionAttribute(string featureKey)
        {
            _featureKey = featureKey;
        }

        // Must be false so DI scope is respected per-request
        public bool IsReusable => false;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            var db   = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var rbac = serviceProvider.GetRequiredService<RbacService>();
            var tenantPermissions = serviceProvider.GetRequiredService<TenantPermissionService>();
            return new PermissionFilter(_featureKey, db, rbac, tenantPermissions);
        }
    }

    /// <summary>
    /// The actual authorization filter — created per-request by HasPermissionAttribute.
    /// </summary>
    public class PermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string               _featureKey;
        private readonly ApplicationDbContext _db;
        private readonly RbacService          _rbac;
        private readonly TenantPermissionService _tenantPermissions;

        public PermissionFilter(
            string featureKey,
            ApplicationDbContext db,
            RbacService rbac,
            TenantPermissionService tenantPermissions)
        {
            _featureKey = featureKey;
            _db         = db;
            _rbac       = rbac;
            _tenantPermissions = tenantPermissions;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            // ── 1. Must be authenticated ──────────────────────────────────────
            if (user?.Identity?.IsAuthenticated != true)
            {
                context.Result = new ObjectResult(new
                {
                    message = "Authentication required.",
                    code    = "UNAUTHENTICATED"
                })
                { StatusCode = StatusCodes.Status401Unauthorized };
                return;
            }

            // SuperAdmin bypasses all permission checks.
            // TenantAdmin is capped by TenantMenuPermissions (never a full bypass).
            if (TenantPermissionService.IsSuperAdmin(user))
                return;

            if (TenantPermissionService.IsTenantAdmin(user))
            {
                if (await _tenantPermissions.HasFeatureAsync(
                    user,
                    _featureKey,
                    context.HttpContext.Request.Method,
                    context.HttpContext.RequestAborted))
                    return;

                context.Result = new ObjectResult(new
                {
                    message = $"Tenant access does not include permission '{_featureKey}'.",
                    code = "TENANT_PERMISSION_DENIED"
                }) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }

            // ── 3. Get IdentityUser.Id from claims ────────────────────────────
            var identityUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
            {
                context.Result = new ObjectResult(new
                {
                    message = "Cannot resolve user identity.",
                    code    = "FORBIDDEN"
                })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }

            // ── 4. Resolve Person → Staff via IdentityUserId ──────────────────
            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);

            if (person == null)
            {
                // 'admin' superuser has no Person record — already handled by SuperAdmin check above
                context.Result = new ObjectResult(new
                {
                    message = $"No person record found. Feature '{_featureKey}' requires a staff profile.",
                    code    = "FORBIDDEN"
                })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }

            if (person.Staff == null)
            {
                context.Result = new ObjectResult(new
                {
                    message = $"User is not yet assigned to a position. Access denied for '{_featureKey}'.",
                    code    = "FORBIDDEN"
                })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }

            // ── 5. Check permission via RBAC engine ───────────────────────────
            var hasAccess = await _rbac.HasAccessAsync(person.Staff.StaffId, _featureKey);

            if (!hasAccess)
            {
                context.Result = new ObjectResult(new
                {
                    message = $"Access denied. User does not have permission: '{_featureKey}'.",
                    code    = "FORBIDDEN"
                })
                { StatusCode = StatusCodes.Status403Forbidden };
            }
        }
    }
}
