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
    /// Staff checks go through <see cref="RbacService"/> so they always honor the
    /// Super Admin → Tenant Admin ceiling. Tenant Admin checks go through
    /// <see cref="TenantPermissionService"/>.
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
        private readonly TenantPermissionService _tenantPermissions;

        public PermissionAuthorizationHandler(
            ApplicationDbContext db,
            RbacService rbac,
            TenantPermissionService tenantPermissions)
        {
            _db = db;
            _rbac = rbac;
            _tenantPermissions = tenantPermissions;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            var user = context.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                context.Fail();
                return;
            }

            if (TenantPermissionService.IsSuperAdmin(user))
            {
                context.Succeed(requirement);
                return;
            }

            if (TenantPermissionService.IsTenantAdmin(user))
            {
                if (await _tenantPermissions.HasFeatureAsync(user, requirement.PermissionKey))
                    context.Succeed(requirement);
                else
                    context.Fail();
                return;
            }

            var staffId = TryGetStaffId(user);
            if (!staffId.HasValue)
            {
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
                context.Succeed(requirement);
            else
                context.Fail();
        }

        private static Guid? TryGetStaffId(ClaimsPrincipal user) =>
            Guid.TryParse(user.FindFirstValue(AccountClaimTypes.StaffId), out var staffId)
                ? staffId
                : null;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequirePermissionAttribute : AuthorizeAttribute
    {
        public RequirePermissionAttribute(string permissionKey)
        {
            Policy = $"Permission:{permissionKey}";
        }
    }
}
