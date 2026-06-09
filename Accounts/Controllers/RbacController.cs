using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
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
        private readonly IPersonAccessService _personAccess;

        public RbacController(RbacService rbac, ApplicationDbContext db, IPersonAccessService personAccess)
        {
            _rbac          = rbac;
            _db            = db;
            _personAccess  = personAccess;
        }

        private string? CurrentUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        private bool IsFullAccessUser =>
            User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

        // ── Admin: list all users with StaffId (for permission assignment UI) ─

        /// <summary>
        /// Returns all registered persons with their StaffId, name, email, loginId.
        /// Admin uses this to pick a user and then call PUT /overrides/{featureKey}.
        /// GET /api/rbac/users
        /// </summary>
        [HttpGet("users")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetAllUsers()
        {
            var persons = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .OrderBy(p => p.FullName)
                .Select(p => new
                {
                    personId       = p.PersonId,
                    identityUserId = p.IdentityUserId,
                    fullName       = p.FullName,
                    email          = p.Email,
                    photoUrl       = p.ProfilePhotoUrl,
                    isHired        = p.Staff != null,
                    staffId        = p.Staff != null ? p.Staff.StaffId : (Guid?)null,
                    loginId        = p.Staff != null ? p.Staff.LoginId : null,
                    jobTitle       = p.Staff != null && p.Staff.Vacancy != null ? p.Staff.Vacancy.JobTitle : null
                })
                .ToListAsync();

            return Ok(persons);
        }

        /// <summary>
        /// Get all current ALLOW overrides saved for a staff member, with feature details.
        /// Used by admin UI to show which permissions a user currently has.
        /// GET /api/rbac/staff/{staffId}/permissions-summary
        /// </summary>
        [HttpGet("staff/{staffId:guid}/permissions-summary")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetPermissionsSummary(Guid staffId)
        {
            // All features in the system
            var allFeatures = await _db.Features.AsNoTracking()
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .ToListAsync();

            // This user's explicit overrides
            var overrides = await _db.UserPermissionOverrides.AsNoTracking()
                .Include(u => u.Feature)
                .Where(u => u.StaffId == staffId)
                .ToDictionaryAsync(u => u.PermissionId, u => u);

            var result = allFeatures.Select(f =>
            {
                overrides.TryGetValue(f.PermissionId, out var ov);
                return new
                {
                    featureKey  = f.FeatureKey,
                    featureName = f.FeatureName,
                    module      = f.Module,
                    status      = ov?.Status ?? "INHERIT",
                    reason      = ov?.Reason,
                    updatedAt   = ov?.SetDate,
                    hasOverride = ov != null
                };
            });

            return Ok(new { staffId, permissions = result });
        }

        /// <summary>
        /// Bulk-save permissions for a user from the admin UI.
        /// Send { "featureKey": "ALLOW"|"DENY"|"INHERIT" } for each feature to update.
        /// POST /api/rbac/staff/{staffId}/bulk-overrides
        /// </summary>
        [HttpPost("staff/{staffId:guid}/bulk-overrides")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> BulkSetOverrides(
            Guid staffId,
            [FromBody] Dictionary<string, string> overrides)
        {
            if (overrides == null || overrides.Count == 0)
                return BadRequest(new { message = "No overrides provided." });

            var (saved, skipped, message) = await _rbac.BulkApplyOverridesAsync(
                staffId, overrides, CurrentUserId);

            if (message == "Staff not found.")
                return NotFound(new { message });

            return Ok(new { message, saved, skipped });
        }

        /// <summary>
        /// Wipe all UserPermissionOverrides for a staff member (Revoke All — one fast DB trip).
        /// POST /api/rbac/staff/{staffId}/clear-overrides
        /// </summary>
        [HttpPost("staff/{staffId:guid}/clear-overrides")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> ClearStaffOverrides(Guid staffId)
        {
            if (!await _db.StaffVacancies.AsNoTracking().AnyAsync(s => s.StaffId == staffId))
                return NotFound(new { message = "Staff not found." });

            var cleared = await _rbac.ClearStaffOverridesAsync(staffId);
            return Ok(new { message = $"Cleared {cleared} override(s).", cleared });
        }

        // ── HasAccess check ───────────────────────────────────────────────────

        /// <summary>
        /// Check if a staff member has access to a specific feature.
        /// Resolution: UserOverride → RoleDefault → Matrix → false
        /// </summary>
        [HttpGet("staff/{staffId:guid}/has-access/{*featureKey}")]
        public async Task<IActionResult> HasAccess(Guid staffId, string featureKey)
        {
            var key = ResolveFeatureKey(featureKey) ?? featureKey;
            return Ok(new
            {
                staffId,
                featureKey = key,
                hasAccess = await _rbac.HasAccessAsync(staffId, key)
            });
        }

        /// <summary>Get all effective permissions for a staff member</summary>
        [HttpGet("staff/{staffId:guid}/effective-permissions")]
        public async Task<IActionResult> GetEffectivePermissions(Guid staffId) =>
            Ok(await _rbac.GetEffectivePermissionsDetailedAsync(staffId));

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

        /// <summary>Get all user-specific overrides for a staff member (flat DTO — no EF cycles).</summary>
        [HttpGet("staff/{staffId:guid}/overrides")]
        public async Task<IActionResult> GetOverrides(Guid staffId)
        {
            var safeOverrides = await _db.UserPermissionOverrides
                .AsNoTracking()
                .Include(u => u.Feature)
                .Where(u => u.StaffId == staffId)
                .Select(u => new
                {
                    permissionId = u.PermissionId,
                    featureKey   = u.Feature != null ? u.Feature.FeatureKey : string.Empty,
                    featureName  = u.Feature != null ? u.Feature.FeatureName : string.Empty,
                    module       = u.Feature != null ? u.Feature.Module : string.Empty,
                    status       = u.Status,
                    reason       = u.Reason,
                    setDate      = u.SetDate
                })
                .ToListAsync();

            return Ok(safeOverrides);
        }

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

            if (person == null)
                return Ok(new List<object>());

            if (await _personAccess.HasPersonGrantsAsync(person.PersonId))
                return Ok(await _personAccess.GetGrantedSidebarAsync(person.PersonId));

            if (person.Staff == null)
                return Ok(new List<object>());

            var sidebar = await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId);
            return Ok(sidebar);
        }

        /// <summary>
        /// Set a user-specific permission override with ALLOW / DENY / INHERIT.
        /// featureKey in URL path (URL-encoded) or query ?featureKey=DEPT_VIEW
        /// </summary>
        [HttpPut("staff/{staffId:guid}/overrides/{*featureKey}")]
        [HttpPut("staff/{staffId:guid}/overrides")]
        public async Task<IActionResult> SetOverride(
            Guid staffId,
            string? featureKey,
            [FromQuery] string? key,
            [FromBody] SetOverrideDto dto)
        {
            var resolvedKey = ResolveFeatureKey(featureKey ?? key);
            if (string.IsNullOrWhiteSpace(resolvedKey))
                return BadRequest(new { message = "featureKey is required in route or query string." });

            if (!Enum.TryParse<PermissionStatus>(dto.Status, true, out var status))
                return BadRequest(new { message = $"Invalid status '{dto.Status}'. Use ALLOW, DENY, or INHERIT." });

            var (ok, msg) = await _rbac.SetUserOverrideAsync(
                staffId, resolvedKey, status, CurrentUserId, dto.Reason);

            if (!ok) return msg.Contains("not found")
                ? NotFound(new { message = msg })
                : BadRequest(new { message = msg });

            return Ok(new { message = msg, staffId, featureKey = resolvedKey, status = status.ToString() });
        }

        /// <summary>
        /// Remove a user-specific override — reverts to role default.
        /// </summary>
        [HttpDelete("staff/{staffId:guid}/overrides/{*featureKey}")]
        [HttpDelete("staff/{staffId:guid}/overrides")]
        public async Task<IActionResult> RemoveOverride(
            Guid staffId,
            string? featureKey,
            [FromQuery] string? key)
        {
            var resolvedKey = ResolveFeatureKey(featureKey ?? key);
            if (string.IsNullOrWhiteSpace(resolvedKey))
                return BadRequest(new { message = "featureKey is required in route or query string." });

            var (ok, msg) = await _rbac.RemoveUserOverrideAsync(staffId, resolvedKey);
            return ok
                ? Ok(new { message = msg, staffId, featureKey = resolvedKey })
                : NotFound(new { message = msg });
        }

        private static string? ResolveFeatureKey(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Uri.UnescapeDataString(raw.Trim()).Trim('/');
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
            var personId = await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.StaffId == staffId)
                .Select(s => s.PersonId)
                .FirstOrDefaultAsync();

            if (!personId.HasValue)
                return BadRequest(new { message = "Staff is not linked to a person record." });

            var (ok, msg, menuIds, keys) = await _personAccess.GrantMenuAsync(
                personId.Value, menuId, CurrentUserId, dto?.Reason);

            return ok
                ? Ok(new { message = msg, grantedMenuIds = menuIds, grantedFeatureKeys = keys, menuId, staffId, personId })
                : BadRequest(new { message = msg });
        }

        /// <summary>Grant menu + features by PersonId (preferred — saves PersonMenus + PersonFeatures).</summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("persons/{personId:guid}/grant-menu/{menuId:int}")]
        public async Task<IActionResult> GrantMenuToPerson(
            Guid personId, int menuId, [FromBody] GrantMenuAccessDto? dto)
        {
            var (ok, msg, menuIds, keys) = await _personAccess.GrantMenuAsync(
                personId, menuId, CurrentUserId, dto?.Reason);

            return ok
                ? Ok(new { message = msg, grantedMenuIds = menuIds, grantedFeatureKeys = keys, personId, menuId })
                : BadRequest(new { message = msg });
        }

        /// <summary>View menus and features granted to a person.</summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("persons/{personId:guid}/access")]
        public async Task<IActionResult> GetPersonAccess(Guid personId) =>
            Ok(await _personAccess.GetPersonAccessSummaryAsync(personId));

        /// <summary>
        /// Revoke menu-bundle (PersonMenus + PersonFeatures).
        /// </summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("staff/{staffId:guid}/revoke-menu/{menuId:int}")]
        public async Task<IActionResult> RevokeMenuAccess(Guid staffId, int menuId)
        {
            var personId = await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.StaffId == staffId)
                .Select(s => s.PersonId)
                .FirstOrDefaultAsync();

            if (!personId.HasValue)
                return BadRequest(new { message = "Staff is not linked to a person record." });

            var (ok, msg) = await _personAccess.RevokeMenuAsync(personId.Value, menuId);
            return ok
                ? Ok(new { message = msg, menuId, staffId, personId })
                : BadRequest(new { message = msg });
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("persons/{personId:guid}/revoke-menu/{menuId:int}")]
        public async Task<IActionResult> RevokeMenuFromPerson(Guid personId, int menuId)
        {
            var (ok, msg) = await _personAccess.RevokeMenuAsync(personId, menuId);
            return ok ? Ok(new { message = msg, personId, menuId }) : BadRequest(new { message = msg });
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
        /// Seed MENU_{id}, MENU_{id}_VIEW/ADD/EDIT/DELETE into Features for every active menu.
        /// Also seeds static system feature keys (DEPT_VIEW, EMPLOYEE_VIEW, etc.).
        /// Safe to call multiple times — idempotent.
        /// POST /api/rbac/seed-features
        /// </summary>
        [HttpPost("seed-features")]
        [AllowAnonymous]
        public async Task<IActionResult> SeedFeatures()
        {
            // ── 1. Seed MENU_{id} keys from the Menus table ───────────────────
            var (menuAdded, menuSkipped) = await _rbac.SeedMenuFeaturesAsync();

            // ── 2. Seed static system feature keys ────────────────────────────
            var staticFeatures = new List<Feature>
            {
                // Organization
                new() { FeatureKey = "DEPT_VIEW",             FeatureName = "View Department",          Module = "Organization" },
                new() { FeatureKey = "DEPT_VIEW_ALL",         FeatureName = "View All Departments",     Module = "Organization" },
                new() { FeatureKey = "DEPT_CREATE",           FeatureName = "Create Department",        Module = "Organization" },
                new() { FeatureKey = "DEPT_EDIT",             FeatureName = "Edit Department",          Module = "Organization" },
                new() { FeatureKey = "DEPT_DELETE",           FeatureName = "Delete Department",        Module = "Organization" },
                // Vacancy
                new() { FeatureKey = "VACANCY_VIEW",          FeatureName = "View Vacancies",           Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_CREATE",        FeatureName = "Create Vacancy",           Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_EDIT",          FeatureName = "Edit Vacancy",             Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_DELETE",        FeatureName = "Delete Vacancy",           Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_ASSIGN",        FeatureName = "Assign Staff to Vacancy",  Module = "Vacancy" },
                // Employee
                new() { FeatureKey = "EMPLOYEE_VIEW",         FeatureName = "View Employees",           Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_VIEW_ALL",     FeatureName = "View All Employees",       Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_EDIT",         FeatureName = "Edit Employee",            Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_DELETE",       FeatureName = "Delete Employee",          Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_TRANSFER",     FeatureName = "Transfer Employee",        Module = "Employee" },
                // Person
                new() { FeatureKey = "PERSON_VIEW",           FeatureName = "View Persons",             Module = "Person" },
                new() { FeatureKey = "PERSON_VIEW_ALL",       FeatureName = "View All Persons",         Module = "Person" },
                new() { FeatureKey = "PERSON_REGISTER",       FeatureName = "Register Person",          Module = "Person" },
                new() { FeatureKey = "PERSON_EDIT",           FeatureName = "Edit Person",              Module = "Person" },
                new() { FeatureKey = "PERSON_DELETE",         FeatureName = "Delete Person",            Module = "Person" },
                new() { FeatureKey = "PERSON_RESET_PASSWORD", FeatureName = "Reset Person Password",   Module = "Person" },
                // Access Groups
                new() { FeatureKey = "ACCESS_GROUP_VIEW",     FeatureName = "View Access Groups",       Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_CREATE",   FeatureName = "Create Access Group",      Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_EDIT",     FeatureName = "Edit Access Group",        Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_DELETE",   FeatureName = "Delete Access Group",      Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_ASSIGN",   FeatureName = "Assign Group to Staff",    Module = "Access" },
                // Location
                new() { FeatureKey = "LOCATION_VIEW",         FeatureName = "View Locations",           Module = "Location" },
                new() { FeatureKey = "LOCATION_MANAGE",       FeatureName = "Manage Locations",         Module = "Location" },
            };

            var existingKeys = await _db.Features.Select(f => f.FeatureKey).ToHashSetAsync();
            var staticToAdd  = staticFeatures.Where(f => !existingKeys.Contains(f.FeatureKey)).ToList();

            if (staticToAdd.Any())
            {
                _db.Features.AddRange(staticToAdd);
                await _db.SaveChangesAsync();
            }

            // ── 3. Link menus to their MENU_{id} features ─────────────────────
            var linkCount = await LinkMenusToFeaturesAsync();

            return Ok(new
            {
                message      = "Seed complete.",
                menuFeatures = new { added = menuAdded, skipped = menuSkipped },
                staticFeatures = new { added = staticToAdd.Count, skipped = existingKeys.Count },
                menuPermissionsLinked = linkCount,
                totalFeatures  = existingKeys.Count + menuAdded + staticToAdd.Count,
                nextStep       = linkCount > 0 
                    ? "Menus are now linked to features. Grant access via: PUT /api/rbac/staff/{staffId}/overrides/{featureKey}" 
                    : "Use POST /api/rbac/link-menus-to-features to establish menu-feature relationships."
            });
        }

        /// <summary>
        /// Links active menus to their corresponding MENU_{id} features in MenuPermissions table.
        /// This is required for menus to show up in the sidebar after permissions are granted.
        /// POST /api/rbac/link-menus-to-features
        /// </summary>
        [HttpPost("link-menus-to-features")]
        [AllowAnonymous]
        public async Task<IActionResult> LinkMenusToFeatures()
        {
            var count = await LinkMenusToFeaturesAsync();
            return Ok(new
            {
                message = $"Linked {count} menus to their features in MenuPermissions table.",
                count,
                nextStep = count > 0 
                    ? "Users will now see menus they have permission for." 
                    : "All menus are already linked. If users still don't see menus, check UserPermissionOverrides table."
            });
        }

        /// <summary>
        /// Helper method to link menus to features via MenuPermissions table.
        /// Ensures that when admin grants MENU_{id} permission, the menu actually appears.
        /// </summary>
        private async Task<int> LinkMenusToFeaturesAsync()
        {
            // Get all active menus
            var activeMenus = await _db.Menus
                .Where(m => m.IsActive)
                .Select(m => m.Id)
                .ToListAsync();

            // Get Features table lookup: MENU_id → PermissionId
            var menuFeatures = await _db.Features
                .Where(f => f.FeatureKey.StartsWith("MENU_") && !f.FeatureKey.Contains("_VIEW") 
                         && !f.FeatureKey.Contains("_ADD") && !f.FeatureKey.Contains("_EDIT") 
                         && !f.FeatureKey.Contains("_DELETE"))
                .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            // Get existing MenuPermissions to avoid duplicates
            var existingLinks = await _db.MenuPermissions
                .Select(mp => new { mp.MenuId, mp.PermissionId })
                .ToHashSetAsync();

            var added = 0;
            foreach (var menuId in activeMenus)
            {
                var featureKey = $"MENU_{menuId}";
                if (menuFeatures.TryGetValue(featureKey, out int permissionId))
                {
                    // Check if link already exists
                    if (!existingLinks.Contains(new { MenuId = menuId, PermissionId = permissionId }))
                    {
                        _db.MenuPermissions.Add(new MenuPermission
                        {
                            MenuId = menuId,
                            PermissionId = permissionId
                        });
                        added++;
                    }
                }
            }

            if (added > 0)
            {
                await _db.SaveChangesAsync();
            }

            return added;
        }
    }

    public class SetOverrideDto
    {
        /// <summary>ALLOW, DENY, or INHERIT</summary>
        public string  Status { get; set; } = "INHERIT";
        public string? Reason { get; set; }
    }
}
