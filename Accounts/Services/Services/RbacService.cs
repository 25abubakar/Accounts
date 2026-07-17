using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Hierarchical RBAC Engine — uses the 2-tier StaffMenuAccess + AccessFeatures system.
    ///
    /// Permission resolution order (highest priority first):
    ///   1. StaffMenuAccess (IsAllow=false)  → menu explicitly denied → all features denied
    ///   2. AccessFeature   (IsAllow=false)  → feature explicitly denied for this menu grant
    ///   3. AccessFeature   (IsAllow=true)   → feature explicitly allowed
    ///   4. StaffMenuAccess (IsAllow=true, no AccessFeature row) → all menu features allowed
    ///   5. RolePermission (dept-specific)   → IsAllowed value (legacy fallback)
    ///   6. RolePermission (global)          → IsAllowed value (legacy fallback)
    ///   7. DepartmentAccessMatrix           → HasAccess value (legacy fallback)
    ///   8. false                            → deny by default
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
        /// Checks new 2-tier RBAC (StaffMenuAccess + AccessFeatures) first,
        /// then falls back to legacy RolePermissions / DepartmentAccessMatrix.
        /// </summary>
        public async Task<bool> HasAccessAsync(Guid staffId, string featureKey)
        {
            // Resolve the PermissionId once
            var permissionId = await _db.Features
                .AsNoTracking()
                .Where(f => f.FeatureKey == featureKey)
                .Select(f => (int?)f.PermissionId)
                .FirstOrDefaultAsync();

            if (permissionId == null) return false;

            // ── Check new 2-tier RBAC ─────────────────────────────────────────
            // Load all menu grants for this staff + their feature rows
            var menuGrants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.AccessFeatures)
                .Where(ma => ma.StaffId == staffId && ma.IsAllow)
                .ToListAsync();

            foreach (var grant in menuGrants)
            {
                var featureRow = grant.AccessFeatures.FirstOrDefault(af => af.PermissionId == permissionId);
                if (featureRow != null)
                    return featureRow.IsAllow;
                // Grant exists with no specific feature row → all features allowed for this menu
                return true;
            }

            // ── Legacy fallback: RolePermissions + DepartmentAccessMatrix ─────
            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (staff == null) return false;

            var jobTitle = staff.Vacancy?.ResolvedJobTitle;
            var deptId   = staff.Vacancy?.OrganizationId;

            if (!string.IsNullOrWhiteSpace(jobTitle))
            {
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

                var globalRolePerm = await _db.RolePermissions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.JobTitle    == jobTitle &&
                        r.DeptId      == null     &&
                        r.PermissionId == permissionId);
                if (globalRolePerm != null) return globalRolePerm.IsAllowed;
            }

            var matrixRow = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.StaffId == staffId && m.PermissionId == permissionId);

            return matrixRow?.HasAccess == true;
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
        /// Resolves overview permission keys for every visible staff member with a
        /// fixed number of set-based queries. This replaces two API calls plus
        /// several permission queries per row on Staff Access Overview.
        /// </summary>
        public async Task<IReadOnlyDictionary<Guid, IReadOnlyCollection<string>>> GetStaffAccessOverviewAsync()
        {
            var staff = await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.PersonId.HasValue)
                .Select(s => new
                {
                    s.StaffId,
                    DepartmentId = s.Vacancy != null ? (int?)s.Vacancy.OrganizationId : null,
                    JobTitle = s.Vacancy != null
                        ? (s.Vacancy.JobTitleNav != null ? s.Vacancy.JobTitleNav.TitleName : s.Vacancy.JobTitle)
                        : null
                }).ToListAsync();
            var staffIds = staff.Select(s => s.StaffId).ToArray();
            if (staffIds.Length == 0) return new Dictionary<Guid, IReadOnlyCollection<string>>();

            var grants = await _db.StaffMenuAccesses.AsNoTracking()
                .Where(g => staffIds.Contains(g.StaffId) && g.IsAllow)
                .Select(g => new { g.Id, g.StaffId, g.MenuId }).ToListAsync();
            var grantIds = grants.Select(g => g.Id).ToArray();
            var accessFeatures = await _db.AccessFeatures.AsNoTracking()
                .Where(f => grantIds.Contains(f.StaffMenuAccessId))
                .Select(f => new { f.StaffMenuAccessId, f.PermissionId, f.IsAllow }).ToListAsync();

            var features = await _db.Features.AsNoTracking()
                .Select(f => new { f.PermissionId, f.FeatureKey }).ToDictionaryAsync(f => f.PermissionId, f => f.FeatureKey);
            var staffWithGrants = grants.Select(g => g.StaffId).ToHashSet();
            var legacyStaff = staff.Where(s => !staffWithGrants.Contains(s.StaffId)).ToList();
            var legacyTitles = legacyStaff.Select(s => s.JobTitle).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToArray();
            var roleRows = await _db.RolePermissions.AsNoTracking()
                .Where(r => legacyTitles.Contains(r.JobTitle))
                .Select(r => new { r.JobTitle, r.DeptId, r.PermissionId, r.IsAllowed }).ToListAsync();
            var legacyIds = legacyStaff.Select(s => s.StaffId).ToArray();
            var matrixRows = await _db.DepartmentAccessMatrix.AsNoTracking()
                .Where(m => legacyIds.Contains(m.StaffId) && m.HasAccess)
                .Select(m => new { m.StaffId, m.PermissionId }).ToListAsync();

            var grantFeatures = accessFeatures.ToLookup(f => f.StaffMenuAccessId);
            var grantsByStaff = grants.ToLookup(g => g.StaffId);
            var roleByTitle = roleRows.ToLookup(r => r.JobTitle, StringComparer.OrdinalIgnoreCase);
            var matrixByStaff = matrixRows.ToLookup(m => m.StaffId);
            var result = new Dictionary<Guid, IReadOnlyCollection<string>>(staff.Count);

            foreach (var employee in staff)
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var employeeGrants = grantsByStaff[employee.StaffId].ToList();
                if (employeeGrants.Count > 0)
                {
                    foreach (var grant in employeeGrants)
                    {
                        keys.Add($"MENU_{grant.MenuId}");
                        foreach (var feature in grantFeatures[grant.Id].Where(f => f.IsAllow))
                            if (features.TryGetValue(feature.PermissionId, out var key)) keys.Add(key);
                    }
                }
                else
                {
                    var effectiveRoles = roleByTitle[employee.JobTitle ?? string.Empty]
                        .Where(r => r.DeptId == null || r.DeptId == employee.DepartmentId)
                        .GroupBy(r => r.PermissionId)
                        .Select(group => group.OrderByDescending(r => r.DeptId.HasValue).First());
                    foreach (var role in effectiveRoles.Where(r => r.IsAllowed))
                        if (features.TryGetValue(role.PermissionId, out var key)) keys.Add(key);
                    foreach (var matrix in matrixByStaff[employee.StaffId])
                        if (features.TryGetValue(matrix.PermissionId, out var key)) keys.Add(key);
                }
                result[employee.StaffId] = keys;
            }
            return result;
        }

        /// <summary>
        /// Returns the HashSet of int PermissionIds the user is allowed to access.
        ///
        /// 2-tier RBAC resolution (StaffMenuAccess + AccessFeatures):
        ///   Grant with NO AccessFeature rows  → user can open that menu, but no
        ///     specific feature PermissionIds are granted via that path.  The menu
        ///     will still be visible because GetFilteredSidebarAsync checks grantedMenuIds
        ///     directly (not via PermissionIds).
        ///   Grant WITH AccessFeature rows    → only the explicitly-allowed PermissionIds
        ///     are added to the result set.
        ///
        /// Falls back to legacy RolePermissions + DepartmentAccessMatrix when no
        /// StaffMenuAccess rows exist.
        /// </summary>
        public async Task<HashSet<int>> GetEffectivePermissionIdsAsync(Guid staffId)
        {
            // ── 1. New 2-tier RBAC (StaffMenuAccess + AccessFeatures) ─────────
            var menuGrants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.AccessFeatures)
                .Where(ma => ma.StaffId == staffId && ma.IsAllow)
                .ToListAsync();

            if (menuGrants.Count > 0)
            {
                var result2Tier = new HashSet<int>();

                foreach (var grant in menuGrants)
                {
                    // Only add PermissionIds that were explicitly granted at the
                    // feature level.  A grant with NO AccessFeature rows gives menu
                    // visibility (handled in GetFilteredSidebarAsync by checking
                    // grantedMenuIds directly) but does not bulk-unlock every feature.
                    foreach (var af in grant.AccessFeatures.Where(af => af.IsAllow))
                        result2Tier.Add(af.PermissionId);
                }

                return result2Tier;
            }

            // ── 2. Legacy fallback: RolePermissions + DepartmentAccessMatrix ──
            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (staff == null) return new HashSet<int>();

            var jobTitle = staff.Vacancy?.ResolvedJobTitle;
            var deptId   = staff.Vacancy?.OrganizationId;

            var rolePermissions = string.IsNullOrWhiteSpace(jobTitle)
                ? new List<RolePermission>()
                : await _db.RolePermissions
                    .AsNoTracking()
                    .Where(r => r.JobTitle == jobTitle &&
                                (r.DeptId == null || r.DeptId == deptId))
                    .ToListAsync();

            var matrixAllowed = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .Where(m => m.StaffId == staffId && m.HasAccess)
                .Select(m => m.PermissionId)
                .ToHashSetAsync();

            var allFeatures = await _db.Features
                .AsNoTracking()
                .Select(f => new { f.PermissionId })
                .ToListAsync();

            var roleDeptLookup   = rolePermissions
                .Where(r => r.DeptId != null)
                .ToDictionary(r => r.PermissionId, r => r.IsAllowed);

            var roleGlobalLookup = rolePermissions
                .Where(r => r.DeptId == null)
                .ToDictionary(r => r.PermissionId, r => r.IsAllowed);

            var legacyResult = new HashSet<int>();
            foreach (var f in allFeatures)
            {
                var pid = f.PermissionId;
                if (!string.IsNullOrWhiteSpace(jobTitle))
                {
                    if (roleDeptLookup.TryGetValue(pid, out var deptAllowed))
                    {
                        if (deptAllowed) legacyResult.Add(pid);
                        continue;
                    }
                    if (roleGlobalLookup.TryGetValue(pid, out var globalAllowed))
                    {
                        if (globalAllowed) legacyResult.Add(pid);
                        continue;
                    }
                }
                if (matrixAllowed.Contains(pid)) legacyResult.Add(pid);
            }
            return legacyResult;
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

            var jobTitle = staff.Vacancy?.ResolvedJobTitle;
            var deptId   = staff.Vacancy?.OrganizationId;

            // ── New 2-tier RBAC grants ─────────────────────────────────────────
            var menuGrants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.AccessFeatures)
                .Where(ma => ma.StaffId == staffId && ma.IsAllow)
                .ToListAsync();

            // Build per-permissionId access map from new system
            var newSystemAllow = new HashSet<int>();
            var newSystemDeny  = new HashSet<int>();
            bool hasAnyGrant   = menuGrants.Count > 0;

            if (hasAnyGrant)
            {
                var allFeatureIds = features.Select(f => f.PermissionId).ToHashSet();
                foreach (var grant in menuGrants)
                {
                    if (!grant.AccessFeatures.Any())
                    {
                        foreach (var pid in allFeatureIds)
                            newSystemAllow.Add(pid);
                    }
                    else
                    {
                        foreach (var af in grant.AccessFeatures)
                        {
                            if (af.IsAllow) newSystemAllow.Add(af.PermissionId);
                            else            newSystemDeny.Add(af.PermissionId);
                        }
                    }
                }
            }

            // ── Legacy fallback ────────────────────────────────────────────────
            var rolePermissions = (!hasAnyGrant && !string.IsNullOrWhiteSpace(jobTitle))
                ? await _db.RolePermissions.AsNoTracking()
                    .Where(r => r.JobTitle == jobTitle && (r.DeptId == null || r.DeptId == deptId))
                    .ToListAsync()
                : new List<RolePermission>();

            var matrixAllowed = !hasAnyGrant
                ? await _db.DepartmentAccessMatrix.AsNoTracking()
                    .Where(m => m.StaffId == staffId && m.HasAccess)
                    .Select(m => m.PermissionId)
                    .ToHashSetAsync()
                : new HashSet<int>();

            var roleDeptLookup   = rolePermissions.Where(r => r.DeptId != null).ToDictionary(r => r.PermissionId, r => r.IsAllowed);
            var roleGlobalLookup = rolePermissions.Where(r => r.DeptId == null).ToDictionary(r => r.PermissionId, r => r.IsAllowed);

            var result = new List<object>();
            foreach (var f in features)
            {
                bool hasAccess;
                string source;

                if (hasAnyGrant)
                {
                    if (newSystemDeny.Contains(f.PermissionId))
                        (hasAccess, source) = (false, "MenuFeatureDeny");
                    else if (newSystemAllow.Contains(f.PermissionId))
                        (hasAccess, source) = (true, "MenuFeatureAllow");
                    else
                        (hasAccess, source) = (false, "NoGrant");
                }
                else
                {
                    (hasAccess, source) = ResolveFromRole(f.PermissionId, jobTitle, roleDeptLookup, roleGlobalLookup, matrixAllowed);
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
            HashSet<int> matrix)
        {
            if (!string.IsNullOrWhiteSpace(jobTitle))
            {
                if (roleDept.TryGetValue(permissionId, out var da))   return (da,   "RoleDefault");
                if (roleGlobal.TryGetValue(permissionId, out var ga)) return (ga,   "RoleDefault");
            }
            if (matrix.Contains(permissionId)) return (true, "Matrix");
            return (false, "Denied");
        }

        // ─────────────────────────────────────────────────────────────────────
        // SIDEBAR FILTERING (login / session endpoint)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the sidebar menu tree visible to this user.
        ///
        /// Pass Guid.Empty for SuperAdmin — they see every menu.
        ///
        /// For regular users the method works directly from StaffMenuAccess:
        ///   1. Load the MenuIds the user was explicitly granted (IsAllow = true).
        ///   2. Walk up the tree to include all ancestor (parent/grandparent) menus
        ///      so the tree structure is never broken.
        ///   3. Build a tree containing only those visible MenuIds.
        ///
        /// This approach is immune to the "no AccessFeature rows → unlock everything"
        /// bug because it never touches MenuPermissions or PermissionIds.
        /// </summary>
        public async Task<List<object>> GetFilteredSidebarAsync(Guid staffId)
        {
            // Load all active menus once (no MenuPermissions join needed)
            var allMenus = await _db.Menus
                .AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            var lookup = allMenus.ToLookup(m => m.ParentId);
            var byId   = allMenus.ToDictionary(m => m.Id);

            // SuperAdmin bypass — show everything
            if (staffId == Guid.Empty)
                return BuildFullTree(null, lookup);

            // ── Load explicitly granted menu IDs from StaffMenuAccess ─────────
            var grantedMenuIds = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Where(ma => ma.StaffId == staffId && ma.IsAllow)
                .Select(ma => ma.MenuId)
                .ToHashSetAsync();

            // No grants at all — return empty sidebar
            if (grantedMenuIds.Count == 0)
                return new List<object>();

            // ── Bubble up: include every ancestor so tree structure is intact ─
            // e.g. if MENU_8 (Staff) is granted, its parent "HR Management" must
            // also appear even if it wasn't explicitly granted.
            var visibleIds = new HashSet<int>(grantedMenuIds);
            foreach (var menuId in grantedMenuIds)
            {
                var current = byId.GetValueOrDefault(menuId);
                while (current?.ParentId != null && byId.TryGetValue(current.ParentId.Value, out var parent))
                {
                    visibleIds.Add(parent.Id);
                    current = parent;
                }
            }

            // ── Build the filtered tree from only visible IDs ─────────────────
            var filteredLookup = allMenus
                .Where(m => visibleIds.Contains(m.Id))
                .ToLookup(m => m.ParentId);

            return BuildFullTree(null, filteredLookup);
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

            // ── New 2-tier RBAC: load StaffMenuAccess + AccessFeatures ────────
            var allMenuGrants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.AccessFeatures)
                .Where(ma => allStaffIds.Contains(ma.StaffId) && ma.IsAllow)
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
                // Check new 2-tier RBAC first
                var grants = allMenuGrants.Where(ma => ma.StaffId == sid).ToList();
                if (grants.Count > 0)
                {
                    bool deny  = grants.Any(ma => ma.AccessFeatures.Any(af => af.PermissionId == permId && !af.IsAllow));
                    bool allow = grants.Any(ma => !ma.AccessFeatures.Any() ||
                                                  ma.AccessFeatures.Any(af => af.PermissionId == permId && af.IsAllow));
                    if (deny)  return new { effectiveAccess = false, source = "MenuFeatureDeny",  hasOverride = true };
                    if (allow) return new { effectiveAccess = true,  source = "MenuFeatureAllow", hasOverride = true };
                }

                // Legacy fallback
                var rp = allRolePerms.FirstOrDefault(r => r.JobTitle == jt && r.DeptId == sDeptId && r.PermissionId == permId);
                if (rp != null) return new { effectiveAccess = rp.IsAllowed, source = "RoleDefault", hasOverride = false };

                var rpG = allRolePerms.FirstOrDefault(r => r.JobTitle == jt && r.DeptId == null && r.PermissionId == permId);
                if (rpG != null) return new { effectiveAccess = rpG.IsAllowed, source = "RoleDefault", hasOverride = false };

                var mx = allMatrixRows.FirstOrDefault(m => m.StaffId == sid && m.PermissionId == permId);
                if (mx != null) return new { effectiveAccess = mx.HasAccess, source = "Matrix", hasOverride = false };

                return new { effectiveAccess = false, source = "Denied", hasOverride = false };
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
        /// Bulk-replace the full permission set for a staff member.
        ///
        /// Uses a CLEAR-THEN-REBUILD pattern:
        ///   1. Delete every existing StaffMenuAccess row for this staff (cascades to AccessFeatures).
        ///   2. Loop the incoming payload; for every key with status == "ALLOW":
        ///        • MENU_{id}        → create a StaffMenuAccess row (IsAllow = true).
        ///        • MENU_{id}_SUFFIX → create an AccessFeature row under the parent grant.
        ///   3. INHERIT / DENY / anything else → skip entirely (do not insert).
        ///   4. Single SaveChangesAsync at the end.
        ///
        /// Safety rules:
        ///   - staffId must not be Guid.Empty
        ///   - MenuId must be > 0 AND present in the Menus table (FK guard)
        ///   - Non-MENU keys (e.g. "ACCESS_GROUP_ASSIGN") are silently skipped
        /// </summary>
        public async Task<(int Saved, int Skipped, string Message)> BulkApplyOverridesAsync(
            Guid staffId,
            IReadOnlyDictionary<string, string> overrides,
            string? setBy,
            string? reason = "Admin UI")
        {
            if (overrides == null || overrides.Count == 0)
                return (0, 0, "No overrides provided.");

            if (staffId == Guid.Empty)
                return (0, 0, "Invalid staffId (empty GUID).");

            if (!await _db.StaffVacancies.AsNoTracking().AnyAsync(s => s.StaffId == staffId))
                return (0, 0, "Staff not found.");

            // ── Step 1: Wipe all existing grants (cascade deletes AccessFeatures) ──
            var existingGrants = await _db.StaffMenuAccesses
                .Where(ma => ma.StaffId == staffId)
                .ToListAsync();

            if (existingGrants.Count > 0)
                _db.StaffMenuAccesses.RemoveRange(existingGrants);

            // ── Step 2: Pre-load valid menu IDs (FK guard) ────────────────────
            var validMenuIds = await _db.Menus.AsNoTracking()
                .Select(m => m.Id)
                .ToHashSetAsync();

            // Seed Feature rows for MENU_* keys that are ALLOW and reference real menus
            var allowedMenuKeys = overrides
                .Where(kv => kv.Value.Trim().Equals("ALLOW", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key.Trim())
                .Where(k => k.StartsWith("MENU_", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var key in allowedMenuKeys)
            {
                var p = key.Split('_');
                if (p.Length >= 2 && int.TryParse(p[1], out int kid) && kid > 0 && validMenuIds.Contains(kid))
                    await EnsureFeatureExistsAsync(key);
            }

            // Feature lookup (after seeding)
            var featureLookup = await _db.Features.AsNoTracking()
                .ToDictionaryAsync(f => f.FeatureKey.Trim(), f => f, StringComparer.OrdinalIgnoreCase);

            // ── Step 3: Rebuild — only insert explicit ALLOW entries ──────────
            // In-memory map: menuId → new StaffMenuAccess (not yet saved)
            var newGrantsByMenuId = new Dictionary<int, StaffMenuAccess>();
            var now     = DateTime.UtcNow;
            int saved   = 0;
            int skipped = 0;

            foreach (var (rawKey, statusStr) in overrides)
            {
                var trimmedKey  = rawKey.Trim();
                var upperStatus = statusStr.Trim().ToUpperInvariant();

                // Only process explicit ALLOW entries — skip INHERIT, DENY, empty
                if (upperStatus != "ALLOW") { skipped++; continue; }

                // Only handle MENU_* keys — non-MENU semantic keys are not stored
                // in StaffMenuAccess and can safely be ignored here
                if (!trimmedKey.StartsWith("MENU_", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++; continue;
                }

                var parts = trimmedKey.Split('_');
                // parts[0]="MENU", parts[1]=menuId, parts[2..]=optional suffix
                if (parts.Length < 2 || !int.TryParse(parts[1], out int menuId) || menuId <= 0)
                {
                    skipped++; continue;
                }

                // FK guard: menuId must exist in the Menus table
                if (!validMenuIds.Contains(menuId))
                {
                    skipped++; continue;
                }

                if (!featureLookup.TryGetValue(trimmedKey, out var feature))
                {
                    skipped++; continue;
                }

                bool isTopLevel = parts.Length == 2; // "MENU_8" (no suffix)

                if (isTopLevel)
                {
                    // Create the parent menu grant if it doesn't already exist in our map
                    if (!newGrantsByMenuId.ContainsKey(menuId))
                    {
                        var grant = new StaffMenuAccess
                        {
                            StaffId     = staffId,
                            MenuId      = menuId,
                            IsAllow     = true,
                            GrantedDate = now
                        };
                        _db.StaffMenuAccesses.Add(grant);
                        newGrantsByMenuId[menuId] = grant;
                    }
                    saved++;
                }
                else
                {
                    // Feature-level key (e.g. "MENU_8_VIEW")
                    // Ensure the parent grant exists first
                    if (!newGrantsByMenuId.TryGetValue(menuId, out var grant))
                    {
                        grant = new StaffMenuAccess
                        {
                            StaffId     = staffId,
                            MenuId      = menuId,
                            IsAllow     = true,
                            GrantedDate = now
                        };
                        _db.StaffMenuAccesses.Add(grant);
                        newGrantsByMenuId[menuId] = grant;
                    }

                    // Add the AccessFeature row (IsAllow = true — DENY rows are never inserted)
                    grant.AccessFeatures.Add(new AccessFeature
                    {
                        PermissionId = feature.PermissionId,
                        IsAllow      = true
                    });
                    saved++;
                }
            }

            // ── Step 4: Persist everything in one round-trip ─────────────────
            await _db.SaveChangesAsync();
            return (saved, skipped, $"{saved} permission(s) saved, {skipped} skipped.");
        }

        /// <summary>
        /// Replaces the same permission set for many staff members in one unit of work.
        /// Shared menu/feature metadata is loaded once, avoiding one HTTP request and
        /// several repeated database queries per selected user.
        /// </summary>
        public async Task<(int UsersUpdated, int Saved, int Skipped, string Message)> BulkApplyOverridesToStaffAsync(
            IReadOnlyCollection<Guid> requestedStaffIds,
            IReadOnlyDictionary<string, string> overrides,
            string? setBy)
        {
            var staffIds = requestedStaffIds.Where(id => id != Guid.Empty).Distinct().ToArray();
            if (staffIds.Length == 0) return (0, 0, 0, "No staff members were selected.");
            if (overrides == null || overrides.Count == 0) return (0, 0, 0, "No overrides provided.");

            var validStaffIds = await _db.StaffVacancies.AsNoTracking()
                .Where(s => staffIds.Contains(s.StaffId)).Select(s => s.StaffId).ToArrayAsync();
            if (validStaffIds.Length != staffIds.Length)
                return (0, 0, 0, "One or more selected staff members were not found.");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
            // A retry re-enters this delegate using the scoped DbContext. Clear any
            // entities left from the failed attempt before rebuilding the graph.
            _db.ChangeTracker.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var menus = await _db.Menus.AsNoTracking().Select(m => new { m.Id, m.Title }).ToListAsync();
            var menuById = menus.ToDictionary(m => m.Id);
            var allowedKeys = overrides
                .Where(pair => pair.Value.Trim().Equals("ALLOW", StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key.Trim().ToUpperInvariant())
                .Where(key => key.StartsWith("MENU_", StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var parsed = new List<(string Key, int MenuId, bool IsTopLevel)>();
            var skippedPerUser = overrides.Count - allowedKeys.Length;
            foreach (var key in allowedKeys)
            {
                var parts = key.Split('_');
                if (parts.Length < 2 || !int.TryParse(parts[1], out var menuId) || !menuById.ContainsKey(menuId))
                { skippedPerUser++; continue; }
                parsed.Add((key, menuId, parts.Length == 2));
            }

            // Seed any missing MENU_* features in a single save, not once per key/user.
            var featureIds = await _db.Features.AsNoTracking()
                .Where(f => allowedKeys.Contains(f.FeatureKey))
                .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId, StringComparer.OrdinalIgnoreCase);
            var missingFeatures = parsed.Where(item => !featureIds.ContainsKey(item.Key))
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .Select(item =>
                {
                    var suffix = item.Key.Split('_').Length >= 3 ? string.Join("_", item.Key.Split('_').Skip(2)) : "";
                    var title = menuById[item.MenuId].Title;
                    var name = suffix switch { "VIEW" => $"{title} - View", "ADD" => $"{title} - Add", "EDIT" => $"{title} - Edit", "DELETE" => $"{title} - Delete", "" => title, _ => $"{title} - {suffix}" };
                    return new Feature { FeatureKey = item.Key, FeatureName = name, Module = "Menu" };
                }).ToList();
            if (missingFeatures.Count > 0)
            {
                _db.Features.AddRange(missingFeatures);
                await _db.SaveChangesAsync();
                foreach (var feature in missingFeatures)
                    featureIds[feature.FeatureKey] = feature.PermissionId;
            }

            var permissionRows = parsed
                .Where(item => featureIds.ContainsKey(item.Key))
                .Select(item => new { item.MenuId, PermissionId = featureIds[item.Key], item.IsTopLevel })
                .Distinct()
                .ToArray();
            var saved = validStaffIds.Length * permissionRows.Length;
            await _db.Database.ExecuteSqlRawAsync(
                "EXEC dbo.usp_Rbac_ReplaceStaffAccess @StaffIdsJson, @PermissionsJson, @GrantedBy",
                new SqlParameter("@StaffIdsJson", JsonSerializer.Serialize(validStaffIds)),
                new SqlParameter("@PermissionsJson", JsonSerializer.Serialize(permissionRows)),
                new SqlParameter("@GrantedBy", (object?)setBy ?? DBNull.Value));
            await transaction.CommitAsync();
            var skipped = skippedPerUser * validStaffIds.Length;
            return (validStaffIds.Length, saved, skipped, $"Access updated for {validStaffIds.Length} user(s).");
            });
        }

        /// <summary>Remove every StaffMenuAccess row for a staff member (one query + one save).</summary>
        public async Task<int> ClearStaffOverridesAsync(Guid staffId)
        {
            var rows = await _db.StaffMenuAccesses
                .Where(ma => ma.StaffId == staffId)
                .ToListAsync();

            if (rows.Count == 0) return 0;

            _db.StaffMenuAccesses.RemoveRange(rows); // CASCADE deletes AccessFeatures
            await _db.SaveChangesAsync();
            return rows.Count;
        }

        /// <summary>
        /// Set a single permission override by FeatureKey string.
        /// MENU_{id} keys control the menu grant; MENU_{id}_* keys control features within a grant.
        /// Non-MENU keys (e.g. ACCESS_GROUP_ASSIGN) are attached as AccessFeature rows on an
        /// existing menu grant, or rejected if no grant exists.
        ///
        /// Safety:
        ///   - staffId must not be Guid.Empty
        ///   - For MENU_* keys, menuId must be > 0 AND exist in the Menus table
        /// </summary>
        public async Task<(bool Success, string Message)> SetUserOverrideAsync(
            Guid staffId, string featureKey, PermissionStatus status, string? setBy, string? reason)
        {
            if (staffId == Guid.Empty)
                return (false, "Invalid staffId (empty GUID).");

            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.");

            bool isMenuKey = featureKey.StartsWith("MENU_", StringComparison.OrdinalIgnoreCase);

            if (isMenuKey)
            {
                // Parse MENU_{menuId} or MENU_{menuId}_SUFFIX
                var parts = featureKey.Split('_');
                if (parts.Length < 2 || !int.TryParse(parts[1], out int menuId) || menuId <= 0)
                    return (false, $"Invalid feature key format: '{featureKey}'. Expected MENU_{{id}} or MENU_{{id}}_SUFFIX.");

                // Guard: MenuId must exist in the Menus table
                if (!await _db.Menus.AnyAsync(m => m.Id == menuId))
                    return (false, $"Menu {menuId} does not exist. Cannot create a menu grant for a non-existent menu.");

                await EnsureFeatureExistsAsync(featureKey);

                var feature = await _db.Features.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.FeatureKey == featureKey);

                if (feature == null)
                    return (false, $"Feature '{featureKey}' could not be created or found.");

                bool isTopLevel = parts.Length == 2;

                var grant = await _db.StaffMenuAccesses
                    .Include(ma => ma.AccessFeatures)
                    .FirstOrDefaultAsync(ma => ma.StaffId == staffId && ma.MenuId == menuId);

                if (isTopLevel)
                {
                    if (status == PermissionStatus.INHERIT)
                    {
                        if (grant != null)
                        {
                            _db.StaffMenuAccesses.Remove(grant); // CASCADE deletes AccessFeatures
                            await _db.SaveChangesAsync();
                        }
                        return (true, $"Menu grant removed — '{featureKey}' reverted to no access.");
                    }

                    bool isAllow = status == PermissionStatus.ALLOW;
                    if (grant == null)
                    {
                        _db.StaffMenuAccesses.Add(new StaffMenuAccess
                        {
                            StaffId = staffId, MenuId = menuId, IsAllow = isAllow, GrantedDate = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        grant.IsAllow = isAllow;
                    }
                    await _db.SaveChangesAsync();
                    return (true, $"Menu grant set: '{featureKey}' = {status} for staff {staffId}.");
                }
                else
                {
                    // Feature-level override within a menu grant
                    if (grant == null)
                    {
                        if (status == PermissionStatus.INHERIT)
                            return (true, "No grant exists; nothing to remove.");

                        grant = new StaffMenuAccess
                        {
                            StaffId = staffId, MenuId = menuId, IsAllow = true, GrantedDate = DateTime.UtcNow
                        };
                        _db.StaffMenuAccesses.Add(grant);
                    }

                    var af = grant.AccessFeatures.FirstOrDefault(x => x.PermissionId == feature.PermissionId);
                    if (status == PermissionStatus.INHERIT)
                    {
                        if (af != null) { _db.AccessFeatures.Remove(af); grant.AccessFeatures.Remove(af); }
                    }
                    else
                    {
                        bool isAllow = status == PermissionStatus.ALLOW;
                        if (af == null)
                            grant.AccessFeatures.Add(new AccessFeature { PermissionId = feature.PermissionId, IsAllow = isAllow });
                        else
                            af.IsAllow = isAllow;
                    }
                    await _db.SaveChangesAsync();
                    return (true, $"Feature override set: '{featureKey}' = {status} for staff {staffId}.");
                }
            }
            else
            {
                // Non-MENU system feature key (e.g. "ACCESS_GROUP_ASSIGN")
                var feature = await _db.Features.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.FeatureKey == featureKey);

                if (feature == null)
                    return (false, $"Feature '{featureKey}' not found. Use GET /api/access/features to see valid keys.");

                // Attach to an existing active menu grant
                var parentGrant = await _db.StaffMenuAccesses
                    .Include(ma => ma.AccessFeatures)
                    .Where(ma => ma.StaffId == staffId && ma.IsAllow)
                    .FirstOrDefaultAsync(ma => ma.AccessFeatures.Any(af => af.PermissionId == feature.PermissionId))
                    ?? await _db.StaffMenuAccesses
                        .Include(ma => ma.AccessFeatures)
                        .FirstOrDefaultAsync(ma => ma.StaffId == staffId && ma.IsAllow);

                if (parentGrant == null)
                    return (false, $"No active menu grant found for staff {staffId}. Grant at least one menu first.");

                var af = parentGrant.AccessFeatures.FirstOrDefault(x => x.PermissionId == feature.PermissionId);
                if (status == PermissionStatus.INHERIT)
                {
                    if (af != null) { _db.AccessFeatures.Remove(af); parentGrant.AccessFeatures.Remove(af); }
                }
                else
                {
                    bool isAllow = status == PermissionStatus.ALLOW;
                    if (af == null)
                        parentGrant.AccessFeatures.Add(new AccessFeature { PermissionId = feature.PermissionId, IsAllow = isAllow });
                    else
                        af.IsAllow = isAllow;
                }
                await _db.SaveChangesAsync();
                return (true, $"Feature override set: '{featureKey}' = {status} for staff {staffId}.");
            }
        }

        public async Task<(bool Success, string Message)> RemoveUserOverrideAsync(Guid staffId, string featureKey)
        {
            var parts = featureKey.Split('_');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int menuId))
                return (false, $"Invalid key '{featureKey}'.");

            bool isTopLevel = parts.Length == 2;
            var grant = await _db.StaffMenuAccesses
                .Include(ma => ma.AccessFeatures)
                .FirstOrDefaultAsync(ma => ma.StaffId == staffId && ma.MenuId == menuId);

            if (grant == null) return (false, "No access grant found for this menu.");

            if (isTopLevel)
            {
                _db.StaffMenuAccesses.Remove(grant);
            }
            else
            {
                var feature = await _db.Features.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.FeatureKey == featureKey);
                if (feature == null) return (false, "Feature not found.");
                var af = grant.AccessFeatures.FirstOrDefault(x => x.PermissionId == feature.PermissionId);
                if (af == null) return (false, "Feature override not found.");
                _db.AccessFeatures.Remove(af);
            }
            await _db.SaveChangesAsync();
            return (true, $"Override removed — '{featureKey}' reverted.");
        }

        public async Task<IEnumerable<object>> GetUserOverridesAsync(Guid staffId) =>
            await _db.StaffMenuAccesses.AsNoTracking()
                .Include(ma => ma.AccessFeatures).ThenInclude(af => af.Feature)
                .Include(ma => ma.Menu)
                .Where(ma => ma.StaffId == staffId)
                .OrderBy(ma => ma.MenuId)
                .Select(ma => new
                {
                    menuId      = ma.MenuId,
                    menuTitle   = ma.Menu != null ? ma.Menu.Title : $"Menu {ma.MenuId}",
                    isAllow     = ma.IsAllow,
                    grantedDate = ma.GrantedDate,
                    features    = ma.AccessFeatures.Select(af => new
                    {
                        permissionId = af.PermissionId,
                        featureKey   = af.Feature != null ? af.Feature.FeatureKey   : string.Empty,
                        featureName  = af.Feature != null ? af.Feature.FeatureName  : string.Empty,
                        af.IsAllow
                    }).ToList()
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

            var payload = featureKeys.ToDictionary(k => k, _ => "ALLOW", StringComparer.OrdinalIgnoreCase);
            await BulkApplyOverridesAsync(staffId, payload, setBy, reason);

            return (true, $"Granted {featureKeys.Count} feature(s) from menu '{menu.Title}'.", featureKeys);
        }

        public async Task<(bool Success, string Message, IReadOnlyList<string> RevokedKeys)>
            RevokeMenuAccessAsync(Guid staffId, int menuId)
        {
            var menu = await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId);
            if (menu == null) return (false, $"Menu {menuId} not found.", Array.Empty<string>());

            var featureKeys = await GetMenuFeatureKeysAsync(menuId);
            if (featureKeys.Count == 0)
                return (true, $"No feature keys linked to menu '{menu.Title}'.", featureKeys);

            var payload = featureKeys.ToDictionary(k => k, _ => "INHERIT", StringComparer.OrdinalIgnoreCase);
            await BulkApplyOverridesAsync(staffId, payload, null, "Menu revoke");

            return (true, $"Removed override(s) for menu '{menu.Title}'.", featureKeys);
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
            featureKey = featureKey.Trim().ToUpperInvariant();

            // Reuse an entity already added by this DbContext. Without this check,
            // repeated keys in one unit of work can queue duplicate INSERTs.
            if (_db.Features.Local.Any(f =>
                    f.FeatureKey.Equals(featureKey, StringComparison.OrdinalIgnoreCase))) return;

            if (await _db.Features.AsNoTracking().AnyAsync(f => f.FeatureKey == featureKey)) return;

            // Only auto-create Feature rows for MENU_* keys
            if (!featureKey.StartsWith("MENU_", StringComparison.OrdinalIgnoreCase)) return;

            var parts = featureKey.Split('_');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int menuId) || menuId <= 0) return;

            // ── Guard: only create Feature rows for menus that actually exist ──
            // If the menu doesn't exist in the Menus table we would later try to
            // insert a StaffMenuAccess row with that MenuId and hit the FK constraint.
            var menu = await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId);
            if (menu == null) return; // menu doesn't exist — do not create the Feature row

            string suffix = parts.Length >= 3 ? string.Join("_", parts.Skip(2)) : "";
            string name   = suffix switch
            {
                "VIEW"   => $"{menu.Title} - View",
                "ADD"    => $"{menu.Title} - Add",
                "EDIT"   => $"{menu.Title} - Edit",
                "DELETE" => $"{menu.Title} - Delete",
                ""       => menu.Title,
                _        => $"{menu.Title} - {suffix}"
            };

            var pendingFeature = new Feature { FeatureKey = featureKey, FeatureName = name, Module = "Menu" };
            _db.Features.Add(pendingFeature);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is Microsoft.Data.SqlClient.SqlException sql &&
                (sql.Number == 2601 || sql.Number == 2627))
            {
                // Another request inserted the same unique FeatureKey after our
                // existence check. Detaching is essential: leaving the failed
                // Added entity tracked makes the caller's next SaveChanges retry
                // the duplicate INSERT (the MENU_5045 failure).
                _db.Entry(pendingFeature).State = EntityState.Detached;
            }
        }
    }
}
