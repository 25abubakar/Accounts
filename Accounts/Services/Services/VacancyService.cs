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
        private readonly JobTitleService      _jobTitleService;
        private readonly ITenantService       _tenantService;
        private readonly IOrganizationScopeService _organizationScope;

        public VacancyService(
            ApplicationDbContext db,
            VacancyCodeService   codeService,
            JobTitleService      jobTitleService,
            ITenantService       tenantService,
            IOrganizationScopeService organizationScope)
        {
            _db              = db;
            _codeService     = codeService;
            _jobTitleService = jobTitleService;
            _tenantService   = tenantService;
            _organizationScope = organizationScope;
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
            if (!await _organizationScope.IsWithinTenantSubtreeAsync(
                    _tenantService.RequiredTenantId,
                    orgId))
                return Array.Empty<VacancyDto>();

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
                    JobTitle      = v.ResolvedJobTitle,
                    Department    = v.Department,
                    IsFilled      = v.IsFilled,
                    EmployeeName  = v.Staff?.Person?.FullName,
                    EmployeeEmail = v.Staff?.Person?.Email,
                    JoiningDate   = null
                };
            });
        }

        public async Task<string?> PreviewCodeAsync(int organizationId, string jobTitle)
        {
            if (!await _organizationScope.IsWithinTenantSubtreeAsync(
                    _tenantService.RequiredTenantId,
                    organizationId))
                return null;

            var orgNode = await _db.OrganizationTree.FindAsync(organizationId);
            if (orgNode == null) return null;

            // Use PreviewAsync — reads counter WITHOUT incrementing it
            return await _codeService.PreviewAsync(organizationId, jobTitle);
        }

        public async Task<(VacancyDto? Vacancy, string? Error)> CreateAsync(CreateVacancyDto dto)
        {
            if (!await _organizationScope.IsWithinTenantSubtreeAsync(
                    _tenantService.RequiredTenantId,
                    dto.OrganizationId))
                return (null, "Organization node is outside the current tenant scope.");

            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return (null, $"Organization node {dto.OrganizationId} not found.");

            // ── Resolve JobTitleId from either Id OR Name OR legacy JobTitle ─
            int jobTitleId;
            string jobTitleForCode;
            
            if (dto.JobTitleId.HasValue && dto.JobTitleId.Value > 0)
            {
                // Frontend sent an Id — validate it exists
                var exists = await _db.JobTitles.AnyAsync(jt => jt.Id == dto.JobTitleId.Value);
                if (!exists)
                    return (null, $"JobTitle Id {dto.JobTitleId.Value} not found.");
                jobTitleId = dto.JobTitleId.Value;
                var title = await _db.JobTitles.FindAsync(jobTitleId);
                jobTitleForCode = title?.TitleName ?? "Unknown";
            }
            else if (!string.IsNullOrWhiteSpace(dto.JobTitleName))
            {
                // Frontend sent a new name — upsert and get the Id
                jobTitleId = await _jobTitleService.UpsertByNameAsync(dto.JobTitleName);
                jobTitleForCode = dto.JobTitleName;
            }
            else if (!string.IsNullOrWhiteSpace(dto.JobTitle))
            {
                // Legacy: JobTitle string — upsert and get the Id
                jobTitleId = await _jobTitleService.UpsertByNameAsync(dto.JobTitle);
                jobTitleForCode = dto.JobTitle;
            }
            else
            {
                return (null, "JobTitleId, JobTitleName, or JobTitle (legacy) is required.");
            }

            var vacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, jobTitleForCode);

            var vacancy = new Vacancy
            {
                VacancyId      = Guid.NewGuid(),
                TenantId       = _tenantService.RequiredTenantId,   // ← stamp tenant
                OrganizationId = dto.OrganizationId,
                VacancyCode    = vacancyCode,
                JobTitleId     = jobTitleId,
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
            if (!await _organizationScope.IsWithinTenantSubtreeAsync(
                    _tenantService.RequiredTenantId,
                    dto.OrganizationId))
                return ([], ["Organization node is outside the current tenant scope."]);

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
                    // ── Resolve JobTitleId from either Id OR Name OR legacy JobTitle ─
                    int jobTitleId;
                    string jobTitleForCode;
                    
                    if (dto.JobTitleId.HasValue && dto.JobTitleId.Value > 0)
                    {
                        var exists = await _db.JobTitles.AnyAsync(jt => jt.Id == dto.JobTitleId.Value);
                        if (!exists)
                        {
                            errors.Add($"Vacancy {i + 1} failed: JobTitle Id {dto.JobTitleId.Value} not found.");
                            continue;
                        }
                        jobTitleId = dto.JobTitleId.Value;
                        var title = await _db.JobTitles.FindAsync(jobTitleId);
                        jobTitleForCode = title?.TitleName ?? "Unknown";
                    }
                    else if (!string.IsNullOrWhiteSpace(dto.JobTitleName))
                    {
                        jobTitleId = await _jobTitleService.UpsertByNameAsync(dto.JobTitleName);
                        jobTitleForCode = dto.JobTitleName;
                    }
                    else if (!string.IsNullOrWhiteSpace(dto.JobTitle))
                    {
                        jobTitleId = await _jobTitleService.UpsertByNameAsync(dto.JobTitle);
                        jobTitleForCode = dto.JobTitle;
                    }
                    else
                    {
                        errors.Add($"Vacancy {i + 1} failed: JobTitleId, JobTitleName, or JobTitle (legacy) is required.");
                        continue;
                    }

                    var vacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, jobTitleForCode);

                    var vacancy = new Vacancy
                    {
                        VacancyId      = Guid.NewGuid(),
                        TenantId       = _tenantService.RequiredTenantId,   // ← stamp tenant
                        OrganizationId = dto.OrganizationId,
                        VacancyCode    = vacancyCode,
                        JobTitleId     = jobTitleId,
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

            if (!await _organizationScope.IsWithinTenantSubtreeAsync(
                    _tenantService.RequiredTenantId,
                    dto.OrganizationId))
                return (null, "Organization node is outside the current tenant scope.");

            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null) return (null, $"Organization node {dto.OrganizationId} not found.");

            // ── Resolve JobTitleId from either Id OR Name OR legacy JobTitle ─
            int newJobTitleId;
            string jobTitleForCode;
            
            if (dto.JobTitleId.HasValue && dto.JobTitleId.Value > 0)
            {
                var exists = await _db.JobTitles.AnyAsync(jt => jt.Id == dto.JobTitleId.Value);
                if (!exists)
                    return (null, $"JobTitle Id {dto.JobTitleId.Value} not found.");
                newJobTitleId = dto.JobTitleId.Value;
                var title = await _db.JobTitles.FindAsync(newJobTitleId);
                jobTitleForCode = title?.TitleName ?? "Unknown";
            }
            else if (!string.IsNullOrWhiteSpace(dto.JobTitleName))
            {
                newJobTitleId = await _jobTitleService.UpsertByNameAsync(dto.JobTitleName);
                jobTitleForCode = dto.JobTitleName;
            }
            else if (!string.IsNullOrWhiteSpace(dto.JobTitle))
            {
                newJobTitleId = await _jobTitleService.UpsertByNameAsync(dto.JobTitle);
                jobTitleForCode = dto.JobTitle;
            }
            else
            {
                return (null, "JobTitleId, JobTitleName, or JobTitle (legacy) is required.");
            }

            bool needsNewCode = vacancy.JobTitleId != newJobTitleId || vacancy.OrganizationId != dto.OrganizationId;

            vacancy.JobTitleId     = newJobTitleId;
            vacancy.Department     = dto.Department;
            vacancy.OrganizationId = dto.OrganizationId;

            if (needsNewCode)
                vacancy.VacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, jobTitleForCode);

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
               .Include(v => v.JobTitleNav)
               .Include(v => v.Staff).ThenInclude(s => s!.Person);

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
                JobTitleId     = v.JobTitleId,
                JobTitle       = v.ResolvedJobTitle,
                Department     = v.Department,
                IsFilled       = v.IsFilled,
                CreatedDate    = v.CreatedDate,
                Employee       = v.Staff == null ? null : new StaffDto
                {
                    StaffId     = v.Staff.StaffId,
                    FullName    = v.Staff.Person?.FullName ?? "-",
                    Email       = v.Staff.Person?.Email,
                    Phone       = v.Staff.Person?.Phone,
                    PhotoUrl    = v.Staff.Person?.ProfilePhotoUrl,
                    VacancyId   = v.Staff.VacancyId,
                    VacancyCode = v.VacancyCode,
                    JobTitle    = v.ResolvedJobTitle,
                    Department  = v.Department ?? node?.Name,
                    BranchName  = node?.Name,
                    CompanyName = p1?.Name,
                    CountryName = p2?.Name,
                    JoiningDate = DateTime.UtcNow
                }
            };
        }
    }
}
