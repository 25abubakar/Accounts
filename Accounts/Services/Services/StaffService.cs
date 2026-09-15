using Accounts.Data;
using Accounts.Models;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class StaffService : IStaffService
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment  _env;
        private readonly ITenantService       _tenantService;

        public StaffService(
            ApplicationDbContext db,
            IWebHostEnvironment  env,
            ITenantService       tenantService)
        {
            _db            = db;
            _env           = env;
            _tenantService = tenantService;
        }

        public async Task<IEnumerable<StaffDto>> GetAllAsync()
        {
            var rows = await SpListQuery.ExecAsync<HrStaffListRow>(
                _db,
                "EXEC dbo.usp_Hr_Staff_List @TenantId",
                CancellationToken.None,
                SpListQuery.TenantId(_tenantService.RequiredTenantId));
            return rows.Select(MapStaffRow);
        }

        public async Task<StaffDto?> GetByIdAsync(Guid id)
        {
            var s = await WithIncludes().FirstOrDefaultAsync(x => x.StaffId == id);
            return s == null ? null : MapToDto(s);
        }

        public async Task<IEnumerable<StaffDto>> SearchAsync(string q)
        {
            var needle = (q ?? string.Empty).Trim();
            var all = await GetAllAsync();
            if (string.IsNullOrWhiteSpace(needle)) return all;
            return all.Where(s =>
                (!string.IsNullOrWhiteSpace(s.FullName) && s.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(s.Email) && s.Email.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<(StaffDto? Staff, string? Error)> HireAsync(Guid vacancyId, HireStaffDto dto)
        {
            // After schema refactor, staff profile columns no longer exist on StaffVacancy.
            // Hiring must link an existing registered Person.
            _ = dto;
            return Task.FromResult<(StaffDto?, string?)>((null, "Direct hire is no longer supported. Register the person first, then use hire-person (vacancy + personId)."));
        }

        public async Task<(StaffDto? Staff, string? Error)> HirePersonAsync(Guid vacancyId, Guid personId)
        {
            var vacancy = await _db.Vacancies.FindAsync(vacancyId);
            if (vacancy == null) return (null, $"Vacancy {vacancyId} not found.");
            if (vacancy.IsFilled) return (null, $"Vacancy '{vacancy.VacancyCode}' is already filled.");

            var person = await _db.Persons.FindAsync(personId);
            if (person == null) return (null, $"Person {personId} not found.");

            if (await _db.StaffVacancies.AnyAsync(s => s.PersonId == personId))
                return (null, $"Person '{person.FullName}' is already hired.");

            var identityUser = await _db.Users.FindAsync(person.IdentityUserId);

            var staff = new StaffVacancy
            {
                StaffId    = Guid.NewGuid(),
                VacancyId  = vacancyId,
                PersonId   = personId,
                LoginId    = identityUser?.UserName,
                TenantId   = vacancy.TenantId   // inherit TenantId from the vacancy
            };

            _db.StaffVacancies.Add(staff);
            var staffAttendanceMenuId = await _db.Menus
                .Where(menu => menu.IsActive && menu.Route == "/attendance/staff")
                .Select(menu => (int?)menu.Id)
                .FirstOrDefaultAsync();
            if (staffAttendanceMenuId.HasValue)
            {
                _db.StaffMenuAccesses.Add(new StaffMenuAccess
                {
                    StaffId = staff.StaffId,
                    MenuId = staffAttendanceMenuId.Value,
                    IsAllow = true,
                    GrantedBy = "System: Staff Attendance",
                    GrantedDate = DateTime.UtcNow
                });
            }
            vacancy.IsFilled = true;
            await _db.SaveChangesAsync();

            var created = await WithIncludes().FirstOrDefaultAsync(s => s.StaffId == staff.StaffId);
            return (MapToDto(created!), null);
        }

        public async Task<(StaffDto? Staff, string? Error)> UpdateAsync(Guid id, UpdateStaffDto dto)
        {
            _ = dto;

            var staff = await _db.StaffVacancies.FindAsync(id);
            if (staff == null) return (null, $"Staff {id} not found.");
            return (MapToDto((await WithIncludes().FirstAsync(x => x.StaffId == staff.StaffId))!), null);
        }

        public async Task<(StaffDto? Staff, string? Error)> TransferAsync(Guid id, TransferStaffDto dto)
        {
            var staff = await _db.StaffVacancies.FindAsync(id);
            if (staff == null) return (null, $"Staff {id} not found.");
            if (!staff.VacancyId.HasValue) return (null, "Staff member is not assigned to any vacancy.");

            var currentVacancy = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .FirstOrDefaultAsync(v => v.VacancyId == staff.VacancyId.Value);
            if (currentVacancy == null) return (null, "Current vacancy not found.");

            var newVacancy = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .FirstOrDefaultAsync(v => v.VacancyId == dto.NewVacancyId);
            if (newVacancy == null) return (null, $"Vacancy {dto.NewVacancyId} not found.");
            if (newVacancy.IsFilled) return (null, $"Vacancy '{newVacancy.VacancyCode}' is already filled.");

            var currentCompany = currentVacancy.Organization?.Parent;
            var currentCountry = currentCompany?.Parent;
            var targetCompany  = newVacancy.Organization?.Parent;
            var targetCountry  = targetCompany?.Parent;

            if (currentCompany?.Id != targetCompany?.Id || currentCountry?.Id != targetCountry?.Id)
                return (null, "Transfers are strictly limited to roles within the same Company and Country.");

            var oldVacancy = await _db.Vacancies.FindAsync(staff.VacancyId.Value);
            if (oldVacancy != null) oldVacancy.IsFilled = false;

            staff.VacancyId     = dto.NewVacancyId;
            newVacancy.IsFilled = true;
            await _db.SaveChangesAsync();

            var updated = await WithIncludes().FirstOrDefaultAsync(s => s.StaffId == id);
            return (MapToDto(updated!), null);
        }

        public async Task<(bool Success, string Message)> EndEmploymentAsync(
            Guid id,
            string status,
            string? reason,
            CancellationToken cancellationToken)
        {
            if (!status.Equals("Fired", StringComparison.OrdinalIgnoreCase) &&
                !status.Equals("Retired", StringComparison.OrdinalIgnoreCase))
                return (false, "Employment status must be Fired or Retired.");

            var normalizedStatus = status.Equals("Retired", StringComparison.OrdinalIgnoreCase)
                ? "Retired"
                : "Fired";
            var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (normalizedReason?.Length > 500)
                return (false, "Termination reason cannot exceed 500 characters.");

            var staff = await _db.StaffVacancies
                .Include(x => x.Person)
                .Include(x => x.Vacancy).ThenInclude(x => x!.DesignationNav)
                .Include(x => x.Vacancy).ThenInclude(x => x!.Organization)
                .SingleOrDefaultAsync(x => x.StaffId == id, cancellationToken);
            if (staff?.Person == null) return (false, $"Staff {id} not found.");
            if (!staff.Person.IsActive &&
                (staff.Person.EmploymentStatus.Equals("Fired", StringComparison.OrdinalIgnoreCase) ||
                 staff.Person.EmploymentStatus.Equals("Retired", StringComparison.OrdinalIgnoreCase)))
                return (false, $"{staff.Person.FullName} is already marked as {staff.Person.EmploymentStatus}.");

            var identityUser = await _db.Users.SingleOrDefaultAsync(
                x => x.Id == staff.Person.IdentityUserId,
                cancellationToken);
            if (identityUser?.IsSuperAdmin == true || identityUser?.IsTenantAdmin == true)
                return (false, "A Super Admin or Tenant Admin account cannot be ended from the employee screen.");

            var vacancy = staff.Vacancy;
            if (vacancy != null)
            {
                var chain = new List<OrganizationTree>();
                for (var node = vacancy.Organization; node != null && chain.Count < 20;)
                {
                    chain.Add(node);
                    node = node.ParentId.HasValue
                        ? await _db.OrganizationTree.SingleOrDefaultAsync(x => x.Id == node.ParentId.Value, cancellationToken)
                        : null;
                }

                OrganizationTree? Find(params string[] labels) => chain.FirstOrDefault(node =>
                    labels.Any(label => string.Equals(node.Label, label, StringComparison.OrdinalIgnoreCase)));

                staff.Person.LastOrganizationId = vacancy.OrganizationId;
                staff.Person.LastVacancyCode = vacancy.VacancyCode;
                staff.Person.LastJobTitle = vacancy.ResolvedJobTitle;
                staff.Person.LastDepartment = vacancy.Department ?? Find("Department", "Sub Department", "SubDepartment", "Unit", "Team", "Section")?.Name;
                staff.Person.LastBranchName = Find("Branch", "Sub Branch", "SubBranch", "Office")?.Name;
                staff.Person.LastCompanyName = Find("Company")?.Name;
                staff.Person.LastCountryName = Find("Country")?.Name;
                vacancy.IsFilled = false;
                staff.VacancyId = null;
            }

            staff.Person.LastLoginId = staff.LoginId;
            staff.Person.LastJoiningDate = await _db.PersonHrProfiles
                .Where(x => x.PersonId == staff.PersonId)
                .Select(x => x.JoiningDate)
                .FirstOrDefaultAsync(cancellationToken);
            staff.Person.EmploymentStatus = normalizedStatus;
            staff.Person.TerminationDateUtc = DateTime.UtcNow;
            staff.Person.TerminationReason = normalizedReason;
            staff.Person.IsActive = false;

            // Prevent a former employee from creating new authenticated sessions and
            // invalidate existing cookies at the next security-stamp validation.
            if (identityUser != null)
            {
                identityUser.LockoutEnabled = true;
                identityUser.LockoutEnd = DateTimeOffset.MaxValue;
                identityUser.SecurityStamp = Guid.NewGuid().ToString();
            }

            await _db.SaveChangesAsync(cancellationToken);
            return (true, $"{staff.Person.FullName} marked as {normalizedStatus}. The position is now vacant.");
        }

        public async Task<(bool Success, string Message)> DeleteAsync(Guid id)
        {
            var staff = await _db.StaffVacancies.FindAsync(id);
            if (staff == null) return (false, $"Staff {id} not found.");

            if (staff.VacancyId.HasValue)
            {
                var vacancy = await _db.Vacancies.FindAsync(staff.VacancyId.Value);
                if (vacancy != null) vacancy.IsFilled = false;
            }

            _db.StaffVacancies.Remove(staff);
            await _db.SaveChangesAsync();
            return (true, "Employee removed from vacancy. Vacancy is now vacant.");
        }

        public Task<(string? PhotoUrl, string? FullUrl, string? Error)> UploadPhotoAsync(
            Guid id, IFormFile photo, string baseUrl)
        {
            _ = id; _ = photo; _ = baseUrl;
            return Task.FromResult<(string?, string?, string?)>((null, null, "Staff photo is no longer stored on StaffVacancy. Upload photo using the Persons endpoints instead."));
        }

        public Task<(bool Success, string Message)> DeletePhotoAsync(Guid id)
        {
            _ = id;
            return Task.FromResult((false, "Staff photo is no longer stored on StaffVacancy. Delete photo using the Persons endpoints instead."));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private IQueryable<StaffVacancy> WithIncludes() =>
            _db.StaffVacancies
               .Include(s => s.Person)
               .Include(s => s.Vacancy)
                   .ThenInclude(v => v!.DesignationNav)
               .Include(s => s.Vacancy)
                   .ThenInclude(v => v!.Organization)
                       .ThenInclude(o => o!.Parent)
                           .ThenInclude(p => p!.Parent)
                               .ThenInclude(p => p!.Parent);

        private static StaffDto MapStaffRow(HrStaffListRow row) => new()
        {
            StaffId = row.StaffId,
            PersonId = row.PersonId,
            FullName = row.FullName,
            Email = row.Email,
            Phone = row.Phone,
            PhotoUrl = row.PhotoUrl,
            IsActive = row.IsActive,
            LoginId = row.LoginId,
            VacancyId = row.VacancyId,
            VacancyCode = row.VacancyCode,
            JobTitle = row.Designation,
            Department = row.Department,
            BranchName = row.BranchName,
            CompanyName = row.CompanyName,
            CountryName = row.CountryName,
            GroupName = row.GroupName,
            ShiftStartTime = row.ShiftStartTime,
            ShiftEndTime = row.ShiftEndTime,
            JoiningDate = row.JoiningDate
        };

        private static StaffDto MapToDto(StaffVacancy s)
        {
            var chain = new List<OrganizationTree>();
            for (var node = s.Vacancy?.Organization; node != null && chain.Count < 20; node = node.Parent)
                chain.Add(node);
            OrganizationTree? Find(params string[] labels) => chain.FirstOrDefault(n =>
                labels.Any(label => string.Equals(n.Label, label, StringComparison.OrdinalIgnoreCase)));
            var department = Find("Department");
            var branch = Find("Branch", "Office");
            var company = Find("Company");
            var country = Find("Country");
            var group = Find("Group");

            return new StaffDto
            {
                StaffId     = s.StaffId,
                PersonId    = s.PersonId,
                FullName    = s.Person?.FullName ?? "-",
                Email       = s.Person?.Email,
                Phone       = s.Person?.Phone,
                PhotoUrl    = s.Person?.ProfilePhotoUrl,
                IsActive    = s.Person?.IsActive ?? false,
                LoginId     = s.LoginId,
                VacancyId   = s.VacancyId,
                VacancyCode = s.Vacancy?.VacancyCode,
                JobTitle    = s.Vacancy?.ResolvedJobTitle,
                Department  = s.Vacancy?.Department ?? department?.Name,
                BranchName  = branch?.Name,
                CompanyName = company?.Name,
                CountryName = country?.Name,
                GroupName   = group?.Name,
                ShiftStartTime = s.Person?.ShiftStartTime,
                ShiftEndTime   = s.Person?.ShiftEndTime,
                JoiningDate = DateTime.UtcNow
            };
        }
    }
}
