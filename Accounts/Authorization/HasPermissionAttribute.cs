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
            var tenantService = serviceProvider.GetRequiredService<ITenantService>();
            var tenantCeiling = serviceProvider.GetRequiredService<ITenantMenuCeilingService>();
            return new PermissionFilter(_featureKey, db, rbac, tenantService, tenantCeiling);
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
        private readonly ITenantService       _tenantService;
        private readonly ITenantMenuCeilingService _tenantCeiling;

        public PermissionFilter(
            string featureKey,
            ApplicationDbContext db,
            RbacService rbac,
            ITenantService tenantService,
            ITenantMenuCeilingService tenantCeiling)
        {
            _featureKey = featureKey;
            _db         = db;
            _rbac       = rbac;
            _tenantService = tenantService;
            _tenantCeiling = tenantCeiling;
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

            // Platform actors are never allowed through tenant-operational feature checks.
            if (user.IsInRole("SuperAdmin") || user.IsInRole("Admin") || _tenantService.IsSuperAdmin)
            {
                SetForbidden(
                    context,
                    "Platform administrators cannot access tenant operational features.",
                    "PLATFORM_OPERATIONAL_ACCESS_DENIED");
                return;
            }

            // TenantAdmin owns tenant operations only inside the SuperAdmin-approved
            // menu/CRUD ceiling. It bypasses staff grants, never the tenant ceiling.
            if (_tenantService.IsTenantAdmin)
            {
                if (_tenantService.TenantId.HasValue &&
                    await _tenantCeiling.AllowsFeatureAsync(
                        _tenantService.TenantId.Value,
                        _featureKey,
                        context.HttpContext.RequestAborted))
                {
                    return;
                }

                SetForbidden(
                    context,
                    $"Tenant access ceiling does not permit '{_featureKey}'.",
                    "TENANT_CEILING_DENIED");
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
                SetForbidden(
                    context,
                    $"Access denied. User does not have permission: '{_featureKey}'.",
                    "FORBIDDEN");
            }
        }

        private static void SetForbidden(
            AuthorizationFilterContext context,
            string message,
            string code)
        {
            context.Result = new ObjectResult(new { message, code })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
