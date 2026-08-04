using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    /// <summary>
    /// Tenant Admin only — manages role-based permissions within a tenant.
    ///
    /// Tenant Admin owns the full tenant/company scope and may delegate menus/features
    /// to staff inside that tenant.
    ///
    /// GET    /api/tenant-roles                        → list all job titles in this tenant
    /// GET    /api/tenant-roles/{jobTitle}/permissions → get permissions for a job title
    /// PUT    /api/tenant-roles/{jobTitle}/permissions → overwrite permissions for a job title
    /// DELETE /api/tenant-roles/{jobTitle}             → remove all permissions for a job title
    /// GET    /api/tenant-roles/allowed-menus          → menus Tenant Admin may delegate
    /// </summary>
    [ApiController]
    [Route("api/tenant-roles")]
    [Authorize]
    [Produces("application/json")]
    public class TenantRolePermissionsController : ControllerBase
    {
        private readonly ApplicationDbContext         _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ITenantMenuCeilingService     _tenantCeiling;

        public TenantRolePermissionsController(
            ApplicationDbContext         db,
            UserManager<ApplicationUser> userManager,
            ITenantMenuCeilingService tenantCeiling)
        {
            _db          = db;
            _userManager = userManager;
            _tenantCeiling = tenantCeiling;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private async Task<(ApplicationUser? user, int? tenantId, bool isTenantAdmin)> GetCallerAsync()
        {
            var uid  = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = uid != null ? await _userManager.FindByIdAsync(uid) : null;
            return (user, user?.TenantId,
                (user?.IsTenantAdmin ?? false) || User.IsInRole("TenantAdmin"));
        }

        // ── GET /api/tenant-roles ──────────────────────────────────────────

        /// <summary>
        /// Returns all distinct job titles that have at least one permission entry
        /// for the caller's tenant, with a summary of granted permission count.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRoles()
        {
            var (user, tenantId, isTenantAdmin) = await GetCallerAsync();
            if (!isTenantAdmin || !tenantId.HasValue)
                return Forbid();

            var allowedPermissionIds = (await _tenantCeiling.GetAllowedPermissionIdsAsync(
                tenantId.Value,
                HttpContext.RequestAborted)).ToArray();
            var roles = await _db.TenantRolePermissions
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId.Value
                            && r.IsAllowed
                            && allowedPermissionIds.Contains(r.PermissionId))
                .GroupBy(r => r.JobTitle)
                .Select(g => new
                {
                    jobTitle        = g.Key,
                    permissionCount = g.Count()
                })
                .OrderBy(r => r.jobTitle)
                .ToListAsync();

            return Ok(roles);
        }

        // ── GET /api/tenant-roles/{jobTitle}/permissions ──────────────────

        /// <summary>
        /// Returns the full set of permissions granted to a specific job title
        /// within the caller's tenant, including feature key details.
        /// </summary>
        [HttpGet("{jobTitle}/permissions")]
        public async Task<IActionResult> GetPermissions(string jobTitle)
        {
            var (user, tenantId, isTenantAdmin) = await GetCallerAsync();
            if (!isTenantAdmin || !tenantId.HasValue)
                return Forbid();

            var allowedPermissionIds = (await _tenantCeiling.GetAllowedPermissionIdsAsync(
                tenantId.Value,
                HttpContext.RequestAborted)).ToArray();
            var permissions = await _db.TenantRolePermissions
                .AsNoTracking()
                .Include(r => r.Feature)
                .Where(r => r.TenantId == tenantId.Value
                         && r.JobTitle == jobTitle
                         && r.IsAllowed
                         && allowedPermissionIds.Contains(r.PermissionId))
                .Select(r => new
                {
                    permissionId = r.PermissionId,
                    featureKey   = r.Feature != null ? r.Feature.FeatureKey : null,
                    featureName  = r.Feature != null ? r.Feature.FeatureName : null,
                    module       = r.Feature != null ? r.Feature.Module : null,
                    deptId       = r.DeptId
                })
                .OrderBy(r => r.module).ThenBy(r => r.featureKey)
                .ToListAsync();

            return Ok(new { jobTitle, permissions });
        }

        // ── PUT /api/tenant-roles/{jobTitle}/permissions ──────────────────

        /// <summary>
        /// Overwrites the complete permission set for a job title within this tenant.
        ///
        /// Body: array of permissionId (int) values.
        ///
        /// Validation: each permissionId must be a valid menu permission.
        /// </summary>
        [HttpPut("{jobTitle}/permissions")]
        public async Task<IActionResult> SetPermissions(string jobTitle, [FromBody] List<int> permissionIds)
        {
            var (user, tenantId, isTenantAdmin) = await GetCallerAsync();
            if (!isTenantAdmin || !tenantId.HasValue)
                return Forbid();

            if (string.IsNullOrWhiteSpace(jobTitle))
                return BadRequest(new { message = "jobTitle is required." });
            var normalizedJobTitle = jobTitle.Trim();
            if (!await _db.JobTitles.AsNoTracking()
                    .AnyAsync(title => title.TenantId == tenantId.Value
                                       && title.TitleName == normalizedJobTitle))
                return BadRequest(new
                {
                    message = "The selected job title does not exist in this tenant."
                });

            var validation = await _tenantCeiling.ValidatePermissionIdsAsync(
                tenantId.Value,
                permissionIds,
                HttpContext.RequestAborted);
            if (!validation.IsValid)
            {
                return BadRequest(new
                {
                    message = "Some permissions are not within your tenant's allowed pool. " +
                              "You can only assign permissions from menus granted to your tenant.",
                    invalidPermissionIds = validation.InvalidPermissionIds
                });
            }

            var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // ── Overwrite: remove existing, insert new ─────────────────────
            var existing = await _db.TenantRolePermissions
                .Where(r => r.TenantId == tenantId.Value && r.JobTitle == normalizedJobTitle)
                .ToListAsync();

            _db.TenantRolePermissions.RemoveRange(existing);

            foreach (var permId in permissionIds.Distinct())
            {
                _db.TenantRolePermissions.Add(new TenantRolePermission
                {
                    TenantId      = tenantId.Value,
                    JobTitle      = normalizedJobTitle,
                    PermissionId  = permId,
                    IsAllowed     = true,
                    SetByUserId   = callerId,
                    CreatedOnUtc  = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message         = $"Permissions updated for role '{jobTitle}'.",
                jobTitle,
                permissionCount = permissionIds.Count
            });
        }

        // ── DELETE /api/tenant-roles/{jobTitle} ───────────────────────────

        /// <summary>Removes all permissions for the specified job title in this tenant.</summary>
        [HttpDelete("{jobTitle}")]
        public async Task<IActionResult> DeleteRole(string jobTitle)
        {
            var (user, tenantId, isTenantAdmin) = await GetCallerAsync();
            if (!isTenantAdmin || !tenantId.HasValue)
                return Forbid();

            var rows = await _db.TenantRolePermissions
                .Where(r => r.TenantId == tenantId.Value && r.JobTitle == jobTitle)
                .ToListAsync();

            if (!rows.Any())
                return NotFound(new { message = $"No permissions found for role '{jobTitle}'." });

            _db.TenantRolePermissions.RemoveRange(rows);
            await _db.SaveChangesAsync();

            return Ok(new { message = $"Role '{jobTitle}' removed ({rows.Count} permissions revoked)." });
        }

        // ── GET /api/tenant-roles/allowed-menus ───────────────────────────

        /// <summary>
        /// Returns all menus + their feature keys that this Tenant Admin may delegate.
        ///
        /// Used by the frontend to populate the Roles & Permissions checklist.
        /// </summary>
        [HttpGet("allowed-menus")]
        public async Task<IActionResult> GetAllowedMenus()
        {
            var (user, tenantId, isTenantAdmin) = await GetCallerAsync();
            if (!isTenantAdmin || !tenantId.HasValue)
                return Forbid();

            var tenantMenuGrants = await _db.TenantMenuPermissions
                .AsNoTracking()
                .Where(grant => grant.TenantId == tenantId.Value
                                && grant.IsAllow
                                && grant.CanView)
                .Select(grant => new
                {
                    grant.MenuId,
                    grant.CanView,
                    grant.CanAdd,
                    grant.CanEdit,
                    grant.CanDelete
                })
                .ToListAsync(HttpContext.RequestAborted);

            var tenantMenuIds = tenantMenuGrants.Select(grant => grant.MenuId).ToArray();
            var grantsByMenu = tenantMenuGrants.ToDictionary(grant => grant.MenuId);
            var allowedPermissionIds = (await _tenantCeiling.GetAllowedPermissionIdsAsync(
                tenantId.Value,
                HttpContext.RequestAborted)).ToArray();

            var menus = await _db.Menus
                .AsNoTracking()
                .Include(m => m.MenuPermissions).ThenInclude(mp => mp.Feature)
                .Where(m => m.IsActive && tenantMenuIds.Contains(m.Id))
                .OrderBy(m => m.SortOrder)
                .Select(m => new
                {
                    menuId   = m.Id,
                    title    = m.Title,
                    route    = m.Route,
                    icon     = m.Icon,
                    features = m.MenuPermissions
                        .Where(mp => allowedPermissionIds.Contains(mp.PermissionId))
                        .Select(mp => new
                    {
                        permissionId = mp.PermissionId,
                        featureKey   = mp.Feature != null ? mp.Feature.FeatureKey : null,
                        featureName  = mp.Feature != null ? mp.Feature.FeatureName : null,
                        module       = mp.Feature != null ? mp.Feature.Module : null
                    }).ToList()
                })
                .ToListAsync();

            return Ok(menus.Select(menu => new
            {
                menu.menuId,
                menu.title,
                menu.route,
                menu.icon,
                canView = grantsByMenu[menu.menuId].CanView,
                canAdd = grantsByMenu[menu.menuId].CanAdd,
                canEdit = grantsByMenu[menu.menuId].CanEdit,
                canDelete = grantsByMenu[menu.menuId].CanDelete,
                menu.features
            }));
        }
    }
}
