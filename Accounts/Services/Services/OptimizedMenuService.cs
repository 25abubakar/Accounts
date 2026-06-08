
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Optimized menu and permission service.
    ///
    /// New RBAC flow (clean, no PersonMenus / PersonFeatures):
    ///   1. Get the user's ASP.NET Identity roles.
    ///   2. RolePermissions  → which PermissionIds those roles allow.
    ///   3. UserPermissionOverrides (by StaffId) → ALLOW adds, DENY removes.
    ///   4. MenuPermissions → which Menus are unlocked by the resolved PermissionIds.
    ///
    /// All filtering is done in-memory after a fixed set of queries (no N+1).
    /// </summary>
    public class OptimizedMenuService
    {
        private readonly ApplicationDbContext _db;

        public OptimizedMenuService(ApplicationDbContext db)
        {
            _db = db;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIMARY SESSION ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build the full user menu session payload.
        /// Pass <c>Guid.Empty</c> for SuperAdmin — they receive every menu with
        /// no permission filtering.
        /// </summary>
        public async Task<UserMenuSessionDto> GetUserMenuSessionAsync(
      Guid staffId,
      bool includeDetailedPermissions = false,
      CancellationToken cancellationToken = default)
        {
            // ── 1. Load all active menus with their permission links ───────────
            var allMenus = await _db.Menus
        .AsNoTracking()
        .Include(m => m.MenuPermissions)
        .Where(m => m.IsActive)
        .OrderBy(m => m.SortOrder)
        .ToListAsync(cancellationToken);

            var menuLookup = allMenus.ToLookup(m => m.ParentId);

            // ── 2. SuperAdmin bypass ───────────────────────────────────────────
            if (staffId == Guid.Empty)
            {
                return new UserMenuSessionDto
                {
                    StaffId = Guid.Empty,
                    IsFullAccess = true,
                    Sidebar = BuildMenuTree(null, menuLookup, allowedPermissionIds: null),
                    AllowedPermissionIds = new List<int>()
                };
            }

            // ── 3. Resolve effective permissions ──────────────────────────────
            var allowedPermissionIds = await GetEffectivePermissionIdsAsync(staffId, cancellationToken);

            // ── 4. Build filtered sidebar in-memory ───────────────────────────
            var result = new UserMenuSessionDto
            {
                StaffId = staffId,
                IsFullAccess = false,
                Sidebar = BuildMenuTree(null, menuLookup, allowedPermissionIds),
                AllowedPermissionIds = allowedPermissionIds.ToList()
            };

            if (includeDetailedPermissions)
            {
                result.DetailedPermissions = await GetDetailedPermissionsAsync(
                  staffId, allowedPermissionIds, cancellationToken);
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // CORE PERMISSION RESOLUTION
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the set of PermissionIds the staff member is effectively allowed.
        ///
        /// Resolution order (highest priority first):
        ///   1. UserPermissionOverride DENY   → hard-remove (overrides everything)
        ///   2. UserPermissionOverride ALLOW  → hard-add
        ///   3. RolePermissions for the staff's JobTitle (dept-specific beats global)
        /// </summary>
        private async Task<HashSet<int>> GetEffectivePermissionIdsAsync(
      Guid staffId,
      CancellationToken cancellationToken)
        {
            // ── Q1: Staff job title + department ──────────────────────────────
            var staffInfo = await _db.StaffVacancies
        .AsNoTracking()
        .Where(s => s.StaffId == staffId)
        .Select(s => new
        {
            JobTitle = s.Vacancy != null ? s.Vacancy.JobTitle : null,
            DeptId = s.Vacancy != null ? s.Vacancy.OrganizationId : (int?)null
        })
        .FirstOrDefaultAsync(cancellationToken);

            if (staffInfo == null)
                return new HashSet<int>();

            // ── Q2: Role-based permissions for this job title ─────────────────
            //   Dept-specific rows take precedence over global (DeptId == null).
            //   We load both and resolve in-memory.
            var rolePermissions = string.IsNullOrWhiteSpace(staffInfo.JobTitle)
        ? new List<(int PermissionId, int? DeptId, bool IsAllowed)>()
        : await _db.RolePermissions
          .AsNoTracking()
          .Where(r => r.JobTitle == staffInfo.JobTitle &&
                (r.DeptId == null || r.DeptId == staffInfo.DeptId))
          .Select(r => new { r.PermissionId, r.DeptId, r.IsAllowed })
          .ToListAsync(cancellationToken)
          .ContinueWith(t => t.Result
            .Select(r => (r.PermissionId, r.DeptId, r.IsAllowed))
            .ToList());

            // Collapse: dept-specific beats global for the same PermissionId
            var roleAllowed = new HashSet<int>();
            // Track which permissionIds have a dept-specific rule so we don't
            // overwrite it with the global rule.
            var hasDeptRule = new HashSet<int>();

            foreach (var (permId, deptId, isAllowed) in rolePermissions)
            {
                if (deptId != null)
                {
                    hasDeptRule.Add(permId);
                    if (isAllowed) roleAllowed.Add(permId);
                    else roleAllowed.Remove(permId);
                }
            }
            foreach (var (permId, deptId, isAllowed) in rolePermissions)
            {
                if (deptId == null && !hasDeptRule.Contains(permId))
                {
                    if (isAllowed) roleAllowed.Add(permId);
                }
            }

            // ── Q3: User-level overrides ───────────────────────────────────────
            var userOverrides = await _db.UserPermissionOverrides
        .AsNoTracking()
        .Where(u => u.StaffId == staffId)
        .Select(u => new { u.PermissionId, u.Status })
        .ToListAsync(cancellationToken);

            // Start from role baseline and apply overrides
            var allowedIds = new HashSet<int>(roleAllowed);

            foreach (var uo in userOverrides)
            {
                if (uo.Status == nameof(PermissionStatus.DENY))
                    allowedIds.Remove(uo.PermissionId);   // Hard deny — even if role says ALLOW
                else if (uo.Status == nameof(PermissionStatus.ALLOW))
                    allowedIds.Add(uo.PermissionId);      // Explicit individual grant
                // INHERIT → no action, role default already applied
            }

            return allowedIds;
        }

        // ─────────────────────────────────────────────────────────────────────
        // MENU TREE BUILDER
        // ─────────────────────────────────────────────────────────────────────

        private List<MenuResponseDto> BuildMenuTree(
      int? parentId,
      ILookup<int?, Menu> menuLookup,
      HashSet<int>? allowedPermissionIds)
        {
            var result = new List<MenuResponseDto>();

            foreach (var menu in menuLookup[parentId])
            {
                if (allowedPermissionIds != null)
                {
                    var requiredIds = menu.MenuPermissions
                      .Select(mp => mp.PermissionId).ToList();

                    // Public menu (no required permissions) OR user holds ≥1 required permission
                    bool canSee = !requiredIds.Any() ||
                 requiredIds.Any(id => allowedPermissionIds.Contains(id));
                    if (!canSee) continue;
                }

                var children = BuildMenuTree(menu.Id, menuLookup, allowedPermissionIds);

                // Drop empty group headers (has children in DB but all were filtered out)
                if (!children.Any() &&
          string.IsNullOrWhiteSpace(menu.Route) &&
          menuLookup[menu.Id].Any())
                    continue;

                result.Add(new MenuResponseDto
                {
                    Id = menu.Id,
                    Title = menu.Title,
                    Icon = menu.Icon,
                    Route = menu.Route,
                    SortOrder = menu.SortOrder,
                    Children = children
                });
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPER / CONVENIENCE METHODS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Check access to a single permission by its integer ID.</summary>
        public async Task<bool> HasAccessAsync(
      Guid staffId,
      int permissionId,
      CancellationToken cancellationToken = default)
        {
            var allowed = await GetEffectivePermissionIdsAsync(staffId, cancellationToken);
            return allowed.Contains(permissionId);
        }

        /// <summary>Check access to a single permission by its FeatureKey string.</summary>
        public async Task<bool> HasAccessByKeyAsync(
      Guid staffId,
      string featureKey,
      CancellationToken cancellationToken = default)
        {
            var feature = await _db.Features
              .AsNoTracking()
              .Where(f => f.FeatureKey == featureKey)
              .Select(f => new { f.PermissionId })
              .FirstOrDefaultAsync(cancellationToken);

            if (feature == null) return false;

            return await HasAccessAsync(staffId, feature.PermissionId, cancellationToken);
        }

        /// <summary>Return all FeatureKey strings the user is allowed to access.</summary>
        public async Task<List<string>> GetAllowedFeatureKeysAsync(
      Guid staffId,
      CancellationToken cancellationToken = default)
        {
            var allowedIds = await GetEffectivePermissionIdsAsync(staffId, cancellationToken);
            if (allowedIds.Count == 0) return new List<string>();

            return await _db.Features
              .AsNoTracking()
              .Where(f => allowedIds.Contains(f.PermissionId))
              .Select(f => f.FeatureKey)
              .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// Detailed per-feature view for admin/debug use.
        /// </summary>
        private async Task<List<PermissionDto>> GetDetailedPermissionsAsync(
      Guid staffId,
      HashSet<int> allowedPermissionIds,
      CancellationToken cancellationToken)
        {
            var allFeatures = await _db.Features
              .AsNoTracking()
              .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
              .ToListAsync(cancellationToken);

            // Load override status for source labelling
            var overrideMap = await _db.UserPermissionOverrides
        .AsNoTracking()
        .Where(u => u.StaffId == staffId)
        .ToDictionaryAsync(u => u.PermissionId, u => u.Status, cancellationToken);

            return allFeatures.Select(f =>
            {
                bool hasAccess = allowedPermissionIds.Contains(f.PermissionId);
                string source;

                if (overrideMap.TryGetValue(f.PermissionId, out var st))
                    source = st == nameof(PermissionStatus.ALLOW) ? "UserAllow" :
                        st == nameof(PermissionStatus.DENY) ? "UserDeny" : "RoleDefault";
                else
                    source = hasAccess ? "RoleDefault" : "Denied";

                return new PermissionDto
                {
                    PermissionId = f.PermissionId,
                    FeatureKey = f.FeatureKey,
                    FeatureName = f.FeatureName,
                    Module = f.Module,
                    HasAccess = hasAccess,
                    Source = source
                };
            }).ToList();
        }
    }
}