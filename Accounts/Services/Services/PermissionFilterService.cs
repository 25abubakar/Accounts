using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Filters data based on user's effective permissions.
    /// Uses the 3-layer RbacService exclusively — supports both legacy and modern Menu feature keys.
    /// </summary>
    public class PermissionFilterService : IPermissionFilterService
    {
        private readonly ApplicationDbContext _db;
        private readonly RbacService _rbac;

        public PermissionFilterService(ApplicationDbContext db, RbacService rbac)
        {
            _db = db;
            _rbac = rbac;
        }

        public Task<bool> CanAccessFeatureAsync(Guid staffId, string featureKey) =>
            _rbac.HasAccessAsync(staffId, featureKey);

        public async Task<IEnumerable<string>> GetAccessibleFeaturesAsync(Guid staffId)
        {
            if (staffId == Guid.Empty)
                return await _db.Features.AsNoTracking()
                    .Select(f => f.FeatureKey)
                    .OrderBy(k => k)
                    .ToListAsync();

            return await _rbac.GetEffectivePermissionsAsync(staffId);
        }

        public async Task<object> GetAccessibleDataAsync(Guid staffId)
        {
            var permissions = (await GetAccessibleFeaturesAsync(staffId)).ToHashSet();

            // 🔥 PERMANENT DATA RECOVERY BRIDGE:
            // Check if user has either the old feature key OR the modern menu UI key
            bool hasDeptAccess = permissions.Contains("DEPT_VIEW") || permissions.Contains("MENU_5_VIEW");
            bool hasStaffAccess = permissions.Contains("EMPLOYEE_VIEW") || permissions.Contains("MENU_8_VIEW");
            bool hasPersonAccess = permissions.Contains("PERSON_VIEW") || permissions.Contains("MENU_8_VIEW");
            bool hasVacancyAccess = permissions.Contains("VACANCY_VIEW") || permissions.Contains("MENU_11_VIEW");

            return new
            {
                staffId,
                permissions = permissions.OrderBy(p => p).ToList(),
                data = new
                {
                    departments = hasDeptAccess
                        ? await GetAccessibleDepartmentsAsync(staffId)
                        : new List<object>(),
                    staff = hasStaffAccess
                        ? await GetAccessibleStaffAsync(staffId)
                        : new List<object>(),
                    persons = hasPersonAccess
                        ? await GetAccessiblePersonsAsync(staffId)
                        : new List<object>(),
                    vacancies = hasVacancyAccess
                        ? await GetAccessibleVacanciesAsync(staffId)
                        : new List<object>()
                }
            };
        }

        public async Task<IEnumerable<object>> GetAccessibleDepartmentsAsync(Guid staffId)
        {
            // Allow view-all fallback if user has global admin setup or modern menu view activated
            bool canViewAll = staffId == Guid.Empty ||
                              await _rbac.HasAccessAsync(staffId, "DEPT_VIEW_ALL") ||
                              await _rbac.HasAccessAsync(staffId, "MENU_5_VIEW");

            if (canViewAll)
            {
                return await _db.OrganizationTree.AsNoTracking()
                    .OrderBy(d => d.Name)
                    .Select(d => (object)new
                    {
                        OrganizationId = d.Id,
                        OrganizationName = d.Name,
                        OrganizationType = d.Label,
                        d.ParentId,
                        d.Code,
                        d.FlagUrl
                    })
                    .ToListAsync();
            }

            if (!await _rbac.HasAccessAsync(staffId, "DEPT_VIEW") && !await _rbac.HasAccessAsync(staffId, "MENU_5_VIEW"))
                return new List<object>();

            var deptId = await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.StaffId == staffId && s.Vacancy != null)
                .Select(s => (int?)s.Vacancy!.OrganizationId)
                .FirstOrDefaultAsync();

            if (deptId == null) return new List<object>();

            return await _db.OrganizationTree.AsNoTracking()
                .Where(d => d.Id == deptId)
                .Select(d => (object)new
                {
                    OrganizationId = d.Id,
                    OrganizationName = d.Name,
                    OrganizationType = d.Label,
                    d.ParentId,
                    d.Code,
                    d.FlagUrl
                })
                .ToListAsync();
        }

        public async Task<IEnumerable<object>> GetAccessibleStaffAsync(Guid staffId)
        {
            if (!await _rbac.HasAccessAsync(staffId, "EMPLOYEE_VIEW") && !await _rbac.HasAccessAsync(staffId, "MENU_8_VIEW"))
                return new List<object>();

            bool canViewAll = staffId == Guid.Empty ||
                              await _rbac.HasAccessAsync(staffId, "EMPLOYEE_VIEW_ALL") ||
                              await _rbac.HasAccessAsync(staffId, "MENU_8_VIEW");

            var query = _db.StaffVacancies.AsNoTracking()
                .Include(s => s.Person)
                .Include(s => s.Vacancy).ThenInclude(v => v!.Organization)
                .AsQueryable();

            if (!canViewAll)
            {
                var deptId = await _db.StaffVacancies.AsNoTracking()
                    .Where(s => s.StaffId == staffId && s.Vacancy != null)
                    .Select(s => (int?)s.Vacancy!.OrganizationId)
                    .FirstOrDefaultAsync();
                if (deptId == null) return new List<object>();
                query = query.Where(s => s.Vacancy != null && s.Vacancy.OrganizationId == deptId);
            }

            return await query.OrderBy(s => s.Person != null ? s.Person.FullName : "")
                .Select(s => (object)new
                {
                    s.StaffId,
                    FullName = s.Person != null ? s.Person.FullName : "-",
                    Email = s.Person != null ? s.Person.Email : null,
                    Phone = s.Person != null ? s.Person.Phone : null,
                    PhotoUrl = s.Person != null ? s.Person.ProfilePhotoUrl : null,
                    PersonId = s.PersonId,
                    s.LoginId,
                    Vacancy = s.Vacancy != null ? new
                    {
                        s.Vacancy.VacancyId,
                        s.Vacancy.VacancyCode,
                        s.Vacancy.JobTitle,
                        s.Vacancy.OrganizationId,
                        OrganizationName = s.Vacancy.Organization != null ? s.Vacancy.Organization.Name : null
                    } : null
                })
                .ToListAsync();
        }

        public async Task<IEnumerable<object>> GetAccessiblePersonsAsync(Guid staffId)
        {
            if (!await _rbac.HasAccessAsync(staffId, "PERSON_VIEW") && !await _rbac.HasAccessAsync(staffId, "MENU_8_VIEW"))
                return new List<object>();

            bool canViewAll = staffId == Guid.Empty ||
                              await _rbac.HasAccessAsync(staffId, "PERSON_VIEW_ALL") ||
                              await _rbac.HasAccessAsync(staffId, "MENU_8_VIEW");

            var query = _db.Persons.AsNoTracking()
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .AsQueryable();

            if (!canViewAll)
            {
                var deptId = await _db.StaffVacancies.AsNoTracking()
                    .Where(s => s.StaffId == staffId && s.Vacancy != null)
                    .Select(s => (int?)s.Vacancy!.OrganizationId)
                    .FirstOrDefaultAsync();
                if (deptId == null) return new List<object>();
                query = query.Where(p => p.Staff != null && p.Staff.Vacancy != null &&
                                         p.Staff.Vacancy.OrganizationId == deptId);
            }

            return await query.OrderBy(p => p.FullName)
                .Select(p => (object)new
                {
                    p.PersonId,
                    p.FullName,
                    LoginId = p.Staff != null ? p.Staff.LoginId : null,
                    p.Email,
                    p.Phone,
                    p.ProfilePhotoUrl,
                    IsHired = p.Staff != null,
                    StaffId = p.Staff != null ? p.Staff.StaffId : (Guid?)null,
                    JobTitle = p.Staff != null && p.Staff.Vacancy != null ? p.Staff.Vacancy.JobTitle : null
                })
                .ToListAsync();
        }

        private async Task<IEnumerable<object>> GetAccessibleVacanciesAsync(Guid staffId)
        {
            if (!await _rbac.HasAccessAsync(staffId, "VACANCY_VIEW") && !await _rbac.HasAccessAsync(staffId, "MENU_11_VIEW"))
                return new List<object>();

            bool canViewAll = staffId == Guid.Empty ||
                              await _rbac.HasAccessAsync(staffId, "VACANCY_VIEW_ALL") ||
                              await _rbac.HasAccessAsync(staffId, "MENU_11_VIEW");

            var query = _db.Vacancies.AsNoTracking()
                .Include(v => v.Organization)
                .AsQueryable();

            if (!canViewAll)
            {
                var deptId = await _db.StaffVacancies.AsNoTracking()
                    .Where(s => s.StaffId == staffId && s.Vacancy != null)
                    .Select(s => (int?)s.Vacancy!.OrganizationId)
                    .FirstOrDefaultAsync();
                if (deptId == null) return new List<object>();
                query = query.Where(v => v.OrganizationId == deptId);
            }

            return await query.OrderBy(v => v.VacancyCode)
                .Select(v => (object)new
                {
                    v.VacancyId,
                    v.VacancyCode,
                    v.JobTitle,
                    v.OrganizationId,
                    OrganizationName = v.Organization != null ? v.Organization.Name : null,
                    v.IsFilled
                })
                .ToListAsync();
        }
    }
}