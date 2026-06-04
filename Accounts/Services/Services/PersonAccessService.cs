using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class PersonAccessService : IPersonAccessService
    {
        private readonly ApplicationDbContext _db;
        private readonly RbacService _rbac;

        public PersonAccessService(ApplicationDbContext db, RbacService rbac)
        {
            _db   = db;
            _rbac = rbac;
        }

        public async Task<bool> HasPersonGrantsAsync(Guid personId, CancellationToken ct = default) =>
            await _db.PersonMenus.AsNoTracking().AnyAsync(x => x.PersonId == personId, ct) ||
            await _db.PersonFeatures.AsNoTracking().AnyAsync(x => x.PersonId == personId, ct);

        public async Task<HashSet<int>> GetGrantedPermissionIdsAsync(Guid personId, CancellationToken ct = default) =>
            await _db.PersonFeatures.AsNoTracking()
                .Where(x => x.PersonId == personId)
                .Select(x => x.PermissionId)
                .ToHashSetAsync(ct);

        public async Task<IReadOnlyList<string>> GetGrantedFeatureKeysAsync(Guid personId, CancellationToken ct = default) =>
            await _db.PersonFeatures.AsNoTracking()
                .Where(x => x.PersonId == personId)
                .Join(_db.Features.AsNoTracking(), pf => pf.PermissionId, f => f.PermissionId, (_, f) => f.FeatureKey)
                .OrderBy(k => k)
                .ToListAsync(ct);

        public async Task<List<object>> GetGrantedSidebarAsync(Guid personId, CancellationToken ct = default)
        {
            var grantedMenuIds = await _db.PersonMenus.AsNoTracking()
                .Where(x => x.PersonId == personId)
                .Select(x => x.MenuId)
                .ToHashSetAsync(ct);

            if (grantedMenuIds.Count == 0)
                return new List<object>();

            var allMenus = await _db.Menus.AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync(ct);

            var byId = allMenus.ToDictionary(m => m.Id);

            // Include parent chain so section headers appear
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
            return BuildTree(null, lookup);
        }

        public async Task<(bool Success, string Message, IReadOnlyList<int> MenuIds, IReadOnlyList<string> FeatureKeys)>
            GrantMenuAsync(Guid personId, int menuId, string? grantedBy, string? reason, CancellationToken ct = default)
        {
            if (!await _db.Persons.AnyAsync(p => p.PersonId == personId, ct))
                return (false, $"Person {personId} not found.", Array.Empty<int>(), Array.Empty<string>());

            var menu = await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId && m.IsActive, ct);
            if (menu == null)
                return (false, $"Menu {menuId} not found.", Array.Empty<int>(), Array.Empty<string>());

            var menuIds = await CollectMenuIdsInSubtreeAsync(menuId, ct);
            var permissionIds = await CollectPermissionIdsForMenusAsync(menuIds, ct);

            // Ensure MENU_{id} features exist and are linked
            foreach (var mid in menuIds)
                await _rbac.EnsureMenuFeatureExistsPublicAsync(mid);

            permissionIds = await CollectPermissionIdsForMenusAsync(menuIds, ct);

            var existingMenus = await _db.PersonMenus
                .Where(x => x.PersonId == personId && menuIds.Contains(x.MenuId))
                .Select(x => x.MenuId)
                .ToHashSetAsync(ct);

            foreach (var mid in menuIds.Where(id => !existingMenus.Contains(id)))
            {
                _db.PersonMenus.Add(new PersonMenu
                {
                    PersonId      = personId,
                    MenuId        = mid,
                    GrantedBy     = grantedBy,
                    GrantedOnUtc  = DateTime.UtcNow
                });
            }

            var existingFeatures = await _db.PersonFeatures
                .Where(x => x.PersonId == personId && permissionIds.Contains(x.PermissionId))
                .Select(x => x.PermissionId)
                .ToHashSetAsync(ct);

            foreach (var pid in permissionIds.Where(id => !existingFeatures.Contains(id)))
            {
                _db.PersonFeatures.Add(new PersonFeature
                {
                    PersonId      = personId,
                    PermissionId  = pid,
                    GrantedBy     = grantedBy,
                    GrantedOnUtc  = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(ct);

            // Sync staff overrides when person is hired (backward compatibility)
            var staff = await _db.StaffVacancies.AsNoTracking()
                .FirstOrDefaultAsync(s => s.PersonId == personId, ct);

            if (staff != null)
            {
                var keys = await _db.Features.AsNoTracking()
                    .Where(f => permissionIds.Contains(f.PermissionId))
                    .Select(f => f.FeatureKey)
                    .ToListAsync(ct);

                foreach (var key in keys)
                    await _rbac.SetUserOverrideAsync(staff.StaffId, key, PermissionStatus.ALLOW, grantedBy, reason);
            }

            var featureKeys = await _db.Features.AsNoTracking()
                .Where(f => permissionIds.Contains(f.PermissionId))
                .Select(f => f.FeatureKey)
                .ToListAsync(ct);

            return (true, $"Granted menu '{menu.Title}' and {permissionIds.Count} feature(s) to person.", menuIds, featureKeys);
        }

        public async Task<(bool Success, string Message)> RevokeMenuAsync(Guid personId, int menuId, CancellationToken ct = default)
        {
            var menuIds = await CollectMenuIdsInSubtreeAsync(menuId, ct);
            var permissionIds = await CollectPermissionIdsForMenusAsync(menuIds, ct);

            var menus = await _db.PersonMenus
                .Where(x => x.PersonId == personId && menuIds.Contains(x.MenuId))
                .ToListAsync(ct);
            _db.PersonMenus.RemoveRange(menus);

            var features = await _db.PersonFeatures
                .Where(x => x.PersonId == personId && permissionIds.Contains(x.PermissionId))
                .ToListAsync(ct);
            _db.PersonFeatures.RemoveRange(features);

            await _db.SaveChangesAsync(ct);

            var staff = await _db.StaffVacancies.AsNoTracking()
                .FirstOrDefaultAsync(s => s.PersonId == personId, ct);

            if (staff != null)
            {
                var keys = await _db.Features.AsNoTracking()
                    .Where(f => permissionIds.Contains(f.PermissionId))
                    .Select(f => f.FeatureKey)
                    .ToListAsync(ct);

                foreach (var key in keys)
                    await _rbac.RemoveUserOverrideAsync(staff.StaffId, key);
            }

            return (true, $"Revoked {menus.Count} menu(s) and {features.Count} feature(s).");
        }

        public async Task GrantFeatureAsync(Guid personId, int permissionId, string? grantedBy, CancellationToken ct = default)
        {
            if (await _db.PersonFeatures.AnyAsync(x => x.PersonId == personId && x.PermissionId == permissionId, ct))
                return;

            _db.PersonFeatures.Add(new PersonFeature
            {
                PersonId     = personId,
                PermissionId = permissionId,
                GrantedBy    = grantedBy,
                GrantedOnUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }

        public async Task RevokeFeatureAsync(Guid personId, int permissionId, CancellationToken ct = default)
        {
            var row = await _db.PersonFeatures
                .FirstOrDefaultAsync(x => x.PersonId == personId && x.PermissionId == permissionId, ct);
            if (row == null) return;
            _db.PersonFeatures.Remove(row);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<object> GetPersonAccessSummaryAsync(Guid personId, CancellationToken ct = default)
        {
            var menus = await _db.PersonMenus.AsNoTracking()
                .Where(x => x.PersonId == personId)
                .Join(_db.Menus.AsNoTracking(), pm => pm.MenuId, m => m.Id, (_, m) => new { m.Id, m.Title, m.Route })
                .ToListAsync(ct);

            var features = await _db.PersonFeatures.AsNoTracking()
                .Where(x => x.PersonId == personId)
                .Join(_db.Features.AsNoTracking(), pf => pf.PermissionId, f => f.PermissionId, (_, f) => new
                {
                    f.PermissionId,
                    f.FeatureKey,
                    f.FeatureName,
                    f.Module
                })
                .ToListAsync(ct);

            return new { personId, menus, features };
        }

        private async Task<List<int>> CollectMenuIdsInSubtreeAsync(int menuId, CancellationToken ct)
        {
            var allMenus = await _db.Menus.AsNoTracking().Where(m => m.IsActive).ToListAsync(ct);
            if (!allMenus.Any(m => m.Id == menuId)) return new List<int>();

            var lookup = allMenus.ToLookup(m => m.ParentId);
            var ids = new List<int>();

            void Walk(int id)
            {
                ids.Add(id);
                foreach (var child in lookup[id]) Walk(child.Id);
            }
            Walk(menuId);
            return ids;
        }

        private async Task<HashSet<int>> CollectPermissionIdsForMenusAsync(IEnumerable<int> menuIds, CancellationToken ct)
        {
            var idList = menuIds.ToList();
            var fromLinks = await _db.MenuPermissions.AsNoTracking()
                .Where(mp => idList.Contains(mp.MenuId))
                .Select(mp => mp.PermissionId)
                .ToListAsync(ct);

            var keys = idList.Select(id => $"MENU_{id}").ToList();
            var fromMenuFeatures = await _db.Features.AsNoTracking()
                .Where(f => keys.Contains(f.FeatureKey))
                .Select(f => f.PermissionId)
                .ToListAsync(ct);

            return fromLinks.Concat(fromMenuFeatures).ToHashSet();
        }

        private static List<object> BuildTree(int? parentId, ILookup<int?, Menu> lookup)
        {
            var result = new List<object>();
            foreach (var menu in lookup[parentId])
            {
                var children = BuildTree(menu.Id, lookup);
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
    }
}
