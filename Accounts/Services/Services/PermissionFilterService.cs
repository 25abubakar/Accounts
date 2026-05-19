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
            return await _rbac.GetEffectivePermissionsAsync(staffId);
        }

        public async Task<object> GetAccessibleDataAsync(Guid staffId)
        {
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
            // Check permission first
            if (!await _rbac.HasAccessAsync(staffId, "DEPT_VIEW"))
                return new List<object>();

            // Get user's own department
            var userStaff = await _db.Staff
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
            // Check permission first
            if (!await _rbac.HasAccessAsync(staffId, "EMPLOYEE_VIEW"))
                return new List<object>();

            // Get user's own department
            var userStaff = await _db.Staff
                .AsNoTracking()
                .Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);

            if (userStaff?.Vacancy == null)
                return new List<object>();

            var userDeptId = userStaff.Vacancy.OrganizationId;

            // Check if user has permission to view all staff
            bool canViewAll = await _rbac.HasAccessAsync(staffId, "EMPLOYEE_VIEW_ALL");

            IQueryable<Models.Staff> query = _db.Staff
                .AsNoTracking()
                .Include(s => s.Person)
                .Include(s => s.Vacancy);

            // If user can't view all, only show staff from their department
            if (!canViewAll)
            {
                query = query.Where(s => s.Vacancy != null && s.Vacancy.OrganizationId == userDeptId);
            }

            var staff = await query
                .OrderBy(s => s.FullName)
                .Select(s => new
                {
                    s.StaffId,
                    s.FullName,
                    s.Email,
                    Phone = s.Phone,
                    s.PhotoUrl,
                    PersonId = s.PersonId,
                    LoginId = s.Person != null ? s.Person.LoginId : null,
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
            // Check permission first
            if (!await _rbac.HasAccessAsync(staffId, "PERSON_VIEW"))
                return new List<object>();

            // Get user's own department
            var userStaff = await _db.Staff
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
                query = query.Where(p => p.BranchId == userDeptId);
            }

            var persons = await query
                .OrderBy(p => p.FullName)
                .Select(p => new
                {
                    p.PersonId,
                    p.FullName,
                    p.LoginId,
                    p.Email,
                    Phone = p.Phone,
                    p.ProfilePhotoUrl,
                    p.BranchId,
                    IsHired = p.Staff != null,
                    StaffId = p.Staff != null ? p.Staff.StaffId : (Guid?)null,
                    JobTitle = p.Staff != null && p.Staff.Vacancy != null ? p.Staff.Vacancy.JobTitle : null
                })
                .ToListAsync<object>();

            return persons;
        }

        private async Task<IEnumerable<object>> GetAccessibleVacanciesAsync(Guid staffId)
        {
            // Check permission first
            if (!await _rbac.HasAccessAsync(staffId, "VACANCY_VIEW"))
                return new List<object>();

            // Get user's own department
            var userStaff = await _db.Staff
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

        private async Task<IEnumerable<object>> GetAccessibleGroupsAsync(Guid staffId)
        {
            // Check permission first
            if (!await _rbac.HasAccessAsync(staffId, "ACCESS_GROUP_VIEW"))
                return new List<object>();

            var groups = await _db.AccessGroups
                .AsNoTracking()
                .Include(g => g.Features)
                .Where(g => g.IsActive)
                .OrderBy(g => g.GroupName)
                .Select(g => new
                {
                    g.GroupId,
                    g.GroupName,
                    g.Description,
                    g.IsActive,
                    g.CreatedDate,
                    Features = g.Features.Select(f => f.FeatureKey).ToList(),
                    StaffCount = g.Staff.Count()
                })
                .ToListAsync<object>();

            return groups;
        }
    }
}
