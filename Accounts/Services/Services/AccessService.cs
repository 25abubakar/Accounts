using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// AccessService — feature/permission management.
    ///
    /// Access Groups are deprecated; this service now only manages Features
    /// and delegates permission resolution to RbacService.
    /// </summary>
    public class AccessService : IAccessService
    {
        private readonly ApplicationDbContext _db;
        private readonly RbacService          _rbac;

        public AccessService(ApplicationDbContext db, RbacService rbac)
        {
            _db   = db;
            _rbac = rbac;
        }

        // ── Features ──────────────────────────────────────────────────────────

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

        // ── Staff permissions (read-only, via RbacService) ────────────────────

        public async Task<IEnumerable<string>> GetStaffPermissionsAsync(Guid staffId) =>
            await _rbac.GetEffectivePermissionsAsync(staffId);

        // ── Toggle a single permission via UserPermissionOverrides ────────────

        public async Task<(bool Success, string Message)> TogglePermissionAsync(
            Guid staffId, string featureKey, bool hasAccess, string? grantedBy)
        {
            var status = hasAccess ? PermissionStatus.ALLOW : PermissionStatus.DENY;
            return await _rbac.SetUserOverrideAsync(staffId, featureKey, status, grantedBy,
                "Set via Access Manager");
        }

        // ── Grant/Revoke all features for a staff member ──────────────────────

        public async Task<(int Count, string Message)> GrantAllAsync(Guid staffId, int deptId, string? grantedBy)
        {
            var features = await _db.Features.AsNoTracking().ToListAsync();
            int count = 0;
            foreach (var f in features)
            {
                var (ok, _) = await _rbac.SetUserOverrideAsync(
                    staffId, f.FeatureKey, PermissionStatus.ALLOW, grantedBy, "Grant all");
                if (ok) count++;
            }
            return (count, $"All {count} permissions granted.");
        }

        public async Task<(int Count, string Message)> RevokeAllAsync(Guid staffId, string? grantedBy)
        {
            var overrides = await _db.UserPermissionOverrides
                .Where(u => u.StaffId == staffId)
                .ToListAsync();
            _db.UserPermissionOverrides.RemoveRange(overrides);
            await _db.SaveChangesAsync();
            return (overrides.Count, $"All {overrides.Count} overrides removed — user now follows role defaults.");
        }

        // ── Department persons (read-only) ────────────────────────────────────

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
