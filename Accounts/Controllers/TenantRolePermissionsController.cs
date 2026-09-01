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
    /// GET    /api/tenant-roles                        → list all job titles in this tenant
    /// GET    /api/tenant-roles/{jobTitle}/permissions → get permissions for a job title
    /// PUT    /api/tenant-roles/{jobTitle}/permissions → overwrite permissions for a job title
    /// DELETE /api/tenant-roles/{jobTitle}             → remove all permissions for a job title
    /// GET    /api/tenant-roles/allowed-menus          → menus Tenant Admin may delegate

    [ApiController]
    [Route("api/tenant-roles")]
    [Authorize]
    [Produces("application/json")]
    public class TenantRolePermissionsController : ControllerBase
    {
        private readonly ApplicationDbContext         _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public TenantRolePermissionsController(
            ApplicationDbContext         db,
            UserManager<ApplicationUser> userManager)
        {
            _db          = db;
            _userManager = userManager;
        }


        private async Task<(ApplicationUser? user, int? tenantId, bool isTenantAdmin)> GetCallerAsync()
        {
            var uid  = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = uid != null ? await _userManager.FindByIdAsync(uid) : null;
            return (user, user?.TenantId,
                (user?.IsTenantAdmin ?? false) || User.IsInRole("TenantAdmin"));
        }

        // ── GET /api/tenant-roles ──────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetRoles()
        {
            var (user, tenantId, isTenantAdmin) = await GetCallerAsync();
            if (!isTenantAdmin || !tenantId.HasValue)
                return Forbid();

            var roles = await _db.TenantRolePermissions
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId.Value && r.IsAllowed)
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

        [HttpGet("{jobTitle}/permissions")]
        public async Task<IActionResult> GetPermissions(string jobTitle)
        {
            var (user, tenantId, isTenantAdmin) = await GetCallerAsync();
            if (!isTenantAdmin || !tenantId.HasValue)
                return Forbid();

            var permissions = await _db.TenantRolePermissions
                .AsNoTracking()
                .Include(r => r.Feature)
                .Where(r => r.TenantId == tenantId.Value
                         && r.JobTitle == jobTitle
                         && r.IsAllowed)
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

        [HttpPut("{jobTitle}/permissions")]
        public async Task<IActionResult> SetPermissions(string jobTitle, [FromBody] List<int> permissionIds)
        {
            var (user, tenantId, isTenantAdmin) = await GetCallerAsync();
            if (!isTenantAdmin || !tenantId.HasValue)
                return Forbid();

            if (string.IsNullOrWhiteSpace(jobTitle))
                return BadRequest(new { message = "jobTitle is required." });

            var allowedPermissionIds = await _db.MenuPermissions
                .AsNoTracking()
                .Select(mp => mp.PermissionId)
                .ToHashSetAsync();

            var invalid = permissionIds.Except(allowedPermissionIds).ToList();
            if (invalid.Any())
            {
                return BadRequest(new
                {
                    message = "Some permissions are not within your tenant's allowed pool. " +
                              "You can only assign permissions from menus granted to your tenant.",
                    invalidPermissionIds = invalid
                });
            }

            var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // ── Overwrite: remove existing, insert new ─────────────────────
            var existing = await _db.TenantRolePermissions
                .Where(r => r.TenantId == tenantId.Value && r.JobTitle == jobTitle)
                .ToListAsync();

            _db.TenantRolePermissions.RemoveRange(existing);

            foreach (var permId in permissionIds.Distinct())
            {
                _db.TenantRolePermissions.Add(new TenantRolePermission
                {
                    TenantId      = tenantId.Value,
                    JobTitle      = jobTitle.Trim(),
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

        [HttpGet("allowed-menus")]
        public async Task<IActionResult> GetAllowedMenus()
        {
            var (user, tenantId, isTenantAdmin) = await GetCallerAsync();
            if (!isTenantAdmin || !tenantId.HasValue)
                return Forbid();

            // Fetch active menus with their linked feature keys.
            var menus = await _db.Menus
                .AsNoTracking()
                .Include(m => m.MenuPermissions).ThenInclude(mp => mp.Feature)
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .Select(m => new
                {
                    menuId   = m.Id,
                    title    = m.Title,
                    route    = m.Route,
                    icon     = m.Icon,
                    features = m.MenuPermissions.Select(mp => new
                    {
                        permissionId = mp.PermissionId,
                        featureKey   = mp.Feature != null ? mp.Feature.FeatureKey : null,
                        featureName  = mp.Feature != null ? mp.Feature.FeatureName : null,
                        module       = mp.Feature != null ? mp.Feature.Module : null
                    }).ToList()
                })
                .ToListAsync();

            return Ok(menus);
        }
    }
}
