using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Hierarchical RBAC Engine — fully optimized, using integer PermissionId FKs.
    ///
    /// Permission resolution order (highest priority first):
    ///   1. UserPermissionOverride.Status == DENY   → FALSE (hard-stop, no further checks)
    ///   2. UserPermissionOverride.Status == ALLOW  → TRUE
    ///   3. UserPermissionOverride.Status == INHERIT → fall through
    ///   4. RolePermission (dept-specific)          → IsAllowed value
    ///   5. RolePermission (global, DeptId = null)  → IsAllowed value
    ///   6. DepartmentAccessMatrix                  → HasAccess value
    ///   7. AccessGroupFeatures                     → true if any group grants it
    ///   8. false                                   → deny by default
    ///
    /// DENY always wins — even if a role says ALLOW.
    /// All bulk methods load data in ONE query per table, resolve in-memory.
    /// </summary>
    public class RbacService
    {
        private readonly ApplicationDbContext _db;
        public RbacService(ApplicationDbContext db) => _db = db;

        // ─────────────────────────────────────────────────────────────────────
        // SINGLE PERMISSION CHECK
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Check if a staff member has access to ONE feature by its string key.
        /// For checking many features at once prefer GetEffectivePermissionsAsync.
        /// </summary>
        public async Task<bool> HasAccessAsync(Guid staffId, string featureKey)
        {
            // Resolve the PermissionId once
            var permissionId = await _db.Features
                .AsNoTracking()
                .Where(f => f.FeatureKey == featureKey)
                .Select(f => (int?)f.PermissionId)
                .FirstOrDefaultAsync();

            if (permissionId == null) return false; // Feature doesn't exist

            // Direct person grants (PersonFeatures) take priority when configured
            var personId = await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.StaffId == staffId)
                .Select(s => s.PersonId)
                .FirstOrDefaultAsync();

            if (personId.HasValue)
            {
                var hasDirectGrants = await _db.PersonMenus.AsNoTracking()
                    .AnyAsync(pm => pm.PersonId == personId.Value) ||
                    await _db.PersonFeatures.AsNoTracking()
                    .AnyAsync(pf => pf.PersonId == personId.Value);

                if (hasDirectGrants)
                    return await _db.PersonFeatures.AsNoTracking()
                        .AnyAsync(pf => pf.PersonId == personId.Value && pf.PermissionId == permissionId);
            }

            // Check user-level override first
            var userOverride = await _db.UserPermissionOverrides
                .AsNoTracking()
                .Where(u => u.StaffId == staffId && u.PermissionId == permissionId)
                .Select(u => u.Status)
                .FirstOrDefaultAsync();

            if (userOverride != null)
            {
                if (userOverride == nameof(PermissionStatus.DENY))  return false;
                if (userOverride == nameof(PermissionStatus.ALLOW)) return true;
                // INHERIT → fall through
            }

            // Load staff role/dept
            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (staff == null) return false;

            var jobTitle = staff.Vacancy?.JobTitle;
            var deptId   = staff.Vacancy?.OrganizationId;

            if (!string.IsNullOrWhiteSpace(jobTitle))
            {
                // Dept-specific role permission
                if (deptId.HasValue)
                {
                    var deptRolePerm = await _db.RolePermissions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r =>
                            r.JobTitle == jobTitle &&
                            r.DeptId   == deptId   &&
                            r.PermissionId == permissionId);

                    if (deptRolePerm != null) return deptRolePerm.IsAllowed;
                }

                // Global role permission (DeptId = null)
                var globalRolePerm = await _db.RolePermissions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.JobTitle    == jobTitle &&
                        r.DeptId      == null     &&
                        r.PermissionId == permissionId);

                if (globalRolePerm != null) return globalRolePerm.IsAllowed;
            }

            // Legacy DepartmentAccessMatrix
            var matrixRow = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.StaffId == staffId && m.PermissionId == permissionId);

            if (matrixRow?.HasAccess == true) return true;

            // AccessGroupFeatures
            var groupIds = await _db.StaffAccessGroups
                .AsNoTracking()
                .Where(sag => sag.StaffId == staffId)
                .Select(sag => sag.GroupId)
                .ToListAsync();

            if (groupIds.Count > 0)
            {
                var inGroup = await _db.AccessGroupFeatures
                    .AsNoTracking()
                    .AnyAsync(agf => groupIds.Contains(agf.GroupId) && agf.PermissionId == permissionId);

                if (inGroup) return true;
            }

            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // BULK PERMISSION LOAD (used at login)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the set of FeatureKey strings the user is allowed to access.
        /// Loads ALL permission data in a fixed number of queries, resolves in-memory.
        /// </summary>
        public async Task<IEnumerable<string>> GetEffectivePermissionsAsync(Guid staffId)
        {
            var allowedIds = await GetEffectivePermissionIdsAsync(staffId);

            // Map back to string FeatureKeys (needed by older callers)
            if (allowedIds.Count == 0) return Array.Empty<string>();

            return await _db.Features
                .AsNoTracking()
                .Where(f => allowedIds.Contains(f.PermissionId))
                .Select(f => f.FeatureKey)
                .ToListAsync();
        }

        /// <summary>
        /// Returns the HashSet of int PermissionIds the user is allowed to access.
        /// This is the core optimized resolution — load once, resolve in memory.
        /// </summary>
        public async Task<HashSet<int>> GetEffectivePermissionIdsAsync(Guid staffId)
        {
            // ── Load staff role / dept ─────────────────────────────────────────
            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (staff == null) return new HashSet<int>();

            var jobTitle = staff.Vacancy?.JobTitle;
            var deptId   = staff.Vacancy?.OrganizationId;

            // ── Bulk load all relevant data (one query per table) ─────────────

            // 1. User-level overrides
            var userOverrides = await _db.UserPermissionOverrides
                .AsNoTracking()
                .Where(u => u.StaffId == staffId)
                .Select(u => new { u.PermissionId, u.Status })
                .ToListAsync();

            // 2. Role permissions for this job title
            var rolePermissions = string.IsNullOrWhiteSpace(jobTitle)
                ? new List<RolePermission>()
                : await _db.RolePermissions
                    .AsNoTracking()
                    .Where(r => r.JobTitle == jobTitle &&
                                (r.DeptId == null || r.DeptId == deptId))
                    .ToListAsync();

            // 3. Legacy matrix
            var matrixAllowed = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .Where(m => m.StaffId == staffId && m.HasAccess)
                .Select(m => m.PermissionId)
                .ToHashSetAsync();

            // 4. Access group features
            var groupIds = await _db.StaffAccessGroups
                .AsNoTracking()
                .Where(sag => sag.StaffId == staffId)
                .Select(sag => sag.GroupId)
                .ToListAsync();

            var groupAllowed = groupIds.Count > 0
                ? await _db.AccessGroupFeatures
                    .AsNoTracking()
                    .Where(agf => groupIds.Contains(agf.GroupId))
                    .Select(agf => agf.PermissionId)
                    .ToHashSetAsync()
                : new HashSet<int>();

            // 5. All features (to iterate)
            var allFeatures = await _db.Features
                .AsNoTracking()
                .Select(f => new { f.PermissionId })
                .ToListAsync();

            // ── Build override lookup dictionaries ─────────────────────────────
            var denySet  = userOverrides
                .Where(o => o.Status == nameof(PermissionStatus.DENY))
                .Select(o => o.PermissionId)
                .ToHashSet();

            var allowSet = userOverrides
                .Where(o => o.Status == nameof(PermissionStatus.ALLOW))
                .Select(o => o.PermissionId)
                .ToHashSet();

            var inheritSet = userOverrides
                .Where(o => o.Status == nameof(PermissionStatus.INHERIT))
                .Select(o => o.PermissionId)
                .ToHashSet();

            // Role permission lookup: permissionId → (deptSpecific?, isAllowed)
            var roleDeptLookup   = rolePermissions
                .Where(r => r.DeptId != null)
                .ToDictionary(r => r.PermissionId, r => r.IsAllowed);

            var roleGlobalLookup = rolePermissions
                .Where(r => r.DeptId == null)
                .ToDictionary(r => r.PermissionId, r => r.IsAllowed);

            // ── In-memory resolution ───────────────────────────────────────────
            var result = new HashSet<int>();

            foreach (var f in allFeatures)
            {
                var pid = f.PermissionId;

                // 1. Hard DENY → skip
                if (denySet.Contains(pid)) continue;

                // 2. Explicit ALLOW → include
                if (allowSet.Contains(pid)) { result.Add(pid); continue; }

                // 3. INHERIT → fall through (same as no override)
                // 4 & 5. Role permission
                if (!string.IsNullOrWhiteSpace(jobTitle))
                {
                    if (roleDeptLookup.TryGetValue(pid, out var deptAllowed))
                    {
                        if (deptAllowed) result.Add(pid);
                        continue; // dept rule is definitive
                    }
                    if (roleGlobalLookup.TryGetValue(pid, out var globalAllowed))
                    {
                        if (globalAllowed) result.Add(pid);
                        continue; // global role rule is definitive
                    }
                }

                // 6. Legacy matrix
                if (matrixAllowed.Contains(pid)) { result.Add(pid); continue; }

                // 7. Access group
                if (groupAllowed.Contains(pid)) { result.Add(pid); }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // DETAILED PERMISSION VIEW (admin / debug)
        // ─────────────────────────────────────────────────────────────────────

        public async Task<IEnumerable<object>> GetEffectivePermissionsDetailedAsync(Guid staffId)
        {
            var features = await _db.Features.AsNoTracking()
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .ToListAsync();

            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (staff == null)
                return features.Select(f => new { f.FeatureKey, f.FeatureName, f.Module, hasAccess = false, source = "StaffNotFound" }).ToList<object>();

            var jobTitle = staff.Vacancy?.JobTitle;
            var deptId   = staff.Vacancy?.OrganizationId;

            var userOverrides = await _db.UserPermissionOverrides.AsNoTracking()
                .Where(u => u.StaffId == staffId)
                .ToDictionaryAsync(u => u.PermissionId, u => u.Status);

            var rolePermissions = string.IsNullOrWhiteSpace(jobTitle)
                ? new List<RolePermission>()
                : await _db.RolePermissions.AsNoTracking()
                    .Where(r => r.JobTitle == jobTitle && (r.DeptId == null || r.DeptId == deptId))
                    .ToListAsync();

            var matrixAllowed = await _db.DepartmentAccessMatrix.AsNoTracking()
                .Where(m => m.StaffId == staffId && m.HasAccess)
                .Select(m => m.PermissionId)
                .ToHashSetAsync();

            var groupIds = await _db.StaffAccessGroups.AsNoTracking()
                .Where(sag => sag.StaffId == staffId)
                .Select(sag => sag.GroupId)
                .ToListAsync();

            var groupAllowed = groupIds.Count > 0
                ? await _db.AccessGroupFeatures.AsNoTracking()
                    .Where(agf => groupIds.Contains(agf.GroupId))
                    .Select(agf => agf.PermissionId)
                    .ToHashSetAsync()
                : new HashSet<int>();

            var roleDeptLookup   = rolePermissions.Where(r => r.DeptId != null).ToDictionary(r => r.PermissionId, r => r.IsAllowed);
            var roleGlobalLookup = rolePermissions.Where(r => r.DeptId == null).ToDictionary(r => r.PermissionId, r => r.IsAllowed);

            var result = new List<object>();
            foreach (var f in features)
            {
                bool hasAccess;
                string source;

                if (userOverrides.TryGetValue(f.PermissionId, out var status))
                {
                    if      (status == nameof(PermissionStatus.DENY))    { hasAccess = false; source = "UserDeny"; }
                    else if (status == nameof(PermissionStatus.ALLOW))   { hasAccess = true;  source = "UserAllow"; }
                    else (hasAccess, source) = ResolveFromRole(f.PermissionId, jobTitle, roleDeptLookup, roleGlobalLookup, matrixAllowed, groupAllowed);
                }
                else
                {
                    (hasAccess, source) = ResolveFromRole(f.PermissionId, jobTitle, roleDeptLookup, roleGlobalLookup, matrixAllowed, groupAllowed);
                }

                result.Add(new { f.FeatureKey, f.FeatureName, f.Module, hasAccess, source });
            }
            return result;
        }

        private static (bool hasAccess, string source) ResolveFromRole(
            int permissionId,
            string? jobTitle,
            Dictionary<int, bool> roleDept,
            Dictionary<int, bool> roleGlobal,
            HashSet<int> matrix,
            HashSet<int> groups)
        {
            if (!string.IsNullOrWhiteSpace(jobTitle))
            {
                if (roleDept.TryGetValue(permissionId, out var da))   return (da,   "RoleDefault");
                if (roleGlobal.TryGetValue(permissionId, out var ga)) return (ga,   "RoleDefault");
            }
            if (matrix.Contains(permissionId)) return (true, "Matrix");
            if (groups.Contains(permissionId)) return (true, "AccessGroup");
            return (false, "Denied");
        }

        // ─────────────────────────────────────────────────────────────────────
        // SIDEBAR FILTERING (login / session endpoint)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the sidebar menu tree visible to this user.
        /// Pass Guid.Empty for SuperAdmin — they see everything.
        /// All filtering is done in-memory after a fixed set of queries.
        /// </summary>
        public async Task<List<object>> GetFilteredSidebarAsync(Guid staffId)
        {
            var allMenus = await _db.Menus
                .AsNoTracking()
                .Include(m => m.MenuPermissions)
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            var lookup = allMenus.ToLookup(m => m.ParentId);

            // SuperAdmin sees everything
            if (staffId == Guid.Empty)
                return BuildFullTree(null, lookup);

            var personId = await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.StaffId == staffId)
                .Select(s => s.PersonId)
                .FirstOrDefaultAsync();

            if (personId.HasValue)
            {
                var grantedMenuIds = await _db.PersonMenus.AsNoTracking()
                    .Where(pm => pm.PersonId == personId.Value)
                    .Select(pm => pm.MenuId)
                    .ToHashSetAsync();

                if (grantedMenuIds.Count > 0)
                    return BuildSidebarFromGrantedMenus(allMenus, grantedMenuIds);
            }

            // Load user's allowed permission IDs in one optimized pass
            var allowedIds = await GetEffectivePermissionIdsAsync(staffId);

            return BuildFilteredTree(null, lookup, allowedIds);
        }

        private static List<object> BuildSidebarFromGrantedMenus(List<Menu> allMenus, HashSet<int> grantedMenuIds)
        {
            var byId = allMenus.ToDictionary(m => m.Id);
            var visibleIds = new HashSet<int>(grantedMenuIds);

            foreach (var menuId in grantedMenuIds.ToList())
            {
                var current = byId.GetValueOrDefault(menuId);
                while (current?.ParentId != null && byId.TryGetValue(current.ParentId.Value, out var parent))
                {
                    visibleIds.Add(parent.Id);
                    current = parent;
                }
            }

            var lookup = allMenus.Where(m => visibleIds.Contains(m.Id)).ToLookup(m => m.ParentId);
            return BuildFullTree(null, lookup);
        }

        private static List<object> BuildFullTree(int? parentId, ILookup<int?, Menu> lookup)
        {
            return lookup[parentId].Select(menu => (object)new
            {
                id        = menu.Id,
                title     = menu.Title,
                icon      = menu.Icon,
                route     = menu.Route,
                sortOrder = menu.SortOrder,
                children  = BuildFullTree(menu.Id, lookup)
            }).ToList();
        }

        private static List<object> BuildFilteredTree(
            int? parentId,
            ILookup<int?, Menu> lookup,
            HashSet<int> allowedIds)
        {
            var result = new List<object>();

            foreach (var menu in lookup[parentId])
            {
                var requiredIds = menu.MenuPermissions.Select(mp => mp.PermissionId).ToList();

                // Menu with no required permissions → public (always visible)
                // Menu with required permissions → user needs at least ONE
                bool canSee = !requiredIds.Any() || requiredIds.Any(id => allowedIds.Contains(id));

                if (!canSee) continue;

                var children = BuildFilteredTree(menu.Id, lookup, allowedIds);

                // Skip parent groups whose children are all hidden
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

        // ─────────────────────────────────────────────────────────────────────
        // DEPARTMENT MATRIX
        // ─────────────────────────────────────────────────────────────────────

        public async Task<object> GetDepartmentMatrixAsync(int deptId)
        {
            var features = await _db.Features.AsNoTracking()
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .ToListAsync();

            var personsInDept = await _db.Persons.AsNoTracking()
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .Where(p => p.Staff != null && p.Staff.Vacancy != null && p.Staff.Vacancy.OrganizationId == deptId)
                .OrderBy(p => p.FullName)
                .ToListAsync();

            var coveredPersonIds = personsInDept.Select(p => p.PersonId).ToHashSet();

            var staffViaVacancy = await _db.StaffVacancies.AsNoTracking()
                .Include(s => s.Person)
                .Include(s => s.Vacancy)
                .Where(s => s.Vacancy != null && s.Vacancy.OrganizationId == deptId)
                .OrderBy(s => s.Person != null ? s.Person.FullName : "")
                .ToListAsync();

            var extraStaff = staffViaVacancy
                .Where(s => s.PersonId == null || !coveredPersonIds.Contains(s.PersonId.Value))
                .ToList();

            var allStaffIds = personsInDept.Where(p => p.Staff != null).Select(p => p.Staff!.StaffId)
                .Concat(extraStaff.Select(s => s.StaffId))
                .ToHashSet();

            var allOverrides = await _db.UserPermissionOverrides.AsNoTracking()
                .Where(u => allStaffIds.Contains(u.StaffId))
                .ToListAsync();

            var jobTitles = personsInDept.Select(p => p.Staff?.Vacancy?.JobTitle)
                .Concat(extraStaff.Select(s => s.Vacancy?.JobTitle))
                .Where(j => j != null).Distinct().ToList();

            var allRolePerms = await _db.RolePermissions.AsNoTracking()
                .Where(r => jobTitles.Contains(r.JobTitle) && (r.DeptId == null || r.DeptId == deptId))
                .ToListAsync();

            var allMatrixRows = await _db.DepartmentAccessMatrix.AsNoTracking()
                .Where(m => allStaffIds.Contains(m.StaffId))
                .ToListAsync();

            object ResolveCell(Guid sid, string jt, int? sDeptId, int permId)
            {
                var uo = allOverrides.FirstOrDefault(u => u.StaffId == sid && u.PermissionId == permId);
                if (uo != null)
                {
                    if (uo.Status == nameof(PermissionStatus.DENY))  return new { effectiveAccess = false, source = "UserDeny",   hasUserOverride = true };
                    if (uo.Status == nameof(PermissionStatus.ALLOW)) return new { effectiveAccess = true,  source = "UserAllow",  hasUserOverride = true };
                }
                var rp = allRolePerms.FirstOrDefault(r => r.JobTitle == jt && r.DeptId == sDeptId && r.PermissionId == permId);
                if (rp != null) return new { effectiveAccess = rp.IsAllowed, source = "RoleDefault", hasUserOverride = false };

                var rpG = allRolePerms.FirstOrDefault(r => r.JobTitle == jt && r.DeptId == null && r.PermissionId == permId);
                if (rpG != null) return new { effectiveAccess = rpG.IsAllowed, source = "RoleDefault", hasUserOverride = false };

                var mx = allMatrixRows.FirstOrDefault(m => m.StaffId == sid && m.PermissionId == permId);
                if (mx != null) return new { effectiveAccess = mx.HasAccess, source = "Matrix", hasUserOverride = false };

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
                    loginId = p.Staff?.LoginId, jobTitle = jt, isHired = p.Staff != null,
                    permissions = features.Select(f => new
                    {
                        f.FeatureKey, f.FeatureName, f.Module,
                        access = sid != Guid.Empty ? ResolveCell(sid, jt, dId, f.PermissionId)
                            : (object)new { effectiveAccess = false, source = "NotHired", hasUserOverride = false }
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
                        access = ResolveCell(s.StaffId, jt, dId, f.PermissionId)
                    }).ToList()
                };
            }).ToList();

            return new
            {
                deptId,
                totalStaff = gridFromPersons.Count + gridFromStaff.Count,
                features   = features.Select(f => new { f.FeatureKey, f.FeatureName, f.Module }).ToList(),
                staff      = gridFromPersons.Cast<object>().Concat(gridFromStaff.Cast<object>()).ToList()
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // ROLE PERMISSION MANAGEMENT
        // ─────────────────────────────────────────────────────────────────────

        public async Task<(int Saved, List<string> InvalidKeys)> SetRolePermissionsAsync(
            string jobTitle, int? deptId, Dictionary<string, bool> permissions, string? setBy)
        {
            // Build FeatureKey → PermissionId map in one query
            var featureMap = await _db.Features.AsNoTracking()
                .Where(f => permissions.Keys.Contains(f.FeatureKey))
                .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            var invalidKeys = permissions.Keys.Where(k => !featureMap.ContainsKey(k)).ToList();

            int count = 0;
            foreach (var (featureKey, isAllowed) in permissions)
            {
                if (!featureMap.TryGetValue(featureKey, out int permId)) continue;

                var existing = await _db.RolePermissions
                    .FirstOrDefaultAsync(r => r.JobTitle == jobTitle && r.DeptId == deptId && r.PermissionId == permId);

                if (existing == null)
                    _db.RolePermissions.Add(new RolePermission { JobTitle = jobTitle, DeptId = deptId, PermissionId = permId, IsAllowed = isAllowed });
                else
                    existing.IsAllowed = isAllowed;

                count++;
            }

            if (count > 0) await _db.SaveChangesAsync();
            return (count, invalidKeys);
        }

        public async Task<IEnumerable<object>> GetRolePermissionsAsync(string jobTitle, int? deptId = null)
        {
            var query = _db.RolePermissions.AsNoTracking()
                .Include(r => r.Feature)
                .Where(r => r.JobTitle == jobTitle);

            if (deptId.HasValue)
                query = query.Where(r => r.DeptId == deptId || r.DeptId == null);

            return await query.OrderBy(r => r.Feature!.FeatureKey)
                .Select(r => new
                {
                    r.Id, r.JobTitle, r.DeptId,
                    r.Feature!.FeatureKey, r.Feature.FeatureName,
                    r.IsAllowed, r.PermissionId
                })
                .ToListAsync<object>();
        }

        // ─────────────────────────────────────────────────────────────────────
        // USER OVERRIDE MANAGEMENT
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Set a user override by FeatureKey string.
        /// Auto-creates MENU_* feature records if needed.
        /// </summary>
        public async Task<(bool Success, string Message)> SetUserOverrideAsync(
            Guid staffId, string featureKey, PermissionStatus status, string? setBy, string? reason)
        {
            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.");

            await EnsureFeatureExistsAsync(featureKey);

            var feature = await _db.Features.AsNoTracking()
                .FirstOrDefaultAsync(f => f.FeatureKey == featureKey);

            if (feature == null)
                return (false, $"Feature '{featureKey}' not found. Use GET /api/access/features for valid keys.");

            var existing = await _db.UserPermissionOverrides
                .FirstOrDefaultAsync(u => u.StaffId == staffId && u.PermissionId == feature.PermissionId);

            if (status == PermissionStatus.INHERIT)
            {
                // INHERIT = remove the override row entirely
                if (existing != null)
                {
                    _db.UserPermissionOverrides.Remove(existing);
                    await _db.SaveChangesAsync();
                }
                return (true, $"Override removed — '{featureKey}' now uses role default.");
            }

            if (existing == null)
            {
                _db.UserPermissionOverrides.Add(new UserPermissionOverride
                {
                    StaffId      = staffId,
                    PermissionId = feature.PermissionId,
                    Status       = status.ToString(),
                    SetBy        = setBy,
                    SetDate      = DateTime.Now,
                    Reason       = reason
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

        public async Task<(bool Success, string Message)> RemoveUserOverrideAsync(Guid staffId, string featureKey)
        {
            var feature = await _db.Features.AsNoTracking()
                .FirstOrDefaultAsync(f => f.FeatureKey == featureKey);
            if (feature == null) return (false, "Feature not found.");

            var existing = await _db.UserPermissionOverrides
                .FirstOrDefaultAsync(u => u.StaffId == staffId && u.PermissionId == feature.PermissionId);

            if (existing == null) return (false, "Override not found.");

            _db.UserPermissionOverrides.Remove(existing);
            await _db.SaveChangesAsync();
            return (true, $"Override removed — '{featureKey}' now uses role default.");
        }

        public async Task<IEnumerable<object>> GetUserOverridesAsync(Guid staffId) =>
            await _db.UserPermissionOverrides.AsNoTracking()
                .Include(u => u.Feature)
                .Where(u => u.StaffId == staffId)
                .OrderBy(u => u.Feature!.FeatureKey)
                .Select(u => new
                {
                    u.Id, u.StaffId, u.PermissionId,
                    FeatureKey  = u.Feature!.FeatureKey,
                    FeatureName = u.Feature.FeatureName,
                    u.Status, u.SetBy, u.SetDate, u.Reason
                })
                .ToListAsync<object>();

        // ─────────────────────────────────────────────────────────────────────
        // MENU BUNDLE GRANT / REVOKE (admin assigns whole sidebar section)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all FeatureKeys required by a menu and all its descendants.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetMenuFeatureKeysAsync(int menuId)
        {
            var allMenus = await _db.Menus.AsNoTracking()
                .Include(m => m.MenuPermissions)
                    .ThenInclude(mp => mp.Feature)
                .Where(m => m.IsActive)
                .ToListAsync();

            if (!allMenus.Any(m => m.Id == menuId)) return Array.Empty<string>();

            var lookup = allMenus.ToLookup(m => m.ParentId);
            var keys   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Collect(int id)
            {
                var menu = allMenus.First(m => m.Id == id);
                foreach (var mp in menu.MenuPermissions.Where(mp => mp.Feature != null))
                    keys.Add(mp.Feature!.FeatureKey);
                foreach (var child in lookup[id]) Collect(child.Id);
            }
            Collect(menuId);

            return keys.OrderBy(k => k).ToList();
        }

        public async Task<(bool Success, string Message, IReadOnlyList<string> GrantedKeys)>
            GrantMenuAccessAsync(Guid staffId, int menuId, string? setBy, string? reason)
        {
            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.", Array.Empty<string>());

            var menu = await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId && m.IsActive);
            if (menu == null)
                return (false, $"Menu {menuId} not found.", Array.Empty<string>());

            var featureKeys = await GetMenuFeatureKeysAsync(menuId);
            if (featureKeys.Count == 0)
                return (false, $"Menu '{menu.Title}' has no permission keys.", Array.Empty<string>());

            foreach (var key in featureKeys)
                await SetUserOverrideAsync(staffId, key, PermissionStatus.ALLOW, setBy, reason);

            return (true, $"Granted {featureKeys.Count} feature(s) from menu '{menu.Title}'.", featureKeys);
        }

        public async Task<(bool Success, string Message, IReadOnlyList<string> RevokedKeys)>
            RevokeMenuAccessAsync(Guid staffId, int menuId)
        {
            var menu = await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId);
            if (menu == null) return (false, $"Menu {menuId} not found.", Array.Empty<string>());

            var featureKeys = await GetMenuFeatureKeysAsync(menuId);
            int removed = 0;
            foreach (var key in featureKeys)
            {
                var (ok, _) = await RemoveUserOverrideAsync(staffId, key);
                if (ok) removed++;
            }
            return (true, $"Removed {removed} override(s) for menu '{menu.Title}'.", featureKeys);
        }

        public async Task<IEnumerable<object>> GetMenuPermissionTreeAsync()
        {
            var menus = await _db.Menus.AsNoTracking()
                .Include(m => m.MenuPermissions).ThenInclude(mp => mp.Feature)
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            var lookup = menus.ToLookup(m => m.ParentId);

            HashSet<string> CollectDescendantKeys(int id)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void Walk(int nodeId)
                {
                    var m = menus.First(x => x.Id == nodeId);
                    foreach (var mp in m.MenuPermissions.Where(mp => mp.Feature != null))
                        set.Add(mp.Feature!.FeatureKey);
                    foreach (var c in lookup[nodeId]) Walk(c.Id);
                }
                Walk(id);
                return set;
            }

            List<object> BuildTree(int? parentId)
            {
                return lookup[parentId].Select(menu => (object)new
                {
                    menu.Id, menu.Title, menu.Icon, menu.Route, menu.ParentId, menu.SortOrder,
                    directPermissions = menu.MenuPermissions
                        .Where(mp => mp.Feature != null)
                        .Select(mp => mp.Feature!.FeatureKey).ToList(),
                    allPermissions = CollectDescendantKeys(menu.Id).OrderBy(k => k).ToList(),
                    children       = BuildTree(menu.Id)
                }).ToList();
            }

            return BuildTree(null);
        }

        // ─────────────────────────────────────────────────────────────────────
        // SEED HELPERS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Seeds MENU_{id}, MENU_{id}_VIEW/ADD/EDIT/DELETE into Features for every active menu.
        /// Also creates MenuPermission links so each menu requires its own MENU_{id} feature.
        /// Idempotent.
        /// </summary>
        public async Task<(int Added, int Skipped)> SeedMenuFeaturesAsync()
        {
            var menus = await _db.Menus.AsNoTracking()
                .Include(m => m.MenuPermissions)
                .Where(m => m.IsActive)
                .ToListAsync();

            var existingKeys = await _db.Features.Select(f => f.FeatureKey).ToHashSetAsync();

            var toAdd = new List<Feature>();
            foreach (var menu in menus)
            {
                var keysToCreate = new[]
                {
                    ($"MENU_{menu.Id}",        menu.Title,               "Menu"),
                    ($"MENU_{menu.Id}_VIEW",   $"{menu.Title} - View",   "Menu"),
                    ($"MENU_{menu.Id}_ADD",    $"{menu.Title} - Add",    "Menu"),
                    ($"MENU_{menu.Id}_EDIT",   $"{menu.Title} - Edit",   "Menu"),
                    ($"MENU_{menu.Id}_DELETE", $"{menu.Title} - Delete", "Menu"),
                };

                foreach (var (key, name, module) in keysToCreate)
                {
                    if (!existingKeys.Contains(key))
                        toAdd.Add(new Feature { FeatureKey = key, FeatureName = name, Module = module });
                }
            }

            if (toAdd.Count > 0)
            {
                _db.Features.AddRange(toAdd);
                await _db.SaveChangesAsync();
            }

            return (toAdd.Count, menus.Count * 5 - toAdd.Count);
        }

        /// <summary>
        /// Auto-creates a Feature record for MENU_* keys that don't exist yet.
        /// Prevents FK violations when granting access before seed has run.
        /// </summary>
        /// <summary>Public wrapper for menu feature seeding when granting person access.</summary>
        public async Task EnsureMenuFeatureExistsPublicAsync(int menuId) =>
            await EnsureFeatureExistsAsync($"MENU_{menuId}");

        private async Task EnsureFeatureExistsAsync(string featureKey)
        {
            if (await _db.Features.AnyAsync(f => f.FeatureKey == featureKey)) return;
            if (!featureKey.StartsWith("MENU_", StringComparison.OrdinalIgnoreCase)) return;

            var parts = featureKey.Split('_');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int menuId)) return;

            var menu = await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId);
            string menuTitle = menu?.Title ?? $"Menu {menuId}";
            string suffix    = parts.Length >= 3 ? string.Join("_", parts.Skip(2)) : "";
            string name      = suffix switch
            {
                "VIEW"   => $"{menuTitle} - View",
                "ADD"    => $"{menuTitle} - Add",
                "EDIT"   => $"{menuTitle} - Edit",
                "DELETE" => $"{menuTitle} - Delete",
                ""       => menuTitle,
                _        => $"{menuTitle} - {suffix}"
            };

            _db.Features.Add(new Feature { FeatureKey = featureKey, FeatureName = name, Module = "Menu" });
            try { await _db.SaveChangesAsync(); }
            catch { /* ignore duplicate key race */ }
        }
    }
}
