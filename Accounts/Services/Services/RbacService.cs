
using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Hierarchical RBAC Engine — 3-layer resolution, integer PermissionId FKs.
    ///
    /// Permission resolution order (highest priority first):
    ///   1. UserPermissionOverride DENY   → FALSE (hard-stop)
    ///   2. UserPermissionOverride ALLOW  → TRUE
    ///   3. RolePermissions (dept-specific beats global) → IsAllowed value
    ///   4. FALSE — deny by default
    ///
    /// DENY always wins over everything.
    /// All bulk methods load data in a fixed number of queries and resolve in-memory.
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
            var permissionId = await _db.Features
              .AsNoTracking()
              .Where(f => f.FeatureKey == featureKey)
              .Select(f => (int?)f.PermissionId)
              .FirstOrDefaultAsync();

            if (permissionId == null) return false;

            // Q1: Check user-level override first (highest priority)
            var userOverride = await _db.UserPermissionOverrides
        .AsNoTracking()
        .Where(u => u.StaffId == staffId && u.PermissionId == permissionId)
        .Select(u => u.Status)
        .FirstOrDefaultAsync();

            if (userOverride != null)
            {
                if (userOverride == nameof(PermissionStatus.DENY)) return false;
                if (userOverride == nameof(PermissionStatus.ALLOW)) return true;
                // INHERIT → fall through to role
            }

            // Q2: Load staff role/dept
            var staff = await _db.StaffVacancies
        .AsNoTracking()
        .Where(s => s.StaffId == staffId)
        .Select(s => new
        {
            JobTitle = s.Vacancy != null ? s.Vacancy.JobTitle : null,
            DeptId = s.Vacancy != null ? (int?)s.Vacancy.OrganizationId : null
        })
        .FirstOrDefaultAsync();

            if (staff == null) return false;

            if (!string.IsNullOrWhiteSpace(staff.JobTitle))
            {
                // Dept-specific role permission (takes precedence over global)
                if (staff.DeptId.HasValue)
                {
                    var deptRolePerm = await _db.RolePermissions
                      .AsNoTracking()
                      .FirstOrDefaultAsync(r =>
                        r.JobTitle == staff.JobTitle &&
                        r.DeptId == staff.DeptId &&
                        r.PermissionId == permissionId);

                    if (deptRolePerm != null) return deptRolePerm.IsAllowed;
                }

                // Global role permission (DeptId = null)
                var globalRolePerm = await _db.RolePermissions
          .AsNoTracking()
          .FirstOrDefaultAsync(r =>
            r.JobTitle == staff.JobTitle &&
            r.DeptId == null &&
            r.PermissionId == permissionId);

                if (globalRolePerm != null) return globalRolePerm.IsAllowed;
            }

            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // BULK PERMISSION LOAD
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the set of FeatureKey strings the user is allowed to access.
        /// </summary>
        public async Task<IEnumerable<string>> GetEffectivePermissionsAsync(Guid staffId)
        {
            var allowedIds = await GetEffectivePermissionIdsAsync(staffId);
            if (allowedIds.Count == 0) return Array.Empty<string>();

            return await _db.Features
              .AsNoTracking()
              .Where(f => allowedIds.Contains(f.PermissionId))
              .Select(f => f.FeatureKey)
              .ToListAsync();
        }

        /// <summary>
        /// Returns the HashSet of int PermissionIds the user is allowed to access.
        /// 3-layer resolution: UserOverrides → RolePermissions → deny.
        /// </summary>
        public async Task<HashSet<int>> GetEffectivePermissionIdsAsync(Guid staffId)
        {
            // Q1: Load staff role / dept
            var staffInfo = await _db.StaffVacancies
        .AsNoTracking()
        .Where(s => s.StaffId == staffId)
        .Select(s => new
        {
            JobTitle = s.Vacancy != null ? s.Vacancy.JobTitle : null,
            DeptId = s.Vacancy != null ? (int?)s.Vacancy.OrganizationId : null
        })
        .FirstOrDefaultAsync();

            if (staffInfo == null) return new HashSet<int>();

            // Q2: Role permissions for this job title (both dept-scoped and global)
            var rolePermissions = string.IsNullOrWhiteSpace(staffInfo.JobTitle)
        ? new List<RolePermission>()
        : await _db.RolePermissions
          .AsNoTracking()
          .Where(r => r.JobTitle == staffInfo.JobTitle &&
                (r.DeptId == null || r.DeptId == staffInfo.DeptId))
          .ToListAsync();

            // Collapse: dept-specific beats global for the same PermissionId
            var roleAllowed = new HashSet<int>();
            var hasDeptRule = new HashSet<int>();

            foreach (var r in rolePermissions.Where(r => r.DeptId != null))
            {
                hasDeptRule.Add(r.PermissionId);
                if (r.IsAllowed) roleAllowed.Add(r.PermissionId);
                else roleAllowed.Remove(r.PermissionId);
            }
            foreach (var r in rolePermissions.Where(r => r.DeptId == null))
            {
                if (!hasDeptRule.Contains(r.PermissionId) && r.IsAllowed)
                    roleAllowed.Add(r.PermissionId);
            }

            // Q3: User-level overrides
            var userOverrides = await _db.UserPermissionOverrides
        .AsNoTracking()
        .Where(u => u.StaffId == staffId)
        .Select(u => new { u.PermissionId, u.Status })
        .ToListAsync();

            // Start from role baseline, apply overrides
            var allowedIds = new HashSet<int>(roleAllowed);

            foreach (var uo in userOverrides)
            {
                if (uo.Status == nameof(PermissionStatus.DENY))
                    allowedIds.Remove(uo.PermissionId);
                else if (uo.Status == nameof(PermissionStatus.ALLOW))
                    allowedIds.Add(uo.PermissionId);
                // INHERIT → no action
            }

            return allowedIds;
        }

        // ─────────────────────────────────────────────────────────────────────
        // DETAILED PERMISSION VIEW (admin / debug)
        // ─────────────────────────────────────────────────────────────────────

        public async Task<IEnumerable<object>> GetEffectivePermissionsDetailedAsync(Guid staffId)
        {
            var features = await _db.Features.AsNoTracking()
              .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
              .ToListAsync();

            var staffInfo = await _db.StaffVacancies
              .AsNoTracking()
              .Where(s => s.StaffId == staffId)
              .Select(s => new
              {
                  JobTitle = s.Vacancy != null ? s.Vacancy.JobTitle : null,
                  DeptId = s.Vacancy != null ? (int?)s.Vacancy.OrganizationId : null
              })
              .FirstOrDefaultAsync();

            if (staffInfo == null)
                return features.Select(f => new
                {
                    f.FeatureKey,
                    f.FeatureName,
                    f.Module,
                    hasAccess = false,
                    source = "StaffNotFound"
                }).ToList<object>();

            var userOverrides = await _db.UserPermissionOverrides.AsNoTracking()
              .Where(u => u.StaffId == staffId)
              .ToDictionaryAsync(u => u.PermissionId, u => u.Status);

            var rolePermissions = string.IsNullOrWhiteSpace(staffInfo.JobTitle)
              ? new List<RolePermission>()
              : await _db.RolePermissions.AsNoTracking()
                .Where(r => r.JobTitle == staffInfo.JobTitle &&
                      (r.DeptId == null || r.DeptId == staffInfo.DeptId))
                .ToListAsync();

            var roleDeptLookup = rolePermissions
              .Where(r => r.DeptId != null)
              .ToDictionary(r => r.PermissionId, r => r.IsAllowed);
            var roleGlobalLookup = rolePermissions
              .Where(r => r.DeptId == null)
              .ToDictionary(r => r.PermissionId, r => r.IsAllowed);

            var result = new List<object>();
            foreach (var f in features)
            {
                bool hasAccess;
                string source;

                if (userOverrides.TryGetValue(f.PermissionId, out var status))
                {
                    if (status == nameof(PermissionStatus.DENY))
                        (hasAccess, source) = (false, "UserDeny");
                    else if (status == nameof(PermissionStatus.ALLOW))
                        (hasAccess, source) = (true, "UserAllow");
                    else
                        (hasAccess, source) = ResolveFromRole(f.PermissionId, roleDeptLookup, roleGlobalLookup);
                }
                else
                {
                    (hasAccess, source) = ResolveFromRole(f.PermissionId, roleDeptLookup, roleGlobalLookup);
                }

                result.Add(new { f.FeatureKey, f.FeatureName, f.Module, hasAccess, source });
            }
            return result;
        }

        private static (bool hasAccess, string source) ResolveFromRole(
          int permissionId,
          Dictionary<int, bool> roleDept,
          Dictionary<int, bool> roleGlobal)
        {
            if (roleDept.TryGetValue(permissionId, out var da)) return (da, "RoleDefault");
            if (roleGlobal.TryGetValue(permissionId, out var ga)) return (ga, "RoleDefault");
            return (false, "Denied");
        }

        // ─────────────────────────────────────────────────────────────────────
        // SIDEBAR FILTERING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the sidebar menu tree visible to this user.
        /// Pass Guid.Empty for SuperAdmin — they see everything.
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

            var allowedIds = await GetEffectivePermissionIdsAsync(staffId);
            return BuildFilteredTree(null, lookup, allowedIds);
        }

        private static List<object> BuildFullTree(int? parentId, ILookup<int?, Menu> lookup)
        {
            return lookup[parentId].Select(menu => (object)new
            {
                id = menu.Id,
                title = menu.Title,
                icon = menu.Icon,
                route = menu.Route,
                sortOrder = menu.SortOrder,
                children = BuildFullTree(menu.Id, lookup)
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

                bool hasRequiredPermissions = requiredIds.Any();
                bool userHasAccess = hasRequiredPermissions && requiredIds.Any(id => allowedIds.Contains(id));
                bool isFolder = string.IsNullOrWhiteSpace(menu.Route);

                // Default Deny logic: Unmapped pages are hidden. Folders are checked based on children.
                bool canSee = userHasAccess || (isFolder && !hasRequiredPermissions);

                if (!canSee) continue;

                var children = BuildFilteredTree(menu.Id, lookup, allowedIds);

                // Skip parent groups whose children are all hidden
                if (!children.Any() && isFolder)
                    continue;

                result.Add(new
                {
                    id = menu.Id,
                    title = menu.Title,
                    icon = menu.Icon,
                    route = menu.Route,
                    sortOrder = menu.SortOrder,
                    children
                });
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ROLE PERMISSION MANAGEMENT
        // ─────────────────────────────────────────────────────────────────────

        public async Task<(int Saved, List<string> InvalidKeys)> SetRolePermissionsAsync(
      string jobTitle, int? deptId, Dictionary<string, bool> permissions, string? setBy)
        {
            var featureMap = await _db.Features.AsNoTracking()
              .Where(f => permissions.Keys.Contains(f.FeatureKey))
              .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            var invalidKeys = permissions.Keys.Where(k => !featureMap.ContainsKey(k)).ToList();

            int count = 0;
            foreach (var (featureKey, isAllowed) in permissions)
            {
                if (!featureMap.TryGetValue(featureKey, out int permId)) continue;

                var existing = await _db.RolePermissions
                  .FirstOrDefaultAsync(r =>
                    r.JobTitle == jobTitle &&
                    r.DeptId == deptId &&
                    r.PermissionId == permId);

                if (existing == null)
                    _db.RolePermissions.Add(new RolePermission
                    {
                        JobTitle = jobTitle,
                        DeptId = deptId,
                        PermissionId = permId,
                        IsAllowed = isAllowed
                    });
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
                  r.Id,
                  r.JobTitle,
                  r.DeptId,
                  r.Feature!.FeatureKey,
                  r.Feature.FeatureName,
                  r.IsAllowed,
                  r.PermissionId
              })
              .ToListAsync<object>();
        }

        // ─────────────────────────────────────────────────────────────────────
        // USER OVERRIDE MANAGEMENT
        // ─────────────────────────────────────────────────────────────────────

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
                    StaffId = staffId,
                    PermissionId = feature.PermissionId,
                    Status = status.ToString(),
                    SetBy = setBy,
                    SetDate = DateTime.Now,
                    Reason = reason
                });
            }
            else
            {
                existing.Status = status.ToString();
                existing.SetBy = setBy;
                existing.SetDate = DateTime.Now;
                existing.Reason = reason;
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
                u.Id,
                u.StaffId,
                u.PermissionId,
                FeatureKey = u.Feature!.FeatureKey,
                FeatureName = u.Feature.FeatureName,
                u.Status,
                u.SetBy,
                u.SetDate,
                u.Reason
            })
            .ToListAsync<object>();

        // ─────────────────────────────────────────────────────────────────────
        // MENU FEATURE KEYS (for admin grant/revoke UI)
        // ─────────────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<string>> GetMenuFeatureKeysAsync(int menuId)
        {
            var allMenus = await _db.Menus.AsNoTracking()
              .Include(m => m.MenuPermissions)
                .ThenInclude(mp => mp.Feature)
              .Where(m => m.IsActive)
              .ToListAsync();

            if (!allMenus.Any(m => m.Id == menuId)) return Array.Empty<string>();

            var lookup = allMenus.ToLookup(m => m.ParentId);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    menu.Id,
                    menu.Title,
                    menu.Icon,
                    menu.Route,
                    menu.ParentId,
                    menu.SortOrder,
                    directPermissions = menu.MenuPermissions
                    .Where(mp => mp.Feature != null)
                    .Select(mp => mp.Feature!.FeatureKey).ToList(),
                    allPermissions = CollectDescendantKeys(menu.Id).OrderBy(k => k).ToList(),
                    children = BuildTree(menu.Id)
                }).ToList();
            }

            return BuildTree(null);
        }

        // ─────────────────────────────────────────────────────────────────────
        // SEED HELPERS
        // ─────────────────────────────────────────────────────────────────────

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
          ($"MENU_{menu.Id}",    menu.Title,       "Menu"),
          ($"MENU_{menu.Id}_VIEW", $"{menu.Title} - View", "Menu"),
          ($"MENU_{menu.Id}_ADD",  $"{menu.Title} - Add",  "Menu"),
          ($"MENU_{menu.Id}_EDIT", $"{menu.Title} - Edit", "Menu"),
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
            string suffix = parts.Length >= 3 ? string.Join("_", parts.Skip(2)) : "";
            string name = suffix switch
            {
                "VIEW" => $"{menuTitle} - View",
                "ADD" => $"{menuTitle} - Add",
                "EDIT" => $"{menuTitle} - Edit",
                "DELETE" => $"{menuTitle} - Delete",
                "" => menuTitle,
                _ => $"{menuTitle} - {suffix}"
            };

            _db.Features.Add(new Feature { FeatureKey = featureKey, FeatureName = name, Module = "Menu" });
            try { await _db.SaveChangesAsync(); }
            catch { /* ignore duplicate key race */ }
        }
    }
}