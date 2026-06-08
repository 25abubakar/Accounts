
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
        private readonly RbacService _rbac;
        private readonly ApplicationDbContext _db;

        public RbacController(RbacService rbac, ApplicationDbContext db)
        {
            _rbac = rbac;
            _db = db;
        }

        private string? CurrentUserId =>
          User.FindFirstValue(ClaimTypes.NameIdentifier);

        private bool IsFullAccessUser =>
          User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

        // ── Admin: list all users ─────────────────────────────────────────────

        /// <summary>
        /// Returns all registered persons with their StaffId, name, email, loginId.
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
                  personId = p.PersonId,
                  identityUserId = p.IdentityUserId,
                  fullName = p.FullName,
                  email = p.Email,
                  photoUrl = p.ProfilePhotoUrl,
                  isHired = p.Staff != null,
                  staffId = p.Staff != null ? p.Staff.StaffId : (Guid?)null,
                  loginId = p.Staff != null ? p.Staff.LoginId : null,
                  jobTitle = p.Staff != null && p.Staff.Vacancy != null ? p.Staff.Vacancy.JobTitle : null
              })
              .ToListAsync();

            return Ok(persons);
        }

        // ── Permissions summary ───────────────────────────────────────────────

        /// <summary>
        /// Get the full permission summary for a staff member, merged with role defaults.
        /// Shows effective status (UserAllow / UserDeny / RoleDefault / Denied) per feature.
        /// GET /api/rbac/staff/{staffId}/permissions-summary
        /// </summary>
        [HttpGet("staff/{staffId:guid}/permissions-summary")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetPermissionsSummary(Guid staffId)
        {
            var allFeatures = await _db.Features.AsNoTracking()
              .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
              .ToListAsync();

            var overrides = await _db.UserPermissionOverrides.AsNoTracking()
              .Include(u => u.Feature)
              .Where(u => u.StaffId == staffId)
              .ToDictionaryAsync(u => u.PermissionId, u => u);

            var result = allFeatures.Select(f =>
            {
                overrides.TryGetValue(f.PermissionId, out var ov);
                return new
                {
                    featureKey = f.FeatureKey,
                    featureName = f.FeatureName,
                    module = f.Module,
                    status = ov?.Status ?? "INHERIT",
                    reason = ov?.Reason,
                    updatedAt = ov?.SetDate,
                    hasOverride = ov != null
                };
            });

            return Ok(new { staffId, permissions = result });
        }

        /// <summary>
        /// Bulk-save permission overrides for a user from the admin UI.
        /// Writes ONLY to UserPermissionOverrides — the single authoritative write path.
        ///
        /// Body: { "EMPLOYEE_EDIT": "ALLOW", "PERSON_DELETE": "DENY", "DEPT_VIEW": "INHERIT" }
        ///   ALLOW   → explicit ALLOW override
        ///   DENY    → explicit DENY override
        ///   INHERIT → remove any existing override (reverts to role default)
        ///
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

            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return NotFound(new { message = $"Staff {staffId} not found." });

            // Map all submitted feature keys to their PermissionId in one query
            var featureKeys = overrides.Keys.ToList();
            var featureMap = await _db.Features.AsNoTracking()
              .Where(f => featureKeys.Contains(f.FeatureKey))
              .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            // Load all existing override rows for this staff member (tracked for updates)
            var existingOverrides = await _db.UserPermissionOverrides
        .Where(u => u.StaffId == staffId)
        .ToDictionaryAsync(u => u.PermissionId);

            int saved = 0, skipped = 0;

            foreach (var (featureKey, statusStr) in overrides)
            {
                if (!featureMap.TryGetValue(featureKey, out int permId)) { skipped++; continue; }
                if (!Enum.TryParse<PermissionStatus>(statusStr, true, out var status)) { skipped++; continue; }

                if (status == PermissionStatus.INHERIT)
                {
                    // INHERIT = remove the override row entirely → reverts to role default
                    if (existingOverrides.TryGetValue(permId, out var toRemove))
                    {
                        _db.UserPermissionOverrides.Remove(toRemove);
                        saved++;
                    }
                }
                else
                {
                    // ALLOW or DENY — upsert the override row
                    if (existingOverrides.TryGetValue(permId, out var existing))
                    {
                        existing.Status = status.ToString();
                        existing.SetBy = CurrentUserId;
                        existing.SetDate = DateTime.UtcNow;
                    }
                    else
                    {
                        _db.UserPermissionOverrides.Add(new UserPermissionOverride
                        {
                            StaffId = staffId,
                            PermissionId = permId,
                            Status = status.ToString(),
                            SetBy = CurrentUserId,
                            SetDate = DateTime.UtcNow,
                            Reason = "Set by admin"
                        });
                    }
                    saved++;
                }
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = $"{saved} permission(s) saved, {skipped} skipped (unknown feature keys or invalid status).",
                staffId,
                saved,
                skipped
            });
        }

        // ── HasAccess check ───────────────────────────────────────────────────

        /// <summary>
        /// Check if a staff member has access to a specific feature.
        /// Resolution: UserOverride → RoleDefault → false
        /// GET /api/rbac/staff/{staffId}/has-access/{featureKey}
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

        /// <summary>Get all effective permissions for a staff member (detailed view)</summary>
        [HttpGet("staff/{staffId:guid}/effective-permissions")]
        public async Task<IActionResult> GetEffectivePermissions(Guid staffId) =>
      Ok(await _rbac.GetEffectivePermissionsDetailedAsync(staffId));

        // ── Sidebar ───────────────────────────────────────────────────────────

        /// <summary>
        /// Get the sidebar menu filtered by the logged-in user's permissions.
        /// Menu items the user cannot access are removed. Empty groups are pruned.
        /// GET /api/rbac/sidebar
        /// </summary>
        [HttpGet("sidebar")]
        public async Task<IActionResult> GetFilteredSidebar()
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Not authenticated." });

            if (IsFullAccessUser)
                return Ok(await _rbac.GetFilteredSidebarAsync(Guid.Empty));

            var person = await _db.Persons
              .AsNoTracking()
              .Include(p => p.Staff)
              .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);

            if (person?.Staff == null)
                return Ok(new List<object>());

            return Ok(await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId));
        }

        // ── Department Matrix ─────────────────────────────────────────────────

        /// <summary>
        /// Get the full permission matrix for a department.
        /// Each cell shows: effectiveAccess, source (UserOverride/RoleDefault/Denied), hasUserOverride.
        /// GET /api/rbac/matrix/{deptId}
        /// </summary>
        [HttpGet("matrix/{deptId:int}")]
        public async Task<IActionResult> GetMatrix(int deptId)
        {
            var features = await _db.Features.AsNoTracking()
              .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
              .ToListAsync();

            var staffInDept = await _db.StaffVacancies
              .AsNoTracking()
              .Include(s => s.Person)
              .Include(s => s.Vacancy)
              .Where(s => s.Vacancy != null && s.Vacancy.OrganizationId == deptId)
              .OrderBy(s => s.Person != null ? s.Person.FullName : "")
              .ToListAsync();

            var staffIds = staffInDept.Select(s => s.StaffId).ToHashSet();
            var jobTitles = staffInDept.Select(s => s.Vacancy?.JobTitle).Where(j => j != null).Distinct().ToList();

            var allOverrides = await _db.UserPermissionOverrides.AsNoTracking()
              .Where(u => staffIds.Contains(u.StaffId))
              .ToListAsync();

            var allRolePerms = await _db.RolePermissions.AsNoTracking()
              .Where(r => jobTitles.Contains(r.JobTitle) && (r.DeptId == null || r.DeptId == deptId))
              .ToListAsync();

            object ResolveCell(Guid sid, string? jt, int? sDeptId, int permId)
            {
                var uo = allOverrides.FirstOrDefault(u => u.StaffId == sid && u.PermissionId == permId);
                if (uo != null)
                {
                    if (uo.Status == nameof(PermissionStatus.DENY)) return new { effectiveAccess = false, source = "UserDeny", hasUserOverride = true };
                    if (uo.Status == nameof(PermissionStatus.ALLOW)) return new { effectiveAccess = true, source = "UserAllow", hasUserOverride = true };
                }
                if (jt != null)
                {
                    var rp = allRolePerms.FirstOrDefault(r => r.JobTitle == jt && r.DeptId == sDeptId && r.PermissionId == permId);
                    if (rp != null) return new { effectiveAccess = rp.IsAllowed, source = "RoleDefault", hasUserOverride = false };

                    var rpG = allRolePerms.FirstOrDefault(r => r.JobTitle == jt && r.DeptId == null && r.PermissionId == permId);
                    if (rpG != null) return new { effectiveAccess = rpG.IsAllowed, source = "RoleDefault", hasUserOverride = false };
                }
                return new { effectiveAccess = false, source = "Denied", hasUserOverride = false };
            }

            var grid = staffInDept.Select(s => new
            {
                staffId = s.StaffId,
                personId = s.PersonId,
                fullName = s.Person?.FullName ?? "-",

                loginId = s.LoginId ?? "-",
                jobTitle = s.Vacancy?.JobTitle ?? "-",
                permissions = features.Select(f => new
                {
                    f.FeatureKey,
                    f.FeatureName,
                    f.Module,
                    access = ResolveCell(s.StaffId, s.Vacancy?.JobTitle, s.Vacancy?.OrganizationId, f.PermissionId)
                }).ToList()
            }).ToList();

            return Ok(new
            {
                deptId,
                totalStaff = grid.Count,
                features = features.Select(f => new { f.FeatureKey, f.FeatureName, f.Module }).ToList(),
                staff = grid
            });
        }

        // ── Role Permissions ──────────────────────────────────────────────────

        /// <summary>
        /// Get default permissions for a job title.
        /// GET /api/rbac/roles/{jobTitle}/permissions?deptId=4
        /// </summary>
        [HttpGet("roles/{jobTitle}/permissions")]
        public async Task<IActionResult> GetRolePermissions(
      string jobTitle, [FromQuery] int? deptId) =>
      Ok(await _rbac.GetRolePermissionsAsync(jobTitle, deptId));

        /// <summary>
        /// Set default permissions for a job title in a department.
        /// Body: { "ATTENDANCE_VIEW": true, "EMPLOYEE_EDIT": false }
        /// PUT /api/rbac/roles/{jobTitle}/permissions
        /// </summary>
        [HttpPut("roles/{jobTitle}/permissions")]
        public async Task<IActionResult> SetRolePermissions(
      string jobTitle,
      [FromQuery] int? deptId,
      [FromBody] Dictionary<string, bool> permissions)
        {
            if (permissions == null || !permissions.Any())
                return BadRequest(new { message = "No permissions provided." });

            var featuresExist = await _db.Features.AnyAsync();
            if (!featuresExist)
                return BadRequest(new
                {
                    message = "Features table is empty. Seed features first via POST /api/rbac/seed-features.",
                    hint = "RolePermissions.PermissionId has a FK to Features table — the key must exist there first."
                });

            var (count, invalidKeys) = await _rbac.SetRolePermissionsAsync(
              jobTitle, deptId, permissions, CurrentUserId);

            if (count == 0 && invalidKeys.Any())
                return BadRequest(new
                {
                    message = "No permissions saved. All provided FeatureKeys are invalid.",
                    invalidKeys,
                    hint = "Use GET /api/access/features to see valid feature keys."
                });

            return Ok(new
            {
                message = $"{count} role permissions saved for '{jobTitle}'.",
                jobTitle,
                deptId,
                saved = count,
                invalidKeys = invalidKeys.Any() ? invalidKeys : null
            });
        }

        // ── User Overrides ────────────────────────────────────────────────────

        /// <summary>Get all user-specific overrides for a staff member</summary>
        [HttpGet("staff/{staffId:guid}/overrides")]
        public async Task<IActionResult> GetOverrides(Guid staffId) =>
      Ok(await _rbac.GetUserOverridesAsync(staffId));

        /// <summary>
        /// Set a user-specific permission override with ALLOW / DENY / INHERIT.
        /// PUT /api/rbac/staff/{staffId}/overrides/{featureKey}
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
        /// DELETE /api/rbac/staff/{staffId}/overrides/{featureKey}
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

        // ── Menu bundle access ────────────────────────────────────────────────

        /// <summary>
        /// Menu tree with all required permission keys per item (for admin access UI).
        /// GET /api/rbac/menu-permissions
        /// </summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("menu-permissions")]
        public async Task<IActionResult> GetMenuPermissionTree() =>
      Ok(await _rbac.GetMenuPermissionTreeAsync());

        /// <summary>
        /// Grant a user access to a sidebar menu and all its child feature keys
        /// by writing ALLOW UserPermissionOverrides for all linked features.
        /// POST /api/rbac/staff/{staffId}/grant-menu/{menuId}
        /// </summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("staff/{staffId:guid}/grant-menu/{menuId:int}")]
        public async Task<IActionResult> GrantMenuAccess(
      Guid staffId, int menuId, [FromBody] GrantMenuAccessDto? dto)
        {
            var (ok, msg, keys) = await _rbac.GrantMenuAccessAsync(
              staffId, menuId, CurrentUserId, dto?.Reason);

            return ok
              ? Ok(new { message = msg, grantedFeatureKeys = keys, menuId, staffId })
              : BadRequest(new { message = msg });
        }

        /// <summary>
        /// Revoke menu access — removes UserPermissionOverride ALLOW rows for menu features.
        /// POST /api/rbac/staff/{staffId}/revoke-menu/{menuId}
        /// </summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("staff/{staffId:guid}/revoke-menu/{menuId:int}")]
        public async Task<IActionResult> RevokeMenuAccess(Guid staffId, int menuId)
        {
            var (ok, msg, keys) = await _rbac.RevokeMenuAccessAsync(staffId, menuId);
            return ok
              ? Ok(new { message = msg, revokedFeatureKeys = keys, menuId, staffId })
              : BadRequest(new { message = msg });
        }

        /// <summary>Preview feature keys that would be granted for a menu subtree.</summary>
        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("menus/{menuId:int}/feature-keys")]
        public async Task<IActionResult> GetMenuFeatureKeys(int menuId) =>
      Ok(new { menuId, featureKeys = await _rbac.GetMenuFeatureKeysAsync(menuId) });

        // ── Seed Features ─────────────────────────────────────────────────────

        /// <summary>
        /// Seed MENU_{id}, MENU_{id}_VIEW/ADD/EDIT/DELETE into Features for every active menu.
        /// Also seeds static system feature keys. Idempotent.
        /// POST /api/rbac/seed-features
        /// </summary>
        [HttpPost("seed-features")]
        [AllowAnonymous]
        public async Task<IActionResult> SeedFeatures()
        {
            var (menuAdded, menuSkipped) = await _rbac.SeedMenuFeaturesAsync();

            var staticFeatures = new List<Feature>
      {
                // Organization
                new() { FeatureKey = "DEPT_VIEW",      FeatureName = "View Department",     Module = "Organization" },
        new() { FeatureKey = "DEPT_VIEW_ALL",    FeatureName = "View All Departments",  Module = "Organization" },
        new() { FeatureKey = "DEPT_CREATE",     FeatureName = "Create Department",    Module = "Organization" },
        new() { FeatureKey = "DEPT_EDIT",      FeatureName = "Edit Department",     Module = "Organization" },
        new() { FeatureKey = "DEPT_DELETE",     FeatureName = "Delete Department",    Module = "Organization" },
                // Vacancy
                new() { FeatureKey = "VACANCY_VIEW",     FeatureName = "View Vacancies",     Module = "Vacancy" },
        new() { FeatureKey = "VACANCY_CREATE",    FeatureName = "Create Vacancy",     Module = "Vacancy" },
        new() { FeatureKey = "VACANCY_EDIT",     FeatureName = "Edit Vacancy",      Module = "Vacancy" },
        new() { FeatureKey = "VACANCY_DELETE",    FeatureName = "Delete Vacancy",     Module = "Vacancy" },
        new() { FeatureKey = "VACANCY_ASSIGN",    FeatureName = "Assign Staff to Vacancy", Module = "Vacancy" },
                // Employee
                new() { FeatureKey = "EMPLOYEE_VIEW",    FeatureName = "View Employees",     Module = "Employee" },
        new() { FeatureKey = "EMPLOYEE_VIEW_ALL",  FeatureName = "View All Employees",   Module = "Employee" },
        new() { FeatureKey = "EMPLOYEE_EDIT",    FeatureName = "Edit Employee",      Module = "Employee" },
        new() { FeatureKey = "EMPLOYEE_DELETE",   FeatureName = "Delete Employee",     Module = "Employee" },
        new() { FeatureKey = "EMPLOYEE_TRANSFER",  FeatureName = "Transfer Employee",    Module = "Employee" },
                // Person
                new() { FeatureKey = "PERSON_VIEW",     FeatureName = "View Persons",      Module = "Person" },
        new() { FeatureKey = "PERSON_VIEW_ALL",   FeatureName = "View All Persons",    Module = "Person" },
        new() { FeatureKey = "PERSON_REGISTER",   FeatureName = "Register Person",     Module = "Person" },
        new() { FeatureKey = "PERSON_EDIT",     FeatureName = "Edit Person",       Module = "Person" },
        new() { FeatureKey = "PERSON_DELETE",    FeatureName = "Delete Person",      Module = "Person" },
        new() { FeatureKey = "PERSON_RESET_PASSWORD", FeatureName = "Reset Person Password", Module = "Person" },
                // Access
                new() { FeatureKey = "ACCESS_GROUP_VIEW",  FeatureName = "View Access Groups",   Module = "Access" },
        new() { FeatureKey = "ACCESS_GROUP_CREATE", FeatureName = "Create Access Group",   Module = "Access" },
        new() { FeatureKey = "ACCESS_GROUP_EDIT",  FeatureName = "Edit Access Group",    Module = "Access" },
        new() { FeatureKey = "ACCESS_GROUP_DELETE", FeatureName = "Delete Access Group",   Module = "Access" },
        new() { FeatureKey = "ACCESS_GROUP_ASSIGN", FeatureName = "Assign Group to Staff",  Module = "Access" },
                // Location
                new() { FeatureKey = "LOCATION_VIEW",    FeatureName = "View Locations",     Module = "Location" },
        new() { FeatureKey = "LOCATION_MANAGE",   FeatureName = "Manage Locations",    Module = "Location" },
      };

            var existingKeys = await _db.Features.Select(f => f.FeatureKey).ToHashSetAsync();
            var staticToAdd = staticFeatures.Where(f => !existingKeys.Contains(f.FeatureKey)).ToList();

            if (staticToAdd.Any())
            {
                _db.Features.AddRange(staticToAdd);
                await _db.SaveChangesAsync();
            }

            var linkCount = await SeedMenuFeatureLinksInternalAsync();

            return Ok(new
            {
                message = "Seed complete.",
                menuFeatures = new { added = menuAdded, skipped = menuSkipped },
                staticFeatures = new { added = staticToAdd.Count, skipped = existingKeys.Count },
                menuPermissionsLinked = linkCount
            });
        }

        /// <summary>
        /// Links active menus to their corresponding MENU_{id} features in MenuPermissions.
        /// POST /api/rbac/link-menus-to-features
        /// </summary>
        [HttpPost("link-menus-to-features")]
        [AllowAnonymous]
        public async Task<IActionResult> LinkMenusToFeatures()
        {
            var count = await SeedMenuFeatureLinksInternalAsync();
            return Ok(new { message = $"Linked {count} menus to their features.", count });
        }

        /// <summary>
        /// Seeds MenuPermissions table linking MENU_{id} and operational keys per menu.
        /// Idempotent.
        /// POST /api/rbac/seed-menu-feature-links
        /// </summary>
        [HttpPost("seed-menu-feature-links")]
        [AllowAnonymous]
        public async Task<IActionResult> SeedMenuFeatureLinks()
        {
            var added = await SeedMenuFeatureLinksInternalAsync();
            return Ok(new { message = $"Menu feature links seeded. {added} MenuPermissions rows added.", added });
        }

        private async Task<int> SeedMenuFeatureLinksInternalAsync()
        {
            var allMenus = await _db.Menus.AsNoTracking()
              .Where(m => m.IsActive).ToListAsync();

            var featureMap = await _db.Features.AsNoTracking()
              .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            var existingLinks = await _db.MenuPermissions
              .Select(mp => new { mp.MenuId, mp.PermissionId })
              .ToHashSetAsync();

            int added = 0;

            foreach (var menu in allMenus)
            {
                var menuKey = $"MENU_{menu.Id}";
                if (featureMap.TryGetValue(menuKey, out int menuPermId))
                {
                    var link1 = new { MenuId = menu.Id, PermissionId = menuPermId };
                    if (!existingLinks.Contains(link1))
                    {
                        _db.MenuPermissions.Add(new MenuPermission { MenuId = menu.Id, PermissionId = menuPermId });
                        existingLinks.Add(link1);
                        added++;
                    }
                }

                var opKeys = GetOperationalKeysForMenu(menu.Route, menu.Title);
                foreach (var opKey in opKeys)
                {
                    if (!featureMap.TryGetValue(opKey, out int opPermId)) continue;
                    var link2 = new { MenuId = menu.Id, PermissionId = opPermId };
                    if (!existingLinks.Contains(link2))
                    {
                        _db.MenuPermissions.Add(new MenuPermission { MenuId = menu.Id, PermissionId = opPermId });
                        existingLinks.Add(link2);
                        added++;
                    }
                }
            }

            if (added > 0) await _db.SaveChangesAsync();
            return added;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string? ResolveFeatureKey(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Uri.UnescapeDataString(raw.Trim()).Trim('/');
        }

        private static readonly Dictionary<string, string[]> _menuOperationalKeys
          = new(StringComparer.OrdinalIgnoreCase)
          {
              ["/hr/vacancies"] = new[] { "VACANCY_VIEW", "VACANCY_CREATE", "VACANCY_EDIT", "VACANCY_DELETE", "VACANCY_ASSIGN" },
              ["positions"] = new[] { "VACANCY_VIEW", "VACANCY_CREATE", "VACANCY_EDIT", "VACANCY_DELETE", "VACANCY_ASSIGN" },
              ["vacancies"] = new[] { "VACANCY_VIEW", "VACANCY_CREATE", "VACANCY_EDIT", "VACANCY_DELETE", "VACANCY_ASSIGN" },
              ["/hr/staff"] = new[] { "EMPLOYEE_VIEW", "EMPLOYEE_VIEW_ALL", "EMPLOYEE_EDIT", "EMPLOYEE_DELETE", "EMPLOYEE_TRANSFER" },
              ["/staff"] = new[] { "EMPLOYEE_VIEW", "EMPLOYEE_VIEW_ALL", "EMPLOYEE_EDIT", "EMPLOYEE_DELETE", "EMPLOYEE_TRANSFER" },
              ["staff members"] = new[] { "EMPLOYEE_VIEW", "EMPLOYEE_VIEW_ALL", "EMPLOYEE_EDIT", "EMPLOYEE_DELETE", "EMPLOYEE_TRANSFER" },
              ["employees"] = new[] { "EMPLOYEE_VIEW", "EMPLOYEE_VIEW_ALL", "EMPLOYEE_EDIT", "EMPLOYEE_DELETE", "EMPLOYEE_TRANSFER" },
              ["/hr/register"] = new[] { "PERSON_REGISTER", "VACANCY_VIEW", "VACANCY_ASSIGN", "PERSON_VIEW" },
              ["register person"] = new[] { "PERSON_REGISTER", "VACANCY_VIEW", "VACANCY_ASSIGN", "PERSON_VIEW" },
              ["/persons"] = new[] { "PERSON_VIEW", "PERSON_VIEW_ALL", "PERSON_REGISTER", "PERSON_EDIT", "PERSON_DELETE" },
              ["persons"] = new[] { "PERSON_VIEW", "PERSON_VIEW_ALL", "PERSON_REGISTER", "PERSON_EDIT", "PERSON_DELETE" },
              ["/org"] = new[] { "DEPT_VIEW", "DEPT_VIEW_ALL" },
              ["organization"] = new[] { "DEPT_VIEW", "DEPT_VIEW_ALL", "DEPT_CREATE", "DEPT_EDIT", "DEPT_DELETE" },
              ["org chart"] = new[] { "DEPT_VIEW", "DEPT_VIEW_ALL" },
              ["companies"] = new[] { "DEPT_VIEW", "DEPT_CREATE", "DEPT_EDIT", "DEPT_DELETE" },
              ["/groups/companies"] = new[] { "DEPT_VIEW", "DEPT_CREATE", "DEPT_EDIT", "DEPT_DELETE" },
              ["/groups/hierarchy"] = new[] { "DEPT_VIEW", "DEPT_VIEW_ALL" },
              ["/access"] = new[] { "ACCESS_GROUP_VIEW", "ACCESS_GROUP_CREATE", "ACCESS_GROUP_EDIT", "ACCESS_GROUP_DELETE", "ACCESS_GROUP_ASSIGN" },
              ["security & access"] = new[] { "ACCESS_GROUP_VIEW", "ACCESS_GROUP_CREATE", "ACCESS_GROUP_EDIT", "ACCESS_GROUP_DELETE", "ACCESS_GROUP_ASSIGN" },
              ["/locations"] = new[] { "LOCATION_VIEW", "LOCATION_MANAGE" },
              ["locations"] = new[] { "LOCATION_VIEW", "LOCATION_MANAGE" },
          };

        private static string[] GetOperationalKeysForMenu(string? route, string? title)
        {
            if (!string.IsNullOrWhiteSpace(route))
            {
                foreach (var (pattern, keys) in _menuOperationalKeys)
                    if (route.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        return keys;
            }
            if (!string.IsNullOrWhiteSpace(title))
            {
                if (_menuOperationalKeys.TryGetValue(title, out var exact)) return exact;
                foreach (var (pattern, keys) in _menuOperationalKeys)
                    if (title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        return keys;
            }
            return Array.Empty<string>();
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public class SetOverrideDto
    {
        /// <summary>ALLOW, DENY, or INHERIT</summary>
        public string Status { get; set; } = "INHERIT";
        public string? Reason { get; set; }
    }

    public class GrantMenuAccessDto
    {
        public string? Reason { get; set; }
    }
}