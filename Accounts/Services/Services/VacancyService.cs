using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class VacancyService : IVacancyService
    {
        private readonly ApplicationDbContext _db;
        private readonly VacancyCodeService   _codeService;

        public VacancyService(ApplicationDbContext db, VacancyCodeService codeService)
        {
            _db          = db;
            _codeService = codeService;
        }

        public async Task<IEnumerable<VacancyDto>> GetAllAsync()
        {
            var list = await WithIncludes().OrderBy(v => v.VacancyCode).ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<VacancyDto?> GetByIdAsync(Guid id)
        {
            var v = await WithIncludes().FirstOrDefaultAsync(v => v.VacancyId == id);
            return v == null ? null : MapToDto(v);
        }

        public async Task<IEnumerable<VacancyDto>> GetVacantAsync()
        {
            var list = await WithIncludes().Where(v => !v.IsFilled).ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<VacancyDto>> GetFilledAsync()
        {
            var list = await WithIncludes().Where(v => v.IsFilled).ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<VacancyDto>> GetByNodeAsync(int orgId)
        {
            var list = await WithIncludes().Where(v => v.OrganizationId == orgId).ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<OrgVacancyReportDto>> GetReportAsync()
        {
            var list = await WithIncludes().OrderBy(v => v.Organization!.Name).ToListAsync();
            return list.Select(v =>
            {
                var node = v.Organization;
                var p1   = node?.Parent;
                var p2   = p1?.Parent;
                return new OrgVacancyReportDto
                {
                    Country       = p2?.Name    ?? "-",
                    Company       = p1?.Name    ?? "-",
                    Branch        = node?.Name  ?? "-",
                    VacancyCode   = v.VacancyCode,
                    JobTitle      = v.JobTitle,
                    Department    = v.Department,
                    IsFilled      = v.IsFilled,
                    EmployeeName  = v.Staff?.FullName,
                    EmployeeEmail = v.Staff?.Email,
                    JoiningDate   = v.Staff?.JoiningDate
                };
            });
        }

        public async Task<string?> PreviewCodeAsync(int organizationId, string jobTitle)
        {
            var orgNode = await _db.OrganizationTree.FindAsync(organizationId);
            if (orgNode == null) return null;

            // Use PreviewAsync — reads counter WITHOUT incrementing it
            return await _codeService.PreviewAsync(organizationId, jobTitle);
        }

        public async Task<(VacancyDto? Vacancy, string? Error)> CreateAsync(CreateVacancyDto dto)
        {
            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return (null, $"Organization node {dto.OrganizationId} not found.");

            var vacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, dto.JobTitle);

            var vacancy = new Vacancy
            {
                VacancyId      = Guid.NewGuid(),
                OrganizationId = dto.OrganizationId,
                VacancyCode    = vacancyCode,
                JobTitle       = dto.JobTitle,
                Department     = dto.Department,
                IsFilled       = false,
                CreatedDate    = DateTime.UtcNow
            };

            _db.Vacancies.Add(vacancy);
            await _db.SaveChangesAsync();

            var created = await WithIncludes().FirstOrDefaultAsync(v => v.VacancyId == vacancy.VacancyId);
            return (MapToDto(created!), null);
        }

        /// <summary>
        /// Creates multiple vacancies in one request.
        /// The loop runs server-side — frontend just sends VacancyCount.
        /// Each vacancy gets a unique auto-incremented code (race-condition safe).
        ///
        /// Example: VacancyCount = 5, JobTitle = "Developer"
        /// Creates: Pakistan-LalGroup-LT-1
        ///          Pakistan-LalGroup-LT-2
        ///          Pakistan-LalGroup-LT-3
        ///          Pakistan-LalGroup-LT-4
        ///          Pakistan-LalGroup-LT-5
        /// </summary>
        public async Task<(IEnumerable<VacancyDto> Created, IEnumerable<string> Errors)> CreateBulkAsync(
            CreateVacancyDto dto)
        {
            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return ([], [$"Organization node {dto.OrganizationId} not found."]);

            int count = dto.VacancyCount < 1 ? 1 : dto.VacancyCount;

            var created = new List<VacancyDto>();
            var errors  = new List<string>();

            // Loop runs entirely on the backend — each iteration gets a unique code
            // from the atomic VacancyCounters table (no race conditions)
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var vacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, dto.JobTitle);

                    var vacancy = new Vacancy
                    {
                        VacancyId      = Guid.NewGuid(),
                        OrganizationId = dto.OrganizationId,
                        VacancyCode    = vacancyCode,
                        JobTitle       = dto.JobTitle,
                        Department     = dto.Department,
                        IsFilled       = false,
                        CreatedDate    = DateTime.UtcNow
                    };

                    _db.Vacancies.Add(vacancy);
                    await _db.SaveChangesAsync();

                    var saved = await WithIncludes().FirstOrDefaultAsync(v => v.VacancyId == vacancy.VacancyId);
                    if (saved != null) created.Add(MapToDto(saved));
                }
                catch (Exception ex)
                {
                    errors.Add($"Vacancy {i + 1} failed: {ex.Message}");
                }
            }

            return (created, errors);
        }

        public async Task<(VacancyDto? Vacancy, string? Error)> UpdateAsync(Guid id, UpdateVacancyDto dto)
        {
            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return (null, $"Position {id} not found.");

            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null) return (null, $"Organization node {dto.OrganizationId} not found.");

            bool needsNewCode = vacancy.JobTitle != dto.JobTitle || vacancy.OrganizationId != dto.OrganizationId;

            vacancy.JobTitle       = dto.JobTitle;
            vacancy.Department     = dto.Department;
            vacancy.OrganizationId = dto.OrganizationId;

            if (needsNewCode)
                vacancy.VacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, dto.JobTitle);

            await _db.SaveChangesAsync();

            var updated = await WithIncludes().FirstOrDefaultAsync(v => v.VacancyId == id);
            return (MapToDto(updated!), null);
        }

        public async Task<(bool Success, string Message)> DeleteAsync(Guid id)
        {
            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return (false, $"Position {id} not found.");

            if (vacancy.IsFilled)
                return (false, "Cannot delete a filled position. Remove the employee first.");

            _db.Vacancies.Remove(vacancy);
            await _db.SaveChangesAsync();
            return (true, $"Position '{vacancy.VacancyCode}' deleted.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private IQueryable<Vacancy> WithIncludes() =>
            _db.Vacancies
               .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
               .Include(v => v.Staff);

        private static VacancyDto MapToDto(Vacancy v)
        {
            var node = v.Organization;
            var p1   = node?.Parent;
            var p2   = p1?.Parent;

            return new VacancyDto
            {
                VacancyId      = v.VacancyId,
                OrganizationId = v.OrganizationId,
                BranchName     = node?.Name  ?? "-",
                CompanyName    = p1?.Name    ?? "-",
                CountryName    = p2?.Name    ?? "-",
                NodeLabel      = node?.Label ?? "-",
                VacancyCode    = v.VacancyCode,
                JobTitle       = v.JobTitle,
                Department     = v.Department,
                IsFilled       = v.IsFilled,
                CreatedDate    = v.CreatedDate,
                Employee       = v.Staff == null ? null : new StaffDto
                {
                    StaffId     = v.Staff.StaffId,
                    FullName    = v.Staff.FullName,
                    Email       = v.Staff.Email,
                    Phone       = v.Staff.Phone,
                    PhotoUrl    = v.Staff.PhotoUrl,
                    VacancyId   = v.Staff.VacancyId,
                    VacancyCode = v.VacancyCode,
                    JobTitle    = v.JobTitle,
                    BranchName  = node?.Name,
                    CompanyName = p1?.Name,
                    CountryName = p2?.Name,
                    JoiningDate = v.Staff.JoiningDate
                }
            };
        }
    }
}
