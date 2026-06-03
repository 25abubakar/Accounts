using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/rbac")]
    [Authorize]
    [Produces("application/json")]
    public class RbacController : ControllerBase
    {
        private readonly RbacService          _rbac;
        private readonly ApplicationDbContext _db;

        public RbacController(RbacService rbac, ApplicationDbContext db)
        {
            _rbac = rbac;
            _db   = db;
        }

        private string? CurrentUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        private bool IsFullAccessUser =>
            User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

        // ── HasAccess check ───────────────────────────────────────────────────

        /// <summary>
        /// Check if a staff member has access to a specific feature.
        /// Resolution: UserOverride → RoleDefault → Matrix → false
        /// </summary>
        [HttpGet("staff/{staffId:guid}/has-access/{featureKey}")]
        public async Task<IActionResult> HasAccess(Guid staffId, string featureKey) =>
            Ok(new
            {
                staffId,
                featureKey,
                hasAccess = await _rbac.HasAccessAsync(staffId, featureKey)
            });

        /// <summary>Get all effective permissions for a staff member</summary>
        [HttpGet("staff/{staffId:guid}/effective-permissions")]
        public async Task<IActionResult> GetEffectivePermissions(Guid staffId) =>
            Ok(new
            {
                staffId,
                permissions = await _rbac.GetEffectivePermissionsAsync(staffId)
            });

        // ── Department Matrix ─────────────────────────────────────────────────

        /// <summary>
        /// Get the full permission matrix for a department.
        /// Each cell shows: effectiveAccess, source (UserOverride/RoleDefault/Matrix/Denied), hasUserOverride.
        /// Optimized — no N+1 queries.
        /// </summary>
        [HttpGet("matrix/{deptId:int}")]
        public async Task<IActionResult> GetMatrix(int deptId) =>
            Ok(await _rbac.GetDepartmentMatrixAsync(deptId));

        // ── Role Permissions ──────────────────────────────────────────────────

        /// <summary>
        /// Get default permissions for a job title.
        /// Example: GET /api/rbac/roles/Agent/permissions?deptId=4
        /// </summary>
        [HttpGet("roles/{jobTitle}/permissions")]
        public async Task<IActionResult> GetRolePermissions(
            string jobTitle, [FromQuery] int? deptId) =>
            Ok(await _rbac.GetRolePermissionsAsync(jobTitle, deptId));

        /// <summary>
        /// Set default permissions for a job title in a department.
        /// Send a dictionary of featureKey → isAllowed.
        /// Example body: { "ATTENDANCE_VIEW": true, "EMPLOYEE_EDIT": false }
        /// </summary>
        [HttpPut("roles/{jobTitle}/permissions")]
        public async Task<IActionResult> SetRolePermissions(
            string jobTitle,
            [FromQuery] int? deptId,
            [FromBody] Dictionary<string, bool> permissions)
        {
            if (permissions == null || !permissions.Any())
                return BadRequest(new { message = "No permissions provided." });

            // Check Features table is not empty first
            var featuresExist = await _db.Features.AnyAsync();
            if (!featuresExist)
                return BadRequest(new
                {
                    message = "Features table is empty. Seed features first via POST /api/rbac/seed-features.",
                    hint    = "RolePermissions.FeatureKey has a FK to Features table — the key must exist there first."
                });

            var (count, invalidKeys) = await _rbac.SetRolePermissionsAsync(
                jobTitle, deptId, permissions, CurrentUserId);

            if (count == 0 && invalidKeys.Any())
                return BadRequest(new
                {
                    message     = "No permissions saved. All provided FeatureKeys are invalid.",
                    invalidKeys,
                    hint        = "Use GET /api/access/features to see valid feature keys."
                });

            return Ok(new
            {
                message     = $"{count} role permissions saved for '{jobTitle}'.",
                jobTitle,
                deptId,
                saved       = count,
                invalidKeys = invalidKeys.Any() ? invalidKeys : null
            });
        }

        // ── User Overrides ────────────────────────────────────────────────────

        /// <summary>Get all user-specific overrides for a staff member</summary>
        [HttpGet("staff/{staffId:guid}/overrides")]
        public async Task<IActionResult> GetOverrides(Guid staffId) =>
            Ok(await _rbac.GetUserOverridesAsync(staffId));

        /// <summary>
        /// Get the sidebar menu filtered by the logged-in user's permissions.
        /// Menu items the user can't access are removed. Empty groups are removed.
        /// </summary>
        [HttpGet("sidebar")]
        public async Task<IActionResult> GetFilteredSidebar()
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Not authenticated." });

            // SuperAdmin / Admin sees everything
            if (IsFullAccessUser)
            {
                var allMenus = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                return Ok(allMenus);
            }

            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);

            if (person?.Staff == null)
                return Ok(new List<object>()); // not hired — empty sidebar

            var sidebar = await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId);
            return Ok(sidebar);
        }

        /// <summary>
        /// Set a user-specific permission override with ALLOW / DENY / INHERIT.
        /// DENY short-circuits ALL other rules — even if role says ALLOW.
        /// Body: { "status": "DENY", "reason": "Suspended by manager" }
        /// </summary>
        [HttpPut("staff/{staffId:guid}/overrides/{featureKey}")]
        public async Task<IActionResult> SetOverride(
            Guid staffId, string featureKey, [FromBody] SetOverrideDto dto)
        {
            if (!Enum.TryParse<PermissionStatus>(dto.Status, true, out var status))
                return BadRequest(new { message = $"Invalid status '{dto.Status}'. Use ALLOW, DENY, or INHERIT." });

            var (ok, msg) = await _rbac.SetUserOverrideAsync(
                staffId, featureKey, status, CurrentUserId, dto.Reason);

            if (!ok) return msg.Contains("not found")
                ? NotFound(new { message = msg })
                : BadRequest(new { message = msg });

            return Ok(new { message = msg });
        }

        /// <summary>
        /// Remove a user-specific override — reverts to role default.
        /// </summary>
        [HttpDelete("staff/{staffId:guid}/overrides/{featureKey}")]
        public async Task<IActionResult> RemoveOverride(Guid staffId, string featureKey)
        {
            var (ok, msg) = await _rbac.RemoveUserOverrideAsync(staffId, featureKey);
            return ok ? Ok(new { message = msg }) : NotFound(new { message = msg });
        }

        // ── Menu bundle access (admin grants sidebar section + child features) ─

        /// <summary>
        /// Menu tree with all permission keys per item (for admin access UI).
        /// </summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("menu-permissions")]
        public async Task<IActionResult> GetMenuPermissionTree() =>
            Ok(await _rbac.GetMenuPermissionTreeAsync());

        /// <summary>
        /// Grant a user access to a sidebar menu and all child feature keys.
        /// Example: menuId for "Accounts &amp; Groups" grants DEPT_VIEW etc. for all children.
        /// </summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("staff/{staffId:guid}/grant-menu/{menuId:int}")]
        public async Task<IActionResult> GrantMenuAccess(
            Guid staffId, int menuId, [FromBody] GrantMenuAccessDto? dto)
        {
            var (ok, msg, keys) = await _rbac.GrantMenuAccessAsync(
                staffId, menuId, CurrentUserId, dto?.Reason);

            return ok
                ? Ok(new { message = msg, grantedKeys = keys, menuId, staffId })
                : BadRequest(new { message = msg });
        }

        /// <summary>
        /// Revoke menu-bundle overrides (reverts to role / matrix defaults).
        /// </summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("staff/{staffId:guid}/revoke-menu/{menuId:int}")]
        public async Task<IActionResult> RevokeMenuAccess(Guid staffId, int menuId)
        {
            var (ok, msg, keys) = await _rbac.RevokeMenuAccessAsync(staffId, menuId);
            return ok
                ? Ok(new { message = msg, revokedKeys = keys, menuId, staffId })
                : BadRequest(new { message = msg });
        }

        /// <summary>
        /// Preview feature keys that would be granted for a menu subtree.
        /// </summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("menus/{menuId:int}/feature-keys")]
        public async Task<IActionResult> GetMenuFeatureKeys(int menuId) =>
            Ok(new { menuId, featureKeys = await _rbac.GetMenuFeatureKeysAsync(menuId) });

        // ── Seed Features ─────────────────────────────────────────────────────

        /// <summary>
        /// Seed all system feature keys into the Features table.
        /// Must be called BEFORE saving RolePermissions — FK requires features to exist.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        [HttpPost("seed-features")]
        [AllowAnonymous]
        public async Task<IActionResult> SeedFeatures()
        {
            var features = new List<Feature>
            {
                // ── Organization ──────────────────────────────────────────────
                new() { FeatureKey = "DEPT_VIEW",            FeatureName = "View Department",         Module = "Organization" },
                new() { FeatureKey = "DEPT_VIEW_ALL",        FeatureName = "View All Departments",    Module = "Organization" },
                new() { FeatureKey = "DEPT_CREATE",          FeatureName = "Create Department",       Module = "Organization" },
                new() { FeatureKey = "DEPT_EDIT",            FeatureName = "Edit Department",         Module = "Organization" },
                new() { FeatureKey = "DEPT_DELETE",          FeatureName = "Delete Department",       Module = "Organization" },

                // ── Vacancy ───────────────────────────────────────────────────
                new() { FeatureKey = "VACANCY_VIEW",         FeatureName = "View Vacancies",          Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_CREATE",       FeatureName = "Create Vacancy",          Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_EDIT",         FeatureName = "Edit Vacancy",            Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_DELETE",       FeatureName = "Delete Vacancy",          Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_ASSIGN",       FeatureName = "Assign Staff to Vacancy", Module = "Vacancy" },

                // ── Employee ──────────────────────────────────────────────────
                new() { FeatureKey = "EMPLOYEE_VIEW",        FeatureName = "View Employees",          Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_VIEW_ALL",    FeatureName = "View All Employees",      Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_EDIT",        FeatureName = "Edit Employee",           Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_DELETE",      FeatureName = "Delete Employee",         Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_TRANSFER",    FeatureName = "Transfer Employee",       Module = "Employee" },

                // ── Person ────────────────────────────────────────────────────
                new() { FeatureKey = "PERSON_VIEW",          FeatureName = "View Persons",            Module = "Person" },
                new() { FeatureKey = "PERSON_VIEW_ALL",      FeatureName = "View All Persons",        Module = "Person" },
                new() { FeatureKey = "PERSON_REGISTER",      FeatureName = "Register Person",         Module = "Person" },
                new() { FeatureKey = "PERSON_EDIT",          FeatureName = "Edit Person",             Module = "Person" },
                new() { FeatureKey = "PERSON_DELETE",        FeatureName = "Delete Person",           Module = "Person" },
                new() { FeatureKey = "PERSON_RESET_PASSWORD",FeatureName = "Reset Person Password",  Module = "Person" },

                // ── Access Groups ─────────────────────────────────────────────
                new() { FeatureKey = "ACCESS_GROUP_VIEW",    FeatureName = "View Access Groups",      Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_CREATE",  FeatureName = "Create Access Group",     Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_EDIT",    FeatureName = "Edit Access Group",       Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_DELETE",  FeatureName = "Delete Access Group",     Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_ASSIGN",  FeatureName = "Assign Group to Staff",   Module = "Access" },

                // ── Location ──────────────────────────────────────────────────
                new() { FeatureKey = "LOCATION_VIEW",        FeatureName = "View Locations",          Module = "Location" },
                new() { FeatureKey = "LOCATION_MANAGE",      FeatureName = "Manage Locations",        Module = "Location" },
            };

            var existingKeys = await _db.Features
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            var toAdd = features.Where(f => !existingKeys.Contains(f.FeatureKey)).ToList();

            if (toAdd.Any())
            {
                _db.Features.AddRange(toAdd);
                await _db.SaveChangesAsync();
            }

            return Ok(new
            {
                message  = $"Seed complete. {toAdd.Count} new features added, {existingKeys.Count} already existed.",
                added    = toAdd.Select(f => f.FeatureKey).ToList(),
                total    = existingKeys.Count + toAdd.Count
            });
        }
    }

    public class SetOverrideDto
    {
        /// <summary>ALLOW, DENY, or INHERIT</summary>
        public string  Status { get; set; } = "INHERIT";
        public string? Reason { get; set; }
    }
}
