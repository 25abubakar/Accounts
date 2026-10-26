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
            var numbers = await _db.Persons.AsNoTracking().Where(x => x.LegacyPersonId != null)
                .ToDictionaryAsync(x => x.PersonId, x => x.LegacyPersonId);
            return rows.Select(row => { var dto = MapStaffRow(row); dto.LegacyPersonId = numbers.GetValueOrDefault(row.PersonId); return dto; });
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
            if (_db.Database.CurrentTransaction != null) return await HirePersonCoreAsync(vacancyId, personId);
            return await _db.Database.CreateExecutionStrategy().ExecuteAsync(() => HirePersonCoreAsync(vacancyId, personId));
        }

        private async Task<(StaffDto? Staff, string? Error)> HirePersonCoreAsync(Guid vacancyId, Guid personId)
        {
            await using var transaction = _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
                : null;
            var tenantId = _tenantService.RequiredTenantId;
            // Lock the selected position and person until this atomic assignment is saved.
            var vacancy = await _db.Vacancies.FromSqlInterpolated(
                $"SELECT * FROM dbo.Vacancies WITH(UPDLOCK,HOLDLOCK) WHERE VacancyId={vacancyId} AND TenantId={tenantId}")
                .SingleOrDefaultAsync();
            if (vacancy == null) return (null, $"Vacancy {vacancyId} not found.");
            if (vacancy.IsFilled || await _db.StaffVacancies.AnyAsync(x => x.VacancyId == vacancyId))
                return (null, $"Vacancy '{vacancy.VacancyCode}' is already filled.");

            var person = await _db.Persons.FromSqlInterpolated(
                $"SELECT * FROM dbo.Persons WITH(UPDLOCK,HOLDLOCK) WHERE PersonId={personId} AND TenantId={tenantId}")
                .SingleOrDefaultAsync();
            if (person == null) return (null, $"Person {personId} not found.");
            var former = person.EmploymentStatus.Equals("Fired", StringComparison.OrdinalIgnoreCase)
                || person.EmploymentStatus.Equals("Retired", StringComparison.OrdinalIgnoreCase);
            var staff = await _db.StaffVacancies.SingleOrDefaultAsync(x => x.PersonId == personId);
            if (staff != null && (staff.VacancyId.HasValue || !former))
                return (null, $"Person '{person.FullName}' is already hired.");
            if (!person.IsActive && !former)
                return (null, "Activate this registered person before hiring.");

            var identityUser = await _db.Users.SingleOrDefaultAsync(x => x.Id == person.IdentityUserId && x.TenantId == tenantId);
            if (identityUser == null) return (null, "The person's account is missing from this company.");
            if (former && (identityUser.IsSuperAdmin || identityUser.IsTenantAdmin))
                return (null, "An administrator account cannot be assigned through employee rejoin.");

            if (former) await PreserveEmploymentEndingAsync(person, staff);
            var newAssignment = staff == null;
            if (staff == null)
            {
                staff = new StaffVacancy
                {
                    StaffId = Guid.NewGuid(), PersonId = personId, TenantId = tenantId,
                    LoginId = person.LastLoginId ?? identityUser.UserName
                };
                _db.StaffVacancies.Add(staff);
            }
            staff.VacancyId = vacancyId;
            person.IsActive = true;
            person.EmploymentStatus = "Hired";
            person.TerminationDateUtc = null;
            person.TerminationReason = null;
            identityUser.LockoutEnabled = true;
            identityUser.LockoutEnd = null;
            identityUser.SecurityStamp = Guid.NewGuid().ToString();

            var staffAttendanceMenuId = await _db.Menus
                .Where(menu => menu.IsActive && menu.Route == "/attendance/staff")
                .Select(menu => (int?)menu.Id).FirstOrDefaultAsync();
            if (newAssignment && staffAttendanceMenuId.HasValue && !await _db.StaffMenuAccesses.AnyAsync(x =>
                    x.StaffId == staff.StaffId && x.MenuId == staffAttendanceMenuId.Value))
                _db.StaffMenuAccesses.Add(new StaffMenuAccess
                {
                    StaffId = staff.StaffId, MenuId = staffAttendanceMenuId.Value, IsAllow = true,
                    GrantedBy = "System: Staff Attendance", GrantedDate = DateTime.UtcNow
                });
            vacancy.IsFilled = true;
            await _db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();

            var created = await WithIncludes().FirstAsync(s => s.StaffId == staff.StaffId);
            var dto = MapToDto(created);
            dto.RequiresPasswordSetup = string.IsNullOrEmpty(identityUser.PasswordHash);
            return (dto, null);
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

            await PreserveEmploymentEndingAsync(staff.Person, staff, vacancy?.VacancyId);
            await _db.SaveChangesAsync(cancellationToken);
            return (true, $"{staff.Person.FullName} marked as {normalizedStatus}. The position is now vacant.");
        }

        private async Task PreserveEmploymentEndingAsync(Person person, StaffVacancy? staff, Guid? vacancyId = null)
        {
            if (await _db.PersonEmploymentHistories.AnyAsync(x => x.PersonId == person.PersonId
                && x.EmploymentStatus == person.EmploymentStatus && x.EndingDateUtc == person.TerminationDateUtc)) return;
            _db.PersonEmploymentHistories.Add(new PersonEmploymentHistory
            {
                TenantId = person.TenantId, PersonId = person.PersonId, StaffId = staff?.StaffId,
                VacancyId = vacancyId ?? staff?.VacancyId, OrganizationId = person.LastOrganizationId,
                LoginId = staff?.LoginId ?? person.LastLoginId, VacancyCode = person.LastVacancyCode,
                JobTitle = person.LastJobTitle, Department = person.LastDepartment,
                BranchName = person.LastBranchName, CompanyName = person.LastCompanyName,
                CountryName = person.LastCountryName, JoiningDate = person.LastJoiningDate,
                EmploymentStatus = person.EmploymentStatus, EndingDateUtc = person.TerminationDateUtc,
                EndingReason = person.TerminationReason
            });
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
               .Include(s => s.Person).ThenInclude(p => p!.Contacts)
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
            LegacyStaffId = row.LegacyStaffId,
            LegacyVacancyId = row.LegacyVacancyId,
            LegacyServiceStatusId = row.LegacyServiceStatusId,
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
                LegacyStaffId = s.LegacyStaffId,
                LegacyPersonId = s.Person?.LegacyPersonId,
                LegacyVacancyId = s.LegacyVacancyId,
                LegacyServiceStatusId = s.LegacyServiceStatusId,
                PersonId    = s.PersonId,
                FullName    = s.Person?.FullName ?? "-",
                Email       = s.Person.GetPrimaryContact("Email"),
                Phone       = s.Person.GetPrimaryContact("Phone"),
                PhotoUrl    = s.Person?.ProfilePhotoUrl,
                IsActive    = s.Person?.IsActive ?? false,
                LoginId     = s.LoginId,
                VacancyId   = s.VacancyId,
                VacancyCode = s.Vacancy?.VacancyCode ?? s.Person?.LastVacancyCode,
                JobTitle    = s.Vacancy?.ResolvedJobTitle,
                Department  = s.Vacancy?.Department ?? department?.Name ?? s.Person?.LastDepartment,
                BranchName  = branch?.Name ?? s.Person?.LastBranchName,
                CompanyName = company?.Name ?? s.Person?.LastCompanyName,
                CountryName = country?.Name ?? s.Person?.LastCountryName,
                GroupName   = group?.Name,
                ShiftStartTime = s.Person?.ShiftStartTime,
                ShiftEndTime   = s.Person?.ShiftEndTime,
                JoiningDate = s.Person?.LastJoiningDate
            };
        }
    }
}
