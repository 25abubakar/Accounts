using Accounts.Data;
using Accounts.Models;
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
            var list = await WithIncludes().ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<StaffDto?> GetByIdAsync(Guid id)
        {
            var s = await WithIncludes().FirstOrDefaultAsync(x => x.StaffId == id);
            return s == null ? null : MapToDto(s);
        }

        public async Task<IEnumerable<StaffDto>> SearchAsync(string q)
        {
            var list = await WithIncludes()
                .Where(s =>
                    (s.Person != null && s.Person.FullName.Contains(q)) ||
                    (s.Person != null && s.Person.Email != null && s.Person.Email.Contains(q)))
                .ToListAsync();
            return list.Select(MapToDto);
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
            return (MapToDto((await WithIncludes().FirstAsync(x => x.StaffId == staff.StaffId))!), "Staff profile fields now live on Person. Update the linked Person instead.");
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
                   .ThenInclude(v => v!.JobTitleNav)
               .Include(s => s.Vacancy)
                   .ThenInclude(v => v!.Organization)
                       .ThenInclude(o => o!.Parent)
                           .ThenInclude(p => p!.Parent)
                               .ThenInclude(p => p!.Parent);

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
                JoiningDate = DateTime.UtcNow
            };
        }
    }
}
