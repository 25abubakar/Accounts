using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Person-scoped access helpers.
    /// All grants are persisted via the 2-tier RBAC system:
    ///   StaffMenuAccess (menu-level grant) + AccessFeature (feature-level flags).
    /// UserPermissionOverrides was dropped in V2 migration.
    /// </summary>
    public class PersonAccessService : IPersonAccessService
    {
        private readonly ApplicationDbContext _db;
        private readonly RbacService _rbac;

        public PersonAccessService(ApplicationDbContext db, RbacService rbac)
        {
            _db   = db;
            _rbac = rbac;
        }

        public Task<bool> HasPersonGrantsAsync(Guid personId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<HashSet<int>> GetGrantedPermissionIdsAsync(Guid personId, CancellationToken ct = default) =>
            Task.FromResult(new HashSet<int>());

        public Task<IReadOnlyList<string>> GetGrantedFeatureKeysAsync(Guid personId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(new List<string>());

        public Task<List<object>> GetGrantedSidebarAsync(Guid personId, CancellationToken ct = default) =>
            Task.FromResult(new List<object>());

        public async Task<(bool Success, string Message, IReadOnlyList<int> MenuIds, IReadOnlyList<string> FeatureKeys)>
            GrantMenuAsync(Guid personId, int menuId, string? grantedBy, string? reason, CancellationToken ct = default)
        {
            if (!await _db.Persons.AnyAsync(p => p.PersonId == personId, ct))
                return (false, $"Person {personId} not found.", Array.Empty<int>(), Array.Empty<string>());

            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.PersonId == personId, ct);

            if (staff == null)
                return (false, "Person is not linked to a staff record. Hire the person first.", Array.Empty<int>(), Array.Empty<string>());

            var menu = await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId && m.IsActive, ct);
            if (menu == null)
                return (false, $"Menu {menuId} not found.", Array.Empty<int>(), Array.Empty<string>());

            var menuIds = await CollectMenuIdsInSubtreeAsync(menuId, ct);

            // Ensure a StaffMenuAccess row exists for each menu in the subtree
            var existingGrants = await _db.StaffMenuAccesses
                .Where(ma => ma.StaffId == staff.StaffId && menuIds.Contains(ma.MenuId))
                .ToListAsync(ct);

            var existingMenuIds = existingGrants.Select(g => g.MenuId).ToHashSet();
            var now = DateTime.UtcNow;

            foreach (var mid in menuIds)
            {
                if (existingMenuIds.Contains(mid))
                {
                    // Ensure it's an ALLOW grant
                    var existing = existingGrants.First(g => g.MenuId == mid);
                    existing.IsAllow     = true;
                    existing.GrantedBy   = grantedBy;
                    existing.GrantedDate = now;
                }
                else
                {
                    _db.StaffMenuAccesses.Add(new StaffMenuAccess
                    {
                        StaffId     = staff.StaffId,
                        MenuId      = mid,
                        IsAllow     = true,
                        GrantedBy   = grantedBy,
                        GrantedDate = now
                    });
                }
            }

            await _db.SaveChangesAsync(ct);

            // Collect feature keys for return value
            var permissionIds = await CollectPermissionIdsForMenusAsync(menuIds, ct);
            var featureKeys = await _db.Features.AsNoTracking()
                .Where(f => permissionIds.Contains(f.PermissionId))
                .Select(f => f.FeatureKey)
                .ToListAsync(ct);

            return (true, $"Granted menu '{menu.Title}' and {menuIds.Count} sub-menu(s) via StaffMenuAccess.", menuIds, featureKeys);
        }

        public async Task<(bool Success, string Message)> RevokeMenuAsync(
            Guid personId, int menuId, CancellationToken ct = default)
        {
            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.PersonId == personId, ct);

            if (staff == null)
                return (false, "Person is not linked to a staff record.");

            var menuIds = await CollectMenuIdsInSubtreeAsync(menuId, ct);

            var grants = await _db.StaffMenuAccesses
                .Where(ma => ma.StaffId == staff.StaffId && menuIds.Contains(ma.MenuId))
                .ToListAsync(ct);

            if (grants.Count > 0)
            {
                _db.StaffMenuAccesses.RemoveRange(grants);
                await _db.SaveChangesAsync(ct);
            }

            return (true, $"Revoked {grants.Count} menu grant(s) for menu subtree.");
        }

        public async Task GrantFeatureAsync(
            Guid personId, int permissionId, string? grantedBy, CancellationToken ct = default)
        {
            var staffId = await ResolveStaffIdAsync(personId, ct);
            if (staffId == null) return;

            var featureKey = await _db.Features.AsNoTracking()
                .Where(f => f.PermissionId == permissionId)
                .Select(f => f.FeatureKey)
                .FirstOrDefaultAsync(ct);

            if (featureKey == null) return;

            await _rbac.SetUserOverrideAsync(staffId.Value, featureKey, PermissionStatus.ALLOW, grantedBy, "Person feature grant");
        }

        public async Task RevokeFeatureAsync(
            Guid personId, int permissionId, CancellationToken ct = default)
        {
            var staffId = await ResolveStaffIdAsync(personId, ct);
            if (staffId == null) return;

            var featureKey = await _db.Features.AsNoTracking()
                .Where(f => f.PermissionId == permissionId)
                .Select(f => f.FeatureKey)
                .FirstOrDefaultAsync(ct);

            if (featureKey == null) return;

            await _rbac.RemoveUserOverrideAsync(staffId.Value, featureKey);
        }

        public async Task<object> GetPersonAccessSummaryAsync(
            Guid personId, CancellationToken ct = default)
        {
            var staff = await _db.StaffVacancies.AsNoTracking()
                .FirstOrDefaultAsync(s => s.PersonId == personId, ct);

            if (staff == null)
                return new { personId, menus = Array.Empty<object>(), features = Array.Empty<object>() };

            // Load all menu grants with their feature-level flags from the 2-tier system
            var menuGrants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.AccessFeatures)
                .Where(ma => ma.StaffId == staff.StaffId && ma.IsAllow)
                .ToListAsync(ct);

            var grantedMenuIds = menuGrants.Select(ma => ma.MenuId).Distinct().ToList();

            var menus = grantedMenuIds.Count == 0
                ? new List<object>()
                : await _db.Menus.AsNoTracking()
                    .Where(m => grantedMenuIds.Contains(m.Id))
                    .Select(m => (object)new { m.Id, m.Title, m.Route })
                    .ToListAsync(ct);

            // Collect allowed feature permission IDs from the 2-tier grants
            var allowedPermIds = new HashSet<int>();
            var allFeatureIds = await _db.Features.AsNoTracking()
                .Select(f => f.PermissionId)
                .ToListAsync(ct);

            foreach (var grant in menuGrants)
            {
                if (!grant.AccessFeatures.Any())
                {
                    foreach (var pid in allFeatureIds)
                        allowedPermIds.Add(pid);
                }
                else
                {
                    foreach (var af in grant.AccessFeatures.Where(af => af.IsAllow))
                        allowedPermIds.Add(af.PermissionId);
                }
            }

            var features = allowedPermIds.Count == 0
                ? new List<object>()
                : await _db.Features.AsNoTracking()
                    .Where(f => allowedPermIds.Contains(f.PermissionId))
                    .Select(f => (object)new
                    {
                        f.PermissionId,
                        f.FeatureKey,
                        f.FeatureName,
                        f.Module
                    })
                    .ToListAsync(ct);

            return new { personId, staffId = staff.StaffId, menus, features };
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private async Task<Guid?> ResolveStaffIdAsync(Guid personId, CancellationToken ct) =>
            await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.PersonId == personId)
                .Select(s => (Guid?)s.StaffId)
                .FirstOrDefaultAsync(ct);

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

        private async Task<HashSet<int>> CollectPermissionIdsForMenusAsync(
            IEnumerable<int> menuIds, CancellationToken ct)
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
    }
}
