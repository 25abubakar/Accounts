using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Optimized menu and permission service.
    ///
    /// V2 RBAC flow (StaffMenuAccess + AccessFeatures):
    ///   1. Get all StaffMenuAccess rows for the user (menu grants).
    ///   2. For each grant, check AccessFeature rows for fine-grained allow/deny.
    ///   3. If no grant rows found, fall back to legacy RolePermissions / DepartmentAccessMatrix.
    ///   4. MenuPermissions → which Menus are unlocked by the resolved PermissionIds.
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
            // ── 1. Load all active menus with their permission links ──────────
            var allMenus = await _db.Menus
                .AsNoTracking()
                .Include(m => m.MenuPermissions)
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync(cancellationToken);

            var menuLookup = allMenus.ToLookup(m => m.ParentId);

            // ── 2. SuperAdmin bypass ──────────────────────────────────────────
            if (staffId == Guid.Empty)
            {
                return new UserMenuSessionDto
                {
                    StaffId              = Guid.Empty,
                    IsFullAccess         = true,
                    Sidebar              = BuildMenuTree(null, menuLookup, allowedPermissionIds: null),
                    AllowedPermissionIds = new List<int>()
                };
            }

            // ── 3. Resolve effective permissions ─────────────────────────────
            var allowedPermissionIds = await GetEffectivePermissionIdsAsync(staffId, cancellationToken);

            // ── 4. Build filtered sidebar in-memory ───────────────────────────
            var result = new UserMenuSessionDto
            {
                StaffId              = staffId,
                IsFullAccess         = false,
                Sidebar              = BuildMenuTree(null, menuLookup, allowedPermissionIds),
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
        ///   1. StaffMenuAccess + AccessFeatures (2-tier RBAC — new system)
        ///   2. RolePermissions for the staff's JobTitle (dept-specific beats global)
        ///   3. DepartmentAccessMatrix (legacy fallback)
        /// </summary>
        private async Task<HashSet<int>> GetEffectivePermissionIdsAsync(
            Guid staffId,
            CancellationToken cancellationToken)
        {
            // ── 1. New 2-tier RBAC (StaffMenuAccess + AccessFeatures) ─────────
            var menuGrants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.AccessFeatures)
                .Where(ma => ma.StaffId == staffId && ma.IsAllow)
                .ToListAsync(cancellationToken);

            if (menuGrants.Count > 0)
            {
                var result2Tier = new HashSet<int>();
                var allFeatureIds = await _db.Features.AsNoTracking()
                    .Select(f => f.PermissionId)
                    .ToListAsync(cancellationToken);

                foreach (var grant in menuGrants)
                {
                    if (!grant.AccessFeatures.Any())
                    {
                        // No feature-level rows → all features allowed for this grant
                        foreach (var pid in allFeatureIds)
                            result2Tier.Add(pid);
                    }
                    else
                    {
                        // Explicit feature flags
                        foreach (var af in grant.AccessFeatures.Where(af => af.IsAllow))
                            result2Tier.Add(af.PermissionId);
                        // IsAllow=false rows are explicitly denied — do not add
                    }
                }

                return result2Tier;
            }

            // ── 2. Legacy fallback: RolePermissions + DepartmentAccessMatrix ──
            var staffInfo = await _db.StaffVacancies
                .AsNoTracking()
                .Where(s => s.StaffId == staffId)
                .Select(s => new
                {
                    JobTitle = s.Vacancy != null ? s.Vacancy.JobTitle : null,
                    DeptId   = s.Vacancy != null ? s.Vacancy.OrganizationId : (int?)null
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (staffInfo == null)
                return new HashSet<int>();

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

            // Dept-specific beats global for the same PermissionId
            var hasDeptRule = new HashSet<int>();
            var roleAllowed = new HashSet<int>();

            foreach (var (permId, deptId, isAllowed) in rolePermissions)
            {
                if (deptId != null)
                {
                    hasDeptRule.Add(permId);
                    if (isAllowed) roleAllowed.Add(permId);
                    else           roleAllowed.Remove(permId);
                }
            }
            foreach (var (permId, deptId, isAllowed) in rolePermissions)
            {
                if (deptId == null && !hasDeptRule.Contains(permId))
                {
                    if (isAllowed) roleAllowed.Add(permId);
                }
            }

            // Include DepartmentAccessMatrix rows
            var matrixPerms = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .Where(m => m.StaffId == staffId && m.HasAccess)
                .Select(m => m.PermissionId)
                .ToListAsync(cancellationToken);

            foreach (var pid in matrixPerms)
                roleAllowed.Add(pid);

            return roleAllowed;
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
                    Id        = menu.Id,
                    Title     = menu.Title,
                    Icon      = menu.Icon,
                    Route     = menu.Route,
                    SortOrder = menu.SortOrder,
                    Children  = children
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

            // Load menu grant + access feature rows for source labelling
            var menuGrants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.AccessFeatures)
                .Where(ma => ma.StaffId == staffId && ma.IsAllow)
                .ToListAsync(cancellationToken);

            var hasGrant    = menuGrants.Count > 0;
            var grantAllow  = new HashSet<int>();
            var grantDeny   = new HashSet<int>();

            foreach (var grant in menuGrants)
            {
                foreach (var af in grant.AccessFeatures)
                {
                    if (af.IsAllow) grantAllow.Add(af.PermissionId);
                    else            grantDeny.Add(af.PermissionId);
                }
            }

            return allFeatures.Select(f =>
            {
                bool hasAccess = allowedPermissionIds.Contains(f.PermissionId);
                string source;

                if (hasGrant)
                {
                    if (grantDeny.Contains(f.PermissionId))
                        source = "MenuFeatureDeny";
                    else if (grantAllow.Contains(f.PermissionId))
                        source = "MenuFeatureAllow";
                    else if (hasAccess)
                        source = "MenuGrant";
                    else
                        source = "NoGrant";
                }
                else
                {
                    source = hasAccess ? "RoleDefault" : "Denied";
                }

                return new PermissionDto
                {
                    PermissionId = f.PermissionId,
                    FeatureKey   = f.FeatureKey,
                    FeatureName  = f.FeatureName,
                    Module       = f.Module,
                    HasAccess    = hasAccess,
                    Source       = source
                };
            }).ToList();
        }
    }
}
