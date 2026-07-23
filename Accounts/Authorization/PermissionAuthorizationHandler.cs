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

        public PermissionAuthorizationHandler(ApplicationDbContext db) => _db = db;

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

            // SuperAdmin/Admin/TenantAdmin bypass
            // TenantAdmin uses TenantMenuPermissions, not RBAC overrides
            if (user.IsInRole("SuperAdmin") || user.IsInRole("Admin") || user.IsInRole("TenantAdmin") ||
                string.Equals(user.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
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

            if (staffId.HasValue && await HasAccessAsync(staffId.Value, requirement.PermissionKey))
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

        private async Task<bool> HasAccessAsync(Guid staffId, string featureKey)
        {
            var permissionId = await _db.Features.AsNoTracking()
                .Where(feature => feature.FeatureKey == featureKey)
                .Select(feature => (int?)feature.PermissionId)
                .FirstOrDefaultAsync();
            if (!permissionId.HasValue) return false;

            // Project only the two booleans needed for the decision. The old
            // handler materialized every access row and then loaded every system
            // feature for each protected request.
            var grants = await _db.StaffMenuAccesses.AsNoTracking()
                .Where(grant => grant.StaffId == staffId && grant.IsAllow)
                .Select(grant => new
                {
                    HasFeatureRules = grant.AccessFeatures.Any(),
                    AllowsPermission = grant.AccessFeatures.Any(feature =>
                        feature.PermissionId == permissionId.Value && feature.IsAllow)
                })
                .ToListAsync();

            if (grants.Count > 0)
                return grants.Any(grant => !grant.HasFeatureRules || grant.AllowsPermission);

            // Legacy fallback. Department-specific role rules take precedence
            // over the global job-title rule, matching the existing RBAC engine.
            var staff = await _db.StaffVacancies.AsNoTracking()
                .Where(item => item.StaffId == staffId)
                .Select(item => new
                {
                    JobTitle = item.Vacancy != null
                        ? (item.Vacancy.JobTitleNav != null
                            ? item.Vacancy.JobTitleNav.TitleName
                            : item.Vacancy.JobTitle)
                        : null,
                    DepartmentId = item.Vacancy != null
                        ? (int?)item.Vacancy.OrganizationId
                        : null
                })
                .FirstOrDefaultAsync();
            if (staff == null) return false;

            if (!string.IsNullOrWhiteSpace(staff.JobTitle))
            {
                var roleRules = await _db.RolePermissions.AsNoTracking()
                    .Where(rule => rule.JobTitle == staff.JobTitle &&
                                   rule.PermissionId == permissionId.Value &&
                                   (rule.DeptId == null || rule.DeptId == staff.DepartmentId))
                    .Select(rule => new { rule.DeptId, rule.IsAllowed })
                    .ToListAsync();
                var departmentRule = roleRules.FirstOrDefault(rule => rule.DeptId == staff.DepartmentId);
                if (departmentRule != null) return departmentRule.IsAllowed;
                var globalRule = roleRules.FirstOrDefault(rule => rule.DeptId == null);
                if (globalRule != null) return globalRule.IsAllowed;
            }

            return await _db.DepartmentAccessMatrix.AsNoTracking().AnyAsync(rule =>
                rule.StaffId == staffId &&
                rule.PermissionId == permissionId.Value &&
                rule.HasAccess);
        }
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
