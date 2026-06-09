using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Person-scoped access helpers. All grants are persisted via UserPermissionOverrides (StaffId).
    /// PersonMenus / PersonFeatures tables are deprecated.
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

            foreach (var mid in menuIds)
                await _rbac.EnsureMenuFeatureExistsPublicAsync(mid);

            var permissionIds = await CollectPermissionIdsForMenusAsync(menuIds, ct);
            var featureKeys = await _db.Features.AsNoTracking()
                .Where(f => permissionIds.Contains(f.PermissionId))
                .Select(f => f.FeatureKey)
                .ToListAsync(ct);

            await UpsertStaffOverridesAsync(staff.StaffId, permissionIds, PermissionStatus.ALLOW, grantedBy, reason ?? "Menu grant", ct);

            return (true, $"Granted menu '{menu.Title}' and {permissionIds.Count} feature(s) via UserPermissionOverrides.", menuIds, featureKeys);
        }

        public async Task<(bool Success, string Message)> RevokeMenuAsync(Guid personId, int menuId, CancellationToken ct = default)
        {
            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.PersonId == personId, ct);

            if (staff == null)
                return (false, "Person is not linked to a staff record.");

            var menuIds = await CollectMenuIdsInSubtreeAsync(menuId, ct);
            var permissionIds = await CollectPermissionIdsForMenusAsync(menuIds, ct);

            var rows = await _db.UserPermissionOverrides
                .Where(u => u.StaffId == staff.StaffId && permissionIds.Contains(u.PermissionId))
                .ToListAsync(ct);

            if (rows.Count > 0)
            {
                _db.UserPermissionOverrides.RemoveRange(rows);
                await _db.SaveChangesAsync(ct);
            }

            return (true, $"Revoked {rows.Count} feature override(s) for menu subtree.");
        }

        public async Task GrantFeatureAsync(Guid personId, int permissionId, string? grantedBy, CancellationToken ct = default)
        {
            var staffId = await ResolveStaffIdAsync(personId, ct);
            if (staffId == null) return;

            var key = await _db.Features.AsNoTracking()
                .Where(f => f.PermissionId == permissionId)
                .Select(f => f.FeatureKey)
                .FirstOrDefaultAsync(ct);

            if (key == null) return;

            await _rbac.SetUserOverrideAsync(staffId.Value, key, PermissionStatus.ALLOW, grantedBy, "Person feature grant");
        }

        public async Task RevokeFeatureAsync(Guid personId, int permissionId, CancellationToken ct = default)
        {
            var staffId = await ResolveStaffIdAsync(personId, ct);
            if (staffId == null) return;

            var key = await _db.Features.AsNoTracking()
                .Where(f => f.PermissionId == permissionId)
                .Select(f => f.FeatureKey)
                .FirstOrDefaultAsync(ct);

            if (key == null) return;

            await _rbac.RemoveUserOverrideAsync(staffId.Value, key);
        }

        public async Task<object> GetPersonAccessSummaryAsync(Guid personId, CancellationToken ct = default)
        {
            var staff = await _db.StaffVacancies.AsNoTracking()
                .FirstOrDefaultAsync(s => s.PersonId == personId, ct);

            if (staff == null)
                return new { personId, menus = Array.Empty<object>(), features = Array.Empty<object>() };

            var features = await _db.UserPermissionOverrides.AsNoTracking()
                .Where(u => u.StaffId == staff.StaffId && u.Status == nameof(PermissionStatus.ALLOW))
                .Join(_db.Features.AsNoTracking(), u => u.PermissionId, f => f.PermissionId, (_, f) => new
                {
                    f.PermissionId,
                    f.FeatureKey,
                    f.FeatureName,
                    f.Module
                })
                .ToListAsync(ct);

            var menuKeys = features
                .Select(f => f.FeatureKey)
                .Where(k => k.StartsWith("MENU_", StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Split('_').ElementAtOrDefault(1))
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .Distinct()
                .ToList();

            var menus = menuKeys.Count == 0
                ? new List<object>()
                : await _db.Menus.AsNoTracking()
                    .Where(m => menuKeys.Contains(m.Id))
                    .Select(m => new { m.Id, m.Title, m.Route })
                    .Cast<object>()
                    .ToListAsync(ct);

            return new { personId, staffId = staff.StaffId, menus, features };
        }

        private async Task<Guid?> ResolveStaffIdAsync(Guid personId, CancellationToken ct) =>
            await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.PersonId == personId)
                .Select(s => (Guid?)s.StaffId)
                .FirstOrDefaultAsync(ct);

        private async Task UpsertStaffOverridesAsync(
            Guid staffId,
            IEnumerable<int> permissionIds,
            PermissionStatus status,
            string? grantedBy,
            string reason,
            CancellationToken ct)
        {
            var idList = permissionIds.ToList();
            var existing = await _db.UserPermissionOverrides
                .Where(u => u.StaffId == staffId && idList.Contains(u.PermissionId))
                .ToDictionaryAsync(u => u.PermissionId, ct);

            var now = DateTime.UtcNow;
            var statusStr = status.ToString().ToUpperInvariant();

            foreach (var permId in idList)
            {
                if (!existing.TryGetValue(permId, out var row))
                {
                    _db.UserPermissionOverrides.Add(new UserPermissionOverride
                    {
                        StaffId      = staffId,
                        PermissionId = permId,
                        Status       = statusStr,
                        SetBy        = grantedBy,
                        SetDate      = now,
                        Reason       = reason
                    });
                }
                else
                {
                    row.Status  = statusStr;
                    row.SetBy   = grantedBy;
                    row.SetDate = now;
                    row.Reason  = reason;
                }
            }

            await _db.SaveChangesAsync(ct);
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
    }
}
