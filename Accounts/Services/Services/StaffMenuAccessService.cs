using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Manages the 2-tier RBAC model:
    ///   Tier 1 — StaffMenuAccess:  which menus a staff member can open
    ///   Tier 2 — AccessFeatures:   which CRUD features inside each menu are allowed
    ///
    /// All writes hit the DB once per operation (no N+1).
    /// All reads for the permissions endpoint use a single joined query.
    /// </summary>
    public class StaffMenuAccessService
    {
        private readonly ApplicationDbContext _db;
        public StaffMenuAccessService(ApplicationDbContext db) => _db = db;

        // ─────────────────────────────────────────────────────────────────────
        // READ — permissions response used at login (GET /api/rbac/staff/{id}/access-tree)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a nested permission tree for a staff member.
        ///
        /// One joined query loads StaffMenuAccess + Menu + AccessFeatures + Feature.
        /// Everything is resolved in-memory — zero N+1 queries.
        ///
        /// Response shape:
        /// {
        ///   staffId, menus: [
        ///     { menuId, menuTitle, route, isAllow, features: [
        ///         { permissionId, featureKey, featureName, isAllow }
        ///     ]}
        ///   ],
        ///   allowedFeatureKeys: ["MENU_8_VIEW", "MENU_8_ADD", ...]
        /// }
        /// </summary>
        public async Task<StaffAccessTreeDto> GetStaffAccessTreeAsync(Guid staffId)
        {
            // Single joined query — EF generates one SQL with multiple JOINs
            var menuAccesses = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Where(sma => sma.StaffId == staffId)
                .Include(sma => sma.Menu)
                .Include(sma => sma.AccessFeatures)
                    .ThenInclude(af => af.Feature)
                .OrderBy(sma => sma.Menu != null ? sma.Menu.SortOrder : 0)
                .ToListAsync();

            var menus = menuAccesses.Select(sma => new MenuAccessDto
            {
                MenuId    = sma.MenuId,
                MenuTitle = sma.Menu?.Title ?? $"Menu {sma.MenuId}",
                Route     = sma.Menu?.Route,
                Icon      = sma.Menu?.Icon,
                IsAllow   = sma.IsAllow,
                Features  = sma.AccessFeatures
                    .Where(af => af.Feature != null)
                    .Select(af => new FeatureAccessDto
                    {
                        PermissionId = af.PermissionId,
                        FeatureKey   = af.Feature!.FeatureKey,
                        FeatureName  = af.Feature.FeatureName,
                        Module       = af.Feature.Module,
                        IsAllow      = af.IsAllow
                    })
                    .OrderBy(f => f.FeatureKey)
                    .ToList()
            }).ToList();

            // Flat list of all allowed feature keys — consumed by frontend can() helper
            var allowedFeatureKeys = menus
                .Where(m => m.IsAllow)
                .SelectMany(m => m.Features.Where(f => f.IsAllow).Select(f => f.FeatureKey))
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            return new StaffAccessTreeDto
            {
                StaffId             = staffId,
                Menus               = menus,
                AllowedFeatureKeys  = allowedFeatureKeys
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // GRANT — write a full menu bundle in exactly 2 DB trips
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Grants a staff member access to a menu and its child feature keys.
        ///
        /// Algorithm (2 DB trips):
        ///   1. Load all Features whose keys match MENU_{menuId}* in one query.
        ///   2. Upsert StaffMenuAccess + all AccessFeature rows, then SaveChanges once.
        /// </summary>
        public async Task<(bool Success, string Message, IReadOnlyList<string> GrantedKeys)>
            GrantMenuAccessAsync(
                Guid   staffId,
                int    menuId,
                bool   isAllow,
                string? grantedBy,
                IEnumerable<(int PermissionId, bool IsAllow)>? featureOverrides = null)
        {
            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.", Array.Empty<string>());

            var menu = await _db.Menus.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == menuId && m.IsActive);
            if (menu == null)
                return (false, $"Menu {menuId} not found or inactive.", Array.Empty<string>());

            // Load the feature rows linked to this menu (MENU_{id}, MENU_{id}_VIEW, etc.)
            var menuFeaturePrefix = $"MENU_{menuId}";
            var features = await _db.Features.AsNoTracking()
                .Where(f => f.FeatureKey == menuFeaturePrefix
                         || f.FeatureKey.StartsWith(menuFeaturePrefix + "_"))
                .ToListAsync();

            // Build override map if provided
            var overrideMap = featureOverrides?.ToDictionary(x => x.PermissionId, x => x.IsAllow)
                              ?? new Dictionary<int, bool>();

            // Load existing StaffMenuAccess (tracked for upsert)
            var existing = await _db.StaffMenuAccesses
                .Include(sma => sma.AccessFeatures)
                .FirstOrDefaultAsync(sma => sma.StaffId == staffId && sma.MenuId == menuId);

            if (existing == null)
            {
                existing = new StaffMenuAccess
                {
                    StaffId     = staffId,
                    MenuId      = menuId,
                    IsAllow     = isAllow,
                    GrantedBy   = grantedBy,
                    GrantedDate = DateTime.UtcNow
                };
                _db.StaffMenuAccesses.Add(existing);
            }
            else
            {
                existing.IsAllow   = isAllow;
                existing.GrantedBy = grantedBy;
            }

            // Save to get the Id if it's new (needed for AccessFeature FK)
            await _db.SaveChangesAsync();

            // Upsert AccessFeature rows — all in memory, one SaveChanges at the end
            var existingFeatureMap = existing.AccessFeatures.ToDictionary(af => af.PermissionId);

            var grantedKeys = new List<string>();

            foreach (var feature in features)
            {
                var featureIsAllow = overrideMap.TryGetValue(feature.PermissionId, out var ov)
                    ? ov
                    : isAllow;

                if (existingFeatureMap.TryGetValue(feature.PermissionId, out var existingAf))
                {
                    existingAf.IsAllow = featureIsAllow;
                }
                else
                {
                    _db.AccessFeatures.Add(new AccessFeature
                    {
                        StaffMenuAccessId = existing.Id,
                        PermissionId      = feature.PermissionId,
                        IsAllow           = featureIsAllow
                    });
                }

                if (featureIsAllow) grantedKeys.Add(feature.FeatureKey);
            }

            await _db.SaveChangesAsync();

            return (true,
                $"Granted menu '{menu.Title}' with {features.Count} feature(s) to staff {staffId}.",
                grantedKeys);
        }

        // ─────────────────────────────────────────────────────────────────────
        // BULK GRANT — grant multiple menus at once (admin wizard)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bulk-grant or bulk-deny a set of menus for a staff member.
        /// Accepts: { menuId → isAllow }
        ///
        /// For each menu, also upserts AccessFeature rows for MENU_{id}_VIEW/ADD/EDIT/DELETE.
        /// Total DB trips: 3 reads (staff verify, features, existing rows) + 1 write.
        /// </summary>
        public async Task<(int Saved, int Skipped, string Message)> BulkGrantMenusAsync(
            Guid staffId,
            IReadOnlyDictionary<int, bool> menuGrants,
            string? grantedBy)
        {
            if (menuGrants == null || menuGrants.Count == 0)
                return (0, 0, "No menu grants provided.");

            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (0, 0, "Staff not found.");

            // ONE query: load all relevant menus
            var menuIds  = menuGrants.Keys.ToList();
            var menus    = await _db.Menus.AsNoTracking()
                .Where(m => menuIds.Contains(m.Id) && m.IsActive)
                .ToDictionaryAsync(m => m.Id);

            // ONE query: load all features that match any MENU_{id}* pattern
            var prefixes = menuIds.Select(id => $"MENU_{id}").ToList();
            var allFeatures = await _db.Features.AsNoTracking()
                .Where(f => prefixes.Any(p => f.FeatureKey == p || f.FeatureKey.StartsWith(p + "_")))
                .ToListAsync();

            // ONE query: load all existing StaffMenuAccess rows (tracked for upsert)
            var existingAccesses = await _db.StaffMenuAccesses
                .Include(sma => sma.AccessFeatures)
                .Where(sma => sma.StaffId == staffId && menuIds.Contains(sma.MenuId))
                .ToListAsync();

            var existingMap = existingAccesses.ToDictionary(sma => sma.MenuId);

            int saved = 0, skipped = 0;

            foreach (var (menuId, isAllow) in menuGrants)
            {
                if (!menus.TryGetValue(menuId, out var menu)) { skipped++; continue; }

                StaffMenuAccess smaRow;
                if (existingMap.TryGetValue(menuId, out var existingSma))
                {
                    existingSma.IsAllow   = isAllow;
                    existingSma.GrantedBy = grantedBy;
                    smaRow = existingSma;
                }
                else
                {
                    smaRow = new StaffMenuAccess
                    {
                        StaffId   = staffId,
                        MenuId    = menuId,
                        IsAllow   = isAllow,
                        GrantedBy = grantedBy,
                        GrantedDate = DateTime.UtcNow
                    };
                    _db.StaffMenuAccesses.Add(smaRow);
                    existingMap[menuId] = smaRow;
                }
                saved++;
            }

            // Save to get generated Ids for new StaffMenuAccess rows
            await _db.SaveChangesAsync();

            // Now upsert AccessFeature rows in-memory
            foreach (var (menuId, isAllow) in menuGrants)
            {
                if (!existingMap.TryGetValue(menuId, out var smaRow)) continue;

                var prefix   = $"MENU_{menuId}";
                var menuFeatures = allFeatures
                    .Where(f => f.FeatureKey == prefix || f.FeatureKey.StartsWith(prefix + "_"))
                    .ToList();

                var existingAfMap = smaRow.AccessFeatures.ToDictionary(af => af.PermissionId);

                foreach (var feature in menuFeatures)
                {
                    if (existingAfMap.TryGetValue(feature.PermissionId, out var existingAf))
                    {
                        existingAf.IsAllow = isAllow;
                    }
                    else
                    {
                        _db.AccessFeatures.Add(new AccessFeature
                        {
                            StaffMenuAccessId = smaRow.Id,
                            PermissionId      = feature.PermissionId,
                            IsAllow           = isAllow
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();
            return (saved, skipped, $"{saved} menu access(es) saved, {skipped} skipped.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // REVOKE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Revokes a staff member's access to a menu.
        /// CASCADE DELETE in DB removes all child AccessFeature rows automatically.
        /// </summary>
        public async Task<(bool Success, string Message)> RevokeMenuAccessAsync(
            Guid staffId, int menuId)
        {
            var row = await _db.StaffMenuAccesses
                .FirstOrDefaultAsync(sma => sma.StaffId == staffId && sma.MenuId == menuId);

            if (row == null)
                return (false, $"No access grant found for staff {staffId}, menu {menuId}.");

            _db.StaffMenuAccesses.Remove(row);
            await _db.SaveChangesAsync();
            return (true, $"Revoked menu {menuId} access (and all child features) for staff {staffId}.");
        }

        /// <summary>Remove all menu access grants for a staff member.</summary>
        public async Task<int> ClearAllAccessAsync(Guid staffId)
        {
            var rows = await _db.StaffMenuAccesses
                .Where(sma => sma.StaffId == staffId)
                .ToListAsync();
            if (rows.Count == 0) return 0;
            _db.StaffMenuAccesses.RemoveRange(rows);
            await _db.SaveChangesAsync();
            return rows.Count;
        }

        // ─────────────────────────────────────────────────────────────────────
        // FEATURE-LEVEL TOGGLE (Tier-2 fine-grained control)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Toggles a single feature flag inside an existing StaffMenuAccess grant.
        /// If the parent StaffMenuAccess row does not exist, returns an error.
        /// </summary>
        public async Task<(bool Success, string Message)> SetFeatureAccessAsync(
            Guid staffId, int menuId, int permissionId, bool isAllow)
        {
            var sma = await _db.StaffMenuAccesses
                .Include(x => x.AccessFeatures)
                .FirstOrDefaultAsync(x => x.StaffId == staffId && x.MenuId == menuId);

            if (sma == null)
                return (false, $"Staff {staffId} has no access grant for menu {menuId}. Grant menu access first.");

            var existing = sma.AccessFeatures
                .FirstOrDefault(af => af.PermissionId == permissionId);

            if (existing != null)
            {
                existing.IsAllow = isAllow;
            }
            else
            {
                _db.AccessFeatures.Add(new AccessFeature
                {
                    StaffMenuAccessId = sma.Id,
                    PermissionId      = permissionId,
                    IsAllow           = isAllow
                });
            }

            await _db.SaveChangesAsync();
            return (true, $"Feature {permissionId} set to {(isAllow ? "ALLOW" : "DENY")} for staff {staffId}, menu {menuId}.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Response DTOs (used only by this service + controller)
    // ─────────────────────────────────────────────────────────────────────────

    public class StaffAccessTreeDto
    {
        public Guid              StaffId            { get; init; }
        public List<MenuAccessDto> Menus            { get; init; } = new();
        public List<string>      AllowedFeatureKeys { get; init; } = new();
    }

    public class MenuAccessDto
    {
        public int                   MenuId    { get; init; }
        public string                MenuTitle { get; init; } = string.Empty;
        public string?               Route     { get; init; }
        public string?               Icon      { get; init; }
        public bool                  IsAllow   { get; init; }
        public List<FeatureAccessDto> Features { get; init; } = new();
    }

    public class FeatureAccessDto
    {
        public int    PermissionId { get; init; }
        public string FeatureKey   { get; init; } = string.Empty;
        public string FeatureName  { get; init; } = string.Empty;
        public string Module       { get; init; } = string.Empty;
        public bool   IsAllow      { get; init; }
    }
}
