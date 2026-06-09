using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Filters data based on user's effective permissions.
    /// Only returns data the user has access to view.
    /// 
    /// Permission Keys Used:
    /// - DEPT_VIEW: Can view departments
    /// - EMPLOYEE_VIEW: Can view staff/employees
    /// - PERSON_VIEW: Can view persons
    /// - VACANCY_VIEW: Can view vacancies
    /// - ACCESS_GROUP_VIEW: Can view access groups
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

        public async Task<bool> CanAccessFeatureAsync(Guid staffId, string featureKey)
        {
            return await _rbac.HasAccessAsync(staffId, featureKey);
        }

        public async Task<IEnumerable<string>> GetAccessibleFeaturesAsync(Guid staffId)
        {
            if (staffId == Guid.Empty)
            {
                return await _db.Features.AsNoTracking()
                    .Select(f => f.FeatureKey)
                    .OrderBy(k => k)
                    .ToListAsync();
            }

            return await _rbac.GetEffectivePermissionsAsync(staffId);
        }

        public async Task<object> GetAccessibleDataAsync(Guid staffId)
        {
            if (staffId == Guid.Empty)
            {
                var allPermissions = await _db.Features.AsNoTracking()
                    .Select(f => f.FeatureKey)
                    .OrderBy(k => k)
                    .ToListAsync();

                return new
                {
                    staffId,
                    permissions = allPermissions,
                    data = new
                    {
                        departments  = await GetAccessibleDepartmentsAsync(staffId),
                        staff        = await GetAccessibleStaffAsync(staffId),
                        persons      = await GetAccessiblePersonsAsync(staffId),
                        vacancies    = await GetAccessibleVacanciesAsync(staffId),
                        accessGroups = await GetAccessibleGroupsAsync(staffId)
                    }
                };
            }

            // Get user's permissions
            var permissions = (await _rbac.GetEffectivePermissionsAsync(staffId)).ToHashSet();

            var result = new
            {
                staffId,
                permissions = permissions.OrderBy(p => p).ToList(),
                data = new
                {
                    departments = permissions.Contains("DEPT_VIEW") 
                        ? await GetAccessibleDepartmentsAsync(staffId) 
                        : new List<object>(),
                    
                    staff = permissions.Contains("EMPLOYEE_VIEW") 
                        ? await GetAccessibleStaffAsync(staffId) 
                        : new List<object>(),
                    
                    persons = permissions.Contains("PERSON_VIEW") 
                        ? await GetAccessiblePersonsAsync(staffId) 
                        : new List<object>(),
                    
                    vacancies = permissions.Contains("VACANCY_VIEW") 
                        ? await GetAccessibleVacanciesAsync(staffId) 
                        : new List<object>(),
                    
                    accessGroups = permissions.Contains("ACCESS_GROUP_VIEW") 
                        ? await GetAccessibleGroupsAsync(staffId) 
                        : new List<object>()
                }
            };

            return result;
        }

        public async Task<IEnumerable<object>> GetAccessibleDepartmentsAsync(Guid staffId)
        {
            if (staffId == Guid.Empty)
            {
                return await _db.OrganizationTree
                    .AsNoTracking()
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

            // Check permission first
            if (!await _rbac.HasAccessAsync(staffId, "DEPT_VIEW"))
                return new List<object>();

            // Get user's own department
            var userStaff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (userStaff?.Vacancy == null)
                return new List<object>();

            var userDeptId = userStaff.Vacancy.OrganizationId;

            // Check if user has permission to view all departments
            bool canViewAll = await _rbac.HasAccessAsync(staffId, "DEPT_VIEW_ALL");

            IQueryable<Models.OrganizationTree> query = _db.OrganizationTree.AsNoTracking();

            // If user can't view all, only show their own department
            if (!canViewAll)
            {
                query = query.Where(d => d.Id == userDeptId);
            }

            var departments = await query
                .OrderBy(d => d.Name)
                .Select(d => new
                {
                    OrganizationId = d.Id,
                    OrganizationName = d.Name,
                    OrganizationType = d.Label,
                    d.ParentId,
                    d.Code,
                    d.FlagUrl
                })
                .ToListAsync<object>();

            return departments;
        }

        public async Task<IEnumerable<object>> GetAccessibleStaffAsync(Guid staffId)
        {
            if (staffId == Guid.Empty)
            {
                return await _db.StaffVacancies
                    .AsNoTracking()
                    .Include(s => s.Person)
                    .Include(s => s.Vacancy).ThenInclude(v => v!.Organization)
                    .OrderBy(s => s.Person != null ? s.Person.FullName : "")
                    .Select(s => (object)new
                    {
                        s.StaffId,
                        FullName = s.Person != null ? s.Person.FullName : "-",
                        Email = s.Person != null ? s.Person.Email : null,
                        Phone = s.Person != null ? s.Person.Phone : null,
                        PhotoUrl = s.Person != null ? s.Person.ProfilePhotoUrl : null,
                        PersonId = s.PersonId,
                        LoginId = s.LoginId,
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

            // Check permission first
            if (!await _rbac.HasAccessAsync(staffId, "EMPLOYEE_VIEW"))
                return new List<object>();

            // Get user's own department
            var userStaff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (userStaff?.Vacancy == null)
                return new List<object>();

            var userDeptId = userStaff.Vacancy.OrganizationId;

            // Check if user has permission to view all staff
            bool canViewAll = await _rbac.HasAccessAsync(staffId, "EMPLOYEE_VIEW_ALL");

            IQueryable<Models.StaffVacancy> query = _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Person)
                .Include(s => s.Vacancy);

            // If user can't view all, only show staff from their department
            if (!canViewAll)
            {
                query = query.Where(s => s.Vacancy != null && s.Vacancy.OrganizationId == userDeptId);
            }

            var staff = await query
                .OrderBy(s => s.Person != null ? s.Person.FullName : "")
                .Select(s => new
                {
                    s.StaffId,
                    FullName = s.Person != null ? s.Person.FullName : "-",
                    Email    = s.Person != null ? s.Person.Email : null,
                    Phone    = s.Person != null ? s.Person.Phone : null,
                    PhotoUrl = s.Person != null ? s.Person.ProfilePhotoUrl : null,
                    PersonId = s.PersonId,
                    LoginId = s.LoginId,
                    Vacancy = s.Vacancy != null ? new
                    {
                        s.Vacancy.VacancyId,
                        s.Vacancy.VacancyCode,
                        s.Vacancy.JobTitle,
                        s.Vacancy.OrganizationId,
                        OrganizationName = s.Vacancy.Organization != null ? s.Vacancy.Organization.Name : null
                    } : null
                })
                .ToListAsync<object>();

            return staff;
        }

        public async Task<IEnumerable<object>> GetAccessiblePersonsAsync(Guid staffId)
        {
            if (staffId == Guid.Empty)
            {
                return await _db.Persons
                    .AsNoTracking()
                    .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                    .OrderBy(p => p.FullName)
                    .Select(p => (object)new
                    {
                        p.PersonId,
                        p.FullName,
                        LoginId = p.Staff != null ? p.Staff.LoginId : null,
                        p.Email,
                        Phone = p.Phone,
                        p.ProfilePhotoUrl,
                        IsHired = p.Staff != null,
                        StaffId = p.Staff != null ? p.Staff.StaffId : (Guid?)null,
                        JobTitle = p.Staff != null && p.Staff.Vacancy != null ? p.Staff.Vacancy.JobTitle : null
                    })
                    .ToListAsync();
            }

            // Check permission first
            if (!await _rbac.HasAccessAsync(staffId, "PERSON_VIEW"))
                return new List<object>();

            // Get user's own department
            var userStaff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (userStaff?.Vacancy == null)
                return new List<object>();

            var userDeptId = userStaff.Vacancy.OrganizationId;

            // Check if user has permission to view all persons
            bool canViewAll = await _rbac.HasAccessAsync(staffId, "PERSON_VIEW_ALL");

            IQueryable<Models.Person> query = _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .ThenInclude(s => s!.Vacancy);

            // If user can't view all, only show persons from their department
            if (!canViewAll)
            {
                query = query.Where(p => p.Staff != null && p.Staff.Vacancy != null && p.Staff.Vacancy.OrganizationId == userDeptId);
            }

            var persons = await query
                .OrderBy(p => p.FullName)
                .Select(p => new
                {
                    p.PersonId,
                    p.FullName,
                    LoginId = p.Staff != null ? p.Staff.LoginId : null,
                    p.Email,
                    Phone = p.Phone,
                    p.ProfilePhotoUrl,
                    IsHired = p.Staff != null,
                    StaffId = p.Staff != null ? p.Staff.StaffId : (Guid?)null,
                    JobTitle = p.Staff != null && p.Staff.Vacancy != null ? p.Staff.Vacancy.JobTitle : null
                })
                .ToListAsync<object>();

            return persons;
        }

        private async Task<IEnumerable<object>> GetAccessibleVacanciesAsync(Guid staffId)
        {
            if (staffId == Guid.Empty)
            {
                return await _db.Vacancies
                    .AsNoTracking()
                    .Include(v => v.Organization)
                    .OrderBy(v => v.VacancyCode)
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

            // Check permission first
            if (!await _rbac.HasAccessAsync(staffId, "VACANCY_VIEW"))
                return new List<object>();

            // Get user's own department
            var userStaff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (userStaff?.Vacancy == null)
                return new List<object>();

            var userDeptId = userStaff.Vacancy.OrganizationId;

            // Check if user has permission to view all vacancies
            bool canViewAll = await _rbac.HasAccessAsync(staffId, "VACANCY_VIEW_ALL");

            IQueryable<Models.Vacancy> query = _db.Vacancies
                .AsNoTracking()
                .Include(v => v.Organization);

            // If user can't view all, only show vacancies from their department
            if (!canViewAll)
            {
                query = query.Where(v => v.OrganizationId == userDeptId);
            }

            var vacancies = await query
                .OrderBy(v => v.VacancyCode)
                .Select(v => new
                {
                    v.VacancyId,
                    v.VacancyCode,
                    v.JobTitle,
                    v.OrganizationId,
                    OrganizationName = v.Organization != null ? v.Organization.Name : null,
                    v.IsFilled
                })
                .ToListAsync<object>();

            return vacancies;
        }

        private Task<IEnumerable<object>> GetAccessibleGroupsAsync(Guid staffId) =>
            Task.FromResult<IEnumerable<object>>(Array.Empty<object>());
    }
}
 