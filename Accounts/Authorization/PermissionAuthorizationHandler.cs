using Accounts.Data;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Authorization
{
    /// <summary>
    /// Dynamic permission-based authorization handler.
    /// Allows protecting endpoints with [Authorize(Policy = "EMPLOYEE_EDIT")] etc.
    /// Uses optimized in-memory permission resolution (no N+1 queries).
    /// </summary>
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string PermissionKey { get; }

        public PermissionRequirement(string permissionKey)
        {
            PermissionKey = permissionKey;
        }
    }

    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly ApplicationDbContext _db;
        private readonly RbacService _rbac;
        private readonly ITenantService _tenantService;
        private readonly ITenantMenuCeilingService _tenantCeiling;

        public PermissionAuthorizationHandler(
            ApplicationDbContext db,
            RbacService rbac,
            ITenantService tenantService,
            ITenantMenuCeilingService tenantCeiling)
        {
            _db = db;
            _rbac = rbac;
            _tenantService = tenantService;
            _tenantCeiling = tenantCeiling;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            var user = context.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
            {
                context.Fail();
                return;
            }

            if (user.IsInRole("SuperAdmin") || user.IsInRole("Admin") || _tenantService.IsSuperAdmin)
            {
                context.Fail(new AuthorizationFailureReason(
                    this,
                    "Platform administrators cannot access tenant operational features."));
                return;
            }

            if (_tenantService.IsTenantAdmin)
            {
                if (_tenantService.TenantId.HasValue &&
                    await _tenantCeiling.AllowsFeatureAsync(
                        _tenantService.TenantId.Value,
                        requirement.PermissionKey))
                {
                    context.Succeed(requirement);
                }
                else
                {
                    context.Fail(new AuthorizationFailureReason(
                        this,
                        "The requested feature is outside the tenant access ceiling."));
                }
                return;
            }

            var staffId = TryGetStaffId(user);
            if (!staffId.HasValue)
            {
                // Existing cookies created before the staff_id claim was added
                // remain valid and use one indexed fallback lookup.
                var identityUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrWhiteSpace(identityUserId))
                {
                    staffId = await _db.Persons.AsNoTracking()
                        .Where(person => person.IdentityUserId == identityUserId)
                        .Select(person => person.Staff != null ? (Guid?)person.Staff.StaffId : null)
                        .FirstOrDefaultAsync();
                }
            }

            if (staffId.HasValue && await _rbac.HasAccessAsync(staffId.Value, requirement.PermissionKey))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }
        }

        private static Guid? TryGetStaffId(ClaimsPrincipal user) =>
            Guid.TryParse(user.FindFirstValue(AccountClaimTypes.StaffId), out var staffId)
                ? staffId
                : null;

    }

    /// <summary>
    /// Custom authorization attribute for easier usage.
    /// Usage: [RequirePermission("EMPLOYEE_EDIT")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequirePermissionAttribute : AuthorizeAttribute
    {
        public RequirePermissionAttribute(string permissionKey)
        {
            Policy = $"Permission:{permissionKey}";
        }
    }
}
