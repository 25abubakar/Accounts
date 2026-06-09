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
            // DEPRECATED: This method is stubbed. Use RbacService.GetEffectivePermissionsAsync() instead.
            await Task.CompletedTask;
            return Array.Empty<string>();
        }

        public async Task<(bool Success, string Message)> TogglePermissionAsync(
            Guid staffId, string featureKey, bool hasAccess, string? grantedBy)
        {
            // DEPRECATED: This method is stubbed. Use RbacService.SetUserOverrideAsync() or StaffMenuAccessService instead.
            await Task.CompletedTask;
            return (false, "This method is deprecated. Use RbacService.SetUserOverrideAsync() or StaffMenuAccessService.");
        }

        public async Task<(int Count, string Message)> GrantAllAsync(
            Guid staffId, int deptId, string? grantedBy)
        {
            // DEPRECATED: This method is stubbed. Use RbacService or StaffMenuAccessService instead.
            await Task.CompletedTask;
            return (0, "This method is deprecated. Use RbacService or StaffMenuAccessService.");
        }

        public async Task<(int Count, string Message)> RevokeAllAsync(Guid staffId, string? grantedBy)
        {
            // DEPRECATED: This method is stubbed. Use RbacService.ClearStaffOverridesAsync() instead.
            await Task.CompletedTask;
            return (0, "This method is deprecated. Use RbacService.ClearStaffOverridesAsync().");
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
