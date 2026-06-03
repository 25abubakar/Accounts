using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Hierarchical RBAC Engine with explicit DENY short-circuit.
    ///
    /// Permission resolution order (strict, highest priority first):
    ///
    ///   1. UserPermissionOverride.Status == DENY   → return FALSE immediately (no further checks)
    ///   2. UserPermissionOverride.Status == ALLOW  → return TRUE immediately
    ///   3. UserPermissionOverride.Status == INHERIT → fall through
    ///   4. RolePermission (dept-specific)          → IsAllowed value
    ///   5. RolePermission (global, DeptId = null)  → IsAllowed value
    ///   6. DepartmentAccessMatrix (legacy)         → HasAccess value
    ///   7. false                                   → deny by default
    ///
    /// DENY always wins — even if a role says ALLOW.
    /// </summary>
    public class RbacService
    {
        private readonly ApplicationDbContext _db;
        public RbacService(ApplicationDbContext db) => _db = db;

        // ── HasAccess — single permission check ───────────────────────────────

        public async Task<bool> HasAccessAsync(Guid staffId, string featureKey)
        {
            // ── 1 & 2. Check user-specific override (DENY short-circuits) ─────
            var userOverride = await _db.UserPermissionOverrides
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.StaffId == staffId && u.FeatureKey == featureKey);

            if (userOverride != null)
            {
                if (userOverride.Status == nameof(PermissionStatus.DENY))
                    return false;   // ← HARD DENY — stop here, no further checks

                if (userOverride.Status == nameof(PermissionStatus.ALLOW))
                    return true;    // ← EXPLICIT ALLOW

                // INHERIT → fall through to role default
            }

            // ── 3. Get staff's job title and dept ─────────────────────────────
            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (staff == null) return false;

            var jobTitle = staff.Vacancy?.JobTitle;
            var deptId   = staff.Vacancy?.OrganizationId;

            if (!string.IsNullOrWhiteSpace(jobTitle))
            {
                // ── 4. Role permission — dept-specific ────────────────────────
                if (deptId.HasValue)
                {
                    var deptRolePerm = await _db.RolePermissions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r =>
                            r.JobTitle == jobTitle &&
                            r.DeptId == deptId &&
                            r.FeatureKey == featureKey);

                    if (deptRolePerm != null)
                        return deptRolePerm.IsAllowed;
                }

                // ── 5. Role permission — global (DeptId = null) ───────────────
                var globalRolePerm = await _db.RolePermissions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.JobTitle == jobTitle &&
                        r.DeptId == null &&
                        r.FeatureKey == featureKey);

                if (globalRolePerm != null)
                    return globalRolePerm.IsAllowed;
            }

            // ── 6. Legacy DepartmentAccessMatrix ─────────────────────────────
            var matrixRow = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.StaffId == staffId && m.FeatureKey == featureKey);

            if (matrixRow?.HasAccess == true)
                return true;

            // ── 7. Access group features ──────────────────────────────────────
            var groupIds = await _db.StaffAccessGroups
                .AsNoTracking()
                .Where(sag => sag.StaffId == staffId)
                .Select(sag => sag.GroupId)
                .ToListAsync();

            if (groupIds.Count > 0)
            {
                var hasGroupFeature = await _db.AccessGroupFeatures
                    .AsNoTracking()
                    .AnyAsync(agf => groupIds.Contains(agf.GroupId) && agf.FeatureKey == featureKey);

                if (hasGroupFeature)
                    return true;
            }

            return false;
        }

        // ── GetEffectivePermissions — all allowed features for one staff ──────

        public async Task<IEnumerable<string>> GetEffectivePermissionsAsync(Guid staffId)
        {
            var features = await _db.Features.AsNoTracking()
                .Select(f => f.FeatureKey).ToListAsync();

            var result = new List<string>();
            foreach (var key in features)
            {
                if (await HasAccessAsync(staffId, key))
                    result.Add(key);
            }
            return result;
        }

        // ── GetFilteredSidebar — menu items the user can actually see ─────────

        /// <summary>
        /// Returns the sidebar menu tree filtered by the user's effective permissions.
        /// Menu items linked to a PermissionKey the user doesn't have are removed.
        /// Menu items with no PermissionKey are always shown (public items).
        /// Empty parent groups are also removed.
        /// Pass Guid.Empty for SuperAdmin — they see everything.
        /// </summary>
        public async Task<List<object>> GetFilteredSidebarAsync(Guid staffId)
        {
            // Load all active menus
            var allMenus = await _db.Menus
                .AsNoTracking()
                .Include(m => m.MenuRoles)
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            // SuperAdmin (Guid.Empty) sees ALL menus — no filtering
            if (staffId == Guid.Empty)
            {
                var lookup2 = allMenus.ToLookup(m => m.ParentId);
                return BuildFullTree(null, lookup2);
            }

            // Load all user permissions in one bulk query
            var userPermissions = (await GetEffectivePermissionsAsync(staffId)).ToHashSet();

            // Build lookup by parentId
            var lookup = allMenus.ToLookup(m => m.ParentId);

            return BuildFilteredTree(null, lookup, userPermissions);
        }

        // Full tree for SuperAdmin — no permission filtering
        private static List<object> BuildFullTree(
            int? parentId,
            ILookup<int?, Menu> lookup)
        {
            var result = new List<object>();
            foreach (var menu in lookup[parentId])
            {
                var children = BuildFullTree(menu.Id, lookup);
                result.Add(new
                {
                    id        = menu.Id,
                    title     = menu.Title,
                    icon      = menu.Icon,
                    route     = menu.Route,
                    sortOrder = menu.SortOrder,
                    children
                });
            }
            return result;
        }

        private static List<object> BuildFilteredTree(
            int? parentId,
            ILookup<int?, Menu> lookup,
            HashSet<string> userPermissions)
        {
            var result = new List<object>();

            foreach (var menu in lookup[parentId])
            {
                var requiredRoles = menu.MenuRoles.Select(r => r.RoleName).ToList();

                // ── Permission check ──────────────────────────────────────────
                // If menu has NO required roles → it's a public item, always show
                // If menu HAS required roles → user must have at least one of them
                bool canSee = !requiredRoles.Any() ||
                              requiredRoles.Any(r => userPermissions.Contains(r));

                if (!canSee) continue;

                // Recursively build children (also filtered)
                var children = BuildFilteredTree(menu.Id, lookup, userPermissions);

                // Skip parent groups that have no visible children
                // (unless the parent itself has a route — it's a leaf node)
                if (!children.Any() && string.IsNullOrWhiteSpace(menu.Route) && lookup[menu.Id].Any())
                    continue;

                result.Add(new
                {
                    id        = menu.Id,
                    title     = menu.Title,
                    icon      = menu.Icon,
                    route     = menu.Route,
                    sortOrder = menu.SortOrder,
                    children
                });
            }

            return result;
        }

        // ── GetDepartmentMatrix — optimized bulk load ─────────────────────────

        public async Task<object> GetDepartmentMatrixAsync(int deptId)
        {
            var features = await _db.Features
                .AsNoTracking()
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .ToListAsync();

            var personsInDept = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .Where(p => p.Staff != null && p.Staff.Vacancy != null && p.Staff.Vacancy.OrganizationId == deptId)
                .OrderBy(p => p.FullName)
                .ToListAsync();

            var coveredPersonIds = personsInDept.Select(p => p.PersonId).ToHashSet();

            var staffViaVacancy = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Person)
                .Include(s => s.Vacancy)
                .Where(s => s.Vacancy != null && s.Vacancy.OrganizationId == deptId)
                .OrderBy(s => s.Person != null ? s.Person.FullName : "")
                .ToListAsync();

            var extraStaff = staffViaVacancy
                .Where(s => s.PersonId == null || !coveredPersonIds.Contains(s.PersonId.Value))
                .ToList();

            var allStaffIds = personsInDept
                .Where(p => p.Staff != null).Select(p => p.Staff!.StaffId)
                .Concat(extraStaff.Select(s => s.StaffId))
                .ToHashSet();

            var allOverrides = await _db.UserPermissionOverrides
                .AsNoTracking()
                .Where(u => allStaffIds.Contains(u.StaffId))
                .ToListAsync();

            var jobTitles = personsInDept
                .Select(p => p.Staff?.Vacancy?.JobTitle)
                .Concat(extraStaff.Select(s => s.Vacancy?.JobTitle))
                .Where(j => j != null).Distinct().ToList();

            var allRolePerms = await _db.RolePermissions
                .AsNoTracking()
                .Where(r => jobTitles.Contains(r.JobTitle) &&
                            (r.DeptId == null || r.DeptId == deptId))
                .ToListAsync();

            var allMatrixRows = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .Where(m => allStaffIds.Contains(m.StaffId))
                .ToListAsync();

            // Resolve cell with DENY short-circuit
            object ResolveCell(Guid staffId, string jobTitle, int? staffDeptId, string featureKey)
            {
                var uo = allOverrides.FirstOrDefault(u =>
                    u.StaffId == staffId && u.FeatureKey == featureKey);

                if (uo != null)
                {
                    if (uo.Status == nameof(PermissionStatus.DENY))
                        return new { effectiveAccess = false, source = "UserDeny", hasUserOverride = true };
                    if (uo.Status == nameof(PermissionStatus.ALLOW))
                        return new { effectiveAccess = true, source = "UserAllow", hasUserOverride = true };
                    // INHERIT → fall through
                }

                var rp = allRolePerms.FirstOrDefault(r =>
                    r.JobTitle == jobTitle && r.DeptId == staffDeptId && r.FeatureKey == featureKey);
                if (rp != null)
                    return new { effectiveAccess = rp.IsAllowed, source = "RoleDefault", hasUserOverride = false };

                var rpGlobal = allRolePerms.FirstOrDefault(r =>
                    r.JobTitle == jobTitle && r.DeptId == null && r.FeatureKey == featureKey);
                if (rpGlobal != null)
                    return new { effectiveAccess = rpGlobal.IsAllowed, source = "RoleDefault", hasUserOverride = false };

                var mx = allMatrixRows.FirstOrDefault(m =>
                    m.StaffId == staffId && m.FeatureKey == featureKey);
                if (mx != null)
                    return new { effectiveAccess = mx.HasAccess, source = "Matrix", hasUserOverride = false };

                return new { effectiveAccess = false, source = "Denied", hasUserOverride = false };
            }

            var gridFromPersons = personsInDept.Select(p =>
            {
                var sid = p.Staff?.StaffId ?? Guid.Empty;
                var jt  = p.Staff?.Vacancy?.JobTitle ?? "";
                var dId = p.Staff?.Vacancy?.OrganizationId;
                return new
                {
                    staffId = sid, personId = p.PersonId, fullName = p.FullName,
                    loginId = p.Staff != null ? p.Staff.LoginId : null, jobTitle = jt, isHired = p.Staff != null,
                    permissions = features.Select(f => new
                    {
                        f.FeatureKey, f.FeatureName, f.Module,
                        access = sid != Guid.Empty
                            ? ResolveCell(sid, jt, dId, f.FeatureKey)
                            : new { effectiveAccess = false, source = "NotHired", hasUserOverride = false }
                    }).ToList()
                };
            }).ToList();

            var gridFromStaff = extraStaff.Select(s =>
            {
                var jt  = s.Vacancy?.JobTitle ?? "";
                var dId = s.Vacancy?.OrganizationId;
                return new
                {
                    staffId = s.StaffId, personId = s.PersonId, fullName = s.Person?.FullName ?? "-",
                    loginId = s.LoginId ?? "-", jobTitle = jt, isHired = true,
                    permissions = features.Select(f => new
                    {
                        f.FeatureKey, f.FeatureName, f.Module,
                        access = ResolveCell(s.StaffId, jt, dId, f.FeatureKey)
                    }).ToList()
                };
            }).ToList();

            var allStaff = gridFromPersons.Cast<object>()
                .Concat(gridFromStaff.Cast<object>()).ToList();

            return new
            {
                deptId,
                totalStaff = allStaff.Count,
                features   = features.Select(f => new { f.FeatureKey, f.FeatureName, f.Module }).ToList(),
                staff      = allStaff
            };
        }

        // ── Role Permission Management ────────────────────────────────────────

        public async Task<(int Saved, List<string> InvalidKeys)> SetRolePermissionsAsync(
            string jobTitle, int? deptId, Dictionary<string, bool> permissions, string? setBy)
        {
            // ── Validate: only save keys that exist in Features table ─────────
            var validKeys = await _db.Features
                .AsNoTracking()
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            var invalidKeys = permissions.Keys
                .Where(k => !validKeys.Contains(k))
                .ToList();

            int count = 0;
            foreach (var (featureKey, isAllowed) in permissions)
            {
                // Skip keys that don't exist in Features — prevents FK violation
                if (!validKeys.Contains(featureKey))
                    continue;

                var existing = await _db.RolePermissions
                    .FirstOrDefaultAsync(r =>
                        r.JobTitle == jobTitle && r.DeptId == deptId && r.FeatureKey == featureKey);

                if (existing == null)
                    _db.RolePermissions.Add(new RolePermission
                    {
                        JobTitle   = jobTitle,
                        DeptId     = deptId,
                        FeatureKey = featureKey,
                        IsAllowed  = isAllowed
                    });
                else
                    existing.IsAllowed = isAllowed;

                count++;
            }

            if (count > 0)
                await _db.SaveChangesAsync();

            return (count, invalidKeys);
        }

        public async Task<IEnumerable<object>> GetRolePermissionsAsync(string jobTitle, int? deptId = null)
        {
            var query = _db.RolePermissions.AsNoTracking().Where(r => r.JobTitle == jobTitle);
            if (deptId.HasValue)
                query = query.Where(r => r.DeptId == deptId || r.DeptId == null);

            return await query.OrderBy(r => r.FeatureKey)
                .Select(r => new { r.Id, r.JobTitle, r.DeptId, r.FeatureKey, r.IsAllowed })
                .ToListAsync<object>();
        }

        // ── User Override Management ──────────────────────────────────────────

        /// <summary>
        /// Set a user override with explicit ALLOW, DENY, or INHERIT.
        /// DENY immediately blocks the feature regardless of role.
        /// </summary>
        public async Task<(bool Success, string Message)> SetUserOverrideAsync(
            Guid staffId, string featureKey, PermissionStatus status,
            string? setBy, string? reason)
        {
            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.");
            if (!await _db.Features.AnyAsync(f => f.FeatureKey == featureKey))
                return (false, $"Feature '{featureKey}' not found.");

            var existing = await _db.UserPermissionOverrides
                .FirstOrDefaultAsync(u => u.StaffId == staffId && u.FeatureKey == featureKey);

            if (existing == null)
            {
                _db.UserPermissionOverrides.Add(new UserPermissionOverride
                {
                    StaffId    = staffId,
                    FeatureKey = featureKey,
                    Status     = status.ToString(),
                    SetBy      = setBy,
                    SetDate    = DateTime.Now,
                    Reason     = reason
                });
            }
            else
            {
                existing.Status  = status.ToString();
                existing.SetBy   = setBy;
                existing.SetDate = DateTime.Now;
                existing.Reason  = reason;
            }

            await _db.SaveChangesAsync();
            return (true, $"Override set: '{featureKey}' = {status} for staff {staffId}.");
        }

        public async Task<(bool Success, string Message)> RemoveUserOverrideAsync(
            Guid staffId, string featureKey)
        {
            var existing = await _db.UserPermissionOverrides
                .FirstOrDefaultAsync(u => u.StaffId == staffId && u.FeatureKey == featureKey);

            if (existing == null) return (false, "Override not found.");

            _db.UserPermissionOverrides.Remove(existing);
            await _db.SaveChangesAsync();
            return (true, $"Override removed. '{featureKey}' now uses role default.");
        }

        public async Task<IEnumerable<object>> GetUserOverridesAsync(Guid staffId) =>
            await _db.UserPermissionOverrides
                .AsNoTracking()
                .Where(u => u.StaffId == staffId)
                .OrderBy(u => u.FeatureKey)
                .Select(u => new { u.Id, u.StaffId, u.FeatureKey, u.Status, u.SetBy, u.SetDate, u.Reason })
                .ToListAsync<object>();

        // ── Menu bundle access (parent + all child feature keys) ──────────────

        /// <summary>
        /// Collects all feature keys required by a menu item and its descendants.
        /// Parent groups with no keys still include keys from visible children.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetMenuFeatureKeysAsync(int menuId)
        {
            var allMenus = await _db.Menus
                .AsNoTracking()
                .Include(m => m.MenuRoles)
                .Where(m => m.IsActive)
                .ToListAsync();

            if (!allMenus.Any(m => m.Id == menuId))
                return Array.Empty<string>();

            var lookup = allMenus.ToLookup(m => m.ParentId);
            var keys   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectKeys(menuId);

            return keys.OrderBy(k => k).ToList();

            void CollectKeys(int id)
            {
                var menu = allMenus.First(m => m.Id == id);
                foreach (var role in menu.MenuRoles)
                    keys.Add(role.RoleName);

                foreach (var child in lookup[id])
                    CollectKeys(child.Id);
            }
        }

        /// <summary>
        /// Grants ALLOW overrides for every feature key in a menu subtree.
        /// Use when admin assigns a sidebar section (e.g. Accounts &amp; Groups) to a user.
        /// </summary>
        public async Task<(bool Success, string Message, IReadOnlyList<string> GrantedKeys)> GrantMenuAccessAsync(
            Guid staffId, int menuId, string? setBy, string? reason)
        {
            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.", Array.Empty<string>());

            var menu = await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId && m.IsActive);
            if (menu == null)
                return (false, $"Menu {menuId} not found.", Array.Empty<string>());

            var featureKeys = await GetMenuFeatureKeysAsync(menuId);
            if (featureKeys.Count == 0)
                return (false, $"Menu '{menu.Title}' has no permission keys (check child items are seeded).", Array.Empty<string>());

            foreach (var key in featureKeys)
            {
                await SetUserOverrideAsync(staffId, key, PermissionStatus.ALLOW, setBy, reason);
            }

            return (true, $"Granted {featureKeys.Count} feature(s) from menu '{menu.Title}'.", featureKeys);
        }

        /// <summary>
        /// Removes user overrides for every feature key in a menu subtree (reverts to role defaults).
        /// </summary>
        public async Task<(bool Success, string Message, IReadOnlyList<string> RevokedKeys)> RevokeMenuAccessAsync(
            Guid staffId, int menuId)
        {
            var menu = await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId);
            if (menu == null)
                return (false, $"Menu {menuId} not found.", Array.Empty<string>());

            var featureKeys = await GetMenuFeatureKeysAsync(menuId);
            int removed = 0;

            foreach (var key in featureKeys)
            {
                var (ok, _) = await RemoveUserOverrideAsync(staffId, key);
                if (ok) removed++;
            }

            return (true, $"Removed {removed} override(s) for menu '{menu.Title}'.", featureKeys);
        }

        /// <summary>Returns menus with their required feature keys (for admin access UI).</summary>
        public async Task<IEnumerable<object>> GetMenuPermissionTreeAsync()
        {
            var menus = await _db.Menus
                .AsNoTracking()
                .Include(m => m.MenuRoles)
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            var lookup = menus.ToLookup(m => m.ParentId);

            return BuildMenuPermTree(null);

            List<object> BuildMenuPermTree(int? parentId)
            {
                var result = new List<object>();
                foreach (var menu in lookup[parentId])
                {
                    var childKeys = CollectDescendantKeys(menu.Id);
                    result.Add(new
                    {
                        menu.Id,
                        menu.Title,
                        menu.Icon,
                        menu.Route,
                        menu.ParentId,
                        menu.SortOrder,
                        directPermissions = menu.MenuRoles.Select(r => r.RoleName).ToList(),
                        allPermissions    = childKeys.OrderBy(k => k).ToList(),
                        children          = BuildMenuPermTree(menu.Id)
                    });
                }
                return result;
            }

            HashSet<string> CollectDescendantKeys(int id)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void Walk(int nodeId)
                {
                    var m = menus.First(x => x.Id == nodeId);
                    foreach (var r in m.MenuRoles) set.Add(r.RoleName);
                    foreach (var c in lookup[nodeId]) Walk(c.Id);
                }
                Walk(id);
                return set;
            }
        }
    }
}
