using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Feature catalog and staff permission helpers.
    /// Access groups and department matrix writes are deprecated — use RbacService / UserPermissionOverrides.
    /// </summary>
    public class AccessService : IAccessService
    {
        private readonly ApplicationDbContext _db;

        public AccessService(ApplicationDbContext db) => _db = db;

        public async Task<IEnumerable<object>> GetAllFeaturesAsync() =>
            await _db.Features
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .Select(f => new { f.FeatureKey, f.FeatureName, f.Module, f.Description })
                .ToListAsync<object>();

        public async Task<IEnumerable<object>> GetFeaturesByModuleAsync(string module) =>
            await _db.Features
                .Where(f => f.Module.ToLower() == module.ToLower())
                .OrderBy(f => f.FeatureKey)
                .Select(f => new { f.FeatureKey, f.FeatureName, f.Module, f.Description })
                .ToListAsync<object>();

        public async Task<IEnumerable<string>> GetStaffPermissionsAsync(Guid staffId)
        {
            var overridePerms = await _db.UserPermissionOverrides
                .AsNoTracking()
                .Where(u => u.StaffId == staffId && u.Status == nameof(PermissionStatus.ALLOW))
                .Join(
                    _db.Features.AsNoTracking(),
                    u => u.PermissionId,
                    f => f.PermissionId,
                    (_, f) => f.FeatureKey)
                .ToListAsync();

            var matrixPerms = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .Where(m => m.StaffId == staffId && m.HasAccess)
                .Join(
                    _db.Features.AsNoTracking(),
                    m => m.PermissionId,
                    f => f.PermissionId,
                    (_, f) => f.FeatureKey)
                .ToListAsync();

            return overridePerms.Union(matrixPerms).Distinct().OrderBy(k => k);
        }

        public async Task<(bool Success, string Message)> TogglePermissionAsync(
            Guid staffId, string featureKey, bool hasAccess, string? grantedBy)
        {
            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.");

            var feature = await _db.Features.FirstOrDefaultAsync(f => f.FeatureKey == featureKey);

            if (feature == null)
            {
                if (featureKey.StartsWith("MENU_", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = featureKey.Split('_');
                    int.TryParse(parts.Length >= 2 ? parts[1] : "0", out int menuId);
                    var menu = menuId > 0
                        ? await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId)
                        : null;
                    string title = menu?.Title ?? $"Menu {menuId}";
                    string suffix = parts.Length >= 3 ? string.Join("_", parts.Skip(2)) : "";
                    string name = suffix switch
                    {
                        "VIEW"   => $"{title} - View",
                        "ADD"    => $"{title} - Add",
                        "EDIT"   => $"{title} - Edit",
                        "DELETE" => $"{title} - Delete",
                        ""       => title,
                        _        => $"{title} - {suffix}"
                    };
                    feature = new Feature { FeatureKey = featureKey, FeatureName = name, Module = "Menu" };
                    _db.Features.Add(feature);
                    try { await _db.SaveChangesAsync(); }
                    catch { /* ignore duplicate key race */ }
                }
                else
                {
                    return (false, $"Feature '{featureKey}' not found. Use GET /api/access/features.");
                }
            }

            var status = hasAccess ? PermissionStatus.ALLOW : PermissionStatus.DENY;
            var now = DateTime.UtcNow;

            var upo = await _db.UserPermissionOverrides
                .FirstOrDefaultAsync(u => u.StaffId == staffId && u.PermissionId == feature.PermissionId);

            if (upo == null)
            {
                _db.UserPermissionOverrides.Add(new UserPermissionOverride
                {
                    StaffId      = staffId,
                    PermissionId = feature.PermissionId,
                    Status       = status.ToString(),
                    SetBy        = grantedBy,
                    SetDate      = now,
                    Reason       = "Set via Access Manager"
                });
            }
            else
            {
                upo.Status  = status.ToString();
                upo.SetBy   = grantedBy;
                upo.SetDate = now;
            }

            await _db.SaveChangesAsync();
            return (true, $"Permission '{featureKey}' {(hasAccess ? "granted" : "revoked")} for staff {staffId}.");
        }

        public async Task<(int Count, string Message)> GrantAllAsync(
            Guid staffId, int deptId, string? grantedBy)
        {
            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (0, $"Staff {staffId} not found.");

            var features = await _db.Features.AsNoTracking().ToListAsync();
            var existing = await _db.UserPermissionOverrides
                .Where(u => u.StaffId == staffId)
                .ToDictionaryAsync(u => u.PermissionId);

            var now = DateTime.UtcNow;
            int count = 0;

            foreach (var f in features)
            {
                if (!existing.TryGetValue(f.PermissionId, out var row))
                {
                    _db.UserPermissionOverrides.Add(new UserPermissionOverride
                    {
                        StaffId      = staffId,
                        PermissionId = f.PermissionId,
                        Status       = nameof(PermissionStatus.ALLOW),
                        SetBy        = grantedBy,
                        SetDate      = now,
                        Reason       = "Grant all via Access Manager"
                    });
                    count++;
                }
                else if (row.Status != nameof(PermissionStatus.ALLOW))
                {
                    row.Status  = nameof(PermissionStatus.ALLOW);
                    row.SetBy   = grantedBy;
                    row.SetDate = now;
                    count++;
                }
            }

            if (count > 0)
                await _db.SaveChangesAsync();

            return (count, $"Granted {count} permission(s) via UserPermissionOverrides.");
        }

        public async Task<(int Count, string Message)> RevokeAllAsync(Guid staffId, string? grantedBy)
        {
            var rows = await _db.UserPermissionOverrides
                .Where(u => u.StaffId == staffId)
                .ToListAsync();

            if (rows.Count == 0)
                return (0, "No overrides to revoke.");

            _db.UserPermissionOverrides.RemoveRange(rows);
            await _db.SaveChangesAsync();
            return (rows.Count, $"Removed {rows.Count} override(s). User falls back to role defaults.");
        }

        public async Task<IEnumerable<object>> GetDepartmentPersonsAsync(int deptId)
        {
            var persons = await _db.Persons
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .Where(p => p.Staff != null && p.Staff.Vacancy != null && p.Staff.Vacancy.OrganizationId == deptId)
                .OrderBy(p => p.FullName)
                .ToListAsync();

            return persons.Select(p => (object)new
            {
                personId    = p.PersonId,
                staffId     = p.Staff?.StaffId,
                fullName    = p.FullName,
                loginId     = p.Staff?.LoginId,
                email       = p.Email,
                photoUrl    = p.ProfilePhotoUrl,
                isHired     = p.Staff != null,
                jobTitle    = p.Staff?.Vacancy?.JobTitle,
                vacancyCode = p.Staff?.Vacancy?.VacancyCode
            });
        }
    }
}
